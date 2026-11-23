using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MultiDepLao.Core;
using MultiDepLao.Win32;

namespace MultiDepLao.App;

public partial class MainWindow : Window
{
    private const double ZaloMinW = 550, ZaloMinH = 680, SidebarNarrow = 56; // DIP
    private double _sidebarWide = AppConfig.SidebarDefaultWidth;
    private double SidebarW => _sidebarCollapsed ? SidebarNarrow : _sidebarWide;
    private bool _sidebarCollapsed;
    private TrayController? _tray;
    private HotkeyController? _hotkeys;
    private bool _inTray;                       // dang an xuong khay (cua so Hide, WindowState van Minimized)
    private WindowState _lastNonMin = WindowState.Normal;
    private Point _dragStart;
    private AccountViewModel? _dragVm;
    private const string DragFormat = "mz.account";

    private readonly Services _svc;
    private readonly ObservableCollection<AccountViewModel> _accounts = new();
    private readonly GlueController _ctrl;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private long _lastFast;
    private bool _fixingPopup;
    private readonly DispatcherTimer _fastTick =new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _ramTick =new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _ownerTick = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _idleTick = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _noteTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly HashSet<string> _starting = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _lockTick = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _retryTick = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _locked, _unlockBusy;
    private bool _closeApproved, _closeFlow, _ramBusy, _ownerBusy, _idleBusy;
    private AccountViewModel? _ctxTarget;

