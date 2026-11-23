using static MultiDepLao.Win32.Native;

namespace MultiDepLao.Win32;

/// <summary>Quyet dinh thuan (khong P/Invoke) cho viec keo Zalo ve vung dich. Tach ra de test.</summary>
public static class GluePolicy
{
    public const int Tolerance = 1;
    public const int ThrottleMs = 100;
    public const int LogIntervalMs = 1000;
    public const int TrailingMs = 150;

    public readonly record struct Decision(bool PosOff, bool SizeOff, bool Correct)
    {
        public bool Off => PosOff || SizeOff;
    }

    public static bool PosOff(RECT r, RECT t) => Math.Abs(r.Left - t.Left) > Tolerance || Math.Abs(r.Top - t.Top) > Tolerance;
    public static bool SizeOff(RECT r, RECT t) => Math.Abs(r.Width - t.Width) > Tolerance || Math.Abs(r.Height - t.Height) > Tolerance;
    public static bool IsOff(RECT r, RECT t) => PosOff(r, t) || SizeOff(r, t);

    /// <summary>Correct = true khi lech va (force hoac da qua throttle). Lech nhung bi throttle -> Correct=false (can kiem lai tre).</summary>
    public static Decision Decide(RECT cur, RECT target, DateTime now, DateTime lastCorrection, bool force)
    {
        bool p = PosOff(cur, target), s = SizeOff(cur, target);
        bool ok = (p || s) && (force || (now - lastCorrection).TotalMilliseconds > ThrottleMs);
        return new Decision(p, s, ok);
    }

    /// <summary>Chieu rong "thu hep" (pixel) cho cua so an, theo dpi (0 -> 96).</summary>
    public static int ParkWidthPx(int parkWidthDip, uint dpi) =>
        (int)Math.Round(parkWidthDip * (dpi == 0 ? 96 : dpi) / 96.0);

    /// <summary>Co nen thu hep cua so an khong: da gan, khong co dinh kich thuoc, dang rong hon muc thu hep.</summary>
    public static bool ShouldPark(bool isFixed, int currentWidth, int parkWidthPx) => !isFixed && currentWidth > parkWidthPx;

    /// <summary>Ep 1 chu ky doi kich thuoc that (w-1 roi w) chi khi dang cho, cua so da dung dich (khong lech) va rong du de tru 1 px.</summary>
    public static bool ShouldNudge(bool pending, RECT cur, RECT target) =>
        pending && target.Width > 2 && !IsOff(cur, target);

    public const int OffscreenGap = 100;

    /// <summary>
    /// Rect "do" cua so Zalo dang an: nam hoan toan ben TRAI vung ao cua moi man hinh (virt = hop bao cac man hinh), nen
    /// khi Zalo tu ShowWindow thi cung khong ai thay. Giu nguyen kich thuoc w x h, top = mep tren vung ao.
    /// </summary>
    public static RECT OffscreenRect(RECT virt, int w, int h) => RECT.FromXywh(virt.Left - w - OffscreenGap, virt.Top, w, h);

    public const int PopupClickWindowMs = 3000;

    /// <summary>Bam trong vung popup con "moi" (trong cua so thoi gian) khong.</summary>
    public static bool ClickRecent(DateTime nowUtc, DateTime? clickAtUtc) =>
        clickAtUtc is { } c && (nowUtc - c).TotalMilliseconds >= 0 && (nowUtc - c).TotalMilliseconds <= PopupClickWindowMs;

    /// <summary>Zalo cua tai khoan dang an tu hien: chuyen chon sang no (thay vi an lai) khi vua bam popup cua no,
    /// va app khong khoa, khong co lop phu, tai khoan chua duoc chon.</summary>
    public static bool ShouldSwitchOnPopupClick(DateTime nowUtc, DateTime? clickAtUtc, bool locked, bool overlay, bool alreadySelected) =>
        !locked && !overlay && !alreadySelected && ClickRecent(nowUtc, clickAtUtc);

    public const int PopupSlackPx = 8;

    /// <summary>Diem (pixel vat ly) nam trong rect noi them slack px moi canh.</summary>
    public static bool PointNearRect(int x, int y, RECT r, int slack = PopupSlackPx) =>
        x >= r.Left - slack && x < r.Right + slack && y >= r.Top - slack && y < r.Bottom + slack;

    /// <summary>Popup dang hien, hoac duoc thay lan cuoi trong cua so thoi gian.</summary>
    public static bool PopupRecent(DateTime nowUtc, bool visibleNow, DateTime? lastSeenUtc) =>
        visibleNow || (lastSeenUtc is { } t && (nowUtc - t).TotalMilliseconds >= 0 && (nowUtc - t).TotalMilliseconds <= PopupClickWindowMs);

    /// <summary>Nhieu kha nang vua bam popup: popup con "moi" va con tro nam gan rect cuoi cung cua no.</summary>
    public static bool PopupClickLikely(DateTime nowUtc, bool visibleNow, DateTime? lastSeenUtc, RECT lastRect, int cx, int cy) =>
        PopupRecent(nowUtc, visibleNow, lastSeenUtc) && PointNearRect(cx, cy, lastRect);

    public static bool LogAllowed(DateTime now, DateTime lastLog) => (now - lastLog).TotalMilliseconds >= LogIntervalMs;
}
