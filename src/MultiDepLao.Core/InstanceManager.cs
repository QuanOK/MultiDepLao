using System.Collections.Concurrent;
using System.Diagnostics;

namespace MultiDepLao.Core;

public enum InstanceState { Stopped, Starting, Running, Stopping, Error }

public sealed record InstanceStatus(string Id, InstanceState State, int? Pid, string? Reason);

public enum StopOutcome { Stopped, AlreadyStopped, Refused, Failed }
public sealed record StopResult(StopOutcome Outcome, string? Reason);

/// <summary>Hook thoat em (tuy chon). Tra ve true neu da gui yeu cau dong.</summary>
public interface IGracefulCloser { bool TryClose(int pid); }

public sealed class InstanceManagerOptions
{
    public string ExeName { get; set; } = "Zalo.exe";
    public string Arguments { get; set; } = "";
    /// <summary>Thay cho ZaloInstall.EnsureNoCapCopyAsync (test tich hop).</summary>
    public Func<CancellationToken, Task<string>>? ExeResolver { get; set; }
    /// <summary>Id giu quyen chup man hinh (null = khong ai). Id nay chay tu zalo-cap\<id> thay vi nocap.</summary>
    public Func<string?>? CaptureOwner { get; set; }
    /// <summary>Thay cho ZaloInstall.EnsureCapCopyAsync (test).</summary>
    public Func<string, CancellationToken, Task<string>>? CapExeResolver { get; set; }
    /// <summary>Tra ve ly do neu dang co ZaloCap ngoai MultiDepLao (khi do chay nocap thay vi cap, de khong gay xung dot).</summary>
    public Func<string?>? CaptureGuard { get; set; }
    public TimeSpan GraceTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan KillWait { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReadyAfter { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>Phan InstanceManager ma CallOwnerService can (tach de test nhanh).</summary>
public interface IInstanceControl
{
    Task<InstanceStatus> StartAsync(string id, CancellationToken ct = default);
    Task<StopResult> StopAsync(string id, CancellationToken ct = default);
    bool IsRunning(string id);
    /// <summary>Cac instance dang song (Starting/Running/Stopping), cu nhat truoc.</summary>
    IReadOnlyList<string> RunningOldestFirst();
}

public enum ExeKind { NoCap, Cap }

public static class ExeSelector
{
    /// <summary>Cap chi khi id la chu quyen chup va khong co ZaloCap ngoai MultiDepLao (guardReason == null).</summary>
    public static ExeKind Choose(string id, string? captureOwner, string? guardReason) =>
        captureOwner != null && string.Equals(captureOwner, id, StringComparison.OrdinalIgnoreCase) && guardReason == null
            ? ExeKind.Cap : ExeKind.NoCap;
}

public sealed class InstanceManager : IDisposable, IInstanceControl
{
    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public InstanceState State = InstanceState.Stopped;
        public int? Pid;
        public DateTime CreatedUtc;
        public DateTime StateSince = DateTime.UtcNow;
        public string? Reason;
    }

    private readonly AppPaths _paths;
    private readonly IProcessProbe _probe;
    private readonly SafetyGate _gate;
    private readonly ZaloInstall? _install;
    private readonly InstanceManagerOptions _opt;
    private readonly ILog _log;
    private readonly IGracefulCloser? _closer;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _monitorCts;

    public event Action<InstanceStatus>? StateChanged;

    public InstanceManager(AppPaths paths, IProcessProbe probe, SafetyGate gate, ZaloInstall? install,
        InstanceManagerOptions? options = null, ILog? log = null, IGracefulCloser? closer = null)
    {
        _paths = paths; _probe = probe; _gate = gate; _install = install;
        _opt = options ?? new InstanceManagerOptions();
        _log = log ?? NullLog.Instance; _closer = closer;
    }

    private Entry E(string id) { _paths.ProfileDir(id); return _entries.GetOrAdd(id, _ => new Entry()); }

    private void Set(string id, Entry e, InstanceState s, string? reason = null)
    {
        e.State = s; e.Reason = reason; e.StateSince = DateTime.UtcNow;
        if (s is InstanceState.Stopped or InstanceState.Error) e.Pid = null;
        _log.Info($"{id}: {s}{(reason != null ? " - " + reason : "")}");
        StateChanged?.Invoke(new InstanceStatus(id, s, e.Pid, reason));
    }

    public InstanceStatus GetStatus(string id)
    {
        var e = E(id);
        return new InstanceStatus(id, e.State, e.Pid, e.Reason);
    }

    public IReadOnlyList<InstanceStatus> GetAll() =>
        _entries.Select(kv => new InstanceStatus(kv.Key, kv.Value.State, kv.Value.Pid, kv.Value.Reason)).ToList();

    public bool IsRunning(string id) =>
        _entries.TryGetValue(id, out var e) && e.State is InstanceState.Starting or InstanceState.Running or InstanceState.Stopping;

    public IReadOnlyList<string> RunningOldestFirst() =>
        _entries.Where(kv => kv.Value.State is InstanceState.Starting or InstanceState.Running or InstanceState.Stopping && kv.Value.Pid != null)
            .OrderBy(kv => kv.Value.CreatedUtc).Select(kv => kv.Key).ToList();

    public async Task<InstanceStatus> StartAsync(string id, CancellationToken ct = default)
    {
        var e = E(id);
        await e.Gate.WaitAsync(ct);
        try
        {
            var v = _gate.Verify(id);
            if (v.Ok)
            {
                e.Pid = v.Instance!.Pid; e.CreatedUtc = v.Instance.CreatedUtc;
                if (e.State is not (InstanceState.Starting or InstanceState.Running)) Set(id, e, InstanceState.Running, "da chay san");
                return GetStatus(id);
            }

            Set(id, e, InstanceState.Starting);
            try
            {
                string? guard = _opt.CaptureGuard?.Invoke();
                var kind = ExeSelector.Choose(id, _opt.CaptureOwner?.Invoke(), guard);
                string exe;
                if (kind == ExeKind.NoCap && guard != null && _opt.CaptureOwner?.Invoke() is { } o && string.Equals(o, id, StringComparison.OrdinalIgnoreCase))
                    _log.Warn($"{id}: la chu quyen chup nhung {guard}; chay khong ZaloCap");
                if (kind == ExeKind.Cap)
                {
                    try
                    {
                        exe = _opt.CapExeResolver != null
                            ? await _opt.CapExeResolver(id, ct)
                            : await (_install ?? throw new InvalidOperationException("Thieu ZaloInstall")).EnsureCapCopyAsync(id, ct);
                        _log.Info($"{id}: chay tu ban sao co ZaloCap");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _log.Warn($"{id}: khong chuan bi duoc ban sao ZaloCap ({ex.Message}); chay khong ZaloCap");
                        kind = ExeKind.NoCap; exe = "";
                    }
                }
                else exe = "";
                if (kind == ExeKind.NoCap)
                    exe = _opt.ExeResolver != null
                        ? await _opt.ExeResolver(ct)
                        : await (_install ?? throw new InvalidOperationException("Thieu ZaloInstall")).EnsureNoCapCopyAsync(null, ct);
                var dir = _paths.ProfileDir(id);
                ChildEnvironment.EnsureDirectories(dir);
                var env = ChildEnvironment.Build(ChildEnvironment.FromCurrent(), dir);

                var psi = new ProcessStartInfo(exe, _opt.Arguments)
                {
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                };
                psi.Environment.Clear();
                foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

                int pid = await Task.Run(() => { using var p = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start tra ve null"); return p.Id; }, ct);
                var info = _probe.Get(pid);
                if (info == null) { Set(id, e, InstanceState.Error, "tien trinh thoat ngay khi khoi dong"); return GetStatus(id); }
                PidRecord.Write(_paths.PidFile(id), pid, info.CreatedUtc);
                e.Pid = pid; e.CreatedUtc = info.CreatedUtc;
                Set(id, e, InstanceState.Starting, $"pid {pid}");
            }
            catch (OperationCanceledException) { Set(id, e, InstanceState.Stopped, "huy"); throw; }
            catch (Exception ex)
            {
                _log.Error($"{id}: khoi dong loi: {ex.Message}");
                Set(id, e, InstanceState.Error, ex.Message);
            }
            return GetStatus(id);
        }
        finally { e.Gate.Release(); }
    }

    public async Task<StopResult> StopAsync(string id, CancellationToken ct = default)
    {
        var e = E(id);
        await e.Gate.WaitAsync(ct);
        try
        {
            var v = _gate.Verify(id);
            if (!v.Ok)
            {
                if (v.Failure is VerifyFailure.NoPidFile or VerifyFailure.ProcessNotFound)
                {
                    if (e.State != InstanceState.Stopped) Set(id, e, InstanceState.Stopped, "khong con tien trinh");
                    return new StopResult(StopOutcome.AlreadyStopped, v.Reason);
                }
                _log.Warn($"{id}: tu choi tat: {v.Reason}");
                return new StopResult(StopOutcome.Refused, v.Reason);
            }
            var inst = v.Instance!;
            Set(id, e, InstanceState.Stopping);

            if (_closer != null)
            {
                bool sent = false;
                try { sent = _closer.TryClose(inst.Pid); } catch (Exception ex) { _log.Warn($"{id}: thoat em loi: {ex.Message}"); }
                if (sent && await WaitGone(inst, _opt.GraceTimeout, ct)) return Finish(id, e);
            }

            // Kiem tra lai NGAY TRUOC khi buoc dung
            var v2 = _gate.Verify(id);
            if (!v2.Ok)
            {
                if (v2.Failure == VerifyFailure.ProcessNotFound) return Finish(id, e);
                Set(id, e, InstanceState.Running, "tu choi tat: " + v2.Reason);
                return new StopResult(StopOutcome.Refused, v2.Reason);
            }
            _probe.KillTree(inst.Pid);
            if (await WaitGone(inst, _opt.KillWait, ct)) return Finish(id, e);
            Set(id, e, InstanceState.Error, "khong tat duoc tien trinh");
            return new StopResult(StopOutcome.Failed, "khong tat duoc tien trinh");
        }
        finally { e.Gate.Release(); }
    }

    private StopResult Finish(string id, Entry e)
    {
        try { File.Delete(_paths.PidFile(id)); } catch { /* bo qua */ }
        Set(id, e, InstanceState.Stopped);
        return new StopResult(StopOutcome.Stopped, null);
    }

    private bool Alive(VerifiedInstance i)
    {
        var info = _probe.Get(i.Pid);
        return info != null && (info.CreatedUtc - i.CreatedUtc).Duration() <= SafetyGate.CreatedTolerance;
    }

    private async Task<bool> WaitGone(VerifiedInstance i, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (!Alive(i)) return true;
            if (sw.Elapsed >= timeout) return false;
            await Task.Delay(100, ct);
        }
    }

    /// <summary>Tang ttren bao tien trinh da hien cua so chinh.</summary>
    public void MarkWindowReady(string id)
    {
        if (_entries.TryGetValue(id, out var e) && e.State == InstanceState.Starting) Set(id, e, InstanceState.Running);
    }

    /// <summary>Nhan lai instance con song qua mz.pid. Khong id thi quet thu muc profiles.</summary>
    public IReadOnlyList<string> AdoptRunning(IEnumerable<string>? ids = null)
    {
        ids ??= Directory.Exists(_paths.ProfilesRoot)
            ? Directory.EnumerateDirectories(_paths.ProfilesRoot).Select(d => Path.GetFileName(d)).Where(AppPaths.IsValidId)
            : Array.Empty<string>();
        var adopted = new List<string>();
        foreach (var id in ids.ToList())
        {
            var v = _gate.Verify(id);
            if (!v.Ok) continue;
            var e = E(id);
            if (e.State is InstanceState.Running or InstanceState.Starting or InstanceState.Stopping) continue;
            e.Pid = v.Instance!.Pid; e.CreatedUtc = v.Instance.CreatedUtc;
            Set(id, e, InstanceState.Running, "nhan lai");
            adopted.Add(id);
        }
        return adopted;
    }

    /// <summary>Mot vong kiem tra: Starting -> Running sau ReadyAfter; phat hien instance tu chet.</summary>
    public void Poll()
    {
        foreach (var kv in _entries)
        {
            var id = kv.Key; var e = kv.Value;
            if (e.State is not (InstanceState.Starting or InstanceState.Running) || e.Pid is not int pid) continue;
            var info = _probe.Get(pid);
            bool alive = info != null && (info.CreatedUtc - e.CreatedUtc).Duration() <= SafetyGate.CreatedTolerance;
            var age = DateTime.UtcNow - e.StateSince;
            if (!alive)
            {
                if (e.State == InstanceState.Starting && age < _opt.ReadyAfter) Set(id, e, InstanceState.Error, "thoat ngay khi khoi dong");
                else Set(id, e, InstanceState.Stopped, "tien trinh da thoat");
            }
            else if (e.State == InstanceState.Starting && age >= _opt.ReadyAfter) Set(id, e, InstanceState.Running);
        }
    }

    public void StartMonitoring(TimeSpan? interval = null)
    {
        if (_monitorCts != null) return;
        var cts = _monitorCts = new CancellationTokenSource();
        var period = interval ?? TimeSpan.FromSeconds(1);
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(period);
            try { while (await timer.WaitForNextTickAsync(cts.Token)) { try { Poll(); } catch (Exception ex) { _log.Error("poll: " + ex.Message); } } }
            catch (OperationCanceledException) { }
        });
    }

    public void Dispose() { _monitorCts?.Cancel(); _monitorCts = null; }
}


