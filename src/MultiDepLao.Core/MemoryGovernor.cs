using System.Runtime.InteropServices;

namespace MultiDepLao.Core;

public interface ISystemMemory
{
    long TotalPhysicalBytes { get; }
}

/// <summary>RAM vat ly that (GlobalMemoryStatusEx).</summary>
public sealed class SystemMemory : ISystemMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX s);

    /// <summary>RAM vat ly con trong (GlobalMemoryStatusEx); null neu doc loi.</summary>
    public static long? AvailablePhysicalBytes()
    {
        var s = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref s) ? (long)s.ullAvailPhys : null;
    }

    public long TotalPhysicalBytes
    {
        get
        {
            var s = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref s) ? (long)s.ullTotalPhys : 8L << 30;
        }
    }
}

/// <summary>Ke hoach nhuong cho. Shortfall &gt; 0: khong du ung vien (UI hoi nguoi dung).</summary>
public sealed record RoomPlan(IReadOnlyList<string> ToSleep, int Shortfall)
{
    public bool Insufficient => Shortfall > 0;
}

public sealed record RoomResult(RoomPlan Plan, IReadOnlyList<string> Slept);

/// <summary>Gioi han so tai khoan chay cung luc (SPEC 4.6). Trang thai "ngu do gioi han RAM" chi nam trong bo nho.</summary>
public sealed class MemoryGovernor
{
    private readonly IInstanceControl _ctl;
    private readonly ProfileStore _profiles;
    private readonly CallOwnerService _call;
    private readonly ISystemMemory _mem;
    private readonly ILog _log;
    private readonly Func<DateTime> _clock;
    private readonly object _l = new();
    private readonly Dictionary<string, DateTime> _used = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _slept = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime _created;

    /// <summary>Phat khi governor cho mot tai khoan ngu (co the tu thread nen).</summary>
    public event Action<string>? Slept;

    public MemoryGovernor(IInstanceControl ctl, ProfileStore profiles, CallOwnerService call, ISystemMemory mem,
        ILog? log = null, Func<DateTime>? clock = null)
    {
        _ctl = ctl; _profiles = profiles; _call = call; _mem = mem; _log = log ?? NullLog.Instance;
        _clock = clock ?? (() => DateTime.UtcNow);
        _created = _clock();
    }

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>So tai khoan toi da chay cung luc.</summary>
    public int EffectiveMax => ComputeAuto(_mem.TotalPhysicalBytes, _profiles.Config.MaxRunning);

    public int AutoMax => ComputeAuto(_mem.TotalPhysicalBytes, null);

    private static int ComputeAuto(long ramBytes, int? configured)
    {
        if (configured is { } m) return Math.Max(1, m);
        double gb = ramBytes / (1024.0 * 1024 * 1024);
        return Math.Max(2, (int)Math.Floor((gb - 4) / 1.5));
    }

    /// <summary>Ghi thoi diem tai khoan duoc chon/dung (tai khoan vua khoi dong cung goi ham nay).</summary>
    public void NoteSelected(string id)
    {
        lock (_l) _used[id] = _clock();
    }

    private DateTime LastUsed(string id)
    {
        lock (_l) return _used.TryGetValue(id, out var t) ? t : _created;
    }

    public bool IsSleptByGovernor(string id)
    {
        lock (_l) return _slept.Contains(id);
    }

    public void ClearSlept(string id)
    {
        lock (_l) _slept.Remove(id);
    }

    private HashSet<string> Excluded(string? selectedId, IEnumerable<string> starting)
    {
        var ex = new HashSet<string>(starting, StringComparer.OrdinalIgnoreCase);
        foreach (var a in _profiles.Accounts) if (a.Pinned) ex.Add(a.Id);
        if (_call.ActualOwnerId is { } o) ex.Add(o);
        if (_profiles.Config.CallOwnerId is { } c) ex.Add(c);
        if (selectedId != null) ex.Add(selectedId);
        return ex;
    }

