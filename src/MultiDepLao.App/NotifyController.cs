using System.Runtime.InteropServices;
using System.Windows.Threading;
using MultiDepLao.Core;
using MultiDepLao.Win32;
using static MultiDepLao.Win32.Native;

namespace MultiDepLao.App;

/// <summary>Theo doi popup thong bao cua tung instance Zalo (theo PID) va hien nhan ten tai khoan. UI thread. Khong doc tieu de/noi dung.</summary>
public sealed class NotifyController : IDisposable
{
    private const int TagH = 26, TagGap = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);

    private sealed class State
    {
        public int Pid;
        public IntPtr Hwnd;
        public DateTime NextFind;
        public bool Visible;
        public RECT Rect;
        public int Count;
        public DateTime? Last;
        public NotifyTagWindow? Tag;
        public string Name = "";
        public bool Consumed;   // da chuyen theo popup nay: khong tinh lai cho den khi popup an
        public RECT LastRect;
        public DateTime? LastSeenUtc;   // lan cuoi thay popup hien (moi tick)
        public DateTime? ClickAt;  // UTC: nghi da bam vao popup (chuot trai trong rect / con tro trong rect luc popup bien mat)
        public Dictionary<IntPtr, GhostInfo> Ghost = new();   // popup da lam trong suot (hwnd -> exstyle goc)
        public IntPtr[]? Hooks;    // WinEvent SHOW theo PID (chi khi HidePopups)
        public int Hidden;         // so lan an popup
        public DateTime LastConcealUtc;   // lan an gan nhat (gop cac SHOW dong thoi cua mot lan hien)
        public DateTime IgnoreUntilUtc;   // SHOW truoc moc nay: an nhung khong tinh tin moi (cu bam toast cua ta)
    }

    /// <summary>
    /// "Bam toast -> mo chat" (Thong bao kin): gui WM_MOUSEMOVE/LBUTTONDOWN/LBUTTONUP thang vao hwnd popup Zalo da an (PostMessage,
    /// khong dung con tro that, khong SendInput) = nhu nguoi dung bam popup goc -> Zalo mo cuoc tro chuyen cua tin MOI NHAT
    /// tren popup. Chi khi popup da tung hien trong phien (Zalo co tai khoan khong bao gio hien popup: bam popup rong vo ich).
    /// Tra chuoi chan doan (khong doc tieu de/van ban). Thu that 2026-11-21: mo dung chat (acc co popup).
    /// </summary>
    public string TryOpenPopupChat(string id)
    {
        if (!_st.TryGetValue(id, out var s)) return "khong co popup";
        if (s.Hidden == 0) return "popup chua tung hien trong phien -> khong bam";
        var h = s.Hwnd;
        if (h == IntPtr.Zero || !IsWindow(h))
        {
            h = IntPtr.Zero;
            try { if (s.Pid > 0) h = ZaloWindowFinder.FindNotifyPopup((uint)s.Pid); } catch { h = IntPtr.Zero; }
            if (h != IntPtr.Zero) s.Hwnd = h;
        }
        if (h == IntPtr.Zero || !IsWindow(h)) return "khong co popup";
        bool vis = IsWindowVisible(h);
        var wr = Rect(h);
        GetClientRect(h, out var cr);
        string diag = $"hwnd=0x{h.ToInt64():X} visible={vis} rect=({wr.Left},{wr.Top},{wr.Right},{wr.Bottom}) " +
                      $"client={cr.Right}x{cr.Bottom} ex=0x{ExStyle(h):X}";
        s.IgnoreUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);   // Chromium tu hien lai popup sau cu bam: khong tinh tin moi
        var (x, y) = ToastClickPolicy.ClientCenter(cr.Right - cr.Left, cr.Bottom - cr.Top);
        var lp = ToastClickPolicy.MakeLParam(x, y);
        var res = new List<string>();
        foreach (var (msg, wp, name) in new[] { (0x0200u, 0, "move"), (0x0201u, 1, "down"), (0x0202u, 0, "up") })
        {
            bool ok = PostMessage(h, msg, (IntPtr)wp, lp);
            res.Add(name + "=" + (ok ? "ok" : "err" + Marshal.GetLastWin32Error()));
        }
        return $"{diag} click=({x},{y}) {string.Join(' ', res)}";
    }

    private sealed class GhostInfo { public long Orig; public bool Ok; }

    /// <summary>Cac SHOW trong khoang nay sau lan an truoc = cung mot lan hien popup (animation), khong tinh la tin moi.</summary>
    private static readonly TimeSpan ConcealMerge = TimeSpan.FromMilliseconds(600);

    private bool _hide;
    private readonly WinEventDelegate _cb;   // giu tham chieu, tranh GC

    /// <summary>
    /// "Thong bao kin" bat: popup tin nhan cua Zalo (tieu chi chat o NotifyPopupPolicy) bi day NGAY ra ngoai moi man hinh bang
    /// SetWindowPos NOACTIVATE (khong dong/an/gui phim chuot), khong ve nhan + khung. Bat som qua WinEvent out-of-context theo PID.
    /// Tat: tra cac popup da day ra ve vi tri cu.
    /// </summary>
    public bool HidePopups
    {
        get => _hide;
        set
        {
            if (_hide == value) return;
            _hide = value;
            if (value) { foreach (var kv in _st) HideAndForget(kv.Key, kv.Value); }
            else { foreach (var s in _st.Values) { Unhook(s); Unghost(s); } }   // Zalo tu quan ly popup: tra exstyle goc
        }
    }

    private void Hook(State s)
    {
        if (s.Hooks != null || s.Pid <= 0) return;
        uint pid = (uint)s.Pid;
        s.Hooks = new[]
        {
            SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _cb, pid, 0, WINEVENT_OUTOFCONTEXT),
        };
    }

    private static void Unhook(State s)
    {
        if (s.Hooks == null) return;
        foreach (var h in s.Hooks) if (h != IntPtr.Zero) UnhookWinEvent(h);
        s.Hooks = null;
    }

    private void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (!_hide || hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != 0) return;
            if (GetAncestor(hwnd, GA_PARENT) != GetDesktopWindow()) return;
            var pid = PidOf(hwnd);
            string? id = null; State? st = null;
            foreach (var kv in _st) if (kv.Value.Pid > 0 && (uint)kv.Value.Pid == pid) { id = kv.Key; st = kv.Value; break; }
            if (st == null || id == null || !ZaloWindowFinder.IsNotifyPopup(hwnd)) return;
            st.Hwnd = hwnd;
            Conceal(id, st, DateTime.UtcNow);
        }
        catch (Exception ex) { _log.Warn("notify: loi callback " + ex.GetType().Name); }
    }

    /// <summary>An popup (SW_HIDE, khong dong/khong gui phim chuot); moi lan popup hien lai = 1 tin hieu "co tin moi".</summary>
    private void Conceal(string id, State s, DateTime now)
    {
        Ghostify(id, s);
        ShowWindowAsync(s.Hwnd, SW_HIDE);
        if (now < s.IgnoreUntilUtc) { s.LastConcealUtc = now; return; }   // SHOW do chinh ta / Chromium hien lai sau cu bam toast
        bool same = now - s.LastConcealUtc < ConcealMerge;
        s.LastConcealUtc = now;
        if (same) return;
        s.Hidden++; s.Count++; s.Last = DateTime.Now; _counts[id] = s.Count;
        _log.Info($"[{id}] thong bao kin: an popup (lan {s.Hidden})");
        PopupShown?.Invoke(id);
    }

    /// <summary>
    /// Lam popup vo hinh ben vung: WS_EX_LAYERED | WS_EX_TRANSPARENT + alpha 0 (cross-process), de cac lan SHOW sau khong ve gi
    /// va chuot xuyen qua. Luu exstyle goc de khoi phuc. Neu Zalo dat lai exstyle thi ap lai. That bai thi chi con SW_HIDE.
    /// </summary>
    private void Ghostify(string id, State s)
    {
        var h = s.Hwnd;
        if (h == IntPtr.Zero || !IsWindow(h)) return;
        const long Need = WS_EX_LAYERED | WS_EX_TRANSPARENT;
        try
        {
            if (s.Ghost.TryGetValue(h, out var g))
            {
                if (!g.Ok || (ExStyle(h) & Need) == Need) return;
                bool again = ApplyGhost(h, out _);
                if (!again) g.Ok = false;
                s.Ghost[h] = g;
                _log.Info($"[{id}] thong bao kin: popup bi dat lai exstyle, ap lai " + (again ? "ok" : "that bai"));
                return;
            }
            long orig = ExStyle(h);
            bool ok = ApplyGhost(h, out int err);
            s.Ghost[h] = new GhostInfo { Orig = orig, Ok = ok };
            _log.Info($"[{id}] thong bao kin: popup trong suot " + (ok ? "ok" : $"that bai ({err})"));
        }
        catch (Exception ex) { _log.Warn("notify: loi trong suot " + ex.GetType().Name); }
    }

    private static bool ApplyGhost(IntPtr h, out int err)
    {
        err = 0;
        long ex = ExStyle(h);
        // Da layered (Chromium dung UpdateLayeredWindow) thi SetLayeredWindowAttributes that bai: bo co roi dat lai.
        if ((ex & WS_EX_LAYERED) != 0) { SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex & ~WS_EX_LAYERED)); ex = ExStyle(h); }
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex | WS_EX_LAYERED | WS_EX_TRANSPARENT));
        if (SetLayeredWindowAttributes(h, 0, 0, LWA_ALPHA)) return true;
        err = Marshal.GetLastWin32Error();
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));   // that bai: tra nguyen
        return false;
    }

    /// <summary>Khoi phuc exstyle goc + bo alpha (cua so con ton tai).</summary>
    private static void Unghost(State s)
    {
        foreach (var (h, g) in s.Ghost)
        {
            if (!g.Ok || !IsWindow(h)) continue;
            try
            {
                SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(g.Orig & ~WS_EX_LAYERED));   // bo layered -> het alpha 0
                if ((g.Orig & WS_EX_LAYERED) != 0)
                {
                    SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(g.Orig));
                    SetLayeredWindowAttributes(h, 0, 255, LWA_ALPHA);
                }
            }
            catch { /* bo qua */ }
        }
        s.Ghost.Clear();
    }

    private static bool CursorIn(RECT r) =>
        GetCursorPos(out var p) && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

    /// <summary>Thoi diem (UTC) nghi nguoi dung vua bam popup cua tai khoan; null neu khong co.</summary>
    /// <summary>Dong bo (khong phu thuoc tick): ClickAt cu, hoac popup moi thay + con tro hien tai gan rect cuoi -> bay gio.</summary>
    public DateTime? PopupClickAt(string id)
    {
        if (!_st.TryGetValue(id, out var s)) return null;
        var now = DateTime.UtcNow;
        if (GluePolicy.ClickRecent(now, s.ClickAt)) return s.ClickAt;
        if (s.LastSeenUtc != null && GetCursorPos(out var p)
            && GluePolicy.PopupClickLikely(now, s.Visible, s.LastSeenUtc, s.LastRect, p.X, p.Y)) return now;
        return null;
    }
    public void ConsumePopupClick(string id)
    {
        if (_st.TryGetValue(id, out var s)) { s.ClickAt = null; s.LastSeenUtc = null; s.Consumed = s.Visible; }
    }

    /// <summary>Chuoi chan doan neu popup moi thay (<= 3 s); null neu khong.</summary>
    public string? PopupDiag(string id)
    {
        if (!_st.TryGetValue(id, out var s) || !GluePolicy.PopupRecent(DateTime.UtcNow, s.Visible, s.LastSeenUtc)) return null;
        var ago = s.Visible ? 0 : (int)(DateTime.UtcNow - s.LastSeenUtc!.Value).TotalMilliseconds;
        var cur = GetCursorPos(out var p) ? $"{p.X},{p.Y}" : "?";
        var r = s.LastRect;
        return $"con tro ({cur}) rect ({r.Left},{r.Top},{r.Right},{r.Bottom}) cach {ago}ms";
    }

    private readonly Func<IReadOnlyList<(string Id, string DisplayName, int Pid)>> _accounts;
    private readonly ILog _log;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, State> _st = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressed;

    public bool TagsEnabled { get; set; } = true;
    public bool Suppressed
    {
        get => _suppressed;
        set { _suppressed = value; if (value) foreach (var s in _st.Values) s.Tag?.HideTag(); }
    }
    public IReadOnlyDictionary<string, int> ShownCounts => _counts;
    public event Action<string>? PopupShown;
    public event Action<string>? PopupHidden;

    public NotifyController(Func<IReadOnlyList<(string Id, string DisplayName, int Pid)>> runningAccounts, ILog log)
    {
        _accounts = runningAccounts; _log = log;
        _cb = OnWinEvent;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();
    public void Stop() { _timer.Stop(); foreach (var s in _st.Values) s.Tag?.HideTag(); }

    private void Tick()
    {
        IReadOnlyList<(string Id, string DisplayName, int Pid)> list;
        try { list = _accounts(); } catch { return; }
        var alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        foreach (var (id, name, pid) in list)
        {
            alive.Add(id);
            if (!_st.TryGetValue(id, out var s)) _st[id] = s = new State();
            if (s.Pid != pid) { Unhook(s); s.Ghost.Clear(); s.Pid = pid; s.Hwnd = IntPtr.Zero; s.NextFind = default; HideAndForget(id, s); }
            if (s.Name != name) { s.Name = name; s.Tag?.SetName(name); }
            if (_hide) Hook(s);

            if (s.Hwnd != IntPtr.Zero && !IsWindow(s.Hwnd)) s.Hwnd = IntPtr.Zero;
            if (s.Hwnd == IntPtr.Zero && pid > 0 && now >= s.NextFind)
            {
                s.NextFind = now.AddSeconds(2);
                try { s.Hwnd = ZaloWindowFinder.FindNotifyPopup((uint)pid); } catch { s.Hwnd = IntPtr.Zero; }
            }
            Observe(id, s, now);
        }
        foreach (var id in _st.Keys.Where(k => !alive.Contains(k)).ToList())
        {
            var s = _st[id];
            Unhook(s);
            if (s.Visible) PopupHidden?.Invoke(id);
            s.Tag?.Close();
            _st.Remove(id);
        }
    }

    /// <summary>Doc trang thai popup (s.Hwnd) va phat su kien hien/an; che do kin: day popup ra ngoai, khong ve nhan.</summary>
    private void Observe(string id, State s, DateTime now)
    {
        {
            if (_hide)
            {
                // Luoi an toan: popup dang hien (hook bo sot / Chromium hien lai) -> an.
                if (s.Hwnd != IntPtr.Zero && IsWindowVisible(s.Hwnd)) Conceal(id, s, now);
                else if (s.Hwnd != IntPtr.Zero) Ghostify(id, s);   // kiem tra/ap lai exstyle
                return;
            }
            bool vis = false; RECT r = default;
            if (s.Hwnd != IntPtr.Zero) { vis = IsWindowVisible(s.Hwnd); if (vis) r = Rect(s.Hwnd); }

            // Chi hinh hoc/trang thai nut: chuot trai dang nhan trong rect popup -> ghi nhan "bam popup". Che do kin: bo qua.
            if (!vis) s.Consumed = false;
            else if (!s.Consumed) { s.LastRect = r; s.LastSeenUtc = now; }
            if (!_hide)
            {
                if (vis && (GetAsyncKeyState(0x01) & 0x8000) != 0 && CursorIn(r)) s.ClickAt = now;
                // Popup vua bien mat khi con tro nam trong rect cuoi cung: bam nhanh hon chu ky 300 ms.
                if (!vis && s.Visible && CursorIn(s.Rect)) s.ClickAt = now;
            }

            if (vis && !s.Visible)
            {
                s.Visible = true; s.Rect = r; s.Count++; s.Last = DateTime.Now; _counts[id] = s.Count;
                _log.Info($"[{id}] thong bao: popup hien (lan {s.Count}) tai {r.Left},{r.Top}");
                PopupShown?.Invoke(id);
                UpdateTag(s, r);
            }
            else if (!vis && s.Visible)
            {
                s.Visible = false;
                s.Tag?.HideTag();
                PopupHidden?.Invoke(id);
            }
            else if (vis && (!r.Same(s.Rect) || s.Tag == null))
            {
                s.Rect = r; UpdateTag(s, r);
            }
        }
    }

    private void HideAndForget(string id, State s)
    {
        s.Tag?.HideTag();
        if (s.Visible) { s.Visible = false; PopupHidden?.Invoke(id); }
    }

    private void UpdateTag(State s, RECT r)
    {
        if (!TagsEnabled || _suppressed || _hide) { s.Tag?.HideTag(); return; }
        s.Tag ??= new NotifyTagWindow(s.Name);
        int w = r.Width, y = r.Top - TagH - TagGap;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        var m = MonitorFromRect(ref r, 2);
        if (m != IntPtr.Zero && GetMonitorInfo(m, ref mi) && y < mi.rcWork.Top) y = r.Bottom + TagGap;
        s.Tag.ShowAround(r, y, TagH);   // nhan + khung quanh popup (w = r.Width dung trong ShowAround)
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (var s in _st.Values) { Unhook(s); Unghost(s); }
        foreach (var s in _st.Values) { try { s.Tag?.Close(); } catch { } }
        _st.Clear();
    }
}
