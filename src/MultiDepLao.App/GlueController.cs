using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MultiDepLao.Core;
using MultiDepLao.Win32;
using static MultiDepLao.Win32.Native;

namespace MultiDepLao.App;

/// <summary>
/// Logic dan cua so Zalo vao vung hien thi (SPEC 4.4), tach khoi XAML. Chay tren UI thread.
/// Chuyen tu MainForm.Tick/EnsureWindow cua HostProto. KHONG ghi tieu de cua so vao log.
/// </summary>
public sealed class GlueController
{
    private const int MinMainW = 380, MinMainH = 450; // nhu HostProto

    private sealed class Slot
    {
        public AccountViewModel Vm = null!;
        public uint Pid;
        public GlueWindow? Glue;
        public WinEventWatcher? Watcher;
        public System.Windows.Threading.DispatcherTimer? Trailing;
        public (RECT rect, bool visible)? Keep;
        public bool Settling;          // dang khoi dong: giu cua so Zalo ngoai man hinh cho den khi on dinh
        public DateTime SettleStart;   // UTC, luc gan lan dau
        public DateTime LastChange;    // UTC, lan cuoi Zalo tu doi style / tu hien / doi cua so
    }

    private static void Changed(Slot s) { if (s.Settling) s.LastChange = DateTime.UtcNow; }

    private void UpdateSettle(Slot s)
    {
        if (!s.Settling || s.Glue is not { IsAttached: true }) return;
        if (!StartupSettle.IsSettled(s.SettleStart, s.LastChange, DateTime.UtcNow)) return;
        s.Settling = false;
        s.Vm.Opening = false;
        s.Vm.HasWindow = true;
        _log.Info($"[{s.Vm.Id}] [glue] khoi dong on dinh sau {(int)(DateTime.UtcNow - s.SettleStart).TotalMilliseconds} ms -> dua vao vung hien thi");
    }

    private readonly Window _window;
    private readonly FrameworkElement _host;
    private readonly InstanceManager _mgr;
    private readonly ILog _log;
    private readonly Dictionary<string, Slot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private int _overlay;
    private bool _closing;
    private bool _locked;
    private RECT _lastRect;

    public AccountViewModel? Selected { get; private set; }
    /// <summary>Nguoi dung bat dau keo mep TRAI cua cua so Zalo (da huy keo cua so).</summary>
    public event Action? ZaloLeftEdgeDragStarted;
    /// <summary>true khi dang co hop thoai/menu cua app mo (PushOverlay).</summary>
    public bool OverlayActive => _overlay > 0;
    /// <summary>Thoi diem (UTC) vua bam popup cua tai khoan (NotifyController); null neu khong.</summary>
    public Func<string, DateTime?>? PopupClickAt { get; set; }
    /// <summary>Yeu cau chuyen chon sang tai khoan vua bam popup (Zalo cua no tu hien khi dang an).</summary>
    public Action<string>? PopupSwitch { get; set; }
    /// <summary>Chuoi chan doan popup gan day (con tro, rect, tuoi); null neu khong co popup gan day.</summary>
    public Func<string, string?>? PopupDiag { get; set; }

    public GlueController(Window window, FrameworkElement host, InstanceManager mgr, ILog log)
    { _window = window; _host = host; _mgr = mgr; _log = log; }

    private bool Minimized => _window.WindowState == WindowState.Minimized;
    private IntPtr HostHwnd => new WindowInteropHelper(_window).Handle;
    private bool DesiredHidden(string id) => _overlay > 0 || Selected == null || !Selected.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
        || (_slots.TryGetValue(id, out var sl) && sl.Settling);

