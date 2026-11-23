using static MultiDepLao.Win32.Native;

namespace MultiDepLao.Win32;

/// <summary>
/// SetWinEventHook WINEVENT_OUTOFCONTEXT (khong inject). PHAI duoc tao (va Dispose) tren thread co message loop
/// (UI thread WPF); cac su kien cung phat tren thread do. Khong doc tieu de cua so.
/// </summary>
public sealed class WinEventWatcher : IDisposable
{
    private readonly uint _rootPid;
    private readonly Action<string> _log;
    private readonly WinEventDelegate _cb; // giu tham chieu, tranh GC
    private readonly List<IntPtr> _hooks = new();
    private HashSet<uint> _tree;
    private readonly HashSet<uint> _notTree = new();

    /// <summary>Cua so chinh dang theo doi (Zalo doi handle: dang nhap -> man hinh chinh).</summary>
    public IntPtr Target { get; set; }

    public event Action? TargetLocationChanged;
    public event Action? TargetMoveSizeStart;
    public event Action? TargetMoveSizeEnd;
    public event Action? TargetShown;
    public event Action? TargetHidden;
    public event Action? TargetDestroyed;
    /// <summary>Mot cua so top-level khac Target trong cay tien trinh vua hien (lop khoa dung de an lai).</summary>
    public event Action<IntPtr, uint>? TopLevelShown;

    public WinEventWatcher(uint rootPid, IntPtr target, Action<string> log)
    {
        _rootPid = rootPid; Target = target; _log = log;
        _cb = Callback;
        _tree = ProcessTree.Descendants(rootPid);
        // Location chi cho main pid (giam tai)
        _hooks.Add(SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _cb, rootPid, 0, WINEVENT_OUTOFCONTEXT));
        _hooks.Add(SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _cb, rootPid, 0, WINEVENT_OUTOFCONTEXT));
        // Destroy/Show/Hide toan he thong, loc theo cay (bat ca tien trinh con). CREATE cung nam trong dai nhung bi bo qua.
        _hooks.Add(SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE, IntPtr.Zero, _cb, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS));
        _log($"[watch] pid={rootPid} hooks: {string.Join(",", _hooks.Select(h => h == IntPtr.Zero ? "FAIL" : "ok"))}");
    }

    private bool InTree(uint pid)
    {
        if (pid == 0) return false;
        if (_tree.Contains(pid)) return true;
        if (_notTree.Contains(pid)) return false;
        _tree = ProcessTree.Descendants(_rootPid);
        if (_tree.Contains(pid)) return true;
        _notTree.Add(pid);
        return false;
    }

    private static bool IsTopLevel(IntPtr h) => GetAncestor(h, GA_PARENT) == GetDesktopWindow();

    private void Callback(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != 0) return;
            if (ev == EVENT_SYSTEM_MOVESIZESTART || ev == EVENT_SYSTEM_MOVESIZEEND)
            {
                if (hwnd == Target) { if (ev == EVENT_SYSTEM_MOVESIZESTART) TargetMoveSizeStart?.Invoke(); else TargetMoveSizeEnd?.Invoke(); }
                return;
            }
            if (ev == EVENT_OBJECT_LOCATIONCHANGE) { if (hwnd == Target) TargetLocationChanged?.Invoke(); return; }

            if (hwnd == Target)
            {
                switch (ev)
                {
                    case EVENT_OBJECT_DESTROY: TargetDestroyed?.Invoke(); break;
                    case EVENT_OBJECT_SHOW: TargetShown?.Invoke(); break;
                    case EVENT_OBJECT_HIDE: TargetHidden?.Invoke(); break;
                }
                return;
            }
            if (ev == EVENT_OBJECT_SHOW)
            {
                var pid = PidOf(hwnd);
                if (InTree(pid) && IsTopLevel(hwnd)) TopLevelShown?.Invoke(hwnd, pid);
            }
        }
        catch (Exception ex) { _log($"[watch] loi callback: {ex.Message}"); }
    }

    public void Dispose()
    {
        foreach (var h in _hooks) if (h != IntPtr.Zero) UnhookWinEvent(h);
        _hooks.Clear();
    }
}
