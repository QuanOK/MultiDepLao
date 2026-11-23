using System.Windows;
using System.Windows.Threading;
using MultiDepLao.Core;
using MultiDepLao.Win32;

namespace MultiDepLao.App;

/// <summary>Tai khoan Zalo Web (WebView2) trong cua so chinh: vong doi, hien/an, tin moi, RAM, cai WebView2.</summary>
public partial class MainWindow
{
    private const string ZaloWebUrl = "https://chat.zalo.me/";
    // Cua so WebView2 bi an thi Chromium han che timer/renderer nen (tieu de trang nhap nhay cham lai, mat tin hieu "tin moi").
    private const string ZaloWebBrowserArgs = "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows";

    private WebAccountHost? _webHost;

    private WebAccountHost Web => _webHost ??= CreateWebHost();

    private WebAccountHost CreateWebHost()
    {
        var h = new WebAccountHost(ZaloWebUrl, _svc.Paths.WebDataDir, new[] { "zalo.me" }, _svc.Log, ZaloWebBrowserArgs);
        h.BackgroundColor = ThemeManager.HostBackground();
        ThemeManager.Changed += () => Dispatcher.BeginInvoke(() => { if (_webHost != null) _webHost.BackgroundColor = ThemeManager.HostBackground(); });
        h.SessionStateChanged += OnWebSessionChanged;
        h.SessionNewMessage += OnWebNewMessage;
        h.SessionAllRead += OnWebAllRead;
        h.SessionUnreadUpdated += OnWebUnreadUpdated;
        return h;
    }

    // ------------------------------------------------------------ vong doi

    private async Task StartWebAsync(AccountViewModel vm)
    {
        if (_webHost?.Get(vm.Id) is { } cur)
        {
            if (cur.State != WebSessionState.Failed) return;
            _webHost.Close(vm.Id);   // loi: tao lai (tai lai trang)
        }
        if (!WebAccountHost.RuntimeAvailable())
        {
            vm.Apply(new InstanceStatus(vm.Id, InstanceState.Error, null, Loc.T("Web.NoRuntime")));
            return;
        }
        _svc.Log.Info($"[{vm.Id}] [web] mo");
        await Web.OpenAsync(vm.Id, WebLayer);
    }

    private void StopWeb(AccountViewModel vm)
    {
        _svc.Log.Info($"[{vm.Id}] [web] dong");
        _webHost?.Close(vm.Id);
        vm.HasUnread = false; vm.UnreadCount = null; vm.HasNotify = false;
        vm.HasWindow = false;
        vm.Apply(new InstanceStatus(vm.Id, InstanceState.Stopped, null, null));
        RefreshWebVisibility();
        UpdateTotal();
    }

    /// <summary>Mo cac tai khoan Web co AutoStart (lan luot, dung chung mot Environment).</summary>
    private async Task AutoStartWebAsync()
    {
        foreach (var vm in _accounts.Where(a => a.IsWeb).ToList())
        {
            var cfg = _svc.Profiles.Accounts.FirstOrDefault(a => a.Id.Equals(vm.Id, StringComparison.OrdinalIgnoreCase));
            if (cfg is not { AutoStart: true } || !vm.CanStart) continue;
            try { await StartAsync(vm); }
            catch (Exception ex) { _svc.Log.Error($"[{vm.Id}] [web] tu mo loi: {ex.GetType().Name}"); }
        }
    }

    private void OnWebSessionChanged(WebSession s)
    {
        if (Find(s.ProfileName) is not { } vm) return;
        switch (s.State)
        {
            case WebSessionState.Starting:
                vm.HasWindow = false;
                vm.Apply(new InstanceStatus(vm.Id, InstanceState.Starting, null, null));
                break;
            case WebSessionState.Running:
                vm.Apply(new InstanceStatus(vm.Id, InstanceState.Running, null, null));
                vm.HasWindow = true;   // an bang "dang cho cua so"; WebView2 hien ngay tai Host
                break;
            default:
                vm.HasWindow = false;
                vm.Apply(new InstanceStatus(vm.Id, InstanceState.Error, null,
                    Loc.F(s.View?.CoreWebView2 == null ? "Web.InitFail" : "Web.Crashed", s.FailReason ?? "?")));
                break;
        }
        RefreshWebVisibility();
        UpdateTotal();
    }