    /// <summary>Chuoi trang thai cua so chinh Zalo cua tai khoan (visible/foreground/rect), cho log chan doan.</summary>
    public string MainWindowState(string id)
    {
        if (!_slots.TryGetValue(id, out var s) || s.Glue is not { } g || g.Hwnd == IntPtr.Zero || !IsWindow(g.Hwnd)) return "khong co cua so";
        var r = Native.Rect(g.Hwnd);
        return $"visible={IsWindowVisible(g.Hwnd)} foreground={GetForegroundWindow() == g.Hwnd} userHidden={g.UserHidden} rect=({r.Left},{r.Top},{r.Right},{r.Bottom})";
    }

    /// <summary>Vung hien thi, pixel thiet bi, toa do man hinh. Null khi chua dung duoc.</summary>
    public RECT? PanelScreenRect()
    {
        if (!_host.IsLoaded || Minimized || PresentationSource.FromVisual(_host) == null) return null;
        var dpi = VisualTreeHelper.GetDpi(_host);
        var tl = _host.PointToScreen(new Point(0, 0));
        int w = (int)Math.Round(_host.ActualWidth * dpi.DpiScaleX), h = (int)Math.Round(_host.ActualHeight * dpi.DpiScaleY);
        if (w <= 0 || h <= 0) return null;
        return RECT.FromXywh((int)Math.Round(tl.X), (int)Math.Round(tl.Y), w, h);
    }

    // ------------------------------------------------------------ chon / lop phu

    public void SetSelected(AccountViewModel? vm)
    {
        Selected = vm;
        var rect = PanelScreenRect();
        foreach (var s in _slots.Values) { s.Glue?.ResetSizeLog(); Reconcile(s, rect); }
        if (rect != null) _lastRect = rect.Value;
    }

    /// <summary>Truoc khi mo hop thoai/menu co the de len vung Zalo: an cua so Zalo dang chon.</summary>
    public void PushOverlay([System.Runtime.CompilerServices.CallerMemberName] string by = "")
    { _overlay++; _log.Info($"[overlay] +1 -> {_overlay} ({by})"); ReconcileSelected(); }
    public void PopOverlay([System.Runtime.CompilerServices.CallerMemberName] string by = "")
    { if (_overlay > 0) _overlay--; _log.Info($"[overlay] -1 -> {_overlay} ({by})"); ReconcileSelected(); }

    // ------------------------------------------------------------ khoa app (SPEC 4.7)

    /// <summary>Khoa: an MOI cua so dang gan (va cua so gan sau nay). Mo khoa: hien lai roi Reconcile nhu thuong.</summary>
    public void SetLocked(bool locked)
    {
        if (_closing || _locked == locked) return;
        _locked = locked;
        if (locked)
        {
            foreach (var s in _slots.Values) s.Glue?.HideForLock();
            return;
        }
        var rect = PanelScreenRect();
        foreach (var s in _slots.Values) { s.Glue?.ShowAfterLock(); Reconcile(s, rect); }
    }

    private void ReconcileSelected()
    {
        if (Selected != null && _slots.TryGetValue(Selected.Id, out var s)) Reconcile(s, PanelScreenRect());
    }

