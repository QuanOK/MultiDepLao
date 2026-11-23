using System.Diagnostics;

namespace MultiDepLao.Core;

/// <summary>Thao tac tien trinh cho ZaloCap.exe (Win32 that o MultiDepLao.Win32; test dung gia).</summary>
public interface ICaptureProcessOps
{
    /// <summary>Moi tien trinh ZaloCap.exe dang chay (kem duong dan exe neu doc duoc).</summary>
    IReadOnlyList<ProcessInfo> FindCapProcesses();
    /// <summary>Hau due (con chau) cua pid, khong gom chinh no.</summary>
    ISet<int> DescendantsOf(int pid);
    /// <summary>Ket thuc DUNG tien trinh nay (khong cay con). Chi tat neu pid + thoi diem tao con khop.</summary>
    bool Kill(ProcessInfo p);
}

public sealed record CaptureSwitchResult(bool Success, string? Reason, IReadOnlyList<string> Released, bool Restarted, TimeSpan Elapsed);

/// <summary>
/// "Chu quyen chup man hinh": toi da MOT tai khoan chay Zalo kem ZaloCap.exe (tu zalo-cap\id). Cac tai khoan khac
/// chay tu nocap. Chuyen quyen: doi ten ZaloCap.exe cua chu cu thanh .off ROI moi tat ZaloCap cua no (Zalo chi
/// khoi dong ZaloCap luc khoi dong nen khong bi hong, khong phai khoi dong lai chu cu).
/// </summary>
public sealed class CaptureOwnerService
{
    private readonly IInstanceControl _ctl;
    private readonly ProfileStore _profiles;
    private readonly AppPaths _paths;
    private readonly SafetyGate _gate;
    private readonly ICaptureProcessOps _ops;
    private readonly ZaloInstall? _install;
    private readonly ILog _log;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Phat khi dang chuyen quyen (true) / xong (false). Co the goi tu thread nen.</summary>
    public event Action<bool>? BusyChanged;
    public event Action<string>? Progress;
    public bool IsBusy { get; private set; }

    /// <summary>Tuy chon: thay buoc khoi dong lai dich (vd giu quyen goi). Tra (Handled=false) = dung Stop/Start mac dinh;
    /// (true, null) = da khoi dong lai xong; (true, loi) = that bai.</summary>
    public Func<string, CancellationToken, Task<(bool Handled, string? Error)>>? RestartOverride { get; set; }

    public CaptureOwnerService(IInstanceControl ctl, ProfileStore profiles, AppPaths paths, SafetyGate gate,
        ICaptureProcessOps ops, ZaloInstall? install = null, ILog? log = null)
    {
        _ctl = ctl; _profiles = profiles; _paths = paths; _gate = gate; _ops = ops; _install = install; _log = log ?? NullLog.Instance;
    }

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Chu quyen chup theo cau hinh (null = khong ai).</summary>
    public string? OwnerId => _profiles.Config.CaptureOwnerId;

    /// <summary>Ly do neu co ZaloCap.exe dang chay ngoai CapRoot (Zalo that / tien trinh khac); null neu khong.</summary>
    public string? ForeignCaptureReason() => ForeignCaptureReason(_ops, _paths);

    public static string? ForeignCaptureReason(ICaptureProcessOps ops, AppPaths paths)
    {
        foreach (var p in ops.FindCapProcesses())
            if (string.IsNullOrEmpty(p.ExePath) || !AppPaths.IsUnder(p.ExePath, paths.CapRoot))
                return "Zalo ngoai MultiDepLao dang giu chup man hinh";
        return null;
    }

    /// <summary>true neu instance dang chay cua id duoc khoi dong tu zalo-cap\id.</summary>
    public bool IsRunningFromCap(string id)
    {
        var v = _gate.Verify(id);
        return v.Ok && AppPaths.IsUnder(v.Instance!.ExePath, _paths.CapDir(id));
    }

    /// <summary>Goi sau AdoptRunning: chi ghi log neu chu quyen chup dang chay tu nocap (doi chu luc app dong). Khong tu khoi dong lai.</summary>
    public void Initialize()
    {
        var owner = OwnerId;
        if (owner != null && _ctl.IsRunning(owner) && !IsRunningFromCap(owner))
            _log.Warn($"quyen chup: {owner} dang chay tu nocap (khong co ZaloCap); can khoi dong lai de nhan quyen chup");
        _install?.PruneCapCopies(owner, _ctl.IsRunning);
    }

