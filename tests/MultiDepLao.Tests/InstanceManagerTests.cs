using System.Diagnostics;
using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class InstanceManagerTests
{
    private sealed class Ctx : IDisposable
    {
        public TempRoot T = new(); public AppPaths P; public string Exe; public IProcessProbe Probe = new WinProcessProbe();
        public List<InstanceStatus> Events = new();
        public Ctx()
        {
            P = T.Paths;
            var dir = Path.Combine(P.NoCapRoot, "Zalo-1.0.0"); Directory.CreateDirectory(dir);
            Exe = Path.Combine(dir, "cmd.exe");   // tien trinh gia: cmd.exe chay ping (cay 2 tang)
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), Exe);
        }
        public InstanceManager Mgr(IProcessProbe? probe = null, string exeName = "cmd.exe", IGracefulCloser? closer = null)
        {
            var pr = probe ?? Probe;
            var m = new InstanceManager(P, pr, new SafetyGate(P, pr, exeName), null,
                new InstanceManagerOptions
                {
                    ExeName = exeName, Arguments = "/c ping -n 40 127.0.0.1 >nul", ExeResolver = _ => Task.FromResult(Exe),
                    ReadyAfter = TimeSpan.FromMilliseconds(300), GraceTimeout = TimeSpan.FromMilliseconds(300),
                }, null, closer);
            m.StateChanged += Events.Add;
            return m;
        }
        public void Dispose()
        {
            foreach (var d in Directory.Exists(P.ProfilesRoot) ? Directory.GetDirectories(P.ProfilesRoot) : Array.Empty<string>())
                Cleanup(Path.GetFileName(d));
            T.Dispose();
        }
        public void Cleanup(string id)
        {
            var r = PidRecord.TryRead(P.PidFile(id));
            if (r != null && Probe.Get(r.Pid)?.ExePath?.StartsWith(P.NoCapRoot, StringComparison.OrdinalIgnoreCase) == true) Probe.KillTree(r.Pid);
        }
    }

    private static HashSet<int> Pings() => Process.GetProcessesByName("ping").Select(p => p.Id).ToHashSet();

    [Fact]
    public async Task Start_Running_Stop_KillsWholeTree()
    {
        using var c = new Ctx(); using var m = c.Mgr();
        var before = Pings();
        var st = await m.StartAsync("acc-01");
        Assert.Equal(InstanceState.Starting, st.State); Assert.NotNull(st.Pid);
        Assert.True(File.Exists(c.P.PidFile("acc-01")));
        Assert.True(Directory.Exists(Path.Combine(c.P.ProfileDir("acc-01"), "AppData", "Local", "Temp")));
        await Task.Delay(700); m.Poll();
        Assert.Equal(InstanceState.Running, m.GetStatus("acc-01").State);
        var child = Pings().Except(before).ToList();
        Assert.NotEmpty(child);

        var r = await m.StopAsync("acc-01");
        Assert.Equal(StopOutcome.Stopped, r.Outcome);
        Assert.Equal(InstanceState.Stopped, m.GetStatus("acc-01").State);
        Assert.Null(c.Probe.Get(st.Pid!.Value));
        await Task.Delay(300);
        Assert.Empty(Pings().Intersect(child));
        Assert.False(File.Exists(c.P.PidFile("acc-01")));
        Assert.Contains(c.Events, e => e.State == InstanceState.Stopping);
    }

    [Fact]
    public async Task MarkWindowReady_PromotesStarting()
    {
        using var c = new Ctx(); using var m = c.Mgr();
        await m.StartAsync("acc-01");
        m.MarkWindowReady("acc-01");
        Assert.Equal(InstanceState.Running, m.GetStatus("acc-01").State);
        await m.StopAsync("acc-01");
    }

    [Fact]
    public async Task Adopt_FromNewManager_ThenStop()
    {
        using var c = new Ctx(); using var m1 = c.Mgr();
        var st = await m1.StartAsync("acc-01");
        using var m2 = c.Mgr();
        Assert.Equal(InstanceState.Stopped, m2.GetStatus("acc-01").State);
        Assert.Equal(new[] { "acc-01" }, m2.AdoptRunning());
        Assert.Equal(InstanceState.Running, m2.GetStatus("acc-01").State);
        Assert.Equal(st.Pid, m2.GetStatus("acc-01").Pid);
        // Start khi dang chay khong khoi chay trung
        var again = await m2.StartAsync("acc-01");
        Assert.Equal(st.Pid, again.Pid);
        Assert.Equal(StopOutcome.Stopped, (await m2.StopAsync("acc-01")).Outcome);
    }

    [Fact]
    public async Task Stop_RefusedWhenGateRefuses_ProcessSurvives()
    {
        using var c = new Ctx(); using var m = c.Mgr();
        var st = await m.StartAsync("acc-01");
        // Cong tu choi: manager cho phep ten exe khac (mo phong tien trinh la)
        using var strict = c.Mgr(exeName: "Zalo.exe");
        var r = await strict.StopAsync("acc-01");
        Assert.Equal(StopOutcome.Refused, r.Outcome);
        Assert.NotNull(c.Probe.Get(st.Pid!.Value));
        Assert.Equal(StopOutcome.Stopped, (await m.StopAsync("acc-01")).Outcome);
    }

    [Fact]
    public async Task Stop_UsesGracefulCloserFirst()
    {
        using var c = new Ctx();
        var closer = new KillingCloser(c.Probe);
        using var m = c.Mgr(closer: closer);
        var st = await m.StartAsync("acc-01");
        var r = await m.StopAsync("acc-01");
        Assert.Equal(StopOutcome.Stopped, r.Outcome);
        Assert.Equal(st.Pid, closer.ClosedPid);
    }

    private sealed class KillingCloser : IGracefulCloser
    {
        private readonly IProcessProbe _p; public int ClosedPid;
        public KillingCloser(IProcessProbe p) => _p = p;
        public bool TryClose(int pid) { ClosedPid = pid; _p.KillTree(pid); return true; }
    }

    [Fact]
    public async Task Stop_FakeProbe_DoesNotKillWhenIdentityMismatch()
    {
        using var t = new TempRoot(); var p = t.Paths; var fake = new FakeProbe();
        Directory.CreateDirectory(p.ProfileDir("acc-01"));
        var created = DateTime.UtcNow;
        fake.Procs[500] = new ProcessInfo(500, "Zalo.exe", Path.Combine(p.NoCapRoot, "Zalo-1", "Zalo.exe"), created.AddHours(1));
        PidRecord.Write(p.PidFile("acc-01"), 500, created);
        using var m = new InstanceManager(p, fake, new SafetyGate(p, fake), null);
        var r = await m.StopAsync("acc-01");
        Assert.Equal(StopOutcome.Refused, r.Outcome);
        Assert.Empty(fake.Killed);
    }

    [Fact]
    public async Task Poll_DetectsSelfDeath()
    {
        using var c = new Ctx(); using var m = c.Mgr();
        var st = await m.StartAsync("acc-01");
        await Task.Delay(700); m.Poll();
        c.Probe.KillTree(st.Pid!.Value);
        await Task.Delay(300); m.Poll();
        Assert.Equal(InstanceState.Stopped, m.GetStatus("acc-01").State);
    }
}
