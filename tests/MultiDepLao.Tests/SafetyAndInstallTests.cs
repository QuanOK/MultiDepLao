using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class SafetyGateTests
{
    private sealed class Ctx : IDisposable
    {
        public TempRoot T = new(); public AppPaths P; public FakeProbe Probe = new(); public SafetyGate Gate;
        public string NoCapExe;
        public Ctx()
        {
            P = T.Paths; Gate = new SafetyGate(P, Probe);
            NoCapExe = Path.Combine(P.NoCapRoot, "Zalo-26.11.12", "Zalo.exe");
            Directory.CreateDirectory(P.ProfileDir("acc-01"));
        }
        public DateTime Created = new(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc);
        public void Proc(int pid, string name = "Zalo.exe", string? path = null, string? cmd = null) =>
            Probe.Procs[pid] = new ProcessInfo(pid, name, path ?? NoCapExe, Created, cmd);
        public void NewPid(int pid, DateTime? created = null) => PidRecord.Write(P.PidFile("acc-01"), pid, created ?? Created);
        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void Accepts_NewFormat()
    {
        using var c = new Ctx(); c.Proc(100); c.NewPid(100);
        var v = c.Gate.Verify("acc-01");
        Assert.True(v.Ok, v.Reason); Assert.Equal(100, v.Instance!.Pid);
    }

    [Fact]
    public void Accepts_LegacyFormat_ProcessCreatedBeforeFileWrite()
    {
        using var c = new Ctx(); c.Proc(100);
        File.WriteAllText(c.P.PidFile("acc-01"), "100");
        File.SetLastWriteTimeUtc(c.P.PidFile("acc-01"), c.Created.AddSeconds(3));
        Assert.True(c.Gate.Verify("acc-01").Ok);
    }

    [Fact]
    public void Rejects_LegacyFormat_ProcessCreatedLongAfterFile()
    {
        using var c = new Ctx(); c.Proc(100);
        File.WriteAllText(c.P.PidFile("acc-01"), "100");
        File.SetLastWriteTimeUtc(c.P.PidFile("acc-01"), c.Created.AddMinutes(-5));
        var v = c.Gate.Verify("acc-01");
        Assert.Equal(VerifyFailure.IdentityMismatch, v.Failure);
    }

    [Fact]
    public void Rejects_MissingPidFile() => Assert.Equal(VerifyFailure.NoPidFile, new Ctx().Gate.Verify("acc-01").Failure);

    [Fact]
    public void Rejects_BadPidFile()
    {
        using var c = new Ctx(); File.WriteAllText(c.P.PidFile("acc-01"), "garbage");
        Assert.Equal(VerifyFailure.BadPidFile, c.Gate.Verify("acc-01").Failure);
    }

    [Fact]
    public void Rejects_NonexistentPid()
    {
        using var c = new Ctx(); c.NewPid(100);
        Assert.Equal(VerifyFailure.ProcessNotFound, c.Gate.Verify("acc-01").Failure);
    }

    [Fact]
    public void Rejects_OtherExeName()
    {
        using var c = new Ctx(); c.Proc(100, "notepad.exe"); c.NewPid(100);
        Assert.Equal(VerifyFailure.WrongExe, c.Gate.Verify("acc-01").Failure);
    }

    [Theory]
    [InlineData(@"%LOCALAPPDATA%\Programs\Zalo\Zalo-26.11.12\Zalo.exe")]
    [InlineData(@"C:\Elsewhere\Zalo.exe")]
    public void Rejects_ExeOutsideNoCap_IncludingRealZalo(string path)
    {
        using var c = new Ctx();
        path = path.Replace("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        c.Proc(100, path: path); c.NewPid(100);
        Assert.Equal(VerifyFailure.OutsideRoot, c.Gate.Verify("acc-01").Failure);
    }

    [Fact]
    public void Rejects_PrefixSiblingOfNoCapRoot()
    {
        using var c = new Ctx(); c.Proc(100, path: c.P.NoCapRoot + "-evil\\Zalo.exe"); c.NewPid(100);
        Assert.Equal(VerifyFailure.OutsideRoot, c.Gate.Verify("acc-01").Failure);
    }

    [Fact]
    public void Rejects_ReusedPid()
    {
        using var c = new Ctx(); c.Proc(100); c.NewPid(100, c.Created.AddHours(-3));
        Assert.Equal(VerifyFailure.IdentityMismatch, c.Gate.Verify("acc-01").Failure);
    }

    [Fact]
    public void Rejects_ElectronChild_WhenCommandLineKnown()
    {
        using var c = new Ctx(); c.Proc(100, cmd: "Zalo.exe --type=renderer"); c.NewPid(100);
        Assert.Equal(VerifyFailure.ChildProcess, c.Gate.Verify("acc-01").Failure);
    }

    [Fact]
    public void Rejects_BadId() => Assert.Equal(VerifyFailure.BadId, new Ctx().Gate.Verify("..").Failure);
}

public class ZaloInstallTests
{
    private static void MakeZalo(string root, string ver, bool cap = true)
    {
        var d = Path.Combine(root, "Zalo-" + ver);
        Directory.CreateDirectory(Path.Combine(d, "plugins", "capture"));
        File.WriteAllText(Path.Combine(d, "Zalo.exe"), "exe " + ver);
        File.WriteAllText(Path.Combine(d, "res.dll"), "dll");
        if (cap) File.WriteAllText(Path.Combine(d, "plugins", "capture", "ZaloCap.exe"), "cap");
    }

    [Fact]
    public void FindLatest_ComparesNumerically()
    {
        using var t = new TempRoot(); var src = t.Sub("zalo");
        MakeZalo(src, "26.9.30"); MakeZalo(src, "26.11.12"); Directory.CreateDirectory(Path.Combine(src, "Zalo-junk"));
        var v = new ZaloInstall(t.Paths, src, null, _ => true).FindLatest();
        Assert.Equal("Zalo-26.11.12", v!.DirName);
    }

    [Fact]
    public async Task EnsureCopy_ExcludesCap_WritesMarker_ReadsOnlySource()
    {
        using var t = new TempRoot(); var src = t.Sub("zalo"); MakeZalo(src, "26.11.12");
        var p = t.Paths; var inst = new ZaloInstall(p, src, null, _ => true);
        double last = 0; var exe = await inst.EnsureNoCapCopyAsync(new Progress<double>(x => last = x));
        var dst = Path.GetDirectoryName(exe)!;
        Assert.True(File.Exists(exe)); Assert.True(File.Exists(Path.Combine(dst, "res.dll")));
        Assert.False(File.Exists(Path.Combine(dst, "plugins", "capture", "ZaloCap.exe")));
        Assert.True(File.Exists(Path.Combine(dst, ZaloInstall.CompleteMarker)));
        Assert.False(File.Exists(Path.Combine(dst, ZaloInstall.CopyingMarker)));
        Assert.True(File.Exists(Path.Combine(src, "Zalo-26.11.12", "plugins", "capture", "ZaloCap.exe")));
        Assert.False(File.Exists(Path.Combine(src, "Zalo-26.11.12", ZaloInstall.CompleteMarker)));
        // lan hai khong chep lai
        File.WriteAllText(Path.Combine(dst, "res.dll"), "changed");
        await inst.EnsureNoCapCopyAsync();
        Assert.Equal("changed", File.ReadAllText(Path.Combine(dst, "res.dll")));
    }

    [Fact]
    public async Task EnsureCopy_RedoesInterruptedCopy()
    {
        using var t = new TempRoot(); var src = t.Sub("zalo"); MakeZalo(src, "26.11.12");
        var p = t.Paths; var dst = Path.Combine(p.NoCapRoot, "Zalo-26.11.12");
        Directory.CreateDirectory(dst);
        File.WriteAllText(Path.Combine(dst, "Zalo.exe"), "partial");
        File.WriteAllText(Path.Combine(dst, ZaloInstall.CopyingMarker), "x");
        File.WriteAllText(Path.Combine(dst, "stale.txt"), "stale");
        await new ZaloInstall(p, src, null, _ => true).EnsureNoCapCopyAsync();
        Assert.Equal("exe 26.11.12", File.ReadAllText(Path.Combine(dst, "Zalo.exe")));
        Assert.False(File.Exists(Path.Combine(dst, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(dst, ZaloInstall.CompleteMarker)));
    }

    [Fact]
    public async Task EnsureCopy_AdoptsLegacySpikeCopy_WithoutMarker()
    {
        using var t = new TempRoot(); var src = t.Sub("zalo"); MakeZalo(src, "26.11.12");
        var p = t.Paths; var dst = Path.Combine(p.NoCapRoot, "Zalo-26.11.12");
        MakeZalo(p.NoCapRoot, "26.11.12", cap: false); File.WriteAllText(Path.Combine(dst, "Zalo.exe"), "legacy");
        await new ZaloInstall(p, src, null, _ => true).EnsureNoCapCopyAsync();
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(dst, "Zalo.exe")));   // khong bi xoa
        Assert.True(File.Exists(Path.Combine(dst, ZaloInstall.CompleteMarker)));
    }

    [Fact]
    public async Task EnsureCopy_ThrowsWhenNoZalo()
    {
        using var t = new TempRoot();
        await Assert.ThrowsAsync<FileNotFoundException>(() => new ZaloInstall(t.Paths, t.Sub("none"), null, _ => true).EnsureNoCapCopyAsync());
    }

    [Fact]
    public void Prune_KeepsInUseAndNewest_OnlyUnderNoCap()
    {
        using var t = new TempRoot(); var p = t.Paths; var src = t.Sub("zalo");
        MakeZalo(src, "1.0.0");
        foreach (var v in new[] { "26.9.30", "26.10.1", "26.11.12" }) MakeZalo(p.NoCapRoot, v, false);
        var removed = new ZaloInstall(p, src, null, _ => true).PruneOldCopies(new[] { "Zalo-26.9.30" });
        Assert.Equal(new[] { "Zalo-26.10.1" }, removed);
        Assert.True(Directory.Exists(Path.Combine(p.NoCapRoot, "Zalo-26.9.30")));
        Assert.True(Directory.Exists(Path.Combine(p.NoCapRoot, "Zalo-26.11.12")));
        Assert.True(Directory.Exists(Path.Combine(src, "Zalo-1.0.0")));
    }

    // ------------------------------------------------------------ L1: kiem chu ky Zalo.exe

    [Fact]
    public async Task Verify_FailsOnCopy_RecopiesFromVerifiedOriginal_OnceCachedAfter()
    {
        using var t = new TempRoot(); var src = t.Sub("zalo"); MakeZalo(src, "26.11.12");
        var p = t.Paths; var dst = Path.Combine(p.NoCapRoot, "Zalo-26.11.12");
        var origExe = Path.Combine(src, "Zalo-26.11.12", "Zalo.exe");
        var calls = new List<string>();
        var inst = new ZaloInstall(p, src, null, f => { calls.Add(f); return File.ReadAllText(f).StartsWith("exe"); });
        await inst.EnsureNoCapCopyAsync();
        // thay Zalo.exe trong ban sao bang file gia (cung vi tri, doi kich thuoc/noi dung)
        File.WriteAllText(Path.Combine(dst, "Zalo.exe"), "FAKE malware");
        calls.Clear();
        var exe = await inst.EnsureNoCapCopyAsync();
        Assert.Equal("exe 26.11.12", File.ReadAllText(exe));          // da chep lai tu goc
        Assert.NotEmpty(calls);                                       // ban sao gia bi kiem (goc da nho tu lan dau)
        // lan sau: da nho ket qua -> khong goi lai
        calls.Clear();
        await inst.EnsureNoCapCopyAsync();
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Verify_OriginalFails_Throws_AndDoesNotTouchCopy()
    {
        using var t = new TempRoot(); var src = t.Sub("zalo"); MakeZalo(src, "26.11.12");
        var p = t.Paths;
        var inst = new ZaloInstall(p, src, null, _ => false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => inst.EnsureNoCapCopyAsync());
        Assert.False(Directory.Exists(Path.Combine(p.NoCapRoot, "Zalo-26.11.12")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inst.EnsureCapCopyAsync("acc-01"));
    }

    [Fact]
    public async Task Verify_CapCopyFake_IsRebuilt()
    {
        using var t = new TempRoot(); var src = t.Sub("zalo"); MakeZalo(src, "26.11.12");
        var p = t.Paths;
        var inst = new ZaloInstall(p, src, null, f => File.ReadAllText(f).StartsWith("exe"));
        var exe = await inst.EnsureCapCopyAsync("acc-01");
        File.Delete(exe);                                              // lien ket cung: xoa chi bo ban sao
        File.WriteAllText(exe, "FAKE malware");
        var exe2 = await inst.EnsureCapCopyAsync("acc-01");
        Assert.Equal("exe 26.11.12", File.ReadAllText(exe2));
        Assert.Equal("exe 26.11.12", File.ReadAllText(Path.Combine(src, "Zalo-26.11.12", "Zalo.exe")));
    }

    [Theory]
    [InlineData("CN=VNG GROUP JSC, O=VNG GROUP JSC, L=Ho Chi Minh, C=VN", true)]
    [InlineData("CN=x, O=\"VNG Corp\", C=VN", true)]
    [InlineData("CN=VNG GROUP JSC, O=Someone Else, C=VN", false)]   // chi xet truong O=
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SubjectOrgContains_ChecksOrgFieldOnly(string? subject, bool expected) =>
        Assert.Equal(expected, Authenticode.SubjectOrgContains(subject, Authenticode.ZaloSignerOrg));

    [Fact]
    public void DefaultVerifier_RejectsUnsignedFakeExe()
    {
        using var t = new TempRoot();
        var f = t.Sub("Zalo.exe"); File.WriteAllText(f, "not an exe");
        Assert.False(ZaloInstall.VerifyZaloSignature(f));
        Assert.False(ZaloInstall.VerifyZaloSignature(t.Sub("missing.exe")));
    }
}