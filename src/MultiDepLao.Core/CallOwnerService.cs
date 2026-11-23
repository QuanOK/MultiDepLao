using System.Diagnostics;

namespace MultiDepLao.Core;

/// <summary>Phat hien pipe cuoc goi Zalo (PipeZCall*). App viet adapter boc MultiDepLao.Win32.CallPipes.</summary>
public interface ICallPipeProbe
{
    bool AnyCallPipe();
    Task<bool> WaitAsync(bool present, TimeSpan timeout, CancellationToken ct);
}

public enum CallOwnerKind
{
    /// <summary>Khong co pipe: chua ai giu quyen goi.</summary>
    None,
    /// <summary>App biet chac tai khoan nao giu.</summary>
    Known,
    /// <summary>Co pipe, co instance cua app chay nhung khong xac dinh duoc cai nao giu.</summary>
    Unknown,
    /// <summary>Co pipe ma khong instance nao cua app chay: Zalo ngoai MultiDepLao giu.</summary>
    External,
}

public sealed record CallOwnerState(CallOwnerKind Kind, string? Id);

public sealed record SwitchResult(bool Success, string? Reason, IReadOnlyList<string> Restarted, TimeSpan Elapsed);

public sealed class CallOwnerOptions
{
    /// <summary>Doi pipe xuat hien sau khi khoi dong instance giu quyen.</summary>
    public TimeSpan PipeAppearTimeout { get; set; } = TimeSpan.FromSeconds(90);
    /// <summary>Doi pipe bien mat sau khi tat mot instance.</summary>
    public TimeSpan PipeGoneAfterStop { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Doi pipe bien mat sau khi da tat moi instance can tat (xac dinh Zalo ngoai).</summary>
    public TimeSpan PipeGoneFinal { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Quan ly "tai khoan goi dien" (SPEC 4.5): thu tu khoi dong, chuyen quyen goi theo quy trinh switch-call.ps1.
/// Moi thao tac tat/mo di qua IInstanceControl (InstanceManager, co SafetyGate).
/// </summary>
public sealed class CallOwnerService
{
    private readonly IInstanceControl _ctl;
    private readonly ProfileStore _profiles;
    private readonly ICallPipeProbe _probe;
    private readonly CallOwnerOptions _opt;
    private readonly ILog _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile string? _actual;

    public CallOwnerService(IInstanceControl ctl, ProfileStore profiles, ICallPipeProbe probe,
        CallOwnerOptions? options = null, ILog? log = null)
    {
        _ctl = ctl; _profiles = profiles; _probe = probe; _opt = options ?? new(); _log = log ?? NullLog.Instance;
    }

    /// <summary>Tai khoan app biet chac dang giu pipe (trong bo nho). Null neu khong biet / da tat.</summary>
    public string? ActualOwnerId => _actual != null && _ctl.IsRunning(_actual) ? _actual : null;

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Goi sau AdoptRunning: neu co pipe va CallOwnerId trong config dang chay thi coi la chu.</summary>
    public void Initialize()
    {
        if (!_probe.AnyCallPipe()) return;
        var cfg = _profiles.Config.CallOwnerId;
        if (cfg != null && _ctl.RunningOldestFirst().Any(r => Eq(r, cfg))) { _actual = cfg; _log.Info($"quyen goi: {cfg} (theo cau hinh, pipe con)"); }
    }

    public CallOwnerState GetState()
    {
        if (!_probe.AnyCallPipe()) return new(CallOwnerKind.None, null);
        var a = ActualOwnerId;
        if (a != null) return new(CallOwnerKind.Known, a);
        return _ctl.RunningOldestFirst().Count == 0 ? new(CallOwnerKind.External, null) : new(CallOwnerKind.Unknown, null);
    }

    // ------------------------------------------------------------ khoi dong

    /// <summary>Khoi dong id; ham khoi dong chu quyen goi truoc neu can. Cac lenh Start/Switch duoc xep hang.</summary>
    public async Task<InstanceStatus> StartAsync(string id, CancellationToken ct = default)
    {
        if (_profiles.IsWeb(id)) throw new InvalidOperationException($"Tai khoan Web '{id}' khong chay Zalo.exe.");
        await _lock.WaitAsync(ct);
        try { return await StartLockedAsync(id, ct); }
        finally { _lock.Release(); }
    }

    /// <summary>Cac tai khoan se duoc bat neu goi StartAsync(id): chi doc. Ke ca chu quyen goi bat truoc.</summary>
    public IReadOnlyList<string> IdsThatWillStart(string id)
    {
        if (_profiles.IsWeb(id) || _ctl.IsRunning(id)) return Array.Empty<string>();
        if (!_probe.AnyCallPipe())
        {
            var owner = _profiles.Config.CallOwnerId;
            if (owner != null && !Eq(owner, id) && !_ctl.IsRunning(owner) && _profiles.PcAccounts.Any(a => Eq(a.Id, owner)))
                return new[] { owner, id };
        }
        return new[] { id };
    }

    private async Task<InstanceStatus> StartLockedAsync(string id, CancellationToken ct)
    {
        if (_ctl.IsRunning(id)) return await _ctl.StartAsync(id, ct);
        if (!_probe.AnyCallPipe())
        {
            var owner = _profiles.Config.CallOwnerId;
            if (owner != null && !Eq(owner, id) && !_ctl.IsRunning(owner) && _profiles.PcAccounts.Any(a => Eq(a.Id, owner)))
            {
                _log.Info($"bat tai khoan goi dien {owner} truoc {id}");
                await StartAndClaimAsync(owner, ct);
            }
        }
        return await StartAndClaimAsync(id, ct);
    }

    /// <summary>Khoi dong; neu luc khoi dong khong co pipe thi doi pipe, thay thi day la chu quyen goi.</summary>
    private async Task<InstanceStatus> StartAndClaimAsync(string id, CancellationToken ct)
    {
        bool noPipe = !_probe.AnyCallPipe();
        var st = await _ctl.StartAsync(id, ct);
        if (noPipe && st.State is not InstanceState.Error)
        {
            if (await _probe.WaitAsync(true, _opt.PipeAppearTimeout, ct))
            {
                _actual = id;
                if (_profiles.Config.CallOwnerId == null) _profiles.SetCallOwner(id);
                _log.Info($"quyen goi dien: {id}");
            }
            else _log.Warn($"{id}: pipe cuoc goi khong xuat hien sau khi khoi dong");
        }
        return st;
    }

    /// <summary>Mo app: bat tai khoan goi dien truoc (neu co AutoStart nao), roi cac tai khoan AutoStart con lai.</summary>
    public async Task StartAutoStartAccountsAsync(CancellationToken ct = default)
    {
        var autos = _profiles.PcAccounts.Where(a => a.AutoStart).Select(a => a.Id).ToList();
        if (autos.Count == 0) return;
        var owner = _profiles.Config.CallOwnerId;
        var order = new List<string>();
        if (owner != null && _profiles.PcAccounts.Any(a => Eq(a.Id, owner))) order.Add(owner);
        order.AddRange(autos.Where(a => !Eq(a, owner)));
        foreach (var id in order)
        {
            try { await StartAsync(id, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.Error($"[{id}] tu bat loi: {ex.Message}"); }
        }
    }

    // ------------------------------------------------------------ chuyen quyen goi

    /// <summary>Cac tai khoan (khac dich) se bi tat roi mo lai khi chuyen. Dich cung se khoi dong lai neu dang chay.</summary>
    public IReadOnlyList<string> PreviewSwitch(string targetId)
    {
        var st = GetState();
        if (st.Kind == CallOwnerKind.None) return Array.Empty<string>();
        if (st.Kind == CallOwnerKind.Known) return Eq(st.Id, targetId) ? Array.Empty<string>() : new[] { st.Id! };
        return _ctl.RunningOldestFirst().Where(i => !Eq(i, targetId)).ToList();
    }

    public async Task<SwitchResult> SwitchToAsync(string targetId, IProgress<string>? progress = null, CancellationToken ct = default, bool forceRestart = false)
    {
        await _lock.WaitAsync(ct);
        var sw = Stopwatch.StartNew();
        var stopped = new List<string>();   // da tat de nhuong pipe (khong ke dich)
        bool targetStopped = false;
        void Say(string m) { _log.Info("chuyen quyen goi: " + m); progress?.Report(m); }
        SwitchResult Fail(string reason) => new(false, reason, stopped.ToList(), sw.Elapsed);
        async Task ReopenAsync()
        {
            foreach (var id in stopped)
            {
                Say($"mo lai {id}");
                try { await _ctl.StartAsync(id, CancellationToken.None); }
                catch (Exception ex) { _log.Error($"[{id}] mo lai loi: {ex.Message}"); }
            }
        }
        try
        {
            if (_profiles.IsWeb(targetId)) return Fail("tai khoan Web khong the giu quyen goi");
            var state = GetState();
            if (state.Kind == CallOwnerKind.Known && Eq(state.Id, targetId) && _ctl.IsRunning(targetId) && !forceRestart)
            {
                Say("tai khoan nay da giu quyen goi");
                return new(true, null, Array.Empty<string>(), sw.Elapsed);
            }

            // 1. Giai phong pipe: chu hien tai truoc, roi cac instance khac (cu nhat truoc) cho den khi pipe mat
            var running = _ctl.RunningOldestFirst();
            var order = new List<string>();
            var known = state.Kind == CallOwnerKind.Known ? state.Id : _profiles.Config.CallOwnerId;
            if (known != null && !Eq(known, targetId) && running.Any(r => Eq(r, known))) order.Add(known);
            order.AddRange(running.Where(r => !Eq(r, targetId) && !order.Any(o => Eq(o, r))));

            foreach (var id in order)
            {
                if (!_probe.AnyCallPipe()) break;
                Say($"tat {id} de giai phong quyen goi");
                var r = await _ctl.StopAsync(id, ct);
                if (r.Outcome is StopOutcome.Refused or StopOutcome.Failed)
                {
                    await ReopenAsync();
                    return Fail($"khong tat duoc tai khoan {id}: {r.Reason}");
                }
                stopped.Add(id);
                await _probe.WaitAsync(false, _opt.PipeGoneAfterStop, ct);
            }

            // 2. Tat dich neu dang chay (de khoi dong lai)
            if (_ctl.IsRunning(targetId))
            {
                Say("tat tai khoan dich de khoi dong lai");
                var r = await _ctl.StopAsync(targetId, ct);
                if (r.Outcome is StopOutcome.Refused or StopOutcome.Failed)
                {
                    await ReopenAsync();
                    return Fail($"khong tat duoc tai khoan dich: {r.Reason}");
                }
                targetStopped = true;
            }
            if (!await _probe.WaitAsync(false, _opt.PipeGoneFinal, ct))
            {
                _actual = null;
                _log.Warn("pipe van con sau khi tat moi instance cua MultiDepLao: Zalo ngoai giu");
                await ReopenAsync();
                if (targetStopped) { try { await _ctl.StartAsync(targetId, CancellationToken.None); } catch { /* bo qua */ } }
                return Fail(Loc.F("Call.ReasonForeign", AppInfo.DisplayName));
            }

            // 3. Khoi dong dich, doi pipe, ghi cau hinh
            _actual = null;
            Say("khoi dong tai khoan dich");
            var st = await _ctl.StartAsync(targetId, ct);
            bool got = st.State is not InstanceState.Error && await _probe.WaitAsync(true, _opt.PipeAppearTimeout, ct);
            if (got) { _actual = targetId; _profiles.SetCallOwner(targetId); }

            // 4. Mo lai cac instance da tat
            await ReopenAsync();
            return got
                ? new(true, null, stopped.ToList(), sw.Elapsed)
                : Fail(st.State is InstanceState.Error ? Loc.T("Call.ReasonStartFail") : Loc.T("Call.ReasonNoPipe"));
        }
        finally { _lock.Release(); }
    }
}
