namespace MultiDepLao.Core;

/// <summary>Anh chup trang thai cap nhat Zalo.</summary>
/// <param name="LatestCopy">Phien ban ban sao nocap hop le moi nhat (null neu chua co).</param>
/// <param name="OutdatedIds">Tai khoan dang chay ban cu hon LatestCopy.</param>
/// <param name="Copying">Dang tao ban sao moi.</param>
public sealed record ZaloUpdateState(Version? LatestCopy, IReadOnlyList<string> OutdatedIds, bool Copying);

/// <summary>
/// Theo doi Zalo tu cap nhat: ban cai goc moi hon ban sao nocap -> tao ban sao moi o nen; cho biet tai khoan
/// nao con chay ban cu; don ban sao cu khong con tien trinh nao dung. Khong tat/khoi dong gi.
/// </summary>
public sealed class ZaloUpdateWatcher : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(30);

    private readonly ZaloInstall _install;
    private readonly AppPaths _paths;
    private readonly IProcessProbe _probe;
    private readonly Func<IReadOnlyList<(string Id, int Pid)>> _running;
    private readonly IProgress<double>? _progress;
    private readonly ILog _log;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private ZaloUpdateState _state = new(null, Array.Empty<string>(), false);

    public ZaloUpdateWatcher(ZaloInstall install, AppPaths paths, IProcessProbe probe,
        Func<IReadOnlyList<(string Id, int Pid)>> running, IProgress<double>? progress = null, ILog? log = null)
    {
        _install = install; _paths = paths; _probe = probe; _running = running; _progress = progress;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Phat tren thread bat ky khi trang thai doi.</summary>
    public event Action<ZaloUpdateState>? Changed;

    public ZaloUpdateState State { get { lock (_sync) return _state; } }

    /// <summary>Phien ban ban sao ma tien trinh dung (theo duong dan exe); null neu khong xac dinh duoc.</summary>
    private Version? VersionOfProcess(int pid)
    {
        var path = _probe.Get(pid)?.ExePath;
        if (string.IsNullOrEmpty(path)) return null;
        var dir = Path.GetDirectoryName(path);
        if (dir == null || !AppPaths.IsUnder(dir, _paths.NoCapRoot)) return null;
        return ZaloInstall.ParseVersion(Path.GetFileName(dir));
    }

    private void Publish(ZaloUpdateState s)
    {
        lock (_sync)
        {
            if (_state.LatestCopy == s.LatestCopy && _state.Copying == s.Copying && _state.OutdatedIds.SequenceEqual(s.OutdatedIds)) return;
            _state = s;
        }
        try { Changed?.Invoke(s); } catch (Exception ex) { _log.Warn("update: Changed loi: " + ex.Message); }
    }

    /// <summary>Tinh lai danh sach tai khoan chay ban cu (re, chi doc). Goi khi trang thai instance doi.</summary>
    public void Refresh(bool? copying = null)
    {
        var latest = _install.FindLatestValidCopy()?.Version;
        var outdated = new List<string>();
        if (latest != null)
            foreach (var (id, pid) in _running())
            {
                var v = VersionOfProcess(pid);
                if (v != null && v < latest) outdated.Add(id);
            }
        Publish(new ZaloUpdateState(latest, outdated, copying ?? State.Copying));
    }

    /// <summary>
    /// Don ban sao cu. Neu khong xac dinh duoc phien ban cua mot tien trinh dang chay thi KHONG don gi (an toan).
    /// Tra ve ten thu muc da xoa.
    /// </summary>
    public IReadOnlyList<string> PruneNow()
    {
        var inUse = new List<string>();
        foreach (var (_, pid) in _running())
        {
            var v = VersionOfProcess(pid);
            if (v == null) return Array.Empty<string>();
            inUse.Add("Zalo-" + v);
        }
        return _install.PruneOldCopies(inUse);
    }

    /// <summary>Goi sau khi mot instance doi trang thai (dung/chet): cap nhat danh sach + don dep.</summary>
    public void OnInstancesChanged()
    {
        try { PruneNow(); Refresh(); }
        catch (Exception ex) { _log.Warn("update: " + ex.Message); }
    }

    /// <summary>Mot lan kiem tra: ban goc > ban sao moi nhat -> tao ban sao. Khong chay trung nhau.</summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (!await _checkLock.WaitAsync(0, ct)) return;
        try
        {
            var src = _install.FindLatest();
            if (src != null)
            {
                var have = _install.FindLatestValidCopy();
                if (have == null || src.Version > have.Version)
                {
                    _log.Info($"update: co ban {src.DirName} (ban sao hien co: {have?.DirName ?? "chua co"}), tao ban sao nen");
                    Refresh(copying: true);
                    try { await _install.EnsureNoCapCopyAsync(_progress, ct); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { _log.Warn("update: tao ban sao loi: " + ex.Message); }
                    finally { Refresh(copying: false); }
                }
            }
            OnInstancesChanged();
        }
        finally { _checkLock.Release(); }
    }

    /// <summary>Kiem tra ngay roi dinh ky.</summary>
    public void Start(TimeSpan? interval = null)
    {
        if (_cts != null) return;
        var cts = _cts = new CancellationTokenSource();
        var period = interval ?? DefaultInterval;
        _ = Task.Run(async () =>
        {
            try
            {
                await CheckAsync(cts.Token);
                using var timer = new PeriodicTimer(period);
                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    try { await CheckAsync(cts.Token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { _log.Warn("update: " + ex.Message); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.Warn("update: " + ex.Message); }
        });
    }

    public void Dispose() { _cts?.Cancel(); _cts = null; }
}
