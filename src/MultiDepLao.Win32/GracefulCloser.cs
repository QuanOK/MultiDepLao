using MultiDepLao.Core;
using static MultiDepLao.Win32.Native;

namespace MultiDepLao.Win32;

/// <summary>
/// Thoat em: gui WM_CLOSE (PostMessage, khong cho) toi moi cua so top-level cua tien trinh chinh
/// (tuong duong taskkill /PID khong /F). Do thu nghiem: ca cay Zalo thoat trong ~0.3-0.6 giay.
/// Chi gui toi hwnd thuoc cay tien trinh cua PID duoc truyen vao; khong doc tieu de/noi dung.
/// </summary>
public sealed class GracefulCloser : IGracefulCloser
{
    private const uint WM_CLOSE = 0x0010;

    public bool TryClose(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            var tree = ProcessTree.Descendants((uint)pid);
            if (!IsZaloExe((uint)pid)) return false;
            var targets = new List<IntPtr>();
            EnumWindows((h, _) =>
            {
                if (PidOf(h) == (uint)pid && tree.Contains(PidOf(h))) targets.Add(h);
                return true;
            }, IntPtr.Zero);
            int sent = 0;
            foreach (var h in targets)
                if (PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero)) sent++;
            return sent > 0;
        }
        catch { return false; }
    }

    private static bool IsZaloExe(uint pid)
    {
        var p = ImagePathOf(pid);
        return p != null && string.Equals(Path.GetFileName(p), "Zalo.exe", StringComparison.OrdinalIgnoreCase);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
}