    // ------------------------------------------------------------ hien / an (airspace)

    /// <summary>
    /// Chi WebView2 cua tai khoan Web dang chon va dang chay moi hien. An khi khoa app (man hinh khoa ve trong cua so,
    /// WebView2 la HWND con nen se de len no va lo noi dung), khi thu nho / an xuong khay.
    /// Hop thoai va menu cua app la cua so rieng nen khong bi WebView2 che, khong can an.
    /// </summary>
    private void RefreshWebVisibility()
    {
        if (_webHost == null) return;
        string? sel = (AccountList.SelectedItem as AccountViewModel)?.Id;
        bool hidden = _inTray || !IsVisible || WindowState == WindowState.Minimized;
        foreach (var s in _webHost.Sessions)
        {
            if (s.View == null) continue;
            bool isSel = sel != null && s.ProfileName.Equals(sel, StringComparison.OrdinalIgnoreCase);
            bool show = WebAccountLogic.ShouldShow(isSel, s.State == WebSessionState.Running, _locked, hidden);
            var v = show ? Visibility.Visible : Visibility.Collapsed;
            if (s.View.Visibility != v) s.View.Visibility = v;
        }
    }

    // ------------------------------------------------------------ tin moi

    /// <summary>Nguoi dung mo tai khoan: bo dau "vua bao". Cham chua doc giu theo favicon cua Zalo (giong PC theo huy hieu
    /// Zalo), chi mat khi Zalo het tin chua doc (<see cref="OnWebAllRead"/>).</summary>
    private void ClearWebUnread(AccountViewModel vm) { vm.HasNotify = false; _toast?.Remove(vm.Id); }

    private bool UserIsLookingAt(AccountViewModel vm) =>
        ReferenceEquals(vm, AccountList.SelectedItem) && IsActive && !_locked && IsVisible && WindowState != WindowState.Minimized;

    private void OnWebAllRead(WebSession s)
    {
        if (Find(s.ProfileName) is not { } vm) return;
        vm.HasUnread = false; vm.UnreadCount = null; vm.HasNotify = false;
        _toast?.Remove(vm.Id);
    }

    /// <summary>So tin chua doc theo anh favicon (1..5, 5+); anh la -> null = chi hien cham.</summary>
    private void OnWebUnreadUpdated(WebSession s)
    {
        if (Find(s.ProfileName) is not { } vm || !s.Unread.HasUnread) return;
        vm.HasUnread = true; vm.UnreadCount = s.Unread.Count;
    }

    private ToastNotifier? _toast;

    private ToastNotifier Toast => _toast ??= new ToastNotifier(_svc.Paths.Root, m => _svc.Log.Warn(m),
        id => Dispatcher.BeginInvoke(() => OnToastActivated(id)));

    /// <summary>Tai khoan cua toast bam luc app dang khoa: chon sau khi mo khoa (ApplyPendingToastSelection).</summary>
    private string? _pendingToastId;

    /// <summary>Bam toast: hien cua so app; chon tai khoan (dang khoa -> chon sau khi mo khoa).</summary>
    private void OnToastActivated(string id)
    {
        try
        {
            _svc.Log.Info($"[{id}] toast: bam (khoa={_locked})");
            RestoreFromTray();
            if (_locked) { _pendingToastId = id; return; }
            _pendingToastId = null;
            if (Find(id) is { } vm) { AccountList.SelectedItem = vm; OpenChatAfterToast(vm); }
        }
        catch (Exception ex) { _svc.Log.Warn("bam toast: " + ex.GetType().Name); }
    }

    /// <summary>Goi ngay sau khi mo khoa.</summary>
    private void ApplyPendingToastSelection()
    {
        var id = _pendingToastId;
        _pendingToastId = null;
        if (id != null && Find(id) is { } vm) { AccountList.SelectedItem = vm; OpenChatAfterToast(vm); }
    }