    /// <summary>Chon tai khoan cho ngu (lau khong dung nhat truoc) de sau khi bat idsToStart so dang chay &lt;= EffectiveMax.</summary>
    public RoomPlan PlanRoom(IEnumerable<string> idsToStart, string? selectedId)
    {
        var starting = idsToStart.ToList();
        var running = _ctl.RunningOldestFirst();
        int newOnes = starting.Count(s => !running.Any(r => Eq(r, s)));
        int need = running.Count + newOnes - EffectiveMax;
        if (need <= 0) return new RoomPlan(Array.Empty<string>(), 0);
        var ex = Excluded(selectedId, starting);
        var cands = running.Where(r => !ex.Contains(r)).OrderBy(LastUsed).ToList();
        var pick = cands.Take(need).ToList();
        return new RoomPlan(pick, need - pick.Count);
    }

    /// <summary>
    /// Thuc thi ke hoach. Neu thieu ung vien va force=false: khong tat gi (UI hoi); force=true: tat nhung cai co the.
    /// </summary>
    public async Task<RoomResult> EnsureRoomAsync(IEnumerable<string> idsToStart, string? selectedId, bool force = false, CancellationToken ct = default)
    {
        var plan = PlanRoom(idsToStart, selectedId);
        if (plan.Insufficient && !force) return new RoomResult(plan, Array.Empty<string>());
        var done = await SleepAsync(plan.ToSleep, "nhuong RAM", ct);
        return new RoomResult(plan, done);
    }

    private async Task<IReadOnlyList<string>> SleepAsync(IEnumerable<string> ids, string why, CancellationToken ct)
    {
        var done = new List<string>();
        foreach (var id in ids)
        {
            var r = await _ctl.StopAsync(id, ct);
            if (r.Outcome is StopOutcome.Stopped or StopOutcome.AlreadyStopped)
            {
                lock (_l) _slept.Add(id);
                done.Add(id);
                _log.Info($"[{id}] cho ngu ({why})");
                Slept?.Invoke(id);
            }
            else _log.Warn($"[{id}] khong cho ngu duoc ({why}): {r.Reason}");
        }
        return done;
    }

    /// <summary>Tai khoan dang chay khong duoc chon qua AutoSleepMinutes (rong neu tinh nang tat).</summary>
    public IReadOnlyList<string> FindIdle(DateTime now, string? selectedId)
    {
        int min = _profiles.Config.AutoSleepMinutes ?? 0;
        if (min <= 0) return Array.Empty<string>();
        var ex = Excluded(selectedId, Array.Empty<string>());
        var limit = TimeSpan.FromMinutes(min);
        return _ctl.RunningOldestFirst().Where(r => !ex.Contains(r) && now - LastUsed(r) > limit).ToList();
    }

    public Task<IReadOnlyList<string>> SleepIdleAsync(DateTime now, string? selectedId, CancellationToken ct = default) =>
        SleepAsync(FindIdle(now, selectedId), "khong dung qua lau", ct);

    public DateTime Now => _clock();

    /// <summary>
    /// Tu bat khi mo app: chu quyen goi truoc, roi cac AutoStart khac, den khi dat gioi han; phan con lai de ngu (ghi log, khong hoi).
    /// Tra ve cac id bi bo qua.
    /// </summary>
    public async Task<IReadOnlyList<string>> StartAutoStartAsync(CancellationToken ct = default)
    {
        var accounts = _profiles.PcAccounts;   // tai khoan Web khong bao gio tu bat Zalo.exe
        var autos = accounts.Where(a => a.AutoStart).Select(a => a.Id).ToList();
        var skipped = new List<string>();
        if (autos.Count == 0) return skipped;
        var owner = _profiles.Config.CallOwnerId;
        var order = new List<string>();
        if (owner != null && autos.Any(a => Eq(a, owner))) order.Add(owner);
        order.AddRange(autos.Where(a => !Eq(a, owner)));
        foreach (var id in order)
        {
            var ids = _call.IdsThatWillStart(id);
            if (ids.Count == 0) continue;
            int newOnes = ids.Count(i => !_ctl.IsRunning(i));
            if (_ctl.RunningOldestFirst().Count + newOnes > EffectiveMax)
            {
                skipped.Add(id);
                _log.Info($"[{id}] tu bat: bo qua (dat gioi han {EffectiveMax}), de ngu");
                continue;
            }
            try
            {
                await _call.StartAsync(id, ct);
                foreach (var i in ids) NoteSelected(i);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.Error($"[{id}] tu bat loi: {ex.Message}"); }
        }
        return skipped;
    }
}