    /// <summary>Dat chu quyen chup (null = bo). Xem lop. Cac lenh duoc xep hang.</summary>
    public async Task<CaptureSwitchResult> SetCaptureOwnerAsync(string? newId, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        var sw = Stopwatch.StartNew();
        var released = new List<string>();
        IsBusy = true; BusyChanged?.Invoke(true);
        void Say(string m) { _log.Info("chuyen quyen chup: " + m); progress?.Report(m); Progress?.Invoke(m); }
        CaptureSwitchResult Fail(string reason) { _log.Warn("chuyen quyen chup that bai: " + reason); return new(false, reason, released.ToList(), false, sw.Elapsed); }
        try
        {
            if (newId != null && !_profiles.PcAccounts.Any(a => Eq(a.Id, newId)))
                return Fail(_profiles.IsWeb(newId) ? "tai khoan Web khong giu quyen chup" : $"khong co tai khoan '{newId}'");

            var current = _profiles.Config.CaptureOwnerId;
            if (Eq(current, newId) && (newId == null || !_ctl.IsRunning(newId) || IsRunningFromCap(newId)))
            {
                Say("khong doi gi");
                return new(true, null, released, false, sw.Elapsed);
            }

            if (newId != null)
            {
                var foreign = ForeignCaptureReason();
                if (foreign != null) return Fail(foreign);
            }

            // a. Nhung quyen: chu cu theo cau hinh + moi instance dang chay tu zalo-cap (tru dich)
            var olds = new List<string>();
            foreach (var id in _ctl.RunningOldestFirst())
                if (!Eq(id, newId) && (Eq(id, current) || IsRunningFromCap(id))) olds.Add(id);
            foreach (var o in olds)
            {
                Say($"thu ZaloCap cua {o}");
                var err = ReleaseCapture(o);
                if (err != null) return Fail($"khong thu hoi duoc quyen chup cua {o}: {err}");
                released.Add(o);
            }

            // b. Luu
            _profiles.SetCaptureOwner(newId);
            if (newId == null) { Say("da bo quyen chup"); _install?.PruneCapCopies(null, _ctl.IsRunning); return new(true, null, released, false, sw.Elapsed); }

            // c. Dich: khoi dong lai neu dang chay (se chay tu ban sao cap)
            bool restarted = false;
            if (_ctl.IsRunning(newId))
            {
                Say($"khoi dong lai {newId} de nhan quyen chup");
                var hook = RestartOverride;
                var (handled, herr) = hook != null ? await hook(newId, ct) : (false, null);
                if (handled)
                {
                    if (herr != null) return Fail($"da luu nhung khong khoi dong lai duoc {newId}: {herr}");
                }
                else
                {
                    var r = await _ctl.StopAsync(newId, ct);
                    if (r.Outcome is StopOutcome.Refused or StopOutcome.Failed)
                        return Fail($"da luu nhung khong tat duoc {newId}: {r.Reason}");
                    var st = await _ctl.StartAsync(newId, ct);
                    if (st.State is InstanceState.Error) return Fail($"da luu nhung khong khoi dong lai duoc {newId}: {st.Reason}");
                }
                restarted = true;
            }
            else Say($"{newId} se co quyen chup khi duoc khoi dong");
            _install?.PruneCapCopies(newId, _ctl.IsRunning);
            return new(true, null, released, restarted, sw.Elapsed);
        }
        finally { IsBusy = false; BusyChanged?.Invoke(false); _lock.Release(); }
    }

    /// <summary>Doi ten ZaloCap.exe -> .off truoc, roi tat cac ZaloCap la hau due cua main da xac minh, exe duoi CapRoot\id. Tra ve loi hoac null.</summary>
    private string? ReleaseCapture(string id)
    {
        var v = _gate.Verify(id);
        if (!v.Ok) return v.Reason;   // khong xac minh duoc main -> khong dung gi
        var capDir = _paths.CapDir(id);
        if (!AppPaths.IsUnder(v.Instance!.ExePath, capDir))
        {
            _log.Info($"{id}: dang chay tu nocap, khong co ZaloCap de thu");
            return null;
        }
        var verDir = Path.GetDirectoryName(v.Instance.ExePath)!;
        var cap = ZaloInstall.CapExeIn(verDir);
        try
        {
            if (File.Exists(cap)) File.Move(cap, cap + ZaloInstall.CapOffSuffix, true);
        }
        catch (Exception e) { return "doi ten ZaloCap.exe loi: " + e.Message; }

        var tree = _ops.DescendantsOf(v.Instance.Pid);
        foreach (var p in _ops.FindCapProcesses())
        {
            if (!tree.Contains(p.Pid)) continue;
            if (string.IsNullOrEmpty(p.ExePath) || !AppPaths.IsUnder(p.ExePath, capDir)) continue;
            if (_ops.Kill(p)) _log.Info($"{id}: da tat ZaloCap pid {p.Pid}");
            else _log.Warn($"{id}: khong tat duoc ZaloCap pid {p.Pid}");
        }
        return null;
    }
}

