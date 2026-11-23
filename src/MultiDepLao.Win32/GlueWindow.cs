using static MultiDepLao.Win32.Native;

namespace MultiDepLao.Win32;

/// <summary>
/// "Dan" mot cua so Zalo (kieu A, khong reparent) vao vung hien thi cua host: go khung, dat owner = host, tool window,
/// dat rect theo vung (pixel thiet bi, toa do man hinh). Moi thao tac ghi deu qua Guard (hwnd con ton tai va thuoc dung pid).
/// Thread-safe (lock). Attach/Restore co the cho toi da ~2 s neu cua so dang minimized.
/// </summary>
public sealed class GlueWindow
{
    private const long FrameBits = WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
    private const uint SyncFlags = SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS;

    /// <summary>Chieu rong (DIP) cua so Zalo bi an duoc "do" lai: duoi ~1000 px de Chromium khong gop cot dieu huong.</summary>
    public const int ParkWidthDip = 844;

    private readonly object _lock = new();
    private readonly Action<string> _log;
    public uint Pid { get; }
    public IntPtr Hwnd { get; }
    public bool IsAttached { get; private set; }
    public bool UserHidden { get; private set; }
    /// <summary>Dang bi khoa app: cua so bi an va EnsureShown/SetHidden(false)/HostMinimized(false) khong hien lai.</summary>
    public bool Locked { get; private set; }
    public bool UseToolWindow { get; set; } = true;

    private long _origStyle, _origExStyle, _appliedStyle;
    private IntPtr _origOwner;
    private RECT _origRect;
    private bool _origVisible, _haveOrig, _hostHidden;


    /// <summary>Cua so co dinh kich thuoc (man hinh dang nhap: khong WS_THICKFRAME) -> canh giua, khong ep kich thuoc.</summary>
    public bool IsFixed => ((_haveOrig ? _origStyle : Style(Hwnd)) & WS_THICKFRAME) == 0;

    private DateTime _lastCorrection = DateTime.MinValue;
    public int Corrections { get; private set; }

    public GlueWindow(uint pid, IntPtr hwnd, Action<string> log) { Pid = pid; Hwnd = hwnd; _log = log; }

    private bool Guard(string op)
    {
        if (!IsWindow(Hwnd)) { _log($"[guard] {op}: hwnd khong con ton tai"); return false; }
        if (PidOf(Hwnd) != Pid) { _log($"[guard] {op}: hwnd khong thuoc pid {Pid} -> TU CHOI"); return false; }
        return true;
    }

    private bool GuardResponsive(string op)
    {
        if (!Guard(op)) return false;
        if (!Responsive(Hwnd, 1000)) { _log($"[guard] {op}: Zalo khong phan hoi -> bo qua"); return false; }
        return true;
    }

    public string Describe()
    {
        var r = Rect(Hwnd);
        return $"hwnd=0x{Hwnd.ToInt64():X} style={Hex(Style(Hwnd))} ex={Hex(ExStyle(Hwnd))} owner=0x{GetWindow(Hwnd, GW_OWNER).ToInt64():X} " +
               $"rect={r} vis={IsWindowVisible(Hwnd)} dpi={GetDpiForWindow(Hwnd)}";
    }

    private RECT? RestoreIfMinimized()
    {
        if (!IsIconic(Hwnd)) return null;
        var normal = NormalRect(Hwnd);
        ShowWindowAsync(Hwnd, SW_RESTORE);
        var t0 = DateTime.Now;
        while (IsIconic(Hwnd) && (DateTime.Now - t0).TotalMilliseconds < 2000) Thread.Sleep(30);
        _log($"[restore-min] rcNormal={normal} iconic={IsIconic(Hwnd)} sau {(int)(DateTime.Now - t0).TotalMilliseconds} ms");
        return normal;
    }

    private void SaveOriginal(RECT? keepRect, bool? keepVisible)
    {
        _origStyle = Style(Hwnd);
        _origExStyle = ExStyle(Hwnd);
        _origOwner = GetWindow(Hwnd, GW_OWNER);
        _origRect = keepRect ?? NormalRect(Hwnd);
        _origVisible = keepVisible ?? IsWindowVisible(Hwnd);
        SanitizeOriginal();
        _haveOrig = true;
        _lastCorrectionLog = DateTime.MinValue;
        Corrections = 0;
        _log($"[orig] style={Hex(_origStyle)} ex={Hex(_origExStyle)} owner=0x{_origOwner.ToInt64():X} rect={_origRect} vis={_origVisible} fixed={IsFixed}");
    }

