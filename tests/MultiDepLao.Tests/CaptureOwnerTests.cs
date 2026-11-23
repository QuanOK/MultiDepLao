using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class CaptureOwnerTests
{
    private static readonly DateTime Created = new(2026, 11, 18, 8, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------ cau hinh
    [Fact]
    public void Config_RoundTrip_AndBackwardCompatible()
    {
        using var t = new TempRoot(); var p = t.Paths; var cs = new ConfigStore(p);
        Directory.CreateDirectory(p.Root);
        File.WriteAllText(p.ConfigFile, "{\"accounts\":[],\"callOwnerId\":\"a\"}");   // file cu: khong co captureOwnerId
        Assert.Null(cs.Load().CaptureOwnerId);

        var store = new ProfileStore(p, cs, _ => false);
        store.Add("A");
        store.SetCaptureOwner("acc-01");
        Assert.Equal("acc-01", new ConfigStore(p).Load().CaptureOwnerId);
        store.SetCaptureOwner(null);
        Assert.Null(new ConfigStore(p).Load().CaptureOwnerId);
    }

    [Fact]
    public void Delete_ClearsCaptureOwner()
    {
        using var t = new TempRoot(); var p = t.Paths;
        var store = new ProfileStore(p, new ConfigStore(p), _ => false);
        var a = store.Add("A"); store.SetCaptureOwner(a.Id);
        store.Delete(a.Id);
        Assert.Null(store.Config.CaptureOwnerId);
    }

    // ------------------------------------------------------------ SafetyGate
    private sealed class GateCtx : IDisposable
    {
        public TempRoot T = new(); public AppPaths P; public FakeProbe Probe = new(); public SafetyGate Gate;
        public GateCtx()
        {
            P = T.Paths; Gate = new SafetyGate(P, Probe);
            foreach (var id in new[] { "acc-01", "acc-02" }) Directory.CreateDirectory(P.ProfileDir(id));
        }
        public Verification Check(string id, string exe)
        {
            Probe.Procs[100] = new ProcessInfo(100, "Zalo.exe", exe, Created);
            PidRecord.Write(P.PidFile(id), 100, Created);
            return Gate.Verify(id);
        }
        public void Dispose() => T.Dispose();
    }

    [Fact]
    public void SafetyGate_AcceptsCapDir_OnlyForSameId()
    {
        using var c = new GateCtx();
        var exe01 = Path.Combine(c.P.CapRoot, "acc-01", "Zalo-1.0.0", "Zalo.exe");
        Assert.True(c.Check("acc-01", exe01).Ok);
        var v = c.Check("acc-02", exe01);                       // cap cua acc-01 khong duoc dung cho acc-02
        Assert.Equal(VerifyFailure.OutsideRoot, v.Failure);
    }

    [Fact]
    public void SafetyGate_RejectsCapRootItself_AndSiblingPrefix()
    {
        using var c = new GateCtx();
        Assert.Equal(VerifyFailure.OutsideRoot, c.Check("acc-01", Path.Combine(c.P.CapRoot, "Zalo.exe")).Failure);
        Assert.Equal(VerifyFailure.OutsideRoot, c.Check("acc-01", Path.Combine(c.P.CapRoot, "acc-01-evil", "Zalo.exe")).Failure);
        Assert.Equal(VerifyFailure.OutsideRoot, c.Check("acc-01", Path.Combine(c.P.CapRoot, "acc-01", "..", "acc-02", "Zalo.exe")).Failure);
    }

    // ------------------------------------------------------------ chon exe
    [Theory]
    [InlineData("a", "a", null, ExeKind.Cap)]
    [InlineData("A", "a", null, ExeKind.Cap)]
    [InlineData("b", "a", null, ExeKind.NoCap)]
    [InlineData("a", null, null, ExeKind.NoCap)]
    [InlineData("a", "a", "Zalo ngoai", ExeKind.NoCap)]
    public void ExeSelector_Choose(string id, string? owner, string? guard, ExeKind expected) =>
        Assert.Equal(expected, ExeSelector.Choose(id, owner, guard));

    [Fact]
    public async Task InstanceManager_PicksCapResolver_OnlyForOwner()
    {
        using var t = new TempRoot(); var p = t.Paths; var probe = new FakeProbe();
        string? picked = null;
        var opt = new InstanceManagerOptions
        {
            CaptureOwner = () => "acc-01",
            ExeResolver = _ => Task.FromResult(Path.Combine(t.Path, "no-such", "Zalo.exe")),
            CapExeResolver = (id, _) => { picked = id; return Task.FromResult(Path.Combine(t.Path, "no-such-cap", "Zalo.exe")); },
        };
        var mgr = new InstanceManager(p, probe, new SafetyGate(p, probe), null, opt);
        await mgr.StartAsync("acc-02");   // Process.Start se loi (khong co exe) nhung chi can biet resolver nao duoc goi
        Assert.Null(picked);
        await mgr.StartAsync("acc-01");
        Assert.Equal("acc-01", picked);
    }

    // ------------------------------------------------------------ ban sao cap
    private static string MakeOriginal(string root, string ver)
    {
        var d = Path.Combine(root, "Zalo-" + ver);
        Directory.CreateDirectory(Path.Combine(d, "plugins", "capture"));
        Directory.CreateDirectory(Path.Combine(d, "res"));
        File.WriteAllText(Path.Combine(d, "Zalo.exe"), "exe");
        File.WriteAllText(Path.Combine(d, "plugins", "capture", "ZaloCap.exe"), "cap");
        File.WriteAllText(Path.Combine(d, "res", "a.dll"), "dll");
        File.WriteAllText(Path.Combine(d, "res", "b.json"), "json");
        return d;
    }

    [Fact]
    public async Task EnsureCapCopy_HasZaloCap_RealCopyForData_RestoresOff_PrunesOtherVersions()
    {
        using var t = new TempRoot(); var p = t.Paths;
        var zroot = t.Sub("Programs");
        var orig = MakeOriginal(zroot, "26.11.12");
        var inst = new ZaloInstall(p, zroot, null, _ => true);
        var exe = await inst.EnsureCapCopyAsync("acc-01");
        var dir = Path.GetDirectoryName(exe)!;
        Assert.Equal(Path.Combine(p.CapRoot, "acc-01", "Zalo-26.11.12"), dir);
        Assert.True(File.Exists(ZaloInstall.CapExeIn(dir)));
        Assert.Equal("dll", File.ReadAllText(Path.Combine(dir, "res", "a.dll")));
        Assert.True(File.Exists(Path.Combine(dir, ZaloInstall.CompleteMarker)));
        // file .json la ban chep that: sua ban sao khong anh huong goc
        File.WriteAllText(Path.Combine(dir, "res", "b.json"), "changed");
        Assert.Equal("json", File.ReadAllText(Path.Combine(orig, "res", "b.json")));

        // ZaloCap.exe.off -> doi lai
        var cap = ZaloInstall.CapExeIn(dir);
        File.Move(cap, cap + ".off");
        await inst.EnsureCapCopyAsync("acc-01");
        Assert.True(File.Exists(cap)); Assert.False(File.Exists(cap + ".off"));

        // ban cai moi -> ban cu cua id nay bi don
        MakeOriginal(zroot, "26.12.1");
        await inst.EnsureCapCopyAsync("acc-01");
        Assert.False(Directory.Exists(dir));
        Assert.True(Directory.Exists(Path.Combine(p.CapRoot, "acc-01", "Zalo-26.12.1")));
    }

    [Fact]
    public async Task PruneCapCopies_KeepsOwnerAndRunning_OnlyUnderCapRoot()
    {
        using var t = new TempRoot(); var p = t.Paths;
        var zroot = t.Sub("Programs"); MakeOriginal(zroot, "26.11.12");
        var inst = new ZaloInstall(p, zroot, null, _ => true);
        foreach (var id in new[] { "a", "b", "c" }) await inst.EnsureCapCopyAsync(id);
        var outside = t.Sub("keep"); Directory.CreateDirectory(outside);
        var removed = inst.PruneCapCopies("a", id => id == "b");
        Assert.Equal(new[] { "c" }, removed);
        Assert.True(Directory.Exists(p.CapDir("a"))); Assert.True(Directory.Exists(p.CapDir("b"))); Assert.False(Directory.Exists(p.CapDir("c")));
        Assert.True(Directory.Exists(outside));
    }

    // ------------------------------------------------------------ chuyen quyen
    private sealed class FakeOps : ICaptureProcessOps
    {
        public List<ProcessInfo> Caps = new();
        public Dictionary<int, HashSet<int>> Tree = new();
        public List<string> Events;
        public Action<ProcessInfo>? OnKill;
        public FakeOps(List<string> ev) => Events = ev;
        public IReadOnlyList<ProcessInfo> FindCapProcesses() => Caps.ToList();
        public ISet<int> DescendantsOf(int pid) => Tree.TryGetValue(pid, out var s) ? s : new HashSet<int>();
        public bool Kill(ProcessInfo p) { Events.Add("kill " + p.Pid); OnKill?.Invoke(p); Caps.Remove(p); return true; }
    }

    private sealed class FakeCtl : IInstanceControl
    {
        public List<string> Running = new(); public List<string> Events;
        public FakeCtl(List<string> ev) => Events = ev;
        public bool IsRunning(string id) => Running.Contains(id);
        public IReadOnlyList<string> RunningOldestFirst() => Running.ToList();
        public Task<StopResult> StopAsync(string id, CancellationToken ct = default)
        { Events.Add("stop " + id); Running.Remove(id); return Task.FromResult(new StopResult(StopOutcome.Stopped, null)); }
        public Task<InstanceStatus> StartAsync(string id, CancellationToken ct = default)
        { Events.Add("start " + id); Running.Add(id); return Task.FromResult(new InstanceStatus(id, InstanceState.Starting, 1, null)); }
    }

    private sealed class SvcCtx : IDisposable
    {
        public TempRoot T = new(); public AppPaths P; public FakeProbe Probe = new();
        public List<string> Events = new(); public FakeCtl Ctl; public FakeOps Ops; public ProfileStore Profiles; public CaptureOwnerService Svc;
        public SvcCtx(string? owner)
        {
            P = T.Paths; Ctl = new(Events); Ops = new(Events);
            var cs = new ConfigStore(P);
            var cfg = new AppConfig { CaptureOwnerId = owner };
            foreach (var id in new[] { "a", "b" }) cfg.Accounts.Add(new AccountConfig { Id = id, DisplayName = id });
            cs.Save(cfg);
            Profiles = new ProfileStore(P, cs, Ctl.IsRunning);
            Svc = new CaptureOwnerService(Ctl, Profiles, P, new SafetyGate(P, Probe), Ops);
        }
        /// <summary>Tai khoan dang chay tu cap (co ZaloCap.exe that tren dia) voi main pid va ZaloCap con.</summary>
        public string RunFromCap(string id, int mainPid, int capPid, string? capExeOverride = null, bool inTree = true)
        {
            var dir = Path.Combine(P.CapRoot, id, "Zalo-1");
            Directory.CreateDirectory(Path.Combine(dir, "plugins", "capture"));
            File.WriteAllText(Path.Combine(dir, "Zalo.exe"), "x");
            var capExe = ZaloInstall.CapExeIn(dir); File.WriteAllText(capExe, "c");
            Directory.CreateDirectory(P.ProfileDir(id));
            Probe.Procs[mainPid] = new ProcessInfo(mainPid, "Zalo.exe", Path.Combine(dir, "Zalo.exe"), Created);
            PidRecord.Write(P.PidFile(id), mainPid, Created);
            Ctl.Running.Add(id);
            Ops.Caps.Add(new ProcessInfo(capPid, "ZaloCap.exe", capExeOverride ?? capExe, Created));
            Ops.Tree[mainPid] = inTree ? new HashSet<int> { capPid } : new HashSet<int>();
            return capExe;
        }
        public void Dispose() => T.Dispose();
    }

    [Fact]
    public async Task Swap_RenamesBeforeKill_PersistsAndRestartsNewOwner()
    {
        using var c = new SvcCtx("a");
        var capExe = c.RunFromCap("a", 100, 101);
        c.Ctl.Running.Add("b");
        c.Ops.OnKill = _ => { Assert.False(File.Exists(capExe)); Assert.True(File.Exists(capExe + ".off")); c.Events.Add("renamed-before-kill"); };
        var r = await c.Svc.SetCaptureOwnerAsync("b");
        Assert.True(r.Success, r.Reason);
        Assert.Equal(new[] { "kill 101", "renamed-before-kill", "stop b", "start b" }, c.Events);
        Assert.Equal("b", c.Profiles.Config.CaptureOwnerId);
        Assert.Equal(new[] { "a" }, r.Released); Assert.True(r.Restarted);
        Assert.True(c.Ctl.IsRunning("a"));                  // chu cu khong bi khoi dong lai
        Assert.DoesNotContain("stop a", c.Events);
    }

    [Fact]
    public async Task Swap_NeverKillsOutsideOldOwnerCapDir_OrOutsideItsTree()
    {
        using var c = new SvcCtx("a");
        c.RunFromCap("a", 100, 101);
        // ZaloCap cua chu cu nhung nam o cay khac (khong phai hau due cua main a)
        c.Ops.Caps.Add(new ProcessInfo(102, "ZaloCap.exe", Path.Combine(c.P.CapRoot, "a", "Zalo-1", "plugins", "capture", "ZaloCap.exe"), Created));
        // hau due cua main a nhung exe nam duoi CapRoot cua id khac
        c.Ops.Tree[100].Add(103);
        c.Ops.Caps.Add(new ProcessInfo(103, "ZaloCap.exe", Path.Combine(c.P.CapRoot, "b", "Zalo-1", "plugins", "capture", "ZaloCap.exe"), Created));
        // hau due cua main a nhung exe nam ngoai CapRoot
        c.Ops.Tree[100].Add(104);
        c.Ops.Caps.Add(new ProcessInfo(104, "ZaloCap.exe", @"C:\Users\x\AppData\Local\Programs\Zalo\Zalo-1\plugins\capture\ZaloCap.exe", Created));
        await c.Svc.SetCaptureOwnerAsync(null);   // null: khong kiem tra foreign, chi thu hoi chu cu
        Assert.Equal(new[] { "kill 101" }, c.Events);
    }

    [Fact]
    public async Task Refuses_WhenForeignZaloCapRunning_AndChangesNothing()
    {
        using var c = new SvcCtx("a");
        var capExe = c.RunFromCap("a", 100, 101);
        c.Ops.Caps.Add(new ProcessInfo(900, "ZaloCap.exe", @"C:\Users\x\AppData\Local\Programs\Zalo\Zalo-1\plugins\capture\ZaloCap.exe", Created));
        var r = await c.Svc.SetCaptureOwnerAsync("b");
        Assert.False(r.Success);
        Assert.Contains("ngoai MultiDepLao", r.Reason);
        Assert.Empty(c.Events);
        Assert.True(File.Exists(capExe));
        Assert.Equal("a", c.Profiles.Config.CaptureOwnerId);
    }

    [Fact]
    public async Task SetNull_ReleasesOldOwnerOnly_NoRestart()
    {
        using var c = new SvcCtx("a");
        var capExe = c.RunFromCap("a", 100, 101);
        var r = await c.Svc.SetCaptureOwnerAsync(null);
        Assert.True(r.Success); Assert.False(r.Restarted);
        Assert.Equal(new[] { "kill 101" }, c.Events);
        Assert.True(File.Exists(capExe + ".off"));
        Assert.Null(c.Profiles.Config.CaptureOwnerId);
    }

    [Fact]
    public async Task NewOwnerNotRunning_JustPersists()
    {
        using var c = new SvcCtx(null);
        var r = await c.Svc.SetCaptureOwnerAsync("b");
        Assert.True(r.Success); Assert.False(r.Restarted); Assert.Empty(c.Events);
        Assert.Equal("b", c.Profiles.Config.CaptureOwnerId);
    }

    [Fact]
    public async Task RestartOverride_Handled_ReplacesPlainStopStart()
    {
        using var c = new SvcCtx(null);
        c.Ctl.Running.Add("b");
        c.Svc.RestartOverride = (id, _) => { c.Events.Add("hook " + id); return Task.FromResult<(bool, string?)>((true, null)); };
        var r = await c.Svc.SetCaptureOwnerAsync("b");
        Assert.True(r.Success, r.Reason); Assert.True(r.Restarted);
        Assert.Equal(new[] { "hook b" }, c.Events);
    }

    [Fact]
    public async Task RestartOverride_NotHandled_FallsBackToPlain_AndErrorFails()
    {
        using var c = new SvcCtx(null);
        c.Ctl.Running.Add("b");
        c.Svc.RestartOverride = (_, _) => Task.FromResult<(bool, string?)>((false, null));
        Assert.True((await c.Svc.SetCaptureOwnerAsync("b")).Success);
        Assert.Equal(new[] { "stop b", "start b" }, c.Events);

        using var d = new SvcCtx(null);
        d.Ctl.Running.Add("b");
        d.Svc.RestartOverride = (_, _) => Task.FromResult<(bool, string?)>((true, "loi gia"));
        var r = await d.Svc.SetCaptureOwnerAsync("b");
        Assert.False(r.Success); Assert.Contains("loi gia", r.Reason);
    }

    [Fact]
    public async Task UnknownAccount_Fails()
    {
        using var c = new SvcCtx(null);
        Assert.False((await c.Svc.SetCaptureOwnerAsync("zzz")).Success);
    }

    [Fact]
    public async Task OldOwnerUnverifiable_AbortsWithoutKilling()
    {
        using var c = new SvcCtx("a");
        var capExe = c.RunFromCap("a", 100, 101);
        File.Delete(c.P.PidFile("a"));                  // main khong xac minh duoc
        var r = await c.Svc.SetCaptureOwnerAsync("b");
        Assert.False(r.Success);
        Assert.Empty(c.Events);
        Assert.True(File.Exists(capExe));
        Assert.Equal("a", c.Profiles.Config.CaptureOwnerId);
    }
}