    public MainWindow(Services svc)
    {
        _svc = svc;
        InitializeComponent();
        ThemeManager.Attach(this);
        _accounts.CollectionChanged += (_, _) => UpdateHeader();
        _ctrl = new GlueController(this, Host, svc.Manager, svc.Log);
        _ctrl.ZaloLeftEdgeDragStarted += OnZaloLeftEdgeDrag;
        AccountList.ItemsSource = _accounts;
        AccountList.ContextMenu = BuildMenu();

        _sidebarCollapsed = svc.Profiles.Config.SidebarCollapsed;
        _sidebarWide = svc.Profiles.Config.EffectiveSidebarWidth;
        foreach (var a in svc.Profiles.Accounts) AddVm(a);
        ApplySidebar(_sidebarCollapsed, false);
        UpdateHeader();
        UpdateTotal();
        _tray = new TrayController(RestoreFromTray, () => { if (!_locked) LockRequested(); },
            () => { RestoreFromTray(); Close(); }, () => _svc.Lock.HasPassword && !_locked,
            () => { RestoreFromTray(); OpenAbout(); }, () => { RestoreFromTray(); OpenDonate(); },
            () => { RestoreFromTray(); OpenWhatsNew(); });
        SourceInitialized += (_, _) =>
        {
            _hotkeys = new HotkeyController(new System.Windows.Interop.WindowInteropHelper(this).Handle,
                () => _accounts.Where(a => a.Pid is int).Select(a => (uint)a.Pid!.Value).ToList(),
                () => LockActive, i => SelectByIndex(i - 1), () => { if (!_locked) LockRequested(); }, svc.Log,
                d => { if (!_ctrl.OverlayActive) SelectRelative(d); });
            _hotkeys.Start();
            // Mo exe lan nua khi app dang o khay -> hien lai (thay vi khong co gi xay ra)
            System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(
                (IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    LogTaskbarMsg(hwnd, msg, w);
                    if (msg == 0x0006 && ((int)w & 0xFFFF) != 0 && !_fixingPopup && IsIconic(hwnd))
                        RestoreIfActivatedWhileMinimized(hwnd);
                    if (SingleInstance.ShowMessage != 0 && (uint)msg == SingleInstance.ShowMessage)
                    { handled = true; Dispatcher.BeginInvoke(RestoreFromTray); }
                    else if (TaskbarCreatedMsg != 0 && (uint)msg == TaskbarCreatedMsg)
                        Dispatcher.BeginInvoke(() => ResendTaskbarIcon(hwnd));
                    return IntPtr.Zero;
                });
        };
        // Khoi dong cung Windows: taskbar hoi icon (WM_GETICON) luc app dang ban -> het gio, dung icon mac dinh va khong hoi lai.
        // Gui lai WM_SETICON vai lan sau khi hien va moi lan hien lai tu khay de taskbar cap nhat.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is not true) return;
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            foreach (var sec in new[] { 1, 5, 20, 60 })
            {
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(sec) };
                t.Tick += (_, _) => { t.Stop(); ResendTaskbarIcon(h); };
                t.Start();
            }
        };
        svc.Manager.StateChanged += st => Dispatcher.BeginInvoke(() => OnStateChanged(st));
        svc.CopyProgress += OnCopyProgress;
        if (svc.Updates != null) { svc.Updates.Changed += s => Dispatcher.BeginInvoke(() => ApplyUpdate(s)); ApplyUpdate(svc.Updates.State); }

        Loaded += (_, _) =>
        {
            ApplyMinSize();
            if (_accounts.Count > 0 && AccountList.SelectedItem == null) AccountList.SelectedIndex = 0;
            RefreshRight(); UpdateTotal();
            Dispatcher.BeginInvoke(TryAutoShowWhatsNew, DispatcherPriority.Background);
            _ = AutoStartWebAsync();
        };
        IsVisibleChanged += (_, _) => RefreshWebVisibility();
        LocationChanged += (_, _) => _ctrl.SyncSelected(false);
        SizeChanged += (_, _) => { _ctrl.ResetSizeLogs(); _ctrl.SyncSelected(true); };
        Host.LayoutUpdated += (_, _) => _ctrl.SyncSelected(false);
        StateChanged += (_, _) =>
        {
            bool min = WindowState == WindowState.Minimized;
            if (!min) _lastNonMin = WindowState;
            _ctrl.HostMinimized(min);
            RefreshWebVisibility();
            LogTaskbarState(min);
            if (min) MakeMainLastActivePopup();
            if (min &&_svc.Profiles.Config.Lock.LockOnMinimize && LockActive) LockNow("thu nho");
            // nut "-" = thu nho binh thuong, VAN giu bieu tuong tren taskbar (nguoi dung yeu cau 2026-11-20, giong Teams);
            // icon khay luon co. Cua so Zalo bi an theo owner + HostMinimized. Chi --tray (mo cung Windows) moi an xuong khay.
        };
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control && !_locked) { e.Handled = true; LockRequested(); }
            else if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && !_locked && !_ctrl.OverlayActive)
            { e.Handled = true; SelectRelative((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); }
        };
        Activated += (_, _) =>
        {
            if (_fixingPopup) return;
            if (_locked) FocusPassword();
            else if (AccountList.SelectedItem is AccountViewModel { IsWeb: true } w) ClearWebUnread(w);   // dang xem tai khoan nay
            else if (AccountList.SelectedItem is AccountViewModel pc) _toast?.Remove(pc.Id);
        };
        _lockTick.Tick += (_, _) => LockTick();
        _retryTick.Tick += (_, _) => UpdateRetryUi();
        _lockTick.Start();
        if (LockActive && svc.Profiles.Config.Lock.LockOnStart) LockNow("mo app");
        _tick.Tick += (_, _) => { _ctrl.Tick(_accounts); };
        _ramTick.Tick += async (_, _) => await UpdateRamAsync();
        _ownerTick.Tick += (_, _) => RefreshCallOwnerTick();
        _idleTick.Tick += async (_, _) => await IdleSleepAsync();
        _noteTimer.Tick += (_, _) => { _noteTimer.Stop(); SleepNote.Visibility = Visibility.Collapsed; };
        svc.Governor.Slept += id => Dispatcher.BeginInvoke(() => OnGovernorSlept(id));
        _fastTick.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;   // chan doan: luong UI bi ket > 600 ms (nut taskbar khong phan hoi)
            if (_lastFast != 0 && now - _lastFast > 600) _svc.Log.Info($"[ui] luong UI tre {now - _lastFast} ms (minimized={WindowState == WindowState.Minimized})");
            _lastFast = now;
            _ctrl.FastTick(_accounts);
        };
        _fastTick.Start();
        _tick.Start(); _ramTick.Start(); _ownerTick.Start(); _idleTick.Start();
        Loc.Changed += OnLanguageChanged;

        // Thong bao tin moi: biet popup thuoc tien trinh nao -> gan ten tai khoan (nhan tren popup + dau o sidebar)
        _notify = new NotifyController(() => _accounts.Where(a => a.State == InstanceState.Running && a.Pid is int)
            .Select(a => (a.Id, a.DisplayName, a.Pid!.Value)).ToList(), svc.Log);
        _notify.PopupShown += id =>
        {
            var vm = Find(id);
            if (vm != null && !(ReferenceEquals(vm, AccountList.SelectedItem) && IsActive && !_locked)) vm.HasNotify = true;
            if (vm != null) OnPcPopupShown(vm);
            if (vm != null && _inTray) _tray?.SetTip(_locked ? Loc.F("Tray.NewMsg", AppInfo.DisplayName) : Loc.F("Tray.NewMsgFrom", AppInfo.DisplayName, vm.DisplayName));
        };
        _notify.Suppressed = _locked;
        _notify.HidePopups = svc.Profiles.Config.PcPrivateNotify;
        _notify.Start();
        // Bam popup cua tai khoan dang an: Zalo tu hien cua so chinh -> chuyen chon sang tai khoan do (khong an lai).
        _ctrl.PopupClickAt = id => _notify?.PopupClickAt(id);
        _ctrl.PopupDiag = id => _notify?.PopupDiag(id);
        _ctrl.PopupSwitch = id =>
        {
            _notify?.ConsumePopupClick(id);
            Dispatcher.BeginInvoke(() =>
            {
                if (_locked || _ctrl.OverlayActive || Find(id) is not { } vm) return;
                _svc.Log.Info($"[{id}] [notify] bam popup -> chuyen sang tai khoan");
                if (_inTray || !IsVisible || WindowState == WindowState.Minimized) RestoreFromTray();
                AccountList.SelectedItem = vm;
                BringToFront();
            });
        };

        // Huy hieu chua doc (UI Automation, chi hinh hoc) cho moi tai khoan dang chay
        if (svc.Profiles.Config.UnreadBadge)
        {
            _unread = new UnreadWatcher(() => _accounts.Where(a => a.State == InstanceState.Running && a.Pid is int)
                .Select(a => (a.Id, a.Pid!.Value)).ToList(), Dispatcher, svc.Log);
            _unread.Changed += (id, s) =>
            {
                var vm = Find(id);
                if (vm == null) return;
                if (s.State == UnreadState.Has) { vm.HasUnread = true; vm.UnreadCount = s.Count; }
                else if (s.State == UnreadState.None) { vm.HasUnread = false; vm.UnreadCount = null; }   // Unknown: giu nguyen
                OnPcUnreadChanged(vm, s);
            };
            _unread.Start();
        }
    }

    private NotifyController? _notify;
    private UnreadWatcher? _unread;

    private void OnGovernorSlept(string id)
    {
        _sleptId = id;
        SleepNote.Text = Loc.F("Footer.Slept", Find(id)?.DisplayName ?? id);
        SleepNote.Visibility = Visibility.Visible;
        _noteTimer.Stop(); _noteTimer.Start();
        UpdateTotal();
    }

    private async Task IdleSleepAsync()
    {
        if (_idleBusy || (_svc.Profiles.Config.AutoSleepMinutes ?? 0) <= 0) return;
        _idleBusy = true;
        try
        {
            var sel = _ctrl.Selected?.Id;
            if (sel != null) _svc.Governor.NoteSelected(sel);   // tai khoan dang xem coi nhu dang dung
            await _svc.Governor.SleepIdleAsync(_svc.Governor.Now, sel);
        }
        catch (Exception ex) { _svc.Log.Warn("tu cho ngu: " + ex.Message); }
        finally { _idleBusy = false; }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(() => { ApplyMinSize(); _ctrl.ResetSizeLogs(); _ctrl.SyncSelected(true); }, DispatcherPriority.Loaded);
    }

    /// <summary>Cua so app toi thieu sao cho vung Zalo >= 550x680 DIP.</summary>
    private void ApplyMinSize()
    {
        double extraW = Math.Max(0, ActualWidth - Root.ActualWidth), extraH = Math.Max(0, ActualHeight - Root.ActualHeight);
        MinWidth = SidebarW + ZaloMinW + extraW;
        // Root cao hon Host dung mot thanh lenh -> chrome cua so (extraH) khong gom thanh lenh, cong rieng.
        MinHeight = CommandBarMetrics.MinWindowHeight(ZaloMinH, extraH);
    }

    // ------------------------------------------------------------ danh sach tai khoan

    private void AddVm(AccountConfig a)
    {
        // Mot tai khoan loi (id hong...) khong duoc lam app khong khoi dong: ghi log (khong co id vi co the la rac) va bo qua.
        try { AddVmCore(a); }
        catch (Exception ex) { _svc.Log.Warn("bo qua tai khoan loi khi nap: " + ex.GetType().Name); }
    }

    private void AddVmCore(AccountConfig a)
    {
        var vm = new AccountViewModel(a.Id, a.DisplayName, a.Color, a.Kind) { Pinned = a.Pinned, Narrow = _sidebarCollapsed, Compact = !_sidebarCollapsed && SidebarMetrics.IsCompact(_sidebarWide) };
        // Tai khoan Web khong qua InstanceManager (khong chay Zalo.exe): trang thai do MainWindow.Web.cs quan ly.
        vm.Apply(a.Kind == AccountKind.Web ? new InstanceStatus(a.Id, InstanceState.Stopped, null, null) : _svc.Manager.GetStatus(a.Id));
        try { vm.LoadAvatar(_svc.Paths.AvatarFile(a.Id)); } catch (Exception ex) { _svc.Log.Warn($"[{a.Id}] nap anh dai dien: {ex.GetType().Name}"); }
        vm.PropertyChanged += (s, _) => { if (ReferenceEquals(s, _ctrl.Selected)) RefreshRight(); };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AccountViewModel.HasUnread)) UpdateHeader(); };
        _accounts.Add(vm);
    }

    // ------------------------------------------------------------ thu gon sidebar

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        ApplySidebar(!_sidebarCollapsed, true);
    }

    private void SidebarSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        => CommitSidebarWidth();

    // ---- keo mep trai cua cua so Zalo = keo sidebar (cua so Zalo bi huy keo, ta doc con tro + trang thai nut chuot)
    private DispatcherTimer? _edgeTimer;
    private DateTime _edgeStart;

    private void OnZaloLeftEdgeDrag()
    {
        if (_edgeTimer != null || _sidebarCollapsed || _locked) return;
        _edgeStart = DateTime.UtcNow;
        _edgeTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(15) };
        _edgeTimer.Tick += (_, _) => EdgeDragTick();
        _edgeTimer.Start();
        _svc.Log.Info("[sidebar] keo mep Zalo bat dau");
    }

    private void EdgeDragTick()
    {
        int vk = Native.GetSystemMetrics(23) != 0 ? 0x02 : 0x01;   // SM_SWAPBUTTON ? VK_RBUTTON : VK_LBUTTON
        bool down = (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
        if (!down || _locked || _sidebarCollapsed || (DateTime.UtcNow - _edgeStart).TotalSeconds > 30)
        { StopEdgeDrag(); return; }
        if (!Native.GetCursorPos(out var p)) return;
        try
        {
            var x = Root.PointFromScreen(new Point(p.X, p.Y)).X;
            SidebarCol.Width = new GridLength(SidebarDragMath.ComputeWidth(x, AppConfig.SidebarMinWidth,
                AppConfig.SidebarMaxWidth, Root.ActualWidth, ZaloMinW));
        }
        catch (InvalidOperationException) { StopEdgeDrag(); }
    }

    private void StopEdgeDrag()
    {
        if (_edgeTimer == null) return;
        _edgeTimer.Stop(); _edgeTimer = null;
        CommitSidebarWidth();
        _svc.Log.Info("[sidebar] keo mep Zalo ket thuc");
    }

    private void CommitSidebarWidth()
    {
        if (_sidebarCollapsed) return;
        _sidebarWide = AppConfig.ClampSidebarWidth(SidebarCol.ActualWidth);
        SidebarCol.Width = new GridLength(_sidebarWide);
        try { _svc.Profiles.SetSidebarWidth(_sidebarWide); }
        catch (Exception ex) { _svc.Log.Error("luu chieu rong sidebar: " + ex.Message); }
        ApplyMinSize();
        Dispatcher.BeginInvoke(() => { _ctrl.ResetSizeLogs(); _ctrl.SyncSelected(true); }, DispatcherPriority.Loaded);
    }

    private void ApplySidebar(bool collapsed, bool persist)
    {
        _sidebarCollapsed = collapsed;
        SidebarCol.MinWidth = collapsed ? 0 : AppConfig.SidebarMinWidth;
        SidebarCol.MaxWidth = collapsed ? SidebarNarrow : AppConfig.SidebarMaxWidth;
        SidebarCol.Width = new GridLength(SidebarW);
        SidebarSplitter.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarSplitter.IsEnabled = !collapsed;
        foreach (var v in _accounts) v.Narrow = collapsed;
        FooterInfo.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        MiniPanel.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        ListHeader.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        AccountList.Padding = collapsed ? new Thickness(0) : new Thickness(8, 0, 8, 0);
        CollapseGlyph.Text = collapsed ? "\uE76C" : "\uE76B";
        DockPanel.SetDock(CollapseButton, collapsed ? Dock.Top : Dock.Right);
        CollapseButton.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        CollapseButton.Margin = collapsed ? new Thickness(0, 10, 0, 6) : new Thickness(0, 10, 10, 8);
        AddButton.Width = collapsed ? 40 : double.NaN;
        AddButton.HorizontalAlignment = collapsed ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        AddButton.Padding = collapsed ? new Thickness(0) : new Thickness(16, 0, 16, 0);
        AddButton.Margin = collapsed ? new Thickness(0, 0, 0, 8) : new Thickness(10, 10, 6, 8);
        AddButton.ToolTip = collapsed ? Loc.T("Sidebar.Add") : null;
        ApplyCompact(collapsed ? SidebarNarrow : _sidebarWide);
        if (persist)
        {
            try { _svc.Profiles.SetSidebarCollapsed(collapsed); }
            catch (Exception ex) { _svc.Log.Error("luu thu gon sidebar: " + ex.Message); }
        }
        if (IsLoaded)
        {
            ApplyMinSize();
            // Vung Zalo doi kich thuoc sau khi layout xong -> dong bo lai (cung co che SizeChanged/LayoutUpdated).
            Dispatcher.BeginInvoke(() => { ApplyMinSize(); _ctrl.ResetSizeLogs(); _ctrl.SyncSelected(true); }, DispatcherPriority.Loaded);
        }
    }

    private void Sidebar_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyCompact(e.NewSize.Width);

    /// <summary>Che do chat (&lt; 240 DIP): ghi chu + thanh RAM an, o co nho, nut Them ngan.</summary>
    private void ApplyCompact(double width)
    {
        bool compact = !_sidebarCollapsed && SidebarMetrics.IsCompact(width);
        foreach (var v in _accounts) v.Compact = compact;
        AddLabel.Text = Loc.T(compact ? "Sidebar.AddShort" : "Sidebar.Add");
        AddLabel.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        _compactNow = compact;
        DonateCol.Width = compact ? GridLength.Auto : new GridLength(3, GridUnitType.Star);
        VersionCol.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(2, GridUnitType.Star);
        DonateLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DonateButton.MinWidth = compact ? 40 : 0;
        DonateButton.ToolTip = compact ? Loc.T("Donate.Title") : null;
        VersionLabel.Text = "v" + AppInfo.Version;
        MiniAbout.ToolTip = Loc.F("Footer.AboutTip", AppInfo.DisplayName, AppInfo.Version);
        UpdateTotal();
    }

    private bool _compactNow;

    private void DonateCard_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenDonate(); }
    private void AboutRow_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenAbout(); }
    private void UpdateHeader()
    {
        int unread = _accounts.Count(a => a.HasUnread);
        // Luon hien nhan PC/Web (nguoi dung 2026-11-20: "tai khoan PC chua co gi de nhan biet").
        foreach (var a in _accounts) a.ShowKind = true;
        HeaderCount.Text = Loc.F("Header.Accounts", _accounts.Count);
        HeaderUnread.Text = unread > 0 ? Loc.F("Header.Unread", unread) : "";
        UpdateTaskbarBadge(unread > 0);
    }

    private System.Windows.Media.DrawingImage? _badge;

    /// <summary>
    /// Cham do tren nut taskbar (overlay icon, ITaskbarList3 qua TaskbarItemInfo) khi co tai khoan con tin chua doc — nhu Teams.
    /// Chi la cham, khong so/noi dung. Windows tat "Show badges on taskbar buttons" thi khong hien.
    /// </summary>
    private void UpdateTaskbarBadge(bool on)
    {
        try
        {
            if (!on) { if (TaskbarItemInfo != null) TaskbarItemInfo.Overlay = null; return; }
            TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
            if (_badge == null)
            {
                var dot = new System.Windows.Media.GeometryDrawing(
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0x39, 0x35)),
                    new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 2),
                    new System.Windows.Media.EllipseGeometry(new Point(8, 8), 7, 7));
                _badge = new System.Windows.Media.DrawingImage(dot);
                _badge.Freeze();
            }
            if (!ReferenceEquals(TaskbarItemInfo.Overlay, _badge)) TaskbarItemInfo.Overlay = _badge;
        }
        catch (Exception ex) { _svc.Log.Warn("cham taskbar: " + ex.GetType().Name); }
    }

    // ------------------------------------------------------------ phim tat

    private void SelectByIndex(int i)
    {
        if (_locked || _ctrl.OverlayActive || i < 0 || i >= _accounts.Count) return;
        AccountList.SelectedIndex = i;
        BringToFront();
    }

    private void SelectRelative(int delta)
    {
        int n = _accounts.Count;
        if (_locked || n == 0) return;
        int cur = AccountList.SelectedIndex;
        SelectByIndex(cur < 0 ? 0 : ((cur + delta) % n + n) % n);
    }

    /// <summary>Phim tat bam khi focus o cua so Zalo (tien trinh khac): dua MultiDepLao (kem cua so Zalo owned) len truoc.</summary>
    private void BringToFront()
    {
        if (IsActive || !IsVisible || WindowState == WindowState.Minimized) return;
        try { Activate(); } catch { /* bo qua */ }
    }

    // ------------------------------------------------------------ khay he thong

    /// <summary>Hien lai tu khay: Show + khoi phuc trang thai truoc khi thu nho + gan lai cua so Zalo dang chon.</summary>
    /// <summary>Khoi dong voi --tray: an thang xuong khay (cua so Zalo khong duoc hien ra ngoai).</summary>
    public void StartInTray()
    {
        _inTray = true;
        WindowState = WindowState.Minimized;
        ShowActivated = false;
        Show();
        _ctrl.HostMinimized(true);
        Dispatcher.BeginInvoke(() => { if (_inTray && WindowState == WindowState.Minimized && !_closeFlow) Hide(); });
    }

    private string? _pendingCallHintId;

    /// <summary>Phien mo cung Windows: Zalo ngoai giu quyen goi. Thong bao khong chan; neu dang khoa thi hoan den khi mo khoa.</summary>
    public void ShowCallHint(string ownerId)
    {
        _pendingCallHintId = ownerId;
        if (!_locked) ShowPendingCallHint();
    }

    private void ShowPendingCallHint()
    {
        var id = _pendingCallHintId;
        if (id == null) return;
        _pendingCallHintId = null;
        Dispatcher.BeginInvoke(() =>
        {
            try { Info(Loc.T("Call.AutoFixTitle"), Loc.F("Call.AutoFixMsg", AppInfo.DisplayName, Find(id)?.DisplayName ?? id)); }
            catch (Exception ex) { _svc.Log.Warn("thong bao quyen goi: " + ex.GetType().Name); }
        });
    }

    private void OpenAbout()
    {
        if (_locked) return;
        _ctrl.PushOverlay();
        try { new AboutDialog(this, _svc.Paths.Root, _svc.IsPortable).ShowDialog(); }
        catch (Exception ex) { _svc.Log.Error("gioi thieu: " + ex.Message); }
        finally { _ctrl.PopOverlay(); }
    }

    private void OpenWhatsNew(string? since = null, bool onlyNew = false)
    {
        if (_locked) return;
        _ctrl.PushOverlay();
        try { new WhatsNewDialog(this, since, onlyNew).ShowDialog(); }
        catch (Exception ex) { _svc.Log.Error("co gi moi: " + ex.Message); }
        finally { _ctrl.PopOverlay(); }
    }

    private bool _whatsNewDone;

    /// <summary>Sau khi cap nhat: hien "Co gi moi" mot lan. Chi khi cua so dang hien, khong khoa, khong co hop thoai khac; con lai de lan sau.</summary>
    private void TryAutoShowWhatsNew()
    {
        if (_whatsNewDone || _locked || _inTray || !IsVisible || WindowState == WindowState.Minimized) return;
        if (Application.Current.Windows.OfType<Window>().Any(w => !ReferenceEquals(w, this) && w.IsVisible)) return;
        _whatsNewDone = true;
        var cfg = _svc.Profiles.Config;
        var seen = cfg.LastSeenVersion;
        bool show = ReleaseNotes.ShouldShow(seen, AppInfo.Version, _accounts.Count > 0);
        try { _svc.Profiles.SetLastSeenVersion(AppInfo.Version); }
        catch (Exception ex) { _svc.Log.Warn("luu phien ban da xem: " + ex.GetType().Name); }
        // Chua tung ghi phien ban da xem (cap nhat tu <= 0.3.3): chi hien ban hien tai, con lai bam "Xem tat ca".
        if (show) OpenWhatsNew(seen ?? (ReleaseNotes.All.Count > 1 ? ReleaseNotes.All[1].Version : null), onlyNew: true);
    }

    private void OpenDonate()
    {
        if (_locked) return;
        _ctrl.PushOverlay();
        try { new DonateDialog(this).ShowDialog(); }
        catch (Exception ex) { _svc.Log.Error("ung ho: " + ex.Message); }
        finally { _ctrl.PopOverlay(); }
    }

    private void RestoreFromTray()
    {
        _inTray = false;
        _tray?.ResetTip();
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = _lastNonMin == WindowState.Minimized ? WindowState.Normal : _lastNonMin;
        Activate();
        RefreshWebVisibility();
        if (WindowState != WindowState.Minimized)
        {
            _ctrl.HostMinimized(false);
            Dispatcher.BeginInvoke(() => { ApplyMinSize(); _ctrl.ResetSizeLogs(); _ctrl.SyncSelected(true); _ctrl.Tick(_accounts); }, DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(TryAutoShowWhatsNew, DispatcherPriority.Background);
        }
    }

    // ------------------------------------------------------------ icon taskbar

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetLastActivePopup(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetActiveWindow(IntPtr h);

    /// <summary>
    /// Khi bam nut taskbar cua cua so da thu nho, shell kich hoat "last active popup" cua no. Neu do la cua so Zalo (owned)
    /// DANG AN thi viec kich hoat co the that bai -> chi nghe "ting", bam lan 2 moi khoi phuc (nghi van, log 2026-11-20 09:25:
    /// lastPopup = cua so Zalo vis=False). Sau khi thu nho, neu lastPopup khong phai cua so chinh va dang an -> dat lai cua so chinh.
    /// </summary>
    private void MakeMainLastActivePopup()
    {
        try
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            var lp = GetLastActivePopup(h);
            if (lp == h || IsWindowVisible(lp)) return;
            _fixingPopup = true;   // WPF phat Activated dong bo: khong de no xoa chua doc / go toast khi cua so dang thu nho
            try { SetActiveWindow(h); } finally { _fixingPopup = false; }
            var after = GetLastActivePopup(h);
            _svc.Log.Info($"[taskbar] lastPopup an -> dat lai cua so chinh: {(after == h ? "ok" : "that bai")} iconic={IsIconic(h)}");
        }
        catch { /* bo qua */ }
    }    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr h);

    /// <summary>Chan doan "bam taskbar lan 1 chi nghe ding": ghi cua so "last active popup" (shell kich hoat cua so nay khi bam
    /// nut taskbar) luc thu nho / khoi phuc. Chi hinh hoc/trang thai, khong doc noi dung.</summary>
    private void LogTaskbarState(bool min)
    {
        try
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            _svc.Log.Info($"[taskbar] {(min ? "thu nho" : "khoi phuc")}: lastPopup={DescribePopup(h)} fg={DescribeWindow(h, GetForegroundWindow())}");
        }
        catch { /* chan doan, bo qua */ }
    }

    /// <summary>"main" / "acc-01(zalo)" / "app:lop" / "pid=N:lop" kem owner, rect, vis/en. Khong doc tieu de/noi dung.</summary>
    private string DescribePopup(IntPtr main)
    {
        var lp = GetLastActivePopup(main);
        return lp == main ? "main" : DescribeWindow(main, lp);
    }

    private string DescribeWindow(IntPtr main, IntPtr w)
    {
        if (w == IntPtr.Zero) return "0";
        if (w == main) return "main";
        var pid = Native.PidOf(w);
        var vm = _accounts.FirstOrDefault(a => a.Pid is int p && (uint)p == pid);
        var who = pid == (uint)Environment.ProcessId ? "app" : vm != null ? vm.Id : $"pid{pid}";
        var own = Native.GetWindow(w, Native.GW_OWNER);
        var r = Native.Rect(w);
        return $"0x{w.ToInt64():X}[{who}:{Native.ClassOf(w)} owner={(own == main ? "main" : $"0x{own.ToInt64():X}")} vis={IsWindowVisible(w)} en={IsWindowEnabled(w)} ({r.Left},{r.Top}) {r.Right - r.Left}x{r.Bottom - r.Top}]";
    }

    /// <summary>
    /// Thu that 2026-11-20 10:51 (log [taskbar]): lan bam taskbar binh thuong = ACTIVATE active roi SYSCOMMAND RESTORE trong ~10 ms;
    /// lan bi "ting" = shell chi KICH HOAT cua so dang thu nho (ACTIVATE active, van iconic) ma khong gui RESTORE — lan bam 2
    /// moi co RESTORE (tre 0,55–1,3 s). Sua: cua so bi kich hoat khi dang thu nho (khong phai do app tu SetActiveWindow) ->
    /// cho 150 ms (de RESTORE binh thuong toi truoc), neu van thu nho va dang foreground thi tu khoi phuc.
    /// </summary>
    private void RestoreIfActivatedWhileMinimized(IntPtr hwnd)
    {
        if (_closeFlow || _inTray) return;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            try
            {
                if (_closeFlow || _inTray || !IsIconic(hwnd) || GetForegroundWindow() != hwnd) return;
                _svc.Log.Info("[taskbar] kich hoat khi dang thu nho, khong co RESTORE -> tu khoi phuc");
                // Gui dung lenh shell van gui (SC_RESTORE) de di dung duong khoi phuc binh thuong (ke ca cua so owned).
                PostMessage(hwnd, 0x0112 /*WM_SYSCOMMAND*/, (IntPtr)0xF120 /*SC_RESTORE*/, IntPtr.Zero);
            }
            catch { /* bo qua */ }
        };
        t.Start();
    }

    /// <summary>1 dong/lan: nhan biet lan bam taskbar nao bi tu choi (WM_SYSCOMMAND/ACTIVATEAPP/ACTIVATE cua cua so chinh).</summary>
    private void LogTaskbarMsg(IntPtr hwnd, int msg, IntPtr w)
    {
        try
        {
            string? s = msg switch
            {
                0x0112 when ((int)w & 0xFFF0) is 0xF020 or 0xF120 or 0xF030 => $"SYSCOMMAND {(((int)w & 0xFFF0) == 0xF020 ? "MINIMIZE" : ((int)w & 0xFFF0) == 0xF120 ? "RESTORE" : "MAXIMIZE")}",
                0x001C => $"ACTIVATEAPP {(w != IntPtr.Zero)}",
                0x0006 => $"ACTIVATE {((int)w & 0xFFFF) switch { 0 => "inactive", 1 => "active", _ => "click" }}",
                _ => null,
            };
            if (s != null && (msg == 0x0112 || IsIconic(hwnd) || GetLastActivePopup(hwnd) != hwnd)) _svc.Log.Info($"[taskbar] msg {s} minimized={IsIconic(hwnd)} lastPopup={DescribePopup(hwnd)} fg={DescribeWindow(hwnd, GetForegroundWindow())}");
        }
        catch { /* chan doan */ }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);

    /// <summary>Explorer khoi dong lai -> taskbar tao lai nut, can gui lai icon.</summary>
    private static readonly uint TaskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");

    /// <summary>Lay icon WPF da gan cho cua so (WM_GETICON) va gui lai WM_SETICON cung handle -> taskbar ve lai icon.</summary>
    private static void ResendTaskbarIcon(IntPtr hwnd)
    {
        const uint WM_GETICON = 0x7F, WM_SETICON = 0x80;
        if (hwnd == IntPtr.Zero) return;
        for (int kind = 0; kind <= 1; kind++) // 0 = ICON_SMALL, 1 = ICON_BIG
        {
            var ic = SendMessage(hwnd, WM_GETICON, (IntPtr)kind, IntPtr.Zero);
            if (ic != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, (IntPtr)kind, ic);
        }
    }

    // ------------------------------------------------------------ keo tha sap xep

    private AccountViewModel? VmAt(object? src) =>
        src is DependencyObject d && ItemsControl.ContainerFromElement(AccountList, d) is ListBoxItem it ? it.DataContext as AccountViewModel : null;

    private void AccountList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragVm = VmAt(e.OriginalSource);
        _dragStart = e.GetPosition(AccountList);
    }

    private void AccountList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragVm = null;

    private void AccountList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragVm == null || e.LeftButton != MouseButtonState.Pressed || _locked) return;
        var p = e.GetPosition(AccountList);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var vm = _dragVm; _dragVm = null;
        try { DragDrop.DoDragDrop(AccountList, new DataObject(DragFormat, vm.Id), DragDropEffects.Move); }
        catch (Exception ex) { _svc.Log.Warn("keo tha: " + ex.Message); }
    }

    private void AccountList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void AccountList_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.Data.GetDataPresent(DragFormat) || e.Data.GetData(DragFormat) is not string id || Find(id) is not { } vm) return;
        var target = VmAt(e.OriginalSource);
        int from = _accounts.IndexOf(vm), to = target != null ? _accounts.IndexOf(target) : _accounts.Count - 1;
        if (from < 0 || to < 0 || from == to) return;
        var sel = AccountList.SelectedItem;
        _accounts.Move(from, to);
        if (sel != null && !ReferenceEquals(AccountList.SelectedItem, sel)) AccountList.SelectedItem = sel;
        try { _svc.Profiles.Reorder(_accounts.Select(a => a.Id).ToList()); }
        catch (Exception ex) { _svc.Log.Error("luu thu tu: " + ex.Message); }
    }

    private AccountViewModel? Find(string id) => _accounts.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private void OnStateChanged(InstanceStatus st)
    {
        Find(st.Id)?.Apply(st);
        UpdateTotal();
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var r in e.RemovedItems.OfType<AccountViewModel>()) _svc.Governor.NoteSelected(r.Id);
        var vm = AccountList.SelectedItem as AccountViewModel;
        _ctrl.SetSelected(vm);
        RefreshRight();
        RefreshWebVisibility();
        if (vm == null) return;
        vm.HasNotify = false;
        if (vm.IsWeb) { ClearWebUnread(vm); return; }
        _toast?.Remove(vm.Id);   // thong bao kin cua tai khoan PC (neu co)
        _svc.Governor.NoteSelected(vm.Id);
        // Tai khoan bi governor cho ngu: chon lai thi tu bat (cho ngu tai khoan khac neu can).
        if (vm.CanStart && _svc.Governor.IsSleptByGovernor(vm.Id)) _ = StartSafeAsync(vm);
    }

    private async Task StartSafeAsync(AccountViewModel vm)
    {
        try { await StartAsync(vm); }
        catch (Exception ex) { _svc.Log.Error($"[{vm.Id}] bat loi: {ex.Message}"); }
    }

    private void AccountList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _ctxTarget = null;
        if (e.OriginalSource is DependencyObject d)
        {
            var item = ItemsControl.ContainerFromElement(AccountList, d) as ListBoxItem;
            if (item?.DataContext is AccountViewModel vm) { item.IsSelected = true; _ctxTarget = vm; }
        }
    }

    private ContextMenu BuildMenu()
    {
        var m = new ContextMenu();
        MenuItem Item(string textKey, Func<AccountViewModel, Task> act, string glyph = "", bool danger = false)
        {
            var mi = new MenuItem { Header = Loc.T(textKey) };
            if (glyph.Length > 0)
            {
                var ic = new TextBlock { Text = glyph, FontSize = 15, Style = (Style)FindResource("Glyph") };
                if (danger) ic.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Err");
                mi.Icon = ic;
            }
            if (danger) mi.SetResourceReference(Control.ForegroundProperty, "Err");
            mi.Click += async (_, _) =>
            {
                if (_ctxTarget is not { } vm) return;
                try { await act(vm); }
                catch (Exception ex) { _svc.Log.Error($"[{vm.Id}] {textKey}: {ex.GetType().Name}: {ex.Message}"); Info(AppInfo.DisplayName, Loc.F("Err.Action", Loc.T(textKey), ex.Message)); }
            };
            m.Items.Add(mi);
            return mi;
        }
        var start = Item("Cmd.Start", StartAsync, "\uE768");
        var stop = Item("Cmd.Sleep", StopAsync, "\uE708");
        Func<AccountViewModel, Task> doRestart = RestartAsync;
        var restart = Item("Cmd.Restart", doRestart, "\uE72C");
        var restartUpd = Item("Cmd.RestartUpdate", doRestart, "\uE895");
        var sw = Item("Menu.SwitchCall", SwitchCallAsync, "\uE717");
        var capSet = Item("Menu.CapSet", vm => SwitchCaptureAsync(vm, true), "\uE722");
        var capClear = Item("Menu.CapClear", vm => SwitchCaptureAsync(vm, false), "\uE722");
        var reattach = Item("Menu.Reattach", ReattachAsync, "\uE71B");
        var auto = new MenuItem { Header = Loc.T("Menu.AutoStart"), IsCheckable = true };
        auto.Click += (_, _) =>
        {
            if (_ctxTarget is not { } vm) return;
            try { _svc.Profiles.SetAutoStart(vm.Id, auto.IsChecked); }
            catch (Exception ex) { _svc.Log.Error($"[{vm.Id}] autostart: {ex.Message}"); }
        };
        m.Items.Add(auto);
        var pin = new MenuItem { Header = Loc.T("Menu.Pin"), IsCheckable = true };
        pin.Click += (_, _) =>
        {
            if (_ctxTarget is not { } vm) return;
            try { _svc.Profiles.SetPinned(vm.Id, pin.IsChecked); vm.Pinned = pin.IsChecked; }
            catch (Exception ex) { _svc.Log.Error($"[{vm.Id}] ghim: {ex.Message}"); }
        };
        m.Items.Add(pin);
        var rename = Item("Menu.Rename", vm => { Rename(vm); return Task.CompletedTask; }, "\uE8AC");
        Item("Menu.Color", vm => { PickColor(vm); return Task.CompletedTask; }, "\uE790");
        Item("Menu.AvatarSet", vm => { PickAvatar(vm); return Task.CompletedTask; }, "");
        var avatarClear = Item("Menu.AvatarClear", vm => { ClearAvatar(vm); return Task.CompletedTask; }, "");
        var delete = Item("Menu.Delete", vm => { Delete(vm); return Task.CompletedTask; }, "\uE74D", danger: true);
        // Luu y: ContextMenuOpening phat tren phan tu so huu menu (ListBox), KHONG phat tren ContextMenu
        // -> phai dong bo trang thai cac muc trong Opened.
        bool pushed = false;   // chi Pop khi da Push (chuot phai vao vung trong: dong menu ngay, khong Push)
        m.Opened += (_, _) =>
        {
            if (_ctxTarget == null) { m.IsOpen = false; return; }
            if (!pushed) { pushed = true; _ctrl.PushOverlay(); }   // Opened lap lai khong duoc dem 2 lan
            var v = _ctxTarget;
            start.IsEnabled = v.CanStart;
            stop.IsEnabled = v.State is InstanceState.Starting or InstanceState.Running;
            restart.IsEnabled = v.State == InstanceState.Running;
            restartUpd.Visibility = v.NeedsUpdate ? Visibility.Visible : Visibility.Collapsed;
            restartUpd.IsEnabled = v.State == InstanceState.Running;
            delete.IsEnabled = v.CanStart || v.IsWeb;   // Web xoa duoc ca khi dang chay (huy WebView2 + xoa profile)
            avatarClear.Visibility = v.HasAvatarImage ? Visibility.Visible : Visibility.Collapsed;
            sw.IsEnabled = !v.IsCallOwner;
            bool capBusy = _svc.CaptureOwner.IsBusy;
            capSet.Visibility = v.IsCaptureOwner && v.CaptureActive ? Visibility.Collapsed : Visibility.Visible;
            capSet.IsEnabled = !capBusy;
            capClear.Visibility = v.IsCaptureOwner ? Visibility.Visible : Visibility.Collapsed;
            capClear.IsEnabled = !capBusy;
            reattach.IsEnabled = v.State == InstanceState.Running;
            if (v.IsWeb)   // quyen goi/chup/gan lai chi co nghia voi Zalo PC
                sw.Visibility = capSet.Visibility = capClear.Visibility = reattach.Visibility = Visibility.Collapsed;
            else
                sw.Visibility = reattach.Visibility = Visibility.Visible;
            var cfgAcc = _svc.Profiles.Accounts.FirstOrDefault(a => a.Id.Equals(v.Id, StringComparison.OrdinalIgnoreCase));
            auto.IsChecked = cfgAcc?.AutoStart ?? false;
            pin.IsChecked = cfgAcc?.Pinned ?? false;
        };
        m.Closed += (_, _) => { if (pushed) { pushed = false; _ctrl.PopOverlay(); } };
        return m;
    }

    // ------------------------------------------------------------ hanh dong

    private async Task RestartAsync(AccountViewModel vm)
    {
        if (vm.IsWeb) { await StopAsync(vm); await StartAsync(vm); return; }
        var id = vm.Id;
        var old = vm.Pid is int p ? ProcessTree.Descendants((uint)p) : new HashSet<uint>();
        await StopAsync(vm);
        if (!await WaitRestartReadyAsync(id, old, TimeSpan.FromSeconds(15)))
        {
            _svc.Log.Warn($"[{id}] khoi dong lai: instance cu chua nha sau 15 s, khong bat lai");
            return;
        }
        for (int retries = 0; ; retries++)
        {
            await StartCoreAsync(vm, force: true);
            // Theo doi den khi len Running/het thoi han Starting som; thoat ngay -> thu lai 1 lan.
            var until = DateTime.UtcNow.AddSeconds(8);
            InstanceStatus st;
            while ((st = _svc.Manager.GetStatus(id)).State == InstanceState.Starting && DateTime.UtcNow < until)
                await Task.Delay(250);
            if (!RestartPolicy.ShouldRetry(st.State, st.Reason, retries)) return;
            _svc.Log.Warn($"[{id}] khoi dong lai: Zalo thoat ngay, thu lai sau 2 s");
            await Task.Delay(2000);
            if (!await WaitRestartReadyAsync(id, old, TimeSpan.FromSeconds(10))) return;
        }
    }

    /// <summary>Cho manager bao Stopped/Error VA cac tien trinh Zalo cu (snapshot truoc khi tat) da thoat.</summary>
    private async Task<bool> WaitRestartReadyAsync(string id, HashSet<uint> oldPids, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            bool ready = RestartPolicy.ReadyToStart(_svc.Manager.GetStatus(id).State) && !oldPids.Any(IsZaloAlive);
            if (ready) { await Task.Delay(300); return true; }   // chut thoi gian cho khoa profile nha
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(200);
        }
    }

    private static bool IsZaloAlive(uint pid)
    {
        try { using var pr = System.Diagnostics.Process.GetProcessById((int)pid); return !pr.HasExited && pr.ProcessName.StartsWith("Zalo", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private Task ReattachAsync(AccountViewModel vm)
    {
        if (!_ctrl.Reattach(vm.Id)) Info(AppInfo.DisplayName, Loc.T("Act.ReattachFail"));
        return Task.CompletedTask;
    }

    private Task StartAsync(AccountViewModel vm) => StartCoreAsync(vm, false);

    /// <summary>force: bo qua vm.CanStart (VM cap nhat bat dong bo qua dispatcher) - chi dung khi da kiem tra manager.</summary>
    private async Task StartCoreAsync(AccountViewModel vm, bool force)
    {
        if ((!force && !vm.CanStart) || !_starting.Add(vm.Id)) return;
        try
        {
            _svc.Log.Info($"[{vm.Id}] nguoi dung bam Bat");
            if (vm.IsWeb) { await StartWebAsync(vm); return; }   // khong qua governor/CallOwner/InstanceManager (khong chay Zalo.exe)
            // Tinh ca tai khoan goi dien se bi bat truoc; governor nhuong cho truoc khi bat.
            var ids = _svc.CallOwner.IdsThatWillStart(vm.Id);
            var sel = _ctrl.Selected?.Id;
            var res = await _svc.Governor.EnsureRoomAsync(ids, sel);
            if (res.Plan.Insufficient)
            {
                int n = _svc.Governor.EffectiveMax;
                if (!Confirm(Loc.T("Act.LimitTitle"), Loc.F("Act.LimitMsg", n)))
                    return;
                _svc.Log.Info($"[{vm.Id}] nguoi dung chap nhan bat vuot gioi han {n}");
                await _svc.Governor.EnsureRoomAsync(ids, sel, force: true);
            }
            _svc.Governor.ClearSlept(vm.Id);
            await _svc.CallOwner.StartAsync(vm.Id); // xep hang, bat chu quyen goi truoc; chay ngoai UI thread
            foreach (var i in ids) _svc.Governor.NoteSelected(i);
        }
        finally { _starting.Remove(vm.Id); }
    }

    private bool Confirm(string title, string msg)
    {
        _ctrl.PushOverlay();
        try { return MessageDialog.Ask(this, title, msg, DialogKind.Warning); }
        finally { _ctrl.PopOverlay(); }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings(false);

    private void OpenSettings(bool focusLock)
    {
        if (_locked) return;
        var cfg = _svc.Profiles.Config;
        bool hadPw = _svc.Lock.HasPassword;
        bool autoStartNow = false;
        try { autoStartNow = _svc.AutoStart.IsEnabled; }
        catch (Exception ex) { _svc.Log.Warn("doc trang thai khoi dong cung Windows: " + ex.GetType().Name); }
        bool zaloRunEntry = false;
        try { zaloRunEntry = new RegistryRunKeyReader().HasZaloEntry(); }
        catch (Exception ex) { _svc.Log.Warn("doc muc Run cua Zalo: " + ex.GetType().Name); }
        _ctrl.PushOverlay();
        try
        {
            var dlg = new SettingsDialog(this, cfg.MaxRunning, _svc.Governor.AutoMax, cfg.AutoSleepMinutes, _accounts.Count(a => a.IsActive && !a.IsWeb),
                _svc.Lock, cfg.Lock, focusLock,
                Loc.F("Cap.Line", _svc.CaptureOwner.OwnerId is { } co ? (Find(co)?.DisplayName ?? co) : Loc.T("Cap.None")), cfg.Theme, cfg.Accent,
                autoStartNow, w => new AboutDialog(w,_svc.Paths.Root, _svc.IsPortable).ShowDialog(), cfg.Language, zaloRunEntry, cfg.PcPrivateNotify) { Owner = this };
            bool ok = dlg.ShowDialog() == true;
            bool hasPw = _svc.Lock.HasPassword;
            var l = cfg.Lock;
            if (ok)
            {
                _svc.Profiles.SetTheme(dlg.Theme);
                ThemeManager.Apply(dlg.Theme);
                _svc.Profiles.SetAccent(dlg.Accent);
                ThemeManager.ApplyAccent(dlg.Accent);
                _svc.Profiles.SetLanguage(dlg.Lang);
                Loc.Set(dlg.Lang);   // doi ngay: cua so chinh, khay, view model (qua Loc.Changed)
                if (dlg.AutoStart != autoStartNow) ApplyAutoStart(dlg.AutoStart);
                _svc.Profiles.SetPcPrivateNotify(dlg.PcNotify);
                if (_notify != null) _notify.HidePopups = dlg.PcNotify;
                _svc.Profiles.SetMaxRunning(dlg.MaxRunning);
                _svc.Profiles.SetAutoSleepMinutes(dlg.AutoSleepMinutes);
                _svc.Profiles.SetLock(hasPw && dlg.LockEnabled, dlg.LockOnStart, dlg.LockOnMinimize, dlg.LockIdleMinutes);
                _svc.Log.Info($"cai dat: maxRunning={(dlg.MaxRunning?.ToString() ?? "auto")} autoSleep={(dlg.AutoSleepMinutes?.ToString() ?? "off")} " +
                              $"khoa={(hasPw && dlg.LockEnabled)} idle={dlg.LockIdleMinutes} accent={AppConfig.NormalizeAccent(dlg.Accent)}");
            }
            else if (hasPw != hadPw)   // mat khau da doi ngay trong hop thoai du bam Huy: giu cau hinh nhat quan
                _svc.Profiles.SetLock(hasPw, l.LockOnStart, l.LockOnMinimize, l.IdleMinutes);
            UpdateTotal();
        }
        catch (Exception ex) { _svc.Log.Error("cai dat: " + ex.Message); }
        finally { _ctrl.PopOverlay(); _hotkeys?.Refresh(); }
    }

    private void ApplyAutoStart(bool on)
    {
        try
        {
            if (on) _svc.AutoStart.Enable(Environment.ProcessPath ?? throw new InvalidOperationException("khong biet duong dan exe"));
            else _svc.AutoStart.Disable();
            _svc.Log.Info("khoi dong cung Windows: " + (on ? "bat" : "tat"));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            _svc.Log.Warn("khoi dong cung Windows: khong ghi duoc (" + ex.GetType().Name + ")");
            MessageDialog.Notify(this, Loc.T("Act.AutoStartFailTitle"), Loc.T("Act.AutoStartFail"), DialogKind.Warning);
        }
    }

    // ------------------------------------------------------------ khoa app

    private bool LockActive => _svc.Profiles.Config.Lock.Enabled && _svc.Lock.HasPassword;

    private void LockButton_Click(object sender, RoutedEventArgs e) => LockRequested();

    private void LockRequested()
    {
        if (_locked) return;
        if (_svc.Lock.HasPassword) LockNow("nguoi dung");
        else OpenSettings(true);
    }


    private void LockTick()
    {
        if (_locked || _closeFlow || !LockActive) return;
        int min = _svc.Profiles.Config.Lock.IdleMinutes;
        var idle = IdleTime.SinceLastInput();
        if (min <= 0 || _ctrl.OverlayActive) return;   // co hop thoai dang mo: thu lai o luot sau
        if (idle >= TimeSpan.FromMinutes(min)) LockNow("roi");
    }

    private void LockNow(string reason)
    {
        if (_locked) return;
        _locked = true;
        RefreshWebVisibility();   // truoc khi hien man hinh khoa: WebView2 (HWND con) se de len va lo noi dung Zalo Web
        _ctrl.SetLocked(true);
        if (_notify != null) _notify.Suppressed = true;   // khong lo ten tai khoan khi dang khoa
        Root.IsEnabled = false;
        LockOverlay.Visibility = Visibility.Visible;
        LockPassword.Clear(); LockError.Text = ""; UnlockButton.IsEnabled = true;
        _svc.Log.Info("khoa app: " + reason);
        UpdateRetryUi();
        FocusPassword();
    }

    private void FocusPassword() =>
        Dispatcher.BeginInvoke(() => { if (_locked && LockPassword.IsEnabled) { LockPassword.Focus(); Keyboard.Focus(LockPassword); } }, DispatcherPriority.Input);

    private void UpdateRetryUi()
    {
        if (!_locked) { _retryTick.Stop(); return; }
        if (_svc.Lock.RetryAfter is { } w)
        {
            _retryMsgShown = true;
            LockError.Text = Loc.F("Lock.TooMany", Math.Ceiling(w.TotalSeconds).ToString("0"));
            LockPassword.IsEnabled = UnlockButton.IsEnabled = false;
            if (!_retryTick.IsEnabled) _retryTick.Start();
        }
        else
        {
            _retryTick.Stop();
            if (!_unlockBusy) { LockPassword.IsEnabled = UnlockButton.IsEnabled = true; if (_retryMsgShown) { _retryMsgShown = false; LockError.Text = ""; } FocusPassword(); }
        }
    }

    private void LockPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; _ = TryUnlockAsync(); }
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e) => _ = TryUnlockAsync();

    private async Task TryUnlockAsync()
    {
        if (!_locked || _unlockBusy || _svc.Lock.RetryAfter != null) return;
        _unlockBusy = true;
        LockPassword.IsEnabled = UnlockButton.IsEnabled = false;
        string pw = LockPassword.Password;
        bool ok = false;
        try { ok = await Task.Run(() => _svc.Lock.Verify(pw)); }
        catch (Exception ex) { _svc.Log.Error("mo khoa: " + ex.GetType().Name); }
        _unlockBusy = false;
        LockPassword.Clear();
        if (ok)
        {
            _locked = false; _retryTick.Stop();
            LockOverlay.Visibility = Visibility.Collapsed; LockError.Text = "";
            LockPassword.IsEnabled = UnlockButton.IsEnabled = true;
            Root.IsEnabled = true;
            _ctrl.SetLocked(false);
            RefreshWebVisibility();
            ApplyPendingToastSelection();
            if (AccountList.SelectedItem is AccountViewModel { IsWeb: true } sw) ClearWebUnread(sw);
            if (_notify != null) _notify.Suppressed = false;
            _svc.Log.Info("mo khoa app");
            ShowPendingCallHint();
            Dispatcher.BeginInvoke(TryAutoShowWhatsNew, DispatcherPriority.Background);
            return;
        }
        LockPassword.IsEnabled = UnlockButton.IsEnabled = true;
        _retryMsgShown = false;
        LockError.Text = Loc.T("Lock.Wrong");
        UpdateRetryUi();
        if (_svc.Lock.RetryAfter == null) FocusPassword();
    }
    private async Task SwitchCallAsync(AccountViewModel vm)
    {
        var restart = _svc.CallOwner.PreviewSwitch(vm.Id)
            .Select(id => Find(id)?.DisplayName ?? id).ToList();
        var msg = Loc.F("Call.Confirm", vm.DisplayName) +
            (vm.IsActive ? Loc.F("Call.TargetRestart", vm.DisplayName) : "") +
            (restart.Count > 0 ? Loc.F("Call.OthersRestart", string.Join(", ", restart.Select(n => $"'{n}'"))) : "") +
            Loc.T("Call.ConfirmTail");
        _ctrl.PushOverlay();
        try
        {
            if (!MessageDialog.Ask(this, Loc.T("Call.Title"), msg, DialogKind.Warning)) return;
            var busy = new BusyWindow(this, Loc.T("Call.Title"), Loc.T("Call.Busy"));
            busy.Show();
            SwitchResult res;
            try
            {
                var prog = new Progress<string>(s => busy.SetDetail(SwitchProgressText.Describe(s, id => Find(id)?.DisplayName, vm.DisplayName)));
                _svc.Log.Info($"[{vm.Id}] nguoi dung chon chuyen quyen goi");
                res = await Task.Run(() => _svc.CallOwner.SwitchToAsync(vm.Id, prog));
            }
            finally { busy.CloseNow(); }
            string text = res.Success
                ? Loc.F("Call.Done", vm.DisplayName, res.Elapsed.TotalSeconds.ToString("0"))
                : Loc.F("Call.Fail", res.Reason ?? "") +
                  (res.Reason == Loc.F("Call.ReasonForeign", AppInfo.DisplayName) ? Loc.F("Call.FailHint", AppInfo.DisplayName) : "");
            MessageDialog.Notify(this, Loc.T("Call.Title"), text, res.Success ? DialogKind.Success : DialogKind.Warning);
        }
        finally { _ctrl.PopOverlay(); RefreshCallOwner(); }
    }

    private async Task SwitchCaptureAsync(AccountViewModel vm, bool enable)
    {
        if (_svc.CaptureOwner.IsBusy) return;
        string? targetId = enable ? vm.Id : null;
        string title = Loc.T("Cap.Title");
        if (enable)
        {
            if (_svc.CaptureOwner.ForeignCaptureReason() != null)
            {
                Info(title, Loc.F("Cap.Foreign", AppInfo.DisplayName));
                return;
            }
            var oldId = _svc.CaptureOwner.OwnerId;
            var oldName = oldId != null && !oldId.Equals(vm.Id, StringComparison.OrdinalIgnoreCase) ? (Find(oldId)?.DisplayName ?? oldId) : null;
            string msg = vm.IsActive
                ? Loc.F("Cap.RestartMsg", vm.DisplayName)
                : Loc.F("Cap.OnStartMsg", vm.DisplayName);
            if (oldName != null) msg += Loc.F("Cap.OldLoses", oldName);
            if (vm.IsCallOwner && vm.IsActive) msg += Loc.T("Cap.CallHolderNote");
            // Da kiem thu: phim tat chup (Alt+Ctrl+S) thuoc Zalo khoi dong truoc = Zalo giu quyen goi
            if (!vm.IsCallOwner) msg += Loc.T("Cap.HotkeyNote");
            if (!Confirm(title, msg + Loc.T("Cap.Continue"))) return;
        }
        else if (!Confirm(title, Loc.F("Cap.ClearConfirm", vm.DisplayName)))
            return;

        _ctrl.PushOverlay();
        try
        {
            var busy = new BusyWindow(this, title, Loc.T("Cap.Busy"));
            busy.Show();
            CaptureSwitchResult res;
            try
            {
                var prog = new Progress<string>(s => busy.SetDetail(SwitchProgressText.Describe(s, id => Find(id)?.DisplayName, vm.DisplayName)));
                _svc.Log.Info($"[{vm.Id}] nguoi dung chon quyen chup: {(enable ? "dat" : "bo")}");
                res = await Task.Run(() => _svc.CaptureOwner.SetCaptureOwnerAsync(targetId, prog));
            }
            finally { busy.CloseNow(); }
            string text = res.Success
                ? (enable ? Loc.F("Cap.DoneOn", vm.DisplayName) : Loc.T("Cap.DoneOff"))
                : Loc.F("Cap.Fail", res.Reason ?? "");
            MessageDialog.Notify(this, title, text, res.Success ? DialogKind.Success : DialogKind.Warning);
        }
        finally { _ctrl.PopOverlay(); RefreshCapture(); }
    }

    private void RefreshCapture() { try { ApplyCapture(ComputeCapture()); } catch { /* bo qua */ } }

    private (string? Owner, bool Active) ComputeCapture()
    {
        var o = _svc.CaptureOwner.OwnerId;
        return (o, o != null && _svc.CaptureOwner.IsRunningFromCap(o));
    }

    private void ApplyCapture((string? Owner, bool Active) c)
    {
        foreach (var a in _accounts)
        {
            bool own = c.Owner != null && a.Id.Equals(c.Owner, StringComparison.OrdinalIgnoreCase);
            a.SetCapture(own, own && c.Active);
        }
    }

    private async void RefreshCallOwnerTick()
    {
        if (_ownerBusy) return;
        _ownerBusy = true;
        try
        {
            var (st, cap) = await Task.Run(() => (_svc.CallOwner.GetState(), ComputeCapture()));
            ApplyOwner(st);
            ApplyCapture(cap);
        }
        catch (Exception ex) { _svc.Log.Warn("quyen goi: " + ex.Message); }
        finally { _ownerBusy = false; }
    }

    private void RefreshCallOwner() { try { ApplyOwner(_svc.CallOwner.GetState()); } catch { /* bo qua */ } }

    private CallOwnerState? _lastOwner;

    private void ApplyOwner(CallOwnerState st)
    {
        _lastOwner = st;
        foreach (var a in _accounts) a.IsCallOwner = st.Kind == CallOwnerKind.Known && a.Id.Equals(st.Id, StringComparison.OrdinalIgnoreCase);
        bool anyPc = _accounts.Any(a => !a.IsWeb);   // chi co tai khoan Web: quyen goi khong lien quan -> khong canh bao
        (CallWarn.Text, CallWarn.Visibility) = st.Kind switch
        {
            _ when !anyPc => ("", Visibility.Collapsed),
            CallOwnerKind.Unknown => (Loc.T("Footer.CallUnknown"), Visibility.Visible),
            CallOwnerKind.External => (Loc.F("Footer.CallExternal", AppInfo.DisplayName), Visibility.Visible),
            _ => ("", Visibility.Collapsed),
        };
    }

    private string? _sleptId;
    private int _copyPct;
    private bool _retryMsgShown;

    /// <summary>Doi ngon ngu (Loc.Changed): lam moi moi chuoi tao trong code; chuoi XAML tu doi qua DynamicResource (LocResources).</summary>
    private void OnLanguageChanged()
    {
        AccountList.ContextMenu = BuildMenu();
        foreach (var v in _accounts) v.RefreshLanguage();
        ApplyCompact(_sidebarCollapsed ? SidebarNarrow : (Sidebar.ActualWidth > 0 ? Sidebar.ActualWidth : _sidebarWide));
        AddButton.ToolTip = _sidebarCollapsed ? Loc.T("Sidebar.Add") : null;
        UpdateHeader();
        RefreshRight();
        UpdateTotal();
        if (_svc.Updates != null) ApplyUpdate(_svc.Updates.State);
        if (_lastOwner is { } st) ApplyOwner(st);
        if (CopyText.Visibility == Visibility.Visible) CopyText.Text = Loc.F("Footer.Copy", _copyPct);
        if (SleepNote.Visibility == Visibility.Visible && _sleptId != null) SleepNote.Text = Loc.F("Footer.Slept", Find(_sleptId)?.DisplayName ?? _sleptId);
        if (_locked) { LockError.Text = ""; _retryMsgShown = false; UpdateRetryUi(); }
    }

    private async Task StopAsync(AccountViewModel vm)
    {
        if (vm.IsWeb) { StopWeb(vm); return; }
        var r = await _svc.Manager.StopAsync(vm.Id);
        if (r.Outcome is StopOutcome.Refused or StopOutcome.Failed)
            Info(AppInfo.DisplayName, Loc.F("Act.StopFail", vm.DisplayName, r.Reason ?? ""));
        else _svc.Governor.ClearSlept(vm.Id);   // nguoi dung tu cho ngu: khong tu bat lai khi chon
    }

    private void Rename(AccountViewModel vm)
    {
        var s = Prompt(Loc.T("Menu.Rename"), Loc.F("Act.RenamePrompt", vm.Id), vm.DisplayName);
        if (s == null) return;
        _svc.Profiles.Rename(vm.Id, s);
        vm.DisplayName = s;
    }

    private void PickColor(AccountViewModel vm)
    {
        _ctrl.PushOverlay();
        try
        {
            var dlg = new ColorDialog(this, vm.DisplayName, AccountPalette.Effective(vm.Id, vm.CustomColor));
            if (dlg.ShowDialog() != true) return;
            _svc.Profiles.SetColor(vm.Id, dlg.Value);
            vm.CustomColor = dlg.Value == "" ? null : dlg.Value;
        }
        finally { _ctrl.PopOverlay(); }
    }

    private void PickAvatar(AccountViewModel vm)
    {
        _ctrl.PushOverlay();
        try
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("Act.AvatarPick"),
                Filter = Loc.T("Act.AvatarFilter"),
            };
            if (ofd.ShowDialog(this) != true) return;
            try
            {
                var dest = _svc.Paths.AvatarFile(vm.Id);
                SaveAvatar(ofd.FileName, dest);
                vm.LoadAvatar(dest);
                _svc.Log.Info($"[{vm.Id}] doi anh dai dien");
            }
            catch (Exception ex)
            {
                _svc.Log.Warn($"[{vm.Id}] doi anh dai dien loi: {ex.GetType().Name}");
                MessageDialog.Notify(this, Loc.T("Act.AvatarTitle"), Loc.T("Act.AvatarFail"), DialogKind.Warning);
            }
        }
        finally { _ctrl.PopOverlay(); }
    }

    /// <summary>Doc anh nguon (khong khoa file), cat vuong giua, thu 128x128, ghi PNG qua tep tam roi move.</summary>
    private static void SaveAvatar(string source, string dest)
    {
        if (new FileInfo(source).Length > AvatarMath.MaxSourceBytes) throw new InvalidDataException("anh qua lon");
        // Doc kich thuoc truoc (khong giai ma diem anh) de gioi han canh dai khi giai ma that.
        int sw, sh;
        {
            var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri(source),
                System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation | System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile,
                System.Windows.Media.Imaging.BitmapCacheOption.None);
            sw = dec.Frames[0].PixelWidth; sh = dec.Frames[0].PixelHeight;
        }
        var (dw, dh) = AvatarMath.DecodeSize(sw, sh);
        var src = new System.Windows.Media.Imaging.BitmapImage();
        src.BeginInit();
        src.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        src.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
        if (dw > 0) src.DecodePixelWidth = dw;
        if (dh > 0) src.DecodePixelHeight = dh;
        src.UriSource = new Uri(source);
        src.EndInit();
        src.Freeze();
        var (x, y, side) = AvatarMath.CenterSquare(src.PixelWidth, src.PixelHeight);
        if (side <= 0) throw new InvalidDataException("anh rong");
        var crop = new System.Windows.Media.Imaging.CroppedBitmap(src, new Int32Rect(x, y, side, side));
        // Ve vao khung dung 128x128 voi HighQuality.
        var dv = new System.Windows.Media.DrawingVisual();
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(dv, System.Windows.Media.BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen())
            dc.DrawImage(crop, new Rect(0, 0, AvatarMath.Size, AvatarMath.Size));
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(AvatarMath.Size, AvatarMath.Size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) enc.Save(fs);
            File.Move(tmp, dest, overwrite: true);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    private void ClearAvatar(AccountViewModel vm)
    {
        try { var f = _svc.Paths.AvatarFile(vm.Id); if (File.Exists(f)) File.Delete(f); }
        catch (Exception ex) { _svc.Log.Warn($"[{vm.Id}] bo anh dai dien loi: {ex.GetType().Name}"); }
        vm.LoadAvatar(_svc.Paths.AvatarFile(vm.Id));
        _svc.Log.Info($"[{vm.Id}] bo anh dai dien");
    }

    private void Delete(AccountViewModel vm)
    {
        if (!vm.IsWeb && !vm.CanStart) { Info(AppInfo.DisplayName, Loc.T("Act.DeleteOnlyStopped")); return; }
        var name = vm.DisplayName;
        var s = Prompt(Loc.T("Act.DeleteTitle"), Loc.F("Act.DeleteMsg", name), "", t => t == name);
        if (s == null) return;
        if (vm.IsWeb)
        {   // huy WebView2 roi xoa profile (dang nhap, du lieu luu) truoc khi bo cau hinh
            try { Web.DeleteProfile(vm.Id); }
            catch (Exception ex) { _svc.Log.Warn($"[{vm.Id}] [web] xoa profile loi: {ex.GetType().Name}"); }
        }
        _ctrl.Drop(vm.Id, "xoa tai khoan");
        _svc.Profiles.Delete(vm.Id);
        try { File.Delete(_svc.Paths.AvatarFile(vm.Id)); } catch { }
        _svc.Log.Info($"[{vm.Id}] da xoa tai khoan");
        _accounts.Remove(vm);
        if (_accounts.Count > 0 && AccountList.SelectedItem == null) AccountList.SelectedIndex = 0;
        RefreshRight();
        RefreshWebVisibility();
        UpdateTotal();
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        string name; AccountKind kind;
        _ctrl.PushOverlay();
        try
        {
            var dlg = new AddAccountDialog(this);
            if (dlg.ShowDialog() != true) return;
            name = dlg.Value; kind = dlg.Kind;
        }
        finally { _ctrl.PopOverlay(); }
        try
        {
            // Zalo Web can WebView2: chua co thi hoi cai; khong dong y/that bai thi khong tao tai khoan.
            if (kind == AccountKind.Web && !await EnsureWebRuntimeAsync()) return;
            var acc = _svc.Profiles.Add(name, kind);
            AddVm(acc);
            var vm = _accounts[^1];
            AccountList.SelectedItem = vm;
            _svc.Log.Info($"[{acc.Id}] them tai khoan ({kind})");
            if (kind == AccountKind.Web) await StartWebAsync(vm);   // mo ngay de hien ma QR
        }
        catch (Exception ex) { _svc.Log.Error("them tai khoan: " + ex.Message); Info(AppInfo.DisplayName, Loc.F("Act.AddFail", ex.Message)); }
    }

    private async void InfoStartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_ctrl.Selected is not { } vm) return;
        try { await StartAsync(vm); }
        catch (Exception ex) { _svc.Log.Error($"[{vm.Id}] bat loi: {ex.Message}"); }
    }

    // ------------------------------------------------------------ hop thoai (an Zalo truoc)

    private string? Prompt(string title, string msg, string initial, Func<string, bool>? validator = null)
    {
        _ctrl.PushOverlay();
        try
        {
            var dlg = new PromptDialog(this, title, msg, initial, validator);
            return dlg.ShowDialog() == true ? dlg.Value : null;
        }
        finally { _ctrl.PopOverlay(); }
    }

    private void Info(string title, string msg)
    {
        _ctrl.PushOverlay();
        try { MessageDialog.Notify(this, title, msg, DialogKind.Info); }
        finally { _ctrl.PopOverlay(); }
    }

    // ------------------------------------------------------------ vung phai / footer

    private void RefreshRight()
    {
        var vm = _ctrl.Selected;
        RefreshCommandBar(vm);
        InfoPanel.Visibility = vm != null && vm.HasWindow ? Visibility.Collapsed : Visibility.Visible;
        if (vm == null)
        {
            InfoTitle.Text = Loc.T("Info.NoneTitle");
            InfoDetail.Text = Loc.T("Info.NoneDetail");
            InfoStartButton.Visibility = Visibility.Collapsed;
            return;
        }
        InfoTitle.Text = vm.DisplayName;
        InfoStartButton.Visibility = vm.Opening ? Visibility.Collapsed : Visibility.Visible;
        InfoStartButton.IsEnabled = vm.CanStart;
        if (vm.Opening) { InfoDetail.Text = Loc.T("Info.Opening"); return; }
        InfoDetail.Text = vm.State switch
        {
            InstanceState.Stopped => Loc.T("Info.Stopped"),
            InstanceState.Starting => Loc.T("Info.Starting"),
            InstanceState.Running => Loc.T("Info.WaitWindow"),
            InstanceState.Stopping => Loc.T("Info.Stopping"),
            _ => Loc.F("Info.Error", vm.Reason ?? Loc.T("Info.Unknown")),
        };
    }

    // ------------------------------------------------------------ thanh lenh

    private void Host_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyCommandBarWidth();

    private void ApplyCommandBarWidth()
    {
        double w = Host.ActualWidth;
        bool icons = CommandBarMetrics.IconOnly(w);
        var tx = icons ? Visibility.Collapsed : Visibility.Visible;
        BtnStartText.Visibility = BtnSleepText.Visibility = BtnRestartText.Visibility = BtnReattachText.Visibility = tx;
        ChipCallText.Visibility = ChipCapText.Visibility = ChipCapOffText.Visibility = ChipOldText.Visibility = ChipPinText.Visibility = tx;
        FlagChips.Visibility = CommandBarMetrics.FlagsVisible(w) ? Visibility.Visible : Visibility.Collapsed;
        AccountName.MaxWidth = CommandBarMetrics.NameMaxWidth(w);
        BtnStart.ToolTip = icons ? Loc.T("Cmd.Start") : null;
        BtnSleep.ToolTip = icons ? Loc.T("Cmd.Sleep") : null;
        BtnRestart.ToolTip = icons ? BtnRestartText.Text : null;
        BtnReattach.ToolTip = icons ? Loc.T("Cmd.Reattach") : null;
    }

    private void RefreshCommandBar(AccountViewModel? vm)
    {
        if (!ReferenceEquals(AccountBar.DataContext, vm)) AccountBar.DataContext = vm;
        AccountBar.Visibility = vm == null ? Visibility.Collapsed : Visibility.Visible;
        if (vm == null) return;
        var a = CommandBarMetrics.ForState(vm.State);
        BtnStart.Visibility = a.Start ? Visibility.Visible : Visibility.Collapsed;
        BtnSleep.Visibility = a.Sleep ? Visibility.Visible : Visibility.Collapsed;
        BtnRestart.Visibility = a.Restart ? Visibility.Visible : Visibility.Collapsed;
        BtnReattach.Visibility = a.Reattach && !vm.IsWeb ? Visibility.Visible : Visibility.Collapsed;   // gan cua so chi co voi Zalo PC
        BtnStart.IsEnabled = BtnSleep.IsEnabled = BtnRestart.IsEnabled = BtnReattach.IsEnabled = a.Enabled;
        BtnRestartText.Text = Loc.T(vm.NeedsUpdate ? "Cmd.RestartUpdate" : "Cmd.Restart");
        BtnRestartGlyph.Text = vm.NeedsUpdate ? "" : "";
        ApplyCommandBarWidth();
    }

    private async void RunCommand(string textKey, Func<AccountViewModel, Task> act)
    {
        if (AccountList.SelectedItem is not AccountViewModel vm) return;
        try { await act(vm); }
        catch (Exception ex) { _svc.Log.Error($"[{vm.Id}] {textKey}: {ex.GetType().Name}: {ex.Message}"); Info(AppInfo.DisplayName, Loc.F("Err.Action", Loc.T(textKey), ex.Message)); }
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e) => RunCommand("Cmd.Start", StartAsync);
    private void BtnSleep_Click(object sender, RoutedEventArgs e) => RunCommand("Cmd.Sleep", StopAsync);
    private void BtnRestart_Click(object sender, RoutedEventArgs e) => RunCommand("Cmd.Restart", RestartAsync);
    private void BtnReattach_Click(object sender, RoutedEventArgs e) => RunCommand("Menu.Reattach", ReattachAsync);

    private void BtnMore_Click(object sender, RoutedEventArgs e)
    {
        if (AccountList.SelectedItem is not AccountViewModel vm || AccountList.ContextMenu is not { } m) return;
        _ctxTarget = vm;
        m.PlacementTarget = BtnMore;
        m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        m.IsOpen = true;
    }

    private void UpdateTotal()
    {
        long total = _accounts.Sum(a => a.RamBytes);
        int running = _accounts.Count(a => a.IsActive && !a.IsWeb);   // gioi han "dang chay" chi tinh Zalo PC
        string lang = Loc.Lang;
        int web = _accounts.Count(a => a.IsActive && a.IsWeb);
        RamHead.Text = web == 0
            ? Loc.F("Footer.RamHead", running, _svc.Governor.EffectiveMax)
            : Loc.F("Footer.RamHeadWeb", running, _svc.Governor.EffectiveMax, web);
        TotalMini.Text = web == 0 ? $"{running}/{_svc.Governor.EffectiveMax}" : $"{running + web}";
        RamRingNumber.Text = RamLegend.FormatGbNumber(total, lang);
        var tip = Loc.F("Footer.RingTip", RamLegend.FormatMb(total, lang));
        if (SystemMemory.AvailablePhysicalBytes() is { } free)
            tip += " · " + Loc.F("Footer.Free", RamLegend.FormatGb(free, lang));
        RamRingView.ToolTip = tip;
        UpdateRamRing();
        UpdateRamLegend(lang);
    }

    /// <summary>Tai khoan dang chay co RAM &gt; 0, RAM giam dan (on dinh): thu tu cho ca vong lan chu thich.</summary>
    private List<AccountViewModel> RunningByRam() =>
        _accounts.Where(a => (a.Pid != null || a.IsWeb) && a.RamBytes > 0).OrderByDescending(a => a.RamBytes).ToList();

    private void UpdateRamLegend(string lang)
    {
        var sorted = RunningByRam();
        int max = _compactNow ? 2 : RamLegend.MaxRows;
        var legend = RamLegend.Build(sorted.Select(a => (name: a.DisplayName, bytes: a.RamBytes, tag: a.AvatarBrush)), max);
        RamLegendPanel.Children.Clear();
        foreach (var r in legend.Rows)
            RamLegendPanel.Children.Add(LegendRow(r.Tag, r.Name, RamLegend.FormatMb(r.Bytes, lang)));
        if (legend.OtherCount > 0)
        {
            var row = LegendRow(null, Loc.F("Footer.More", legend.OtherCount, RamLegend.FormatMb(legend.OtherBytes, lang)), "");
            row.Background = System.Windows.Media.Brushes.Transparent;
            row.ToolTip = string.Join("\n", sorted.Skip(max).Select(a => $"{a.DisplayName} · {RamLegend.FormatMb(a.RamBytes, lang)}"));
            RamLegendPanel.Children.Add(row);
        }
        else if (legend.Rows.Count == 0)
            RamLegendPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("Footer.None"), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)FindResource("Muted"),
            });
    }
    private static Grid LegendRow(System.Windows.Media.Brush? dot, string name, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (dot != null) g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = dot, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        var n = new TextBlock { Text = name, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        n.SetResourceReference(TextBlock.ForegroundProperty, dot != null ? "Fg" : "Muted");
        Grid.SetColumn(n, 1); g.Children.Add(n);
        var v = new TextBlock { Text = value, FontSize = 11.5, Margin = new Thickness(8, 0, 0, 0) };
        v.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetColumn(v, 2); g.Children.Add(v);
        return g;
    }

    /// <summary>Vong RAM: moi tai khoan dang chay mot cung, thang do = tong RAM may (cung thang SidebarMetrics.TotalFraction), qua RamRing.Arcs.</summary>
    private void UpdateRamRing()
    {
        long machine = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var items = RunningByRam();
        RamRingView.Set(items.Select(a => a.RamBytes).ToList(), items.Select(a => a.AvatarBrush).ToList(), machine);
    }
    private async Task UpdateRamAsync()
    {
        if (_ramBusy) return;
        _ramBusy = true;
        try
        {
            var items = _accounts.Where(a => a.Pid != null).Select(a => (vm: a, pid: (uint)a.Pid!.Value)).ToList();
            var sizes = await Task.Run(() => items.Select(i => ProcessTree.TotalWorkingSetBytes(i.pid)).ToArray());
            for (int i = 0; i < items.Count; i++) if (items[i].vm.Pid != null) items[i].vm.RamBytes = sizes[i];
            await UpdateWebRamAsync();   // Zalo Web: tien trinh WebView2 chung, chia cho cac tai khoan Web (WebAccountLogic.SplitRam)
            UpdateTotal();
        }
        catch (Exception ex) { _svc.Log.Warn("ram: " + ex.Message); }
        finally { _ramBusy = false; }
    }

    private void OnCopyProgress(double p)
    {
        bool show = p < 1;
        CopyText.Visibility = CopyBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _copyPct = (int)(p * 100);
        CopyText.Text = Loc.F("Footer.Copy", _copyPct);
        CopyBar.Value = p;
    }

    /// <summary>Zalo tu cap nhat: danh dau tai khoan chay ban cu + dong thong bao o sidebar.</summary>
    private void ApplyUpdate(ZaloUpdateState s)
    {
        foreach (var a in _accounts) a.NeedsUpdate = s.OutdatedIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase);
        if (s.OutdatedIds.Count > 0 && s.LatestCopy != null)
        {
            UpdateNote.Text = Loc.F("Footer.UpdateNote", s.LatestCopy);
            UpdateNote.Visibility = Visibility.Visible;
        }
        else UpdateNote.Visibility = Visibility.Collapsed;
    }

    // ------------------------------------------------------------ dong app

    /// <summary>Goi khi crash/thoat: tra moi cua so Zalo ve nguyen trang.</summary>
    public void EmergencyRestore()
    {
        _ctrl.RestoreAll("thoat/crash");
        try { DisposeWeb(); } catch { /* bo qua */ }
        try { _tray?.Dispose(); } catch { /* khong de bieu tuong ma */ }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeApproved) return;
        e.Cancel = true;
        if (_closeFlow) return;
        _closeFlow = true;
        _ = CloseFlowAsync();
    }

    private async Task CloseFlowAsync()
    {
        await Task.Yield(); // thoat khoi OnClosing truoc khi goi Close() lai
        try
        {
            var active = _accounts.Where(a => a.IsActive).ToList();
            bool keep = App.KeepRunningOnExit;
            if (!keep && active.Count > 0)
            {
                _ctrl.PushOverlay();
                var yes = MessageDialog.Ask(this, Loc.F("Act.CloseTitle", AppInfo.DisplayName), Loc.F("Act.CloseMsg", AppInfo.DisplayName, active.Count), DialogKind.Question);
                if (!yes) { _ctrl.PopOverlay(); _closeFlow = false; return; }
            }
            _tick.Stop(); _fastTick.Stop(); _ramTick.Stop(); _ownerTick.Stop(); _idleTick.Stop(); _lockTick.Stop(); _retryTick.Stop();
            _notify?.Dispose();
            _unread?.Dispose();
            _hotkeys?.Dispose();
            _tray?.Dispose();
            Loc.Changed -= OnLanguageChanged;
            DisposeWeb();   // huy WebView2 cua cac tai khoan Web (khong de msedgewebview2 mo coi); dang nhap con tren dia
            // Zalo PC can tat bang InstanceManager: VM dang active, hoac manager bao dang chay (VM cap nhat tre), hoac dang trong luc bat
            var pcIds = _accounts.Where(a => !a.IsWeb && (a.IsActive || _svc.Manager.IsRunning(a.Id) || _starting.Contains(a.Id)))
                .Select(a => a.Id).ToList();
            if (keep) _ctrl.RestoreAll("dong app");
            else
            {
                // Tat Zalo TRUOC khi cua so con gan/do ngoai man hinh (tranh "loi" cua so goc ~150 ms); chi tra cua so cua instance con song
                try { App.StartupCts.Cancel(); } catch { /* luot tu bat dang do se dung */ }
                _ctrl.BeginClosing();
                var dlg = pcIds.Count > 0 ? new ProgressWindow(this, Loc.T("Act.StoppingTitle"), pcIds.Count) : null;
                dlg?.Show();
                int done = 0;
                async Task StopOne(string id)
                {
                    try
                    {
                        var res = await _svc.Manager.StopAsync(id);
                        _svc.Log.Info($"[{id}] dong app: {res.Outcome}");
                        if (res.Outcome is not (StopOutcome.Stopped or StopOutcome.AlreadyStopped))
                            _ctrl.Drop(id, "dong app: chua tat duoc");   // con song -> tra cua so ve nguyen trang
                    }
                    catch (Exception ex) { _svc.Log.Error($"[{id}] dong app loi: {ex.Message}"); }
                }
                await Task.WhenAll(pcIds.Select(async id => { await StopOne(id); dlg?.Report(++done, pcIds.Count); }));
                // Lenh Bat/tu bat dang do dang (chua toi InstanceManager) co the sinh Zalo.exe sau luot tat dau:
                // cho no xong roi quet lai moi tai khoan PC (StopAsync chi tac dong instance co mz.pid khop, khong co -> AlreadyStopped)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (_accounts.Any(a => !a.IsWeb && _starting.Contains(a.Id)) && sw.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(100);
                var allPc = _accounts.Where(a => !a.IsWeb).Select(a => a.Id).ToList();
                await Task.WhenAll(allPc.Select(StopOne));
                dlg?.Close();
                _ctrl.RestoreAll("dong app");   // phan sot (cua so da chet thi Guard bo qua)
            }
        }
        catch (Exception ex) { _svc.Log.Error("dong app: " + ex); try { _ctrl.RestoreAll("dong app (loi)"); } catch { } }
        _closeApproved = true;
        Close();
    }
}

