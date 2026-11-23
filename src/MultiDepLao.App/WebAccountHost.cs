using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MultiDepLao.Core;
using MultiDepLao.Win32;

namespace MultiDepLao.App;

public enum WebSessionState { Starting, Running, Failed }

/// <summary>Mot tai khoan Web = mot WebView2 (mot profile). Chay tren UI thread.</summary>
public sealed class WebSession
{
    internal WebSession(string profileName, Panel container)
    {
        ProfileName = profileName; Container = container;
        Unread.NewMessage += () => NewMessage?.Invoke(this);
        Unread.UnreadChanged += has => { if (!has) AllRead?.Invoke(this); };
        Unread.Updated += () => UnreadUpdated?.Invoke(this);
    }

    /// <summary>Thoi diem mo phien: tin chua doc co san luc vua mo chi hien cham, khong bao.</summary>
    public DateTime OpenedAtUtc { get; } = DateTime.UtcNow;

    public string ProfileName { get; }
    internal Panel Container { get; }
    public WebView2? View { get; internal set; }
    /// <summary>Tin chua doc tu dang favicon cua trang (khong luu, khong giai ma).</summary>
    public FaviconUnreadDetector Unread { get; } = new();
    public WebSessionState State { get; private set; } = WebSessionState.Starting;
    /// <summary>Ten loai loi (khong co noi dung trang).</summary>
    public string? FailReason { get; private set; }
    public bool Closed { get; internal set; }

    public event Action<WebSession>? StateChanged;
    public event Action<WebSession>? NewMessage;
    /// <summary>Het tin chua doc (favicon tro lai binh thuong, ke ca doc tren thiet bi khac).</summary>
    public event Action<WebSession>? AllRead;
    /// <summary>Favicon doi (so tin chua doc co the doi).</summary>
    public event Action<WebSession>? UnreadUpdated;

    internal void SetState(WebSessionState s, string? reason = null)
    {
        if (State == s && FailReason == reason) return;
        State = s; FailReason = reason;
        StateChanged?.Invoke(this);
    }

    internal void DisposeView()
    {
        Closed = true;
        var v = View; View = null;
        if (v == null) return;
        try { Container.Children.Remove(v); } catch { /* bo qua */ }
        try { v.Dispose(); } catch { /* bo qua */ }
    }
}

/// <summary>
/// Chay nhieu tai khoan web trong WebView2 (mot Environment dung chung, moi tai khoan mot profile). Khong gan voi Zalo:
/// URL trang chu, thu muc du lieu, ten mien noi bo va ten profile deu la tham so. Chi mo trang nhu trinh duyet:
/// KHONG chay script, KHONG chan/sua request, KHONG doc DOM/cookie/noi dung thong bao, KHONG doi UserAgent.
/// Khong ghi tieu de/URL cua trang vao log (co the chua ten nguoi).
/// </summary>
public sealed class WebAccountHost : IDisposable
{
    private readonly string _homeUrl, _userDataDir;
    private readonly IReadOnlyList<string> _internalDomains;
    private readonly string? _browserArgs;
    private readonly ILog _log;
    private readonly Dictionary<string, WebSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private Task<CoreWebView2Environment>? _env;
    private bool _disposed;

    /// <summary>Phat khi mot tai khoan doi trang thai (Starting/Running/Failed). UI thread.</summary>
    public event Action<WebSession>? SessionStateChanged;
    /// <summary>Phat khi mot tai khoan co tin moi (nhap nhay tieu de). UI thread.</summary>
    public event Action<WebSession>? SessionNewMessage;
    /// <summary>Phat khi mot tai khoan het tin chua doc. UI thread.</summary>
    public event Action<WebSession>? SessionAllRead;
    /// <summary>Favicon cua mot tai khoan doi (cap nhat so tin chua doc). UI thread.</summary>
    public event Action<WebSession>? SessionUnreadUpdated;

    public WebAccountHost(string homeUrl, string userDataDir, IReadOnlyList<string> internalDomains, ILog log, string? browserArgs = null)
    {
        _homeUrl = homeUrl; _userDataDir = userDataDir; _internalDomains = internalDomains; _log = log; _browserArgs = browserArgs;
    }

    private System.Drawing.Color _bg = System.Drawing.Color.White;

    /// <summary>Mau nen WebView2 truoc khi trang ve (tranh chop trang khi theme toi). Dat lai se cap nhat moi phien.</summary>
    public System.Drawing.Color BackgroundColor
    {
        get => _bg;
        set
        {
            _bg = value;
            foreach (var s in _sessions.Values)
                try { if (s.View != null) s.View.DefaultBackgroundColor = value; } catch { /* bo qua */ }
        }
    }

    public WebSession? Get(string profileName) => _sessions.GetValueOrDefault(profileName);
    public IReadOnlyList<WebSession> Sessions => _sessions.Values.ToList();

