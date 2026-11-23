using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using MultiDepLao.Core;

namespace MultiDepLao.App;

/// <summary>
/// Bieu tuong khay he thong cua MultiDepLao: Shell_NotifyIconW (P/Invoke, khong WinForms) + cua so an HwndSource
/// nhan callback. Menu Win32 (CreatePopupMenu/TrackPopupMenuEx). Chay tren UI thread.
/// </summary>
public sealed class TrayController : IDisposable
{
    public static string DefaultTip { get; } = $"{AppInfo.DisplayName} {AppInfo.Version}";

    private const uint CallbackMsg = 0x8001;   // WM_APP + 1
    private const uint IconId = 1;
    private const int IdOpen = 1, IdLock = 2, IdAbout = 3, IdWhatsNew = 4, IdDonate = 5, IdExit = 6;

    private readonly Action _open, _lockNow, _exit, _about, _donate, _whatsNew;
    private readonly Func<bool> _canLock;
    private readonly HwndSource _src;
    private readonly uint _taskbarCreated;
    private IntPtr _hIcon;
    private string _tip = DefaultTip;
    private bool _disposed;

    public TrayController(Action open, Action lockNow, Action exit, Func<bool> canLock, Action about, Action donate, Action whatsNew)
    {
        _open = open; _lockNow = lockNow; _exit = exit; _canLock = canLock; _about = about; _donate = donate; _whatsNew = whatsNew;
        // Cua so top-level an (khong phai message-only: message-only khong nhan broadcast "TaskbarCreated").
        var p = new HwndSourceParameters("MultiDepLaoTray")
        {
            WindowStyle = unchecked((int)0x80000000),   // WS_POPUP, khong WS_VISIBLE
            ExtendedWindowStyle = 0x80,                 // WS_EX_TOOLWINDOW
            Width = 0,
            Height = 0,
        };
        _src = new HwndSource(p);
        _src.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _hIcon = LoadIcon();
        AddIcon();
        Loc.Changed += OnLocChanged;
    }

    private void OnLocChanged()
    {
        if (_disposed) return;
        ResetTip();   // tooltip dang hien co the chua chu theo ngon ngu cu (menu dung chu moi moi lan mo)
    }

    public void SetTip(string text)
    {
        if (_disposed) return;
        _tip = TrayLogic.ClampTip(text);
        var nid = NewData(NIF_TIP | NIF_SHOWTIP);
        nid.szTip = _tip;
        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    public void ResetTip() => SetTip(DefaultTip);

    /// <summary>Thong bao Windows ngan (bong bong khay). Chi truyen chuoi do app tu dung, khong noi dung tin.</summary>
    public void Balloon(string text)
    {
        if (_disposed) return;
        try
        {
            var nid = NewData(NIF_INFO);
            nid.szInfo = text.Length > 255 ? text[..255] : text;
            nid.szInfoTitle = AppInfo.DisplayName;
            nid.dwInfoFlags = NIIF_INFO;
            Shell_NotifyIconW(NIM_MODIFY, ref nid);
        }
        catch { /* bo qua */ }
    }

    private void AddIcon()
    {
        var nid = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        nid.uCallbackMessage = CallbackMsg;
        nid.hIcon = _hIcon;
        nid.szTip = _tip;
        Shell_NotifyIconW(NIM_ADD, ref nid);
        var v = NewData(0);
        v.uVersion = 4;
        _v4 = Shell_NotifyIconW(NIM_SETVERSION, ref v);   // that bai -> callback kieu cu (WM_RBUTTONUP, khong WM_CONTEXTMENU)
    }

    private bool _v4;

    private NOTIFYICONDATAW NewData(uint flags)
    {
        return new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = _src.Handle, uID = IconId, uFlags = flags,
            szTip = "", szInfo = "", szInfoTitle = "",
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed) return IntPtr.Zero;
        if ((uint)msg == _taskbarCreated)
        {
            AddIcon();   // Explorer khoi dong lai: dang ky lai icon
        }
        else if ((uint)msg == CallbackMsg)
        {
            int ev = (int)((long)lParam & 0xFFFF);
            handled = true;
            if (ev == WM_LBUTTONDBLCLK) Dispatch(_open);
            else if (ev == WM_CONTEXTMENU)
            {
                // v4: wParam = toa do man hinh (x thap, y cao, co dau) cua cho bam / bieu tuong
                ShowMenu((short)((long)wParam & 0xFFFF), (short)(((long)wParam >> 16) & 0xFFFF));
            }
            else if (ev == WM_RBUTTONUP && !_v4 && GetCursorPos(out var pt))
            {
                ShowMenu(pt.X, pt.Y);   // shell khong nhan NOTIFYICON_VERSION_4: menu theo con tro
            }
        }
        return IntPtr.Zero;
    }

