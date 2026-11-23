using System.Runtime.InteropServices;
using System.Windows.Interop;
using MultiDepLao.Core;

namespace MultiDepLao.App;

/// <summary>
/// Phim tat Ctrl+1..9 (+ Ctrl+L neu co khoa) CHI dang ky khi cua so dang o foreground thuoc MultiDepLao hoac mot cua so
/// Zalo cua tai khoan dang chay (theo EVENT_SYSTEM_FOREGROUND); huy dang ky ngay khi foreground sang app khac.
/// Chay tren UI thread (hook OUTOFCONTEXT can message loop; RegisterHotKey gan voi hwnd cua UI thread).
/// </summary>
public sealed class HotkeyController : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000, VK_TAB = 0x09;
    private const int IdTabNext = 0x4D5A11, IdTabPrev = 0x4D5A12;
    private const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
    private const int IdSelectBase = 0x4D5A00, IdLock = 0x4D5A10;   // 1..9 -> base+1..9

    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    private readonly IntPtr _hwnd;
    private readonly Func<IEnumerable<uint>> _accountPids;
    private readonly Func<bool> _wantLockKey;
    private readonly Action<int> _select;
    private readonly Action _lock;
    private readonly ILog _log;
    private readonly WinEventProc _proc;   // giu song delegate
    private HwndSource? _src;
    private IntPtr _hook;
    private readonly Action<int>? _selectRelative;
    private bool _registered, _lockRegistered, _tabRegistered, _failLogged;

    public HotkeyController(IntPtr hwnd, Func<IEnumerable<uint>> accountPids, Func<bool> wantLockKey, Action<int> select, Action lockNow, ILog log,
        Action<int>? selectRelative = null)
    {
        _hwnd = hwnd; _accountPids = accountPids; _wantLockKey = wantLockKey; _select = select; _lock = lockNow; _log = log;
        _selectRelative = selectRelative;
        _proc = (_, _, _, _, _, _, _) => Refresh();
    }

    public void Start()
    {
        _src = HwndSource.FromHwnd(_hwnd);
        _src?.AddHook(WndProc);
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_hook == IntPtr.Zero) _log.Warn("phim tat: khong cai duoc hook foreground");
        Refresh();
    }

    /// <summary>Tinh lai co nen dang ky hay khong (goi khi foreground doi, hoac khi cau hinh khoa doi).</summary>
    public void Refresh()
    {
        var plan = HotkeyPlan.For(IsMineForeground(), GetForegroundWindow() == _hwnd, _wantLockKey());
        if (plan.Select && !_registered)
        {
            _registered = true;
            for (int i = 1; i <= 9; i++) Reg(IdSelectBase + i, (uint)('0' + i));
        }
        else if (!plan.Select && _registered)
        {
            _registered = false;
            for (int i = 1; i <= 9; i++) UnregisterHotKey(_hwnd, IdSelectBase + i);
        }
        if (plan.Lock && !_lockRegistered) { _lockRegistered = true; Reg(IdLock, 'L'); }
        else if (!plan.Lock && _lockRegistered) { _lockRegistered = false; UnregisterHotKey(_hwnd, IdLock); }
        // Ctrl+Tab / Ctrl+Shift+Tab: WebView2 nuot phim (HWND rieng) nen PreviewKeyDown cua WPF khong thay; chi dang ky khi cua so chinh la foreground.
        if (plan.TabSwitch && !_tabRegistered)
        {
            _tabRegistered = true;
            Reg(IdTabNext, VK_TAB, MOD_CONTROL | MOD_NOREPEAT);
            Reg(IdTabPrev, VK_TAB, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT);
        }
        else if (!plan.TabSwitch && _tabRegistered) UnregisterTab();
    }

    private void UnregisterTab()
    {
        _tabRegistered = false;
        UnregisterHotKey(_hwnd, IdTabNext); UnregisterHotKey(_hwnd, IdTabPrev);
    }

    private void Reg(int id, uint vk, uint mods = MOD_CONTROL | MOD_NOREPEAT)
    {
        if (RegisterHotKey(_hwnd, id, mods, vk)) return;
        if (!_failLogged) { _failLogged = true; _log.Warn($"phim tat: RegisterHotKey loi {Marshal.GetLastWin32Error()} (phim bi app khac giu?)"); }
    }

    private bool IsMineForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        if (GetWindowThreadProcessId(fg, out uint pid) == 0) return false;
        if (pid == (uint)Environment.ProcessId) return true;
        foreach (var p in _accountPids()) if (p == pid) return true;
        return false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        int id = wParam.ToInt32();
        if (id > IdSelectBase && id <= IdSelectBase + 9) { handled = true; _select(id - IdSelectBase); }
        else if (id == IdLock) { handled = true; _lock(); }
        else if (id == IdTabNext || id == IdTabPrev) { handled = true; _selectRelative?.Invoke(id == IdTabNext ? 1 : -1); }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
        if (_registered) for (int i = 1; i <= 9; i++) UnregisterHotKey(_hwnd, IdSelectBase + i);
        if (_lockRegistered) UnregisterHotKey(_hwnd, IdLock);
        if (_tabRegistered) UnregisterTab();
        _registered = _lockRegistered = false;
        _src?.RemoveHook(WndProc);
    }
}
