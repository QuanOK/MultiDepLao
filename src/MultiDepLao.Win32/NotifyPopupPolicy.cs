using static MultiDepLao.Win32.Native;

namespace MultiDepLao.Win32;

/// <summary>
/// Quyet dinh thuan (khong P/Invoke) ve popup tin nhan cua Zalo. Tieu chi nhan dien CHAT de khong dung nham cua so khac
/// (cuoc goi den, cua so chinh...): lop Chrome_WidgetWin_1 + WS_EX_TOPMOST + kich thuoc 352x82 (DIP; chap nhan ca 352x82
/// pixel tho va 352x82 nhan theo DPI cua cua so, sai so 1 px). Thuoc cay tien trinh cua instance do noi goi dam bao.
/// </summary>
public static class NotifyPopupPolicy
{
    public const int LogicalW = 352, LogicalH = 82;

    private static bool SizeIs(int w, int h, uint dpi)
    {
        double k = (dpi == 0 ? 96 : dpi) / 96.0;
        return Math.Abs(w - Math.Round(LogicalW * k)) <= 1 && Math.Abs(h - Math.Round(LogicalH * k)) <= 1;
    }

    public static bool IsPopupShape(string className, bool topmost, int w, int h, uint dpi) =>
        className == ZaloWindowFinder.ChromeClass && topmost && (SizeIs(w, h, 96) || SizeIs(w, h, dpi));

    /// <summary>Con giao voi vung ao cua cac man hinh (virt) -> can day ra. Da o ngoai -> khong lam gi (tranh vong lap LOCATIONCHANGE).</summary>
    public static bool ShouldPush(RECT r, RECT virt) =>
        r.Right > virt.Left && r.Left < virt.Right && r.Bottom > virt.Top && r.Top < virt.Bottom;

    /// <summary>Hop bao cac man hinh -> cho dat popup (ngoai ben trai vung ao, giu kich thuoc).</summary>
    public static RECT Target(RECT virt, RECT cur) => GluePolicy.OffscreenRect(virt, cur.Width, cur.Height);
}