    /// <summary>true neu may co WebView2 Runtime dung duoc (registry hoac API).</summary>
    public static bool RuntimeAvailable()
    {
        if (WebView2Runtime.InstalledVersion() != null) return true;
        try { return WebView2Runtime.NormalizeVersion(CoreWebView2Environment.GetAvailableBrowserVersionString()) != null; }
        catch { return false; }
    }

    private Task<CoreWebView2Environment> GetEnv() => _env ??= CreateEnvAsync();

    private async Task<CoreWebView2Environment> CreateEnvAsync()
    {
        await Task.Yield();   // de _env duoc gan truoc khi co the loi dong bo
        Directory.CreateDirectory(_userDataDir);
        var opt = new CoreWebView2EnvironmentOptions();
        if (!string.IsNullOrEmpty(_browserArgs)) opt.AdditionalBrowserArguments = _browserArgs;
        return await CoreWebView2Environment.CreateAsync(null, _userDataDir, opt);
    }

    /// <summary>Tao WebView2 cho tai khoan (da co phien thi tra phien do) va mo trang chu. Loi khoi tao -> phien o trang thai Failed.</summary>
    public async Task<WebSession> OpenAsync(string profileName, Panel container)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WebAccountHost));
        if (_sessions.TryGetValue(profileName, out var existing)) return existing;
        var s = new WebSession(profileName, container);
        s.StateChanged += x => SessionStateChanged?.Invoke(x);
        s.NewMessage += x => SessionNewMessage?.Invoke(x);
        s.AllRead += x => SessionAllRead?.Invoke(x);
        s.UnreadUpdated += x => SessionUnreadUpdated?.Invoke(x);
        _sessions[profileName] = s;
        var view = new WebView2 { Visibility = Visibility.Collapsed, DefaultBackgroundColor = BackgroundColor };
        s.View = view;
        container.Children.Add(view);
        SessionStateChanged?.Invoke(s);
        var envTask = GetEnv();
        try
        {
            CoreWebView2Environment env;
            try { env = await envTask; }
            catch { if (ReferenceEquals(_env, envTask)) _env = null; throw; }
            var opts = env.CreateCoreWebView2ControllerOptions();
            opts.ProfileName = profileName;
            await view.EnsureCoreWebView2Async(env, opts);
            if (s.Closed) return s;
            Hook(s, view.CoreWebView2);
            view.CoreWebView2.Navigate(_homeUrl);
            s.SetState(WebSessionState.Running);
        }
        catch (Exception ex)
        {
            if (s.Closed) return s;
            _log.Warn($"[{profileName}] [web] khoi tao loi: {ex.GetType().Name} 0x{ex.HResult:X8}");
            s.SetState(WebSessionState.Failed, ex.GetType().Name);
        }
        return s;
    }

#if DEBUG
    private static readonly bool Diag = Environment.GetEnvironmentVariable("MULTIDEPLAO_WEB_DIAG") == "1";
#else
    private const bool Diag = false;