    /// <summary>Thu nghiem: tai khoan PC + Thong bao kin -> "bam" popup da an bang thong diep chuot de Zalo mo dung chat.</summary>
    private void OpenChatAfterToast(AccountViewModel vm)
    {
        if (vm.IsWeb || _notify is not { HidePopups: true } n) return;
        var id = vm.Id;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            try
            {
                _svc.Log.Info($"[{id}] toast: mo chat -> {n.TryOpenPopupChat(id)}");
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    _svc.Log.Info($"[{id}] toast: sau mo chat: {_ctrl.MainWindowState(id)}");
                };
                t.Start();
            }
            catch (Exception ex) { _svc.Log.Warn("mo chat tu toast: " + ex.GetType().Name); }
        });
    }

    // ------------------------------------------------------------ thong bao kin cho tai khoan PC

    private readonly Dictionary<string, PcUnread> _pcLast = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _pcFirstSeen = new(StringComparer.OrdinalIgnoreCase);

    private bool PcWarmingUp(string id) =>
        !_pcFirstSeen.TryGetValue(id, out var t) || PcUnreadNotifyLogic.IsWarmingUp(t, DateTime.UtcNow);

    /// <summary>Goi tu UnreadWatcher (UI thread) khi trang thai huy hieu cua tai khoan PC doi. Luon cap nhat the; chi gui toast neu bat tuy chon.</summary>
    private void OnPcUnreadChanged(AccountViewModel vm, UnreadResult r)
    {
        if (vm.IsWeb) return;
        if (vm.State != InstanceState.Running) { _pcLast.Remove(vm.Id); _pcFirstSeen.Remove(vm.Id); _pcPopupToastAt.Remove(vm.Id); _toast?.Remove(vm.Id); return; }
        var now = new PcUnread(r.State switch
        {
            UnreadState.Has => PcUnreadKind.Has, UnreadState.None => PcUnreadKind.None, _ => PcUnreadKind.Unknown,
        }, r.Count);
        _pcLast.TryGetValue(vm.Id, out var prev);   // mac dinh Kind=Unknown (0): coi nhu chua co tin
        if (prev.Kind == PcUnreadKind.Unknown) prev = new PcUnread(PcUnreadKind.None, null);
        if (!_pcFirstSeen.ContainsKey(vm.Id)) _pcFirstSeen[vm.Id] = DateTime.UtcNow;   // lan quet dau cua phien chay nay
        bool warm = PcWarmingUp(vm.Id);
        _pcLast[vm.Id] = PcUnreadNotifyLogic.Merge(prev, now);
        if (now.Kind == PcUnreadKind.None) _toast?.Remove(vm.Id);
        if (!_svc.Profiles.Config.PcPrivateNotify) return;
        if (PcUnreadNotifyLogic.ShouldNotify(prev, now, UserIsLookingAt(vm), warm)) SendPcToast(vm, now.Count, false);
    }

    private readonly Dictionary<string, DateTime> _pcPopupToastAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Popup Zalo moi hien (da bi day ra ngoai man hinh khi "Thong bao kin" bat): tin hieu "co tin moi" nhanh nhat (khong doc gi
    /// tu popup). So lay theo so da biet (khong co -> chuoi khong so).
    /// </summary>
    private void OnPcPopupShown(AccountViewModel vm)
    {
        if (vm.IsWeb || !_svc.Profiles.Config.PcPrivateNotify) return;
        if (!PcUnreadNotifyLogic.ShouldNotifyOnHiddenPopup(UserIsLookingAt(vm), PcWarmingUp(vm.Id))) return;
        int? count = _pcLast.TryGetValue(vm.Id, out var last) && last.Kind == PcUnreadKind.Has ? last.Count : null;
        SendPcToast(vm, count, true);
    }

    private void SendPcToast(AccountViewModel vm, int? count, bool fromPopup)
    {
        string? n = PcUnreadNotifyLogic.CountText(count);
        string text = _locked ? Loc.F("Tray.NewMsg", AppInfo.DisplayName)
            : n != null ? Loc.F("Pc.NewMessages", vm.DisplayName, n) : Loc.F("Web.NewMessage", vm.DisplayName);
        var nowUtc = DateTime.UtcNow;
        if (fromPopup) _pcPopupToastAt[vm.Id] = nowUtc;
        // Quet so chua doc ngay sau toast do popup: cap nhat im lang cung Tag, khong bat banner lan 2.
        bool quiet = !fromPopup && PcUnreadNotifyLogic.ShouldQuietScanToast(_pcPopupToastAt.TryGetValue(vm.Id, out var t) ? t : null, nowUtc);
        bool sent = Toast.Send(vm.Id, AppInfo.DisplayName, text, ToastLogic.GroupPc, quiet);
        _svc.Log.Info($"[{vm.Id}] toast: gui ({n ?? "?"}) popup={fromPopup} suppress={(sent ? Toast.LastSuppressed : quiet)}");
        if (!sent) _tray?.Balloon(text);
        if (_inTray) _tray?.SetTip(text);
    }

    private void OnWebNewMessage(WebSession s)
    {
        if (Find(s.ProfileName) is not { } vm) return;
        vm.HasUnread = true; vm.UnreadCount = s.Unread.Count;
        if (UserIsLookingAt(vm)) return;
        // Tin chua doc co san luc vua mo phien (mo app/bat tai khoan): chi hien cham, khong bao.
        if (DateTime.UtcNow - s.OpenedAtUtc < TimeSpan.FromSeconds(20)) return;
        // Dang khoa: khong lo ten tai khoan.
        string text = _locked ? Loc.F("Tray.NewMsg", AppInfo.DisplayName) : Loc.F("Web.NewMessage", vm.DisplayName);
        // Toast thay the toast cu cung tai khoan (Tag); that bai -> balloon.
        bool sent = Toast.Send(vm.Id, AppInfo.DisplayName, text);
        _svc.Log.Info($"[{vm.Id}] toast: gui (?) popup=False suppress={(sent ? Toast.LastSuppressed : false)}");
        if (!sent) _tray?.Balloon(text);
        if (_inTray) _tray?.SetTip(text);
    }

    // ------------------------------------------------------------ RAM

    private async Task UpdateWebRamAsync()
    {
        var vms = _accounts.Where(a => a.IsWeb && a.State == InstanceState.Running).ToList();
        if (vms.Count == 0 || _webHost is not { HasEnvironment: true } host) return;
        if (await host.MeasureAsync() is not { } m) return;
        var split = WebAccountLogic.SplitRam(vms.Select(a => a.Id).ToList(), m.Own, m.Total);
        foreach (var a in vms)
            if (a.State == InstanceState.Running && split.TryGetValue(a.Id, out var b)) a.RamBytes = b;
    }

    // ------------------------------------------------------------ cai WebView2 (chi khi nguoi dung dong y)

    /// <summary>true = may da co WebView2 (san hoac vua cai xong). Hoi truoc khi cai; that bai/huy -> false.</summary>
    private async Task<bool> EnsureWebRuntimeAsync()
    {
        if (WebAccountHost.RuntimeAvailable()) return true;
        _ctrl.PushOverlay();
        try
        {
            if (!MessageDialog.Ask(this, Loc.T("Kind.Web"), Loc.T("Web.InstallAsk"), DialogKind.Question)) return false;
            var dlg = new WebInstallWindow(this);
            dlg.Show();
            InstallResult res;
            try
            {
                var progress = new Progress<string>(dlg.SetStage);   // tao tren UI thread
                _svc.Log.Info("[web] nguoi dung dong y cai WebView2");
                res = await WebView2Runtime.InstallAsync(_svc.Paths, progress, dlg.Token);
            }
            finally { dlg.CloseNow(); }
            if (res.Success)
            {
                _svc.Log.Info("[web] cai WebView2 xong");
                MessageDialog.Notify(this, Loc.T("Web.InstallTitle"), Loc.T("Web.InstallDone"), DialogKind.Success);
                return true;
            }
            _svc.Log.Warn($"[web] cai WebView2 that bai: {res.Failure}");
            if (res.Failure == InstallFailure.Cancelled) return false;
            var key = WebAccountLogic.InstallFailureKey(res.Failure);
            var reason = res.Failure == InstallFailure.ExitCode ? Loc.F(key, WebAccountLogic.ExitCodeOf(res.Detail)) : Loc.T(key);
            MessageDialog.Notify(this, Loc.T("Web.InstallTitle"), Loc.F("Web.InstallFail", reason) + "\n\n" + Loc.T("Web.AddFailHint"), DialogKind.Warning);
            return false;
        }
        finally { _ctrl.PopOverlay(); }
    }

    // ------------------------------------------------------------ thoat

    private void DisposeWeb()
    {
        var h = _webHost;
        if (h == null) return;
        if (Dispatcher.CheckAccess()) h.Dispose();
        else Dispatcher.BeginInvoke(h.Dispose);
    }
}
