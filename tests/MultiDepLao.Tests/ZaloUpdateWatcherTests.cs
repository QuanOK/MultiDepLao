using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class ZaloUpdateWatcherTests
{
    private static void MakeZalo(string root, string ver)
    {
        var d = Path.Combine(root, "Zalo-" + ver);
        Directory.CreateDirectory(Path.Combine(d, "plugins"));
        File.WriteAllText(Path.Combine(d, "Zalo.exe"), "exe " + ver);
        File.WriteAllText(Path.Combine(d, "res.dll"), "dll");
    }

    private sealed class Env : IDisposable
    {
        public TempRoot T = new(); public AppPaths P; public string Src; public ZaloInstall Inst; public FakeProbe Probe = new();
        public List<(string, int)> Running = new();
        public ZaloUpdateWatcher W;
        public Env()
        {
            P = T.Paths; Src = T.Sub("zalo"); Inst = new ZaloInstall(P, Src, null, _ => true);
            W = new ZaloUpdateWatcher(Inst, P, Probe, () => Running.ToList());
        }
        public void RunOn(string id, int pid, string ver)
        {
            Probe.Procs[pid] = new ProcessInfo(pid, "Zalo.exe", Path.Combine(P.NoCapRoot, "Zalo-" + ver, "Zalo.exe"), DateTime.UtcNow);
            Running.Add((id, pid));
        }
        public void Dispose() { W.Dispose(); T.Dispose(); }
    }

    [Fact]
    public async Task Check_NewSourceVersion_CreatesCopy_AndMarksOldRunningOutdated()
    {
        using var e = new Env();
        MakeZalo(e.Src, "26.9.30");
        await e.W.CheckAsync();
        Assert.True(Directory.Exists(Path.Combine(e.P.NoCapRoot, "Zalo-26.9.30")));
        e.RunOn("a", 100, "26.9.30"); e.RunOn("b", 101, "26.9.30");
        e.W.Refresh();
        Assert.Empty(e.W.State.OutdatedIds);

        MakeZalo(e.Src, "26.11.12");   // Zalo tu cap nhat
        await e.W.CheckAsync();
        Assert.True(File.Exists(Path.Combine(e.P.NoCapRoot, "Zalo-26.11.12", ZaloInstall.CompleteMarker)));
        Assert.Equal(new Version(26, 11, 12), e.W.State.LatestCopy);
        Assert.Equal(new[] { "a", "b" }, e.W.State.OutdatedIds.OrderBy(x => x));
        // ban cu dang duoc dung -> khong bi don
        Assert.True(Directory.Exists(Path.Combine(e.P.NoCapRoot, "Zalo-26.9.30")));
    }

    [Fact]
    public async Task Check_NothingNew_DoesNotRecopy()
    {
        using var e = new Env();
        MakeZalo(e.Src, "26.9.30");
        await e.W.CheckAsync();
        var dst = Path.Combine(e.P.NoCapRoot, "Zalo-26.9.30");
        File.WriteAllText(Path.Combine(dst, "res.dll"), "changed");
        await e.W.CheckAsync();
        Assert.Equal("changed", File.ReadAllText(Path.Combine(dst, "res.dll")));
    }

    [Fact]
    public async Task Check_SourceOlderThanCopy_IsIgnored()
    {
        using var e = new Env();
        MakeZalo(e.P.NoCapRoot, "26.11.12");           // ban sao cu cua spike (nhan khong can danh dau)
        MakeZalo(e.Src, "26.9.30");
        await e.W.CheckAsync();
        Assert.False(Directory.Exists(Path.Combine(e.P.NoCapRoot, "Zalo-26.9.30")));
    }

    [Fact]
    public async Task Check_InterruptedCopy_IsRedone()
    {
        using var e = new Env();
        MakeZalo(e.Src, "26.11.12");
        var dst = Path.Combine(e.P.NoCapRoot, "Zalo-26.11.12");
        Directory.CreateDirectory(dst);
        File.WriteAllText(Path.Combine(dst, "Zalo.exe"), "partial");
        File.WriteAllText(Path.Combine(dst, ZaloInstall.CopyingMarker), "x");
        await e.W.CheckAsync();
        Assert.Equal("exe 26.11.12", File.ReadAllText(Path.Combine(dst, "Zalo.exe")));
        Assert.False(File.Exists(Path.Combine(dst, ZaloInstall.CopyingMarker)));
        Assert.Equal(new Version(26, 11, 12), e.W.State.LatestCopy);
    }

    [Fact]
    public async Task Prune_RemovesOldCopyOnlyWhenNoProcessUsesIt()
    {
        using var e = new Env();
        MakeZalo(e.Src, "26.9.30"); await e.W.CheckAsync();
        var old = Path.Combine(e.P.NoCapRoot, "Zalo-26.9.30");
        e.RunOn("a", 100, "26.9.30");
        MakeZalo(e.Src, "26.11.12"); await e.W.CheckAsync();
        e.W.OnInstancesChanged();
        Assert.True(Directory.Exists(old));

        e.Probe.Procs.Remove(100); e.Running.Clear();   // tai khoan dung
        e.W.OnInstancesChanged();
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(Path.Combine(e.P.NoCapRoot, "Zalo-26.11.12")));
        Assert.Empty(e.W.State.OutdatedIds);
    }

    [Fact]
    public async Task Prune_UnknownProcessPath_DeletesNothing()
    {
        using var e = new Env();
        MakeZalo(e.Src, "26.9.30"); await e.W.CheckAsync();
        e.Probe.Procs[100] = new ProcessInfo(100, "", null, DateTime.UtcNow);   // khong doc duoc duong dan
        e.Running.Add(("a", 100));
        MakeZalo(e.Src, "26.11.12"); await e.W.CheckAsync();
        Assert.Empty(e.W.PruneNow());
        Assert.True(Directory.Exists(Path.Combine(e.P.NoCapRoot, "Zalo-26.9.30")));
    }

    [Fact]
    public async Task ConcurrentChecks_AndEnsure_CopyOnce()
    {
        using var e = new Env();
        MakeZalo(e.Src, "26.11.12");
        var start = e.Inst.EnsureNoCapCopyAsync();
        await Task.WhenAll(start, e.W.CheckAsync(), e.Inst.EnsureNoCapCopyAsync(), e.W.CheckAsync());
        var dst = Path.Combine(e.P.NoCapRoot, "Zalo-26.11.12");
        Assert.True(File.Exists(Path.Combine(dst, ZaloInstall.CompleteMarker)));
        Assert.False(File.Exists(Path.Combine(dst, ZaloInstall.CopyingMarker)));
    }

    [Fact]
    public async Task Check_NoZaloInstalled_DoesNotThrow()
    {
        using var e = new Env();
        await e.W.CheckAsync();
        Assert.Null(e.W.State.LatestCopy);
    }
}