    private void Reconcile(Slot s, RECT? rect)
    {
        var g = s.Glue;
        if (g == null || !g.IsAttached) return;
        bool switching = false;
        bool want = DesiredHidden(s.Vm.Id);
        if (g.UserHidden != want)
        {
            if (!want && rect != null) g.Sync(rect.Value); // nong lai kich thuoc truoc khi hien (cua so dang thu hep)
            g.SetHidden(want);
            if (!want && rect != null) g.Sync(rect.Value);
        }
        else if (!want && !Minimized && rect != null) g.EnsureShown(rect.Value);
        // Zalo tu hien cua so dang bi ta an (vd sau khi khoi dong lai/dang nhap xong): an lai. Neu khong, cua so Zalo
        // (owned, luon tren app) se che mat hop thoai modal cua app -> app trong nhu bi do (loi 2026-11-17 15:43).
        else if (want && IsWindow(g.Hwnd) && IsWindowVisible(g.Hwnd))
        {
            var click = PopupClickAt?.Invoke(s.Vm.Id);
            bool sw = GluePolicy.ShouldSwitchOnPopupClick(DateTime.UtcNow, click, _locked, OverlayActive, false);
            var diag = PopupDiag?.Invoke(s.Vm.Id);
            if (diag != null)
                _log.Info($"[{s.Vm.Id}] [notify] tu hien sau popup: {diag} -> " +
                    (sw ? "chuyen" : $"bo qua ({(_locked ? "dang khoa" : OverlayActive ? "co lop phu" : "con tro ngoai popup")})"));
            if (sw)
            {
                switching = true;
                PopupSwitch?.Invoke(s.Vm.Id);   // MainWindow: nhan click, khoi phuc neu can, chon tai khoan
            }
            else
            {
                if (GluePolicy.ClickRecent(DateTime.UtcNow, click))
                    _log.Info($"[{s.Vm.Id}] [notify] bam popup nhung bo qua ({(_locked ? "dang khoa" : "co lop phu")})");
                var vr = Native.Rect(g.Hwnd);
                Changed(s);
                _log.Info($"[{s.Vm.Id}] [glue] Zalo tu hien khi dang an -> an lai (rect={vr} tren-man-hinh={MonitorFromRect(ref vr, 0) != IntPtr.Zero})");
                g.SetHidden(true);
            }
        }
        // Host dang thu nho / an xuong khay: moi nhanh tren co the (SetHidden(false), Zalo tu hien) vua hien cua so -> an lai.
        if (!switching && Minimized && IsWindow(g.Hwnd) && IsWindowVisible(g.Hwnd)) g.HostMinimized(true);
    }

    // ------------------------------------------------------------ dong bo vi tri

    /// <summary>Goi khi LocationChanged/SizeChanged/DpiChanged/LayoutUpdated. force=false: bo qua neu rect khong doi.</summary>
    public void SyncSelected(bool force)
    {
        var rect = PanelScreenRect();
        if (rect == null) return;
        if (!force && rect.Value.Same(_lastRect)) return;
        _lastRect = rect.Value;
        if (Selected != null && _slots.TryGetValue(Selected.Id, out var s) && s.Glue is { IsAttached: true } g && !g.UserHidden)
            g.Sync(rect.Value);
    }

    public void ResetSizeLogs() { foreach (var s in _slots.Values) s.Glue?.ResetSizeLog(); }

    public void HostMinimized(bool minimized)
    {
        foreach (var s in _slots.Values) s.Glue?.HostMinimized(minimized);
        if (!minimized) { SyncSelected(true); VerifyShownAfterRestore(80); VerifyShownAfterRestore(400); }
    }