#endif

    private static string ShortHash(string? v) => FaviconUnreadDetector.ShortHash(v);

    /// <summary>Chan doan: dang URI favicon (scheme, do dai; voi http(s) them host + path, bo query) — khong co noi dung tin.</summary>
    private static string DescribeUri(string? u)
    {
        if (string.IsNullOrEmpty(u)) return "uri=(rong)";
        if (u.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return $"uri=data len={u.Length} type={u.Substring(5, Math.Max(0, Math.Min(u.IndexOf(',') - 5, 30)))}";
        return Uri.TryCreate(u, UriKind.Absolute, out var x) ? $"uri={x.Scheme}://{x.Host}{x.AbsolutePath} q={x.Query.Length}" : $"uri=? len={u.Length}";
    }

    private void OpenExternal(string id, string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return;
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { _log.Warn($"[{id}] [web] khong mo duoc link ngoai: {ex.GetType().Name}"); }
    }

    /// <summary>Cua so moi kieu about:blank: dua cho mot WebView2 an (cung profile), chan dieu huong http(s) dau tien va
    /// mo dia chi do bang trinh duyet mac dinh, roi huy khung. Tu huy sau 15 s neu khong co dieu huong.</summary>
    private async Task CatchPopupAsync(WebSession s, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var env = await GetEnv();
        var catcher = new WebView2 { Visibility = Visibility.Collapsed, DefaultBackgroundColor = BackgroundColor };
        s.Container.Children.Add(catcher);
        bool done = false;
        void Finish()
        {
            if (done) return;
            done = true;
            catcher.Dispatcher.BeginInvoke(() =>
            {
                try { s.Container.Children.Remove(catcher); } catch { /* bo qua */ }
                try { catcher.Dispose(); } catch { /* bo qua */ }
            });
        }
        try
        {
            var opts = env.CreateCoreWebView2ControllerOptions();
            opts.ProfileName = s.ProfileName;
            await catcher.EnsureCoreWebView2Async(env, opts);
            catcher.CoreWebView2.NavigationStarting += (_, n) =>
            {
                if (!Uri.TryCreate(n.Uri, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return;
                n.Cancel = true;
                OpenExternal(s.ProfileName, n.Uri);
                Finish();
            };
            catcher.CoreWebView2.WindowCloseRequested += (_, _) => Finish();
            e.NewWindow = catcher.CoreWebView2;
            _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ => Finish(), TaskScheduler.FromCurrentSynchronizationContext());
        }
        catch { Finish(); throw; }
    }

    private void Hook(WebSession s, CoreWebView2 core)
    {
        string id = s.ProfileName;
        // Chi tu cap quyen cho origin la mien noi bo (hoac mien con); origin la -> de mac dinh.
        core.PermissionRequested += (_, e) =>
        {
            if (!WebAccountLogic.IsInternalOrigin(e.Uri, _internalDomains)) return;
            if (e.PermissionKind is CoreWebView2PermissionKind.PersistentStorage or CoreWebView2PermissionKind.Notifications
                or CoreWebView2PermissionKind.ClipboardRead)
                e.State = CoreWebView2PermissionState.Allow;   // con lai: mac dinh (hoi / tu choi)
        };
        // KHONG BAO GIO de WebView2 tu mo cua so rieng (thu tay 2026-11-19: link ngoai bi mo trong cua so WebView2 co thanh
        // dia chi thay vi trinh duyet). http(s) do nguoi dung bam -> trinh duyet mac dinh (ke ca link zalo.me);
        // about:blank -> khung an nhan cua so moi, bat dia chi http(s) dau tien roi dua cho trinh duyet.
        core.NewWindowRequested += async (_, e) =>
        {
            var act = WebAccountLogic.ClassifyNewWindow(e.Uri, Array.Empty<string>(), e.IsUserInitiated);
            if (Diag) _log.Info($"[{id}] [web-diag] cua so moi {DescribeUri(e.Uri)} user={e.IsUserInitiated} -> {act}");
            e.Handled = true;
            if (act == WebLinkAction.OpenExternal) { OpenExternal(id, e.Uri); return; }
            if (act == WebLinkAction.Block || !e.IsUserInitiated) return;
            var deferral = e.GetDeferral();
            try { await CatchPopupAsync(s, e); }
            catch (Exception ex) { _log.Warn($"[{id}] [web] khung bat cua so moi loi: {ex.GetType().Name}"); }
            finally { deferral.Complete(); }
        };
        // Dieu huong cap cao nhat ra ngoai mien noi bo: huy (mo trinh duyet mac dinh neu do nguoi dung bam). Khung con khong bi anh huong.
        core.NavigationStarting += (_, e) =>
        {
            var act = WebAccountLogic.ClassifyNavigation(e.Uri, _internalDomains, e.IsUserInitiated);
            if (act == WebAccountLogic.NavAction.Allow) return;
            e.Cancel = true;
            if (Diag) _log.Info($"[{id}] [web-diag] chan dieu huong host={(Uri.TryCreate(e.Uri, UriKind.Absolute, out var bu) ? bu.Host : "?")} -> {act}");
            if (act == WebAccountLogic.NavAction.CancelAndOpenExternal) OpenExternal(id, e.Uri);
        };
        // Tin chua doc: chi xem dang dia chi favicon (FaviconUnreadDetector); khong luu, khong giai ma anh.
        core.FaviconChanged += (_, _) =>
        {
            var u = core.FaviconUri;
            if (Diag)
            {
                _log.Info($"[{id}] [web-diag] favicon h={ShortHash(u)} {DescribeUri(u)} vis={s.View?.Visibility}");
                // Luu anh favicon nhung (logo + so do, khong co noi dung tin) de nghien cuu doc so tin chua doc.
                try
                {
                    int comma = u?.IndexOf(',') ?? -1;
                    if (FaviconUnreadDetector.IsBadge(u) && comma > 0)
                    {
                        var dir = Path.Combine(Path.GetDirectoryName(_userDataDir.TrimEnd('\\', '/'))!, "logs", "favicons");
                        Directory.CreateDirectory(dir);
                        var f = Path.Combine(dir, ShortHash(u) + ".png");
                        if (!File.Exists(f)) File.WriteAllBytes(f, Convert.FromBase64String(u!.Substring(comma + 1)));
                    }
                }
                catch { /* chan doan: bo qua */ }
            }
            s.Unread.OnFavicon(u);
        };
        // Chan doan (MULTIDEPLAO_WEB_DIAG=1): chi ghi ma bam ngan + do dai, KHONG ghi chu cua tieu de.
        if (Diag)
            core.DocumentTitleChanged += (_, _) =>
                _log.Info($"[{id}] [web-diag] title h={ShortHash(core.DocumentTitle)} len={core.DocumentTitle?.Length ?? 0} vis={s.View?.Visibility}");
        core.ProcessFailed += (_, e) =>
        {
            var kind = e.ProcessFailedKind;
            _log.Warn($"[{id}] [web] ProcessFailed {kind}");
            switch (kind)
            {
                case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                    _env = null;   // moi truong het hieu luc: lan mo sau tao lai
                    s.SetState(WebSessionState.Failed, kind.ToString());
                    break;
                case CoreWebView2ProcessFailedKind.RenderProcessExited:
                    s.SetState(WebSessionState.Failed, kind.ToString());
                    break;
                // Unresponsive/Gpu/Utility/Frame...: WebView2 tu phuc hoi hoac nguoi dung cho; chi ghi log.
            }
        };
        // DownloadStarting: de mac dinh (WebView2 tu luu vao Downloads va hien hop tai cua no).
    }

    /// <summary>Huy WebView2 cua tai khoan (giai phong renderer). Du lieu dang nhap con nguyen tren dia.</summary>
    public void Close(string profileName)
    {
        if (_sessions.Remove(profileName, out var s)) s.DisposeView();
    }

    /// <summary>
    /// Xoa du lieu profile cua tai khoan: neu dang chay thi CoreWebView2Profile.Delete() roi huy view; sau do xoa them
    /// thu muc profile con sot (neu khoa thi bo qua, chi ghi log). Chi dong den EBWebView\&lt;ten profile&gt;.
    /// </summary>
    public void DeleteProfile(string profileName)
    {
        if (_sessions.Remove(profileName, out var s))
        {
            try { s.View?.CoreWebView2?.Profile.Delete(); }
            catch (Exception ex) { _log.Warn($"[{profileName}] [web] Profile.Delete loi: {ex.GetType().Name}"); }
            s.DisposeView();
        }
        if (profileName.Length == 0 || profileName.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || profileName.Contains("..")) return;
        var root = Path.GetFullPath(Path.Combine(_userDataDir, "EBWebView"));
        var dir = Path.GetFullPath(Path.Combine(root, profileName));
        if (!AppPaths.IsUnder(dir, root) || !Directory.Exists(dir)) return;
        for (int i = 0; i < 5; i++)
        {
            try { Directory.Delete(dir, true); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (i == 4) _log.Warn($"[{profileName}] [web] chua xoa het thu muc profile: {ex.GetType().Name}");
                else System.Threading.Thread.Sleep(200);
            }
        }
    }

    /// <summary>
    /// Do bo nho cac tien trinh WebView2: tong (byte) va phan renderer gan duoc cho tung tai khoan (qua FrameId cua trang chinh).
    /// Khong xac dinh duoc renderer cua tai khoan nao thi tai khoan do khong co muc trong Own (SplitRam chia deu phan con lai).
    /// </summary>
    public async Task<(Dictionary<string, long> Own, long Total)?> MeasureAsync()
    {
        var live = _sessions.Values.Where(x => x.View?.CoreWebView2 != null).ToList();
        var envTask = _env;
        if (live.Count == 0 || envTask is not { IsCompletedSuccessfully: true }) return null;
        try
        {
            var infos = await envTask.Result.GetProcessExtendedInfosAsync();
            var frames = new Dictionary<uint, string>();   // FrameId -> tai khoan
            foreach (var x in live)
                try { frames[(uint)x.View!.CoreWebView2.FrameId] = x.ProfileName; } catch { /* khong ho tro */ }
            var rows = infos.Select(i => (Pid: (uint)i.ProcessInfo.ProcessId, Kind: i.ProcessInfo.Kind,
                Owners: i.ProcessInfo.Kind == CoreWebView2ProcessKind.Renderer
                    ? i.AssociatedFrameInfos.Select(f => frames.GetValueOrDefault((uint)f.FrameId)).Where(n => n != null).Distinct().Select(n => n!).ToList()
                    : new List<string>())).ToList();
            return await Task.Run(() =>
            {
                var own = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (var r in rows)
                {
                    long ws = ProcessTree.WorkingSetOf(r.Pid);
                    total += ws;
                    if (r.Owners.Count == 1) own[r.Owners[0]] = own.GetValueOrDefault(r.Owners[0]) + ws;   // renderer chung nhieu tai khoan: de phan chia deu
                }
                return ((Dictionary<string, long> Own, long Total)?)(own, total);
            });
        }
        catch (Exception ex) { _log.Warn("[web] do RAM loi: " + ex.GetType().Name); return null; }
    }

    public bool HasEnvironment => _env is { IsCompletedSuccessfully: true };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var s in _sessions.Values.ToList()) s.DisposeView();
        _sessions.Clear();
    }
}
