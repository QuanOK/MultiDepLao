using MultiDepLao.Core;

namespace MultiDepLao.Tests;

[Xunit.Collection("Loc")]
public class CallOwnerServiceTests
{
    public CallOwnerServiceTests() => Loc.Set("vi");

    /// <summary>Mo phong Zalo: instance khoi dong khi chua co pipe thi tao pipe (sau 30 ms) va giu den khi bi tat.</summary>
    private sealed class FakeWorld : IInstanceControl, ICallPipeProbe
    {
        private readonly object _l = new();
        public readonly List<string> Events = new();
        public readonly List<string> Running = new();
        public string? PipeHolder;           // id hoac "EXT"
        public bool PipeUp;
        public int InFlight, MaxInFlight;
        public bool PipeNeverAppears;
        public bool RefuseStopOf; public string? RefuseId;

        public bool AnyCallPipe() { lock (_l) return PipeUp; }
        public async Task<bool> WaitAsync(bool present, TimeSpan timeout, CancellationToken ct)
        {
            var end = DateTime.UtcNow + timeout;
            while (true)
            {
                if (AnyCallPipe() == present) return true;
                if (DateTime.UtcNow >= end) return false;
                await Task.Delay(5, ct);
            }
        }
        public bool IsRunning(string id) { lock (_l) return Running.Contains(id); }
        public IReadOnlyList<string> RunningOldestFirst() { lock (_l) return Running.ToList(); }

        public async Task<InstanceStatus> StartAsync(string id, CancellationToken ct = default)
        {
            int n = Interlocked.Increment(ref InFlight);
            lock (_l) MaxInFlight = Math.Max(MaxInFlight, n);
            try
            {
                lock (_l)
                {
                    if (Running.Contains(id)) return new(id, InstanceState.Running, 1, null);
                    Events.Add("start " + id); Running.Add(id);
                    if (!PipeUp && !PipeNeverAppears) { PipeHolder = id; _ = Task.Delay(30).ContinueWith(_ => { lock (_l) { if (PipeHolder == id) PipeUp = true; } }); }
                }
                await Task.Delay(20, ct);
                return new(id, InstanceState.Starting, 1, null);
            }
            finally { Interlocked.Decrement(ref InFlight); }
        }
        public Task<StopResult> StopAsync(string id, CancellationToken ct = default)
        {
            lock (_l)
            {
                if (RefuseStopOf && id == RefuseId) return Task.FromResult(new StopResult(StopOutcome.Refused, "test"));
                if (!Running.Remove(id)) return Task.FromResult(new StopResult(StopOutcome.AlreadyStopped, null));
                Events.Add("stop " + id);
                if (PipeHolder == id) { PipeHolder = null; PipeUp = false; }
                return Task.FromResult(new StopResult(StopOutcome.Stopped, null));
            }
        }
    }

    private sealed class Ctx : IDisposable
    {
        public readonly TempRoot T = new();
        public readonly FakeWorld W = new();
        public readonly ProfileStore Profiles;
        public readonly CallOwnerService Svc;
        public Ctx(string? owner = null, params string[] autoStart)
        {
            var p = T.Paths; var cs = new ConfigStore(p);
            var cfg = new AppConfig { CallOwnerId = owner };
            foreach (var id in new[] { "a", "b", "c" })
                cfg.Accounts.Add(new AccountConfig { Id = id, DisplayName = id, Order = id[0] - 'a', AutoStart = autoStart.Contains(id) });
            cs.Save(cfg);
            Profiles = new ProfileStore(p, cs, W.IsRunning);
            Svc = new CallOwnerService(W, Profiles, W, new CallOwnerOptions
            {
                PipeAppearTimeout = TimeSpan.FromSeconds(2), PipeGoneAfterStop = TimeSpan.FromMilliseconds(100), PipeGoneFinal = TimeSpan.FromMilliseconds(200),
            });
        }
        public void Dispose() => T.Dispose();
    }

    [Fact]
    public async Task FirstAccount_BecomesOwner_AndConfigWritten()
    {
        using var c = new Ctx();
        await c.Svc.StartAsync("b");
        Assert.Equal("b", c.Svc.ActualOwnerId);
        Assert.Equal("b", c.Profiles.Config.CallOwnerId);
        Assert.Equal(new CallOwnerState(CallOwnerKind.Known, "b"), c.Svc.GetState());
        await c.Svc.StartAsync("a");   // da co pipe: khong doi chu
        Assert.Equal("b", c.Svc.ActualOwnerId);
    }

    [Fact]
    public async Task Start_OwnerFirst_WhenNoPipe()
    {
        using var c = new Ctx(owner: "b");
        await c.Svc.StartAsync("a");
        Assert.Equal(new[] { "start b", "start a" }, c.W.Events);
        Assert.Equal("b", c.Svc.ActualOwnerId);
    }

    [Fact]
    public async Task AutoStart_OwnerFirstThenOthers()
    {
        using var c = new Ctx(owner: "c", autoStart: new[] { "a", "b" });   // chu khong AutoStart nhung co tai khoan khac AutoStart
        await c.Svc.StartAutoStartAccountsAsync();
        Assert.Equal(new[] { "start c", "start a", "start b" }, c.W.Events);
        Assert.Equal("c", c.Svc.ActualOwnerId);
    }

    [Fact]
    public async Task AutoStart_None_DoesNothing()
    {
        using var c = new Ctx(owner: "c");
        await c.Svc.StartAutoStartAccountsAsync();
        Assert.Empty(c.W.Events);
    }

