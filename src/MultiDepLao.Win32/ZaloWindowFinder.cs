using static MultiDepLao.Win32.Native;

namespace MultiDepLao.Win32;

/// <summary>Thong tin cua so top-level. Co y KHONG co tieu de.</summary>
public readonly record struct WindowInfo(IntPtr Hwnd, uint Pid, string ClassName, long Style, long ExStyle, RECT Rect, bool Visible);

public static class ZaloWindowFinder
{
    public const string ChromeClass = "Chrome_WidgetWin_1";

    /// <summary>
    /// Cua so chinh cua mot instance: top-level, class Chrome_WidgetWin_1, dung pid, co WS_CAPTION (HostProto tung loc them
    /// theo tieu de "Zalo*"; ban nay KHONG doc tieu de), visible hoac (du phong) dang an o khay; dien tich lon nhat,
    /// minimized dung rcNormalPosition. Khac HostProto: cua so khong WS_CAPTION nhung la tool window co owner (da bi
    /// GlueWindow go khung, vd sau crash cua app) van duoc nhan. Dung minW/minH de loai cua so phu nho.
    /// </summary>
    public static IntPtr FindMainWindow(uint mainPid, int minW = 0, int minH = 0)
    {
        IntPtr best = IntPtr.Zero, bestTray = IntPtr.Zero; long bestArea = -1, bestTrayArea = -1;
        EnumWindows((h, _) =>
        {
            if (PidOf(h) != mainPid || ClassOf(h) != ChromeClass) return true;
            long st = Style(h);
            bool framed = (st & WS_CAPTION) == WS_CAPTION;
            // Owner co the da bi he thong xoa (host chet sau khi app bi kill) -> chi doi owner khi khong co nguong kich thuoc.
            bool strippedByUs = !framed && (ExStyle(h) & WS_EX_TOOLWINDOW) != 0 && (ExStyle(h) & WS_EX_TOPMOST) == 0
                                && (GetWindow(h, GW_OWNER) != IntPtr.Zero || (minW > 0 && minH > 0));
            // Zalo sau khi khoi dong tu bo WS_CAPTION, giu WS_THICKFRAME (style 0x04070000, khong tool window): van la cua so chinh.
            bool zaloFrameless = !framed && (st & WS_THICKFRAME) != 0
                                 && (ExStyle(h) & (WS_EX_TOOLWINDOW | WS_EX_TOPMOST)) == 0 && minW > 0 && minH > 0;
            if (!framed && !strippedByUs && !zaloFrameless) return true;
            var r = NormalRect(h);
            if (r.Width < minW || r.Height < minH) return true;
            long area = (long)r.Width * r.Height;
            if (IsWindowVisible(h)) { if (area > bestArea) { bestArea = area; best = h; } }
            else if (area > bestTrayArea) { bestTrayArea = area; bestTray = h; } // Zalo tu an xuong khay
            return true;
        }, IntPtr.Zero);
        return best != IntPtr.Zero ? best : bestTray;
    }

    /// <summary>Cac cua so top-level (ke ca an) thuoc cac pid cho truoc.</summary>
    public static List<WindowInfo> EnumTopLevel(IEnumerable<uint> pids)
    {
        var set = pids as ISet<uint> ?? new HashSet<uint>(pids);
        var list = new List<WindowInfo>();
        EnumWindows((h, _) =>
        {
            var pid = PidOf(h);
            if (set.Contains(pid))
                list.Add(new WindowInfo(h, pid, ClassOf(h), Style(h), ExStyle(h), Rect(h), IsWindowVisible(h)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Popup thong bao tin moi (tieu chi: NotifyPopupPolicy), thuoc cay tien trinh cua mainPid. IntPtr.Zero neu khong co.</summary>
    public static IntPtr FindNotifyPopup(uint mainPid)
    {
        foreach (var w in EnumTopLevel(ProcessTree.Descendants(mainPid)))
            if (NotifyPopupPolicy.IsPopupShape(w.ClassName, (w.ExStyle & WS_EX_TOPMOST) != 0, w.Rect.Width, w.Rect.Height, GetDpiForWindow(w.Hwnd)))
                return w.Hwnd;
        return IntPtr.Zero;
    }

    /// <summary>Cua so top-level nay co dang la popup tin nhan (theo hinh dang) khong. Goi noi tu dam bao pid.</summary>
    public static bool IsNotifyPopup(IntPtr h) =>
        IsWindow(h) && NotifyPopupPolicy.IsPopupShape(ClassOf(h), (ExStyle(h) & WS_EX_TOPMOST) != 0, Rect(h).Width, Rect(h).Height, GetDpiForWindow(h));
}