    private void Dispatch(Action a)
    {
        _src.Dispatcher.BeginInvoke(() => { if (!_disposed) a(); });
    }

    private void ShowMenu(int x, int y)
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        int cmd;
        try
        {
            AppendMenuW(menu, 0, IdOpen, Loc.F("Tray.Open", AppInfo.DisplayName));
            bool lockVisible = false;
            try { lockVisible = _canLock(); } catch { /* an muc Khoa */ }
            if (lockVisible) AppendMenuW(menu, 0, IdLock, Loc.T("Tray.Lock"));
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, 0, IdAbout, Loc.T("Tray.About"));
            AppendMenuW(menu, 0, IdWhatsNew, Loc.T("Tray.WhatsNew"));
            AppendMenuW(menu, 0, IdDonate, Loc.T("Tray.Donate"));
            AppendMenuW(menu, 0, IdExit, Loc.T("Tray.Exit"));
            SetForegroundWindow(_src.Handle);   // de menu tu dong khi bam ra ngoai
            cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_NONOTIFY, x, y, _src.Handle, IntPtr.Zero);
            PostMessageW(_src.Handle, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
        }
        finally { DestroyMenu(menu); }
        switch (cmd)
        {
            case IdOpen: Dispatch(_open); break;
            case IdLock: Dispatch(_lockNow); break;
            case IdAbout: Dispatch(_about); break;
            case IdWhatsNew: Dispatch(_whatsNew); break;
            case IdDonate: Dispatch(_donate); break;
            case IdExit: Dispatch(_exit); break;
        }
    }

    /// <summary>Logo app (Assets/app.ico): doc truc tiep thu muc .ico, chon anh hop co icon nho he thong theo DPI.</summary>
    private static IntPtr LoadIcon()
    {
        try
        {
            var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            using var ms = new MemoryStream();
            using (var s = res.Stream) s.CopyTo(ms);
            var d = ms.ToArray();
            int count = BitConverter.ToUInt16(d, 4);
            var sizes = new List<int>(count);
            for (int i = 0; i < count; i++) { int w = d[6 + i * 16]; sizes.Add(w == 0 ? 256 : w); }
            int cx = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
            int idx = TrayLogic.PickIconIndex(sizes, cx);
            if (idx < 0) return IntPtr.Zero;
            int e = 6 + idx * 16;
            int bytes = BitConverter.ToInt32(d, e + 8), off = BitConverter.ToInt32(d, e + 12);
            var img = new byte[bytes];
            Array.Copy(d, off, img, 0, bytes);
            return CreateIconFromResourceEx(img, (uint)bytes, true, 0x00030000, cx, cx, 0);
        }
        catch { return IntPtr.Zero; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loc.Changed -= OnLocChanged;
        try
        {
            var nid = NewData(0);
            Shell_NotifyIconW(NIM_DELETE, ref nid);
            _src.RemoveHook(WndProc);
            _src.Dispose();
            if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
        }
        catch { /* bo qua */ }
    }

    // ---- Win32 ----
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const uint NIIF_INFO = 1;
    private const int WM_LBUTTONDBLCLK = 0x203, WM_RBUTTONUP = 0x205, WM_CONTEXTMENU = 0x7B;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT pt);
    private const uint MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
    private const int SM_CXSMICON = 49;

    // CharSet.Unicode + ByValTStr: kich thuoc marshal = 976 byte (x64), uVersion o offset 812 nhu shellapi.h.
    // (Ban dau dung [InlineArray] char khong CharSet -> marshal ANSI 1 byte/ky tu: 528 byte, uVersion lech -> shell khong
    // nhan NOTIFYICON_VERSION_4 -> chuot phai khong co WM_CONTEXTMENU, tooltip/balloon hong chu.)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;   // union voi uTimeout
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint ver, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