    /// <summary>
    /// Sau khi app bi kill/mat dien, cua so Zalo con trang thai "da dan" (khong khung, tool window, owner chet, co the an /
    /// ngoai man hinh). Neu "trang thai goc" doc duoc la nhu vay thi thay bang gia tri mac dinh hop ly de Restore sau nay
    /// tra ve cua so binh thuong. Khong luu file: dau hieu nhan biet du ro (tool window + khong WS_CAPTION).
    /// </summary>
    private void SanitizeOriginal()
    {
        bool stuck = (_origExStyle & WS_EX_TOOLWINDOW) != 0 && (_origStyle & WS_CAPTION) != WS_CAPTION;
        if (stuck)
        {
            _origStyle |= WS_CAPTION | FrameBits;
            _origExStyle = (_origExStyle & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW;
            _origVisible = true;
            _log("[orig] phat hien cua so ket tu lan chay truoc -> chuan hoa style/exstyle");
        }
        // Owner chi hop le neu la cua so con song cung pid Zalo; con lai (host cu da chet, hoac cua so khac) -> bo.
        if (_origOwner != IntPtr.Zero && (!IsWindow(_origOwner) || PidOf(_origOwner) != Pid))
        {
            _log("[orig] owner goc khong hop le -> bo");
            _origOwner = IntPtr.Zero;
        }
        // Vi tri ngoai moi man hinh hoac qua nho -> dat giua man hinh chinh voi kich thuoc mac dinh.
        var r = _origRect;
        bool onScreen = r.Width > 0 && r.Height > 0 && MonitorFromRect(ref r, 0) != IntPtr.Zero;
        if (!onScreen || r.Width < 300 || r.Height < 300)
        {
            int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
            int w = Math.Min(1000, Math.Max(400, sw - 100)), h = Math.Min(700, Math.Max(450, sh - 100));
            _origRect = RECT.FromXywh(Math.Max(0, (sw - w) / 2), Math.Max(0, (sh - h) / 2), w, h);
            _log($"[orig] rect goc khong hop le {r} -> mac dinh {_origRect}");
        }
    }

    private IntPtr SetLong(int idx, long v)
    {
        System.Runtime.InteropServices.Marshal.SetLastPInvokeError(0);
        var prev = SetWindowLongPtr(Hwnd, idx, (IntPtr)v);
        int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        if (prev == IntPtr.Zero && err != 0) _log($"[warn] SetWindowLongPtr({idx}) loi Win32 {err}");
        return prev;
    }

    /// <summary>Rect dich (toa do man hinh). Co dinh -> canh giua theo kich thuoc goc.</summary>
    public RECT TargetFor(RECT panelScreen)
    {
        if (IsFixed && _haveOrig)
        {
            int w = _origRect.Width, h = _origRect.Height;
            int x = panelScreen.Left + Math.Max(0, (panelScreen.Width - w) / 2);
            int y = panelScreen.Top + Math.Max(0, (panelScreen.Height - h) / 2);
            return RECT.FromXywh(x, y, w, h);
        }
        return panelScreen;
    }

    private void Place(RECT t, uint extra = 0) =>
        SetWindowPos(Hwnd, IntPtr.Zero, t.Left, t.Top, t.Width, t.Height, SyncFlags | extra);

    /// <summary>Dan vao host. keepRect/keepVisible: nguyen trang goc da biet (sau Forget). False neu da gan hoac Guard tu choi.</summary>
    public bool Attach(IntPtr hostHwnd, RECT panelScreen, bool hidden, RECT? keepRect = null, bool? keepVisible = null)
    {
        lock (_lock)
        {
            if (IsAttached) { _log("[glue] dang gan roi"); return false; }
            if (!GuardResponsive("Attach")) return false;
            var minRect = RestoreIfMinimized();
            if (minRect != null) keepRect ??= minRect;
            SaveOriginal(keepRect, keepVisible);
            if (hidden) Park(); // ra ngoai man hinh truoc khi doi style (cua so co the dang hien)

            _appliedStyle = _origStyle & ~FrameBits;
            SetLong(GWL_STYLE, _appliedStyle);
            if (UseToolWindow)
                SetLong(GWL_EXSTYLE, (_origExStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
            SetLong(GWLP_HWNDPARENT, hostHwnd.ToInt64());
            IsAttached = true;
            UserHidden = hidden;

            if (IsWindowVisible(Hwnd)) ShowWindowAsync(Hwnd, SW_HIDE); // taskbar/Alt-Tab danh gia lai
            var target = TargetFor(panelScreen);
            if (hidden) target = ParkTarget(target.Width, target.Height);
            Place(target, SWP_FRAMECHANGED);
            if (!hidden && !Locked) { ShowWindowAsync(Hwnd, SW_SHOWNA); if (!IsFixed) PendingNudge = true; }
            _log($"[glue] gan xong{(hidden ? " (an)" : "")}{(Locked ? " (khoa)" : "")}. {Describe()}");
            return true;
        }
    }

    /// <summary>Zalo tu doi style (vd man hinh dang nhap -> man hinh chinh cung handle)?</summary>
    public bool StyleTampered()
    {
        if (!IsAttached || !IsWindow(Hwnd)) return false;
        return (Style(Hwnd) & FrameBits) != (_appliedStyle & FrameBits);
    }

    /// <summary>Dong bo vi tri theo vung hien thi (toa do man hinh).</summary>
    public void Sync(RECT panelScreen)
    {
        lock (_lock)
        {
            if (!IsAttached || !Guard("Sync")) return;
            Place(TargetFor(panelScreen));
        }
    }

    /// <summary>Goi tu WinEventWatcher.TargetLocationChanged: keo Zalo ve dung cho neu no tu di chuyen.</summary>
    public void OnLocationChanged(RECT panelScreen, bool hostMinimized) => Enforce(panelScreen, hostMinimized, false);

    /// <summary>
    /// Keo Zalo ve vung dich neu lech vi tri HOAC kich thuoc. force=false: throttle 100 ms; force=true: bo throttle.
    /// Tra ve true neu van con lech / bi throttle (nguoi goi nen kiem lai tre).
    /// </summary>
    public bool Enforce(RECT panelScreen, bool hostMinimized, bool force)
    {
        if (!IsAttached || UserHidden || Locked || hostMinimized) return false;
        if (!IsWindow(Hwnd) || IsIconic(Hwnd)) return false;
        var target = TargetFor(panelScreen);
        var r = Rect(Hwnd);
        var now = DateTime.Now;
        var d = GluePolicy.Decide(r, target, now, _lastCorrection, force);
        if (!d.Off) return false;
        if (!d.Correct) return true;
        _lastCorrection = now;
        Corrections++;
        if (GluePolicy.LogAllowed(now, _lastCorrectionLog))
        {
            _lastCorrectionLog = now;
            _log($"[glue] Zalo lech {(d.PosOff ? "vi tri" : "")}{(d.PosOff && d.SizeOff ? "+" : "")}{(d.SizeOff ? "kich thuoc" : "")} {r} -> keo ve {target} (lan {Corrections})");
        }
        Sync(panelScreen);
        if (d.SizeOff && !IsFixed) PendingNudge = true;
        return GluePolicy.IsOff(Rect(Hwnd), target);
    }

    /// <summary>
    /// Cho ep relayout: sau gan khi dang hien / sau keo ve kich thuoc, Zalo (Electron) co the khong nhan WM_SIZE that
    /// (SetWindowPos cung kich thuoc) nen noi dung ket bo cuc cu. Nudge() dat w-1 roi w khi cua so da on dinh.
    /// </summary>
    public bool PendingNudge { get; private set; }

    public bool Nudge(RECT panelScreen)
    {
        lock (_lock)
        {
            if (!PendingNudge) return false;
            if (!IsAttached || UserHidden || Locked || _hostHidden || !Guard("Nudge") || IsIconic(Hwnd) || !IsWindowVisible(Hwnd))
            { PendingNudge = false; return false; }
            var t = TargetFor(panelScreen);
            if (!GluePolicy.ShouldNudge(true, Rect(Hwnd), t)) return false; // con lech: giu co, Enforce se xu ly
            PendingNudge = false;
            _log($"[glue] ep ve lai (w-1 -> w) {t}");
            SetWindowPos(Hwnd, IntPtr.Zero, t.Left, t.Top, t.Width - 1, t.Height, SyncFlags);
            SetWindowPos(Hwnd, IntPtr.Zero, t.Left, t.Top, t.Width, t.Height, SyncFlags);
            return true;
        }
    }

    private DateTime _lastCorrectionLog = DateTime.MinValue;

    public void ResetSizeLog() => _lastCorrectionLog = DateTime.MinValue;

    /// <summary>
    /// Zalo bat dau vong keo/doi kich thuoc cua chinh no (nguoi dung keo mep Zalo): huy vong bang PostMessage WM_CANCELMODE
    /// (thong diep chuan cua Windows, khong inject). Chi khi da gan, dang hien, khong khoa.
    /// </summary>
    public void CancelUserMove()
    {
        lock (_lock)
        {
            if (!IsAttached || UserHidden || Locked || !Guard("CancelUserMove")) return;
            PostMessage(Hwnd, WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
            _log("[glue] Zalo bat dau keo/doi kich thuoc -> huy (WM_CANCELMODE)");
        }
    }

    /// <summary>An/hien theo viec tai khoan co dang duoc chon khong. Khi dang khoa, hidden=false chi ghi nho, khong hien.</summary>
    public void SetHidden(bool hidden)
    {
        lock (_lock)
        {
            if (!Guard("SetHidden")) return;
            UserHidden = hidden;
            ShowWindowAsync(Hwnd, hidden || Locked ? SW_HIDE : SW_SHOWNA);
            if (hidden && IsAttached) Park();
        }
    }

    private static RECT VirtualScreen() => RECT.FromXywh(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    /// <summary>Rect do ngoai man hinh cho cua so an: thu hep (neu khong co dinh) de UnreadBadgeProbe khong quet toan cay.</summary>
    private RECT ParkTarget(int w, int h)
    {
        if (!IsFixed) { int pw = ParkWidthPx(); if (GluePolicy.ShouldPark(false, w, pw)) w = pw; }
        return GluePolicy.OffscreenRect(VirtualScreen(), w, h);
    }

    /// <summary>Dat cua so an ra ngoai man hinh (thu hep neu can). Khi Zalo tu ShowWindow luc dang an thi no hien ngoai man hinh.</summary>
    private void Park()
    {
        var r = Rect(Hwnd);
        var t = ParkTarget(r.Width, r.Height);
        if (t.Left == r.Left && t.Top == r.Top && t.Width == r.Width) return;
        SetWindowPos(Hwnd, IntPtr.Zero, t.Left, t.Top, t.Width, t.Height, SyncFlags);
    }

    /// <summary>
    /// Zalo (dang bi ta an) tu di chuyen cua so vao man hinh (khoi phuc vi tri luu, cua so dang nhap...): dua ra ngoai ngay,
    /// truoc khi no ShowWindow. Goi tu WinEventWatcher.TargetLocationChanged.
    /// </summary>
    public void KeepParked()
    {
        lock (_lock)
        {
            if (!IsAttached || !UserHidden || !IsWindow(Hwnd) || IsIconic(Hwnd) || PidOf(Hwnd) != Pid) return;
            var r = Rect(Hwnd);
            if (MonitorFromRect(ref r, 0) == IntPtr.Zero) return; // da o ngoai man hinh
            _log($"[park] Zalo tu dua cua so an vao man hinh {r} -> dua ra ngoai (lan {++ParkedAgain})");
            Park();
        }
    }

    /// <summary>So lan phai dua lai ra ngoai man hinh sau khi Zalo tu di chuyen vao (chan doan).</summary>
    public int ParkedAgain { get; private set; }

    private int ParkWidthPx() => GluePolicy.ParkWidthPx(ParkWidthDip, GetDpiForWindow(Hwnd));

    /// <summary>Tai khoan dang chon bi Zalo minimize / an xuong khay: hien lai va canh lai. Khong lam gi khi khoa/host an.</summary>
    public bool EnsureShown(RECT panelScreen)
    {
        lock (_lock)
        {
            if (!IsAttached || UserHidden || _hostHidden || Locked || !Guard("EnsureShown")) return false;
            bool iconic = IsIconic(Hwnd), vis = IsWindowVisible(Hwnd);
            if (!iconic && vis) return false;
            _log($"[glue] Zalo bi {(iconic ? "minimized" : "an")} -> hien lai");
            if (iconic) ShowWindowAsync(Hwnd, SW_RESTORE);
            Place(TargetFor(panelScreen), SWP_FRAMECHANGED);
            ShowWindowAsync(Hwnd, SW_SHOWNA);
            return true;
        }
    }

    /// <summary>Host minimize/restore (khong doi UserHidden).</summary>
    public void HostMinimized(bool minimized)
    {
        lock (_lock)
        {
            if (!IsAttached || !Guard("HostMinimized")) return;
            _hostHidden = minimized;
            if (minimized) ShowWindowAsync(Hwnd, SW_HIDE);
            else if (!UserHidden && !Locked) ShowWindowAsync(Hwnd, SW_SHOWNA);
        }
    }

    /// <summary>Khoa app: an cua so bat ke tai khoan co dang chon hay khong. Goi lai khi cua so tu hien (TopLevelShown) la an toan.</summary>
    public void HideForLock()
    {
        lock (_lock)
        {
            Locked = true;
            if (!IsAttached || !Guard("HideForLock")) return;
            ShowWindowAsync(Hwnd, SW_HIDE);
        }
    }

    /// <summary>Mo khoa: hien lai neu tai khoan dang chon (khong UserHidden) va host khong minimized.</summary>
    public void ShowAfterLock()
    {
        lock (_lock)
        {
            Locked = false;
            if (!IsAttached || UserHidden || _hostHidden || !Guard("ShowAfterLock")) return;
            ShowWindowAsync(Hwnd, SW_SHOWNA);
        }
    }

    /// <summary>Khoi phuc nguyen trang. Idempotent, an toan goi tu bat ky thread nao.</summary>
    public void Restore(string reason)
    {
        lock (_lock) RestoreInternal(reason);
    }

    /// <summary>
    /// Zalo tu doi style: bo owner/exstyle cua ta, KHONG ghi lai style/rect cu. Tra ve rect/visible goc de lan Attach sau
    /// giu nguyen trang ban dau.
    /// </summary>
    public (RECT rect, bool visible)? Forget(string reason)
    {
        lock (_lock)
        {
            if (!IsAttached || !_haveOrig) return null;
            var keep = (_origRect, _origVisible);
            if (Guard("Forget"))
            {
                _log($"[forget] {reason}");
                SetLong(GWLP_HWNDPARENT, _origOwner.ToInt64());
                SetLong(GWL_EXSTYLE, _origExStyle);
            }
            IsAttached = false;
            return keep;
        }
    }

    private void RestoreInternal(string reason)
    {
        if (!IsAttached || !_haveOrig) return;
        if (!Guard("Restore")) { IsAttached = false; return; }
        _log($"[restore] ly do: {reason}");

        if (IsIconic(Hwnd)) { ShowWindowAsync(Hwnd, SW_RESTORE); Thread.Sleep(100); }
        bool curVisible = IsWindowVisible(Hwnd);
        // Neu Zalo tu an cua so (khong phai ta an) thi khong hien lai
        bool hiddenByZalo = !curVisible && !UserHidden && !_hostHidden && !Locked;

        SetLong(GWLP_HWNDPARENT, _origOwner.ToInt64());
        long style = (_origStyle & ~WS_VISIBLE) | (Style(Hwnd) & WS_VISIBLE);
        SetLong(GWL_STYLE, style);
        SetLong(GWL_EXSTYLE, _origExStyle);

        SetWindowPos(Hwnd, IntPtr.Zero, _origRect.Left, _origRect.Top, _origRect.Width, _origRect.Height,
            SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_ASYNCWINDOWPOS);

        if (curVisible) ShowWindowAsync(Hwnd, SW_HIDE); // taskbar danh gia lai sau khi bo toolwindow/owner
        if (!hiddenByZalo) ShowWindowAsync(Hwnd, SW_SHOWNA);

        IsAttached = false;
        UserHidden = false;
        _hostHidden = false;
        _log($"[restore] xong. {Describe()}");
    }
}
