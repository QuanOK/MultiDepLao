using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class MemoryGovernorTests
{
    private sealed class FakeCtl : IInstanceControl, ICallPipeProbe
    {
        public readonly List<string> Running = new();
        public readonly List<string> Stops = new();
        public bool PipeUp;
        public bool IsRunning(string id) => Running.Contains(id);
        public IReadOnlyList<string> RunningOldestFirst() => Running.ToList();
        public Task<InstanceStatus> StartAsync(string id, CancellationToken ct = default) { Running.Add(id); return Task.FromResult(new InstanceStatus(id, InstanceState.Running, 1, null)); }
        public Task<StopResult> StopAsync(string id, CancellationToken ct = default)
        {
            if (!Running.Remove(id)) return Task.FromResult(new StopResult(StopOutcome.AlreadyStopped, null));
            Stops.Add(id); return Task.FromResult(new StopResult(StopOutcome.Stopped, null));
        }
        public bool AnyCallPipe() => PipeUp;
        public Task<bool> WaitAsync(bool present, TimeSpan timeout, CancellationToken ct) => Task.FromResult(PipeUp == present);
    }

    private sealed class FakeMem : ISystemMemory { public long TotalPhysicalBytes { get; set; } }

    private sealed class Ctx : IDisposable
    {
        public readonly TempRoot T = new();
        public readonly FakeCtl W = new();
        public readonly FakeMem Mem = new();
        public readonly ProfileStore Profiles;
        public readonly CallOwnerService Call;
        public readonly MemoryGovernor Gov;
        public DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public Ctx(int gb = 16, int? max = null, int? autoSleep = null, string? owner = null, string? pinned = null, int count = 5)
        {
            Mem.TotalPhysicalBytes = gb * 1024L * 1024 * 1024;
            var cs = new ConfigStore(T.Paths);
            var cfg = new AppConfig { MaxRunning = max, AutoSleepMinutes = autoSleep, CallOwnerId = owner };
            for (int i = 0; i < count; i++)
            {
                var id = ((char)('a' + i)).ToString();
                cfg.Accounts.Add(new AccountConfig { Id = id, DisplayName = id, Order = i, Pinned = id == pinned });
            }
            cs.Save(cfg);
            Profiles = new ProfileStore(T.Paths, cs, W.IsRunning);
            Call = new CallOwnerService(W, Profiles, W);
            Gov = new MemoryGovernor(W, Profiles, Call, Mem, null, () => Now);
        }
        public void Run(params string[] ids) { foreach (var i in ids) { W.Running.Add(i); Gov.NoteSelected(i); Now = Now.AddMinutes(1); } }
        public void Dispose() => T.Dispose();
    }

    [Theory]
    [InlineData(16, 8)]
    [InlineData(8, 2)]
    [InlineData(4, 2)]
    [InlineData(32, 18)]
    public void EffectiveMax_Formula(int gb, int expected)
    {
        using var c = new Ctx(gb);
        Assert.Equal(expected, c.Gov.EffectiveMax);
    }

    [Fact]
    public void EffectiveMax_ConfigOverride_MinOne()
    {
        using var c = new Ctx(16, max: 1);
        Assert.Equal(1, c.Gov.EffectiveMax);
        c.Profiles.SetMaxRunning(0);
        Assert.Equal(1, c.Gov.EffectiveMax);
    }

    [Fact]
    public void Plan_NoRoomNeeded_Empty()
    {
        using var c = new Ctx(max: 3);
        c.Run("a", "b");
        var p = c.Gov.PlanRoom(new[] { "c" }, "c");
        Assert.Empty(p.ToSleep); Assert.False(p.Insufficient);
    }

    [Fact]
    public void Plan_PicksLeastRecentlyUsed()
    {
        using var c = new Ctx(max: 3);
        c.Run("a", "b", "c");
        c.Gov.NoteSelected("a");   // a moi dung -> b la cu nhat
        c.Now = c.Now.AddMinutes(1);
        var p = c.Gov.PlanRoom(new[] { "d" }, "d");
        Assert.Equal(new[] { "b" }, p.ToSleep);
    }

    [Fact]
    public void Plan_ExcludesPinnedOwnerSelected()
    {
        using var c = new Ctx(max: 4, owner: "a", pinned: "b");
        c.Run("a", "b", "c", "d");   // a chu goi, b ghim, c dang chon -> chi d
        var p = c.Gov.PlanRoom(new[] { "e" }, "c");
        Assert.Equal(new[] { "d" }, p.ToSleep);
    }

    [Fact]
    public void Plan_ExcludesActualCallOwner()
    {
        using var c = new Ctx(max: 2, owner: "a");
        c.W.PipeUp = true;
        c.Run("a", "b");
        c.Call.Initialize();
        Assert.Equal("a", c.Call.ActualOwnerId);
        var p = c.Gov.PlanRoom(new[] { "c" }, "c");
        Assert.Equal(new[] { "b" }, p.ToSleep);
    }

    [Fact]
    public void Plan_Insufficient_WhenAllExcluded()
    {
        using var c = new Ctx(max: 2, owner: "a", pinned: "b");
        c.Run("a", "b");
        var p = c.Gov.PlanRoom(new[] { "c" }, "c");
        Assert.True(p.Insufficient); Assert.Equal(1, p.Shortfall); Assert.Empty(p.ToSleep);
    }

    [Fact]
    public async Task EnsureRoom_Insufficient_StopsNothingUnlessForced()
    {
        using var c = new Ctx(max: 3, pinned: "a");
        c.Run("a", "b", "c");
        // can 2 cho trong, chi co b,c khong bi loai? chon c dang chon -> ung vien chi b
        var r = await c.Gov.EnsureRoomAsync(new[] { "d", "e" }, "c");
        Assert.True(r.Plan.Insufficient); Assert.Empty(c.W.Stops);
        r = await c.Gov.EnsureRoomAsync(new[] { "d", "e" }, "c", force: true);
        Assert.Equal(new[] { "b" }, c.W.Stops);
    }

    [Fact]
    public void Plan_TwoToStart_WhenCallOwnerNotRunning()
    {
        using var c = new Ctx(max: 2, owner: "a");
        c.Run("b", "c");
        var ids = c.Call.IdsThatWillStart("d");
        Assert.Equal(new[] { "a", "d" }, ids);
        var p = c.Gov.PlanRoom(ids, "d");
        Assert.Equal(2, p.ToSleep.Count);
        Assert.False(p.Insufficient);
    }

    [Fact]
    public void IdsThatWillStart_Cases()
    {
        using var c = new Ctx(owner: "a");
        Assert.Equal(new[] { "a" }, c.Call.IdsThatWillStart("a"));
        Assert.Equal(new[] { "a", "b" }, c.Call.IdsThatWillStart("b"));
        c.W.PipeUp = true;
        Assert.Equal(new[] { "b" }, c.Call.IdsThatWillStart("b"));
        c.Run("b");
        Assert.Empty(c.Call.IdsThatWillStart("b"));
    }

    [Fact]
    public async Task EnsureRoom_StopsVictimsAndMarksSlept()
    {
        using var c = new Ctx(max: 2);
        c.Run("a", "b");
        var slept = new List<string>();
        c.Gov.Slept += slept.Add;
        var r = await c.Gov.EnsureRoomAsync(new[] { "c" }, "c");
        Assert.Equal(new[] { "a" }, c.W.Stops);
        Assert.Equal(new[] { "a" }, r.Slept);
        Assert.Equal(new[] { "a" }, slept);
        Assert.True(c.Gov.IsSleptByGovernor("a"));
        Assert.False(c.Gov.IsSleptByGovernor("b"));
        c.Gov.ClearSlept("a");
        Assert.False(c.Gov.IsSleptByGovernor("a"));
    }

    [Fact]
    public void FindIdle_DisabledByDefault()
    {
        using var c = new Ctx();
        c.Run("a", "b");
        Assert.Empty(c.Gov.FindIdle(c.Now.AddHours(5), null));
    }

    [Fact]
    public void FindIdle_ReturnsOldOnesExceptExcluded()
    {
        using var c = new Ctx(autoSleep: 10, pinned: "b", owner: "c");
        c.Run("a", "b", "c", "d", "e");   // lan luot 1 phut
        var now = c.Now.AddMinutes(9);     // d dung luc +3 -> 11 phut (qua 10); e dung luc +4 -> dung 10 (chua qua)
        var idle = c.Gov.FindIdle(now, "a");   // a dang chon, b ghim, c chu goi
        Assert.Equal(new[] { "d" }, idle);
    }

    [Fact]
    public void OldConfigWithoutNewFields_Loads()
    {
        using var t = new TempRoot();
        Directory.CreateDirectory(t.Paths.Root);
        File.WriteAllText(t.Paths.ConfigFile, "{\"accounts\":[{\"id\":\"a\",\"displayName\":\"a\"}],\"maxRunning\":3}");
        var cfg = new ConfigStore(t.Paths).Load();
        Assert.Equal(3, cfg.MaxRunning); Assert.Null(cfg.AutoSleepMinutes);
        Assert.Single(cfg.Accounts);
    }

    [Fact]
    public void Store_SetAutoSleep_ZeroMeansOff()
    {
        using var c = new Ctx();
        c.Profiles.SetAutoSleepMinutes(15); Assert.Equal(15, c.Profiles.Config.AutoSleepMinutes);
        c.Profiles.SetAutoSleepMinutes(0); Assert.Null(c.Profiles.Config.AutoSleepMinutes);
    }
}