    [Fact]
    public async Task SwitchTo_TwoRunning_StopsOwner_StartsTarget_ReopensOld()
    {
        using var c = new Ctx();
        await c.Svc.StartAsync("a"); await c.Svc.StartAsync("b");
        c.W.Events.Clear();
        Assert.Equal(new[] { "a" }, c.Svc.PreviewSwitch("b"));
        var r = await c.Svc.SwitchToAsync("b");
        Assert.True(r.Success, r.Reason);
        Assert.Equal(new[] { "stop a", "stop b", "start b", "start a" }, c.W.Events);
        Assert.Equal(new[] { "a" }, r.Restarted);
        Assert.Equal("b", c.Svc.ActualOwnerId);
        Assert.Equal("b", c.Profiles.Config.CallOwnerId);
        // chuyen nguoc lai
        c.W.Events.Clear();
        r = await c.Svc.SwitchToAsync("a");
        Assert.True(r.Success, r.Reason);
        Assert.Equal(new[] { "stop b", "stop a", "start a", "start b" }, c.W.Events);
        Assert.Equal("a", c.Profiles.Config.CallOwnerId);
    }

    [Fact]
    public async Task SwitchTo_CurrentOwner_IsNoOp()
    {
        using var c = new Ctx();
        await c.Svc.StartAsync("a"); await c.Svc.StartAsync("b");
        c.W.Events.Clear();
        var r = await c.Svc.SwitchToAsync("a");
        Assert.True(r.Success); Assert.Empty(r.Restarted); Assert.Empty(c.W.Events);
        Assert.Empty(c.Svc.PreviewSwitch("a"));
    }

    [Fact]
    public async Task SwitchTo_NotRunningTarget_JustStartsIt()
    {
        using var c = new Ctx();
        await c.Svc.StartAsync("a");
        c.W.Events.Clear();
        var r = await c.Svc.SwitchToAsync("c");
        Assert.True(r.Success, r.Reason);
        Assert.Equal(new[] { "stop a", "start c", "start a" }, c.W.Events);
        Assert.Equal("c", c.Profiles.Config.CallOwnerId);
    }

    [Fact]
    public async Task SwitchTo_ExternalPipe_ReopensEverything_ReturnsError()
    {
        using var c = new Ctx();
        c.W.PipeUp = true; c.W.PipeHolder = "EXT";   // Zalo ngoai giu pipe
        await c.Svc.StartAsync("a"); await c.Svc.StartAsync("b");
        Assert.Equal(CallOwnerKind.Unknown, c.Svc.GetState().Kind);
        c.W.Events.Clear();
        var r = await c.Svc.SwitchToAsync("b");
        Assert.False(r.Success);
        Assert.Contains("ngoài " + AppInfo.DisplayName, r.Reason);
        Assert.Equal(new[] { "stop a", "stop b", "start a", "start b" }, c.W.Events);
        Assert.Contains("a", c.W.Running); Assert.Contains("b", c.W.Running);
        Assert.Null(c.Profiles.Config.CallOwnerId);
    }

    [Fact]
    public async Task SwitchTo_StopRefused_Aborts_NothingLost()
    {
        using var c = new Ctx();
        await c.Svc.StartAsync("a"); await c.Svc.StartAsync("b");
        c.W.RefuseStopOf = true; c.W.RefuseId = "a";
        var r = await c.Svc.SwitchToAsync("b");
        Assert.False(r.Success);
        Assert.Contains("a", c.W.Running); Assert.Contains("b", c.W.Running);
    }

    [Fact]
    public async Task Start_PipeNeverAppears_DoesNotSetOwner()
    {
        using var c = new Ctx();
        c.W.PipeNeverAppears = true;
        await c.Svc.StartAsync("a");
        Assert.Null(c.Svc.ActualOwnerId);
        Assert.Null(c.Profiles.Config.CallOwnerId);
    }

    [Fact]
    public void State_External_WhenPipeButNoInstance()
    {
        using var c = new Ctx();
        c.W.PipeUp = true;
        Assert.Equal(CallOwnerKind.External, c.Svc.GetState().Kind);
    }

    [Fact]
    public void Initialize_AdoptsConfigOwner_WhenRunningAndPipe()
    {
        using var c = new Ctx(owner: "b");
        c.W.Running.AddRange(new[] { "a", "b" }); c.W.PipeUp = true;
        Assert.Equal(CallOwnerKind.Unknown, c.Svc.GetState().Kind);
        c.Svc.Initialize();
        Assert.Equal(new CallOwnerState(CallOwnerKind.Known, "b"), c.Svc.GetState());
    }

    [Fact]
    public async Task OwnerStopped_ActualOwnerBecomesNull_NextStartTakesOver()
    {
        using var c = new Ctx();
        await c.Svc.StartAsync("a");
        await c.W.StopAsync("a");
        Assert.Null(c.Svc.ActualOwnerId);
        Assert.Equal(CallOwnerKind.None, c.Svc.GetState().Kind);
        await c.Svc.StartAsync("b");   // config van tro a (a khong chay -> bat a truoc)
        Assert.Equal("a", c.Svc.ActualOwnerId);
    }

    [Fact]
    public async Task ConcurrentStarts_AreSerialized()
    {
        using var c = new Ctx();
        await Task.WhenAll(c.Svc.StartAsync("a"), c.Svc.StartAsync("b"), c.Svc.StartAsync("c"));
        Assert.Equal(1, c.W.MaxInFlight);
        Assert.Equal("a", c.Svc.ActualOwnerId);   // tai khoan dau tien giu pipe, 2 cai sau khong tranh
        Assert.Equal(new[] { "start a", "start b", "start c" }, c.W.Events);
    }
}