    /// <summary>true neu <paramref name="w"/> nam duoi <paramref name="main"/> trong thu tu Z (di len tu w ma gap main).</summary>
    private static bool IsBelow(IntPtr w, IntPtr main)
    {
        if (w == IntPtr.Zero || main == IntPtr.Zero) return false;
        var h = GetWindow(w, 3 /*GW_HWNDPREV*/);
        for (int i = 0; h != IntPtr.Zero && i < 5000; i++, h = GetWindow(h, 3))
            if (h == main) return true;
        return false;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindow")]
    private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>
    /// Thu that 2026-11-20 (0.4.4/0.4.5-t1): sau khi khoi phuc, cua so Zalo cua tai khoan dang chon doi luc chua hien, hoac hien
    /// nhung nam DUOI cua so chinh (bi che -> vung hien thi xam, phai "Gan lai" tay). Kiem lai sau <paramref name="delayMs"/> ms
    /// (goi 80 ms de khong thay nen xam, va 400 ms lam luoi an toan): chua hien thi hien lai; nam duoi thi dua len.
    /// </summary>
    private void VerifyShownAfterRestore(int delayMs)
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            try
            {
                if (_closing || Minimized || _locked || _overlay > 0 || Selected == null) return;
                if (!_slots.TryGetValue(Selected.Id, out var s) || s.Glue is not { IsAttached: true } g || g.UserHidden) return;
                if (!IsWindow(g.Hwnd)) return;
                if (!IsWindowVisible(g.Hwnd))
                {
                    _log.Info($"[{s.Vm.Id}] [glue] sau khoi phuc cua so chua hien -> hien lai");
                    g.HostMinimized(false);
                    SyncSelected(true);
                }
                // Thu that 11:32 (0.4.5-t1): cua so Zalo hien, dung vi tri, nhung nam DUOI cua so chinh (Z 57 vs 54) -> bi che,
                // vung hien thi chi thay nen xam. Owned cross-process khong duoc Windows xep lai sau khoi phuc -> dua len.
                var main = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
                if (IsBelow(g.Hwnd, main))
                {
                    SetWindowPos(g.Hwnd, IntPtr.Zero /*HWND_TOP*/, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200 /*NOSIZE|NOMOVE|NOACTIVATE|NOOWNERZORDER*/);
                    _log.Info($"[{s.Vm.Id}] [glue] sau khoi phuc cua so Zalo nam duoi cua so chinh -> dua len (con duoi: {IsBelow(g.Hwnd, main)})");
                }
            }
            catch (Exception ex) { _log.Error($"[glue] kiem tra sau khoi phuc loi: {ex.Message}"); }
        };
        t.Start();
    }

    // ------------------------------------------------------------ vong 1 giay

    public void Tick(IEnumerable<AccountViewModel> accounts)
    {
        if (_closing) return;
        if (_locked)   // cua so tu hien len (vd bam bieu tuong khay) -> an lai
            foreach (var s in _slots.Values)
                if (s.Glue is { IsAttached: true } g && IsWindow(g.Hwnd) && IsWindowVisible(g.Hwnd)) g.HideForLock();
        foreach (var vm in accounts)
        {
            bool alive = vm.State is InstanceState.Starting or InstanceState.Running or InstanceState.Stopping;
            if (!alive || vm.Pid is not int pid) { Drop(vm.Id, "tai khoan khong con chay"); continue; }
            _slots.TryGetValue(vm.Id, out var s);
            if (s != null && s.Pid != (uint)pid) { Drop(vm.Id, "doi pid"); s = null; }
            if (vm.State == InstanceState.Stopping || Minimized) { if (s != null) Reconcile(s, null); continue; }
            if (s == null)
            {
                s = new Slot { Vm = vm, Pid = (uint)pid, Settling = vm.State == InstanceState.Starting && !vm.IsWeb };
                s.LastChange = DateTime.UtcNow;
                _slots[vm.Id] = s;
                if (s.Settling) vm.Opening = true;
            }
            try { EnsureWindow(s); UpdateSettle(s); Reconcile(s, PanelScreenRect()); }
            catch (Exception ex) { _log.Error($"[{vm.Id}] tick loi: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Vong nhanh (~40 ms) chi khi co tai khoan dang Starting chua gan duoc cua so: bat cua so Zalo som nhat co the
    /// (truoc khi no hien/ve lan dau), thay vi cho vong 1 giay. Re khi khong co gi de lam.
    /// </summary>
    public void FastTick(IReadOnlyList<AccountViewModel> accounts)
    {
        if (_closing || Minimized) return;
        List<AccountViewModel>? want = null;
        foreach (var vm in accounts)
        {
            if (vm.State != InstanceState.Starting || vm.Pid is null) continue;
            if (_slots.TryGetValue(vm.Id, out var s) && s.Glue is { IsAttached: true } && s.Pid == (uint)vm.Pid.Value) continue;
            (want ??= new()).Add(vm);
        }
        if (want != null) Tick(want);
    }

    private static bool BigEnough(IntPtr h) { var r = NormalRect(h); return r.Width >= MinMainW && r.Height >= MinMainH; }
    private static long Area(IntPtr h) { var r = NormalRect(h); return (long)r.Width * r.Height; }
    private Action<string> LogFor(Slot s) => m => _log.Info($"[{s.Vm.Id}] {m}");

    private void EnsureWindow(Slot s)
    {
        var cur = s.Glue;
        var log = LogFor(s);
        if (cur != null && IsWindow(cur.Hwnd) && PidOf(cur.Hwnd) == s.Pid)
        {
            if (cur.StyleTampered())
            {
                Changed(s);
                s.Keep = cur.Forget($"Zalo tu doi style -> {Hex(Style(cur.Hwnd))}, gan lai");
                s.Vm.HasWindow = false;
            }
            if (!cur.IsAttached) { TryAttach(s); return; }
            bool ours = cur.UserHidden || cur.Locked || IsWindowVisible(cur.Hwnd);
            if (ours && !cur.IsFixed) return; // cua so chinh on dinh
            var cand = ZaloWindowFinder.FindMainWindow(s.Pid, MinMainW, MinMainH);
            if (cand == IntPtr.Zero || cand == cur.Hwnd || !BigEnough(cand)) return;
            if (ours && Area(cand) <= Area(cur.Hwnd)) return;
            log($"[detect] doi cua so chinh 0x{cur.Hwnd.ToInt64():X} -> 0x{cand.ToInt64():X}");
            cur.Restore("doi cua so chinh");
            NewGlue(s, cand);
            TryAttach(s);
            return;
        }
        if (cur != null)
        {
            log($"[detect] cua so 0x{cur.Hwnd.ToInt64():X} khong con -> tim lai");
            cur.Restore("hwnd mat");
            s.Glue = null; s.Keep = null; s.Vm.HasWindow = false;
        }
        var h = ZaloWindowFinder.FindMainWindow(s.Pid, MinMainW, MinMainH);
        if (h == IntPtr.Zero || !BigEnough(h)) return;
        NewGlue(s, h);
        _mgr.MarkWindowReady(s.Vm.Id);
        TryAttach(s);
    }

    private void NewGlue(Slot s, IntPtr h)
    {
        s.Glue = new GlueWindow(s.Pid, h, LogFor(s));
        if (s.SettleStart == default) s.SettleStart = DateTime.UtcNow;
        Changed(s);
        if (_locked) s.Glue.HideForLock();
        s.Keep = null;
        LogFor(s)($"[detect] cua so chinh: {s.Glue.Describe()} fixed={s.Glue.IsFixed}");
        if (s.Watcher == null)
        {
            s.Watcher = new WinEventWatcher(s.Pid, h, LogFor(s));
            s.Watcher.TargetShown += () =>
            {
                Changed(s);
                if (_locked) s.Glue?.HideForLock();
                else if (s.Glue is { IsAttached: true } && (DesiredHidden(s.Vm.Id) || Minimized)) Reconcile(s, null);
            };
            s.Trailing = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GluePolicy.TrailingMs) };
            s.Trailing.Tick += (_, _) =>
            {
                s.Trailing.Stop();
                var r = PanelScreenRect();
                if (r != null) try { s.Glue?.Enforce(r.Value, Minimized, true); s.Glue?.Nudge(r.Value); } catch (Exception ex) { _log.Error($"[{s.Vm.Id}] trailing loi: {ex.Message}"); }
            };
            // Debounce: moi su kien lam lai dem 150 ms -> luon co 1 lan kiem tra sau su kien cuoi.
            void ArmTrailing() { s.Trailing.Stop(); s.Trailing.Start(); }
            s.Watcher.TargetLocationChanged += () =>
            {
                if (s.Glue is { IsAttached: true, UserHidden: true } pg) { pg.KeepParked(); return; }
                var r = PanelScreenRect();
                if (r == null || s.Glue is not { IsAttached: true }) return;
                s.Glue.OnLocationChanged(r.Value, Minimized);
                ArmTrailing();
            };
            s.Watcher.TargetMoveSizeStart += () =>   // chay tren UI thread (hook OUTOFCONTEXT)
            {
                if (_locked) return;
                s.Glue?.CancelUserMove();
                if (s.Glue is { IsAttached: true, UserHidden: false } gw && IsWindowVisible(gw.Hwnd) && GetCursorPos(out var pt))
                {
                    var zr = Native.Rect(gw.Hwnd);
                    if (Math.Abs(pt.X - zr.Left) <= 12 && pt.Y >= zr.Top && pt.Y <= zr.Bottom)
                        ZaloLeftEdgeDragStarted?.Invoke();
                }
            };
            s.Watcher.TargetMoveSizeEnd += () =>
            {
                var r = PanelScreenRect();
                if (r == null || s.Glue is not { IsAttached: true }) return;
                s.Glue.Enforce(r.Value, Minimized, true);
                ArmTrailing();
            };
        }
        else s.Watcher.Target = h;
    }

    private void TryAttach(Slot s)
    {
        var g = s.Glue; var rect = PanelScreenRect();
        if (g == null || rect == null) return;
        var keep = s.Keep; s.Keep = null;
        g.UseToolWindow = true;
        g.Attach(HostHwnd, rect.Value, DesiredHidden(s.Vm.Id), keep?.rect, keep?.visible);
        if (_locked) g.HideForLock();
        s.Vm.HasWindow = g.IsAttached && !s.Settling;
        if (g.PendingNudge && s.Trailing != null) { s.Trailing.Stop(); s.Trailing.Start(); }
    }

    // ------------------------------------------------------------ go bo / thoat

    public void Drop(string id, string reason)
    {
        if (!_slots.Remove(id, out var s)) return;
        try { s.Glue?.Restore(reason); } catch (Exception ex) { _log.Error($"[{id}] restore loi: {ex.Message}"); }
        try { s.Trailing?.Stop(); } catch { /* bo qua */ }
        try { s.Watcher?.Dispose(); } catch { /* bo qua */ }
        try { s.Vm.HasWindow = false; s.Vm.Opening = false; } catch { /* goi tu thread khac khi crash */ }
    }

    /// <summary>
    /// Gan lai thu cong: tra cua so cua tai khoan ve nguyen trang, tim lai cua so chinh theo pid, gan lai va Reconcile.
    /// False neu tai khoan khong dang chay / chua tim thay cua so.
    /// </summary>
    public bool Reattach(string id)
    {
        if (_closing || !_slots.TryGetValue(id, out var s)) return false;
        _log.Info($"[{id}] [glue] gan lai thu cong");
        try { s.Glue?.Restore("gan lai thu cong"); } catch (Exception ex) { _log.Error($"[{id}] restore loi: {ex.Message}"); }
        s.Glue = null; s.Keep = null; s.Vm.HasWindow = false;
        try { EnsureWindow(s); Reconcile(s, PanelScreenRect()); }
        catch (Exception ex) { _log.Error($"[{id}] gan lai loi: {ex.GetType().Name}: {ex.Message}"); return false; }
        bool ok = s.Glue is { IsAttached: true };
        _log.Info($"[{id}] [glue] gan lai {(ok ? "xong" : "chua duoc (khong thay cua so)")}");
        return ok;
    }

    /// <summary>Ngung gan/sap xep nhung CHUA tra cua so (cua so van gan + do ngoai man hinh). Dung khi sap tat Zalo.</summary>
    public void BeginClosing() => _closing = true;

    /// <summary>Tra moi cua so Zalo ve nguyen trang va ngung dan. Idempotent.</summary>
    public void RestoreAll(string reason)
    {
        _closing = true;
        foreach (var id in _slots.Keys.ToList()) Drop(id, reason);
    }
}

