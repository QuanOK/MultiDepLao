namespace MultiDepLao.Core;

public enum PcUnreadKind { Unknown, None, Has }

/// <summary>Trang thai chua doc cua tai khoan PC (tu huy hieu Zalo). Count: 1..5, 6 = "5+", null = khong biet so.</summary>
public readonly record struct PcUnread(PcUnreadKind Kind, int? Count);

/// <summary>Phan thuan cua "Thong bao kin" cho tai khoan PC: khi nao bao toast (khong noi dung tin).</summary>
public static class PcUnreadNotifyLogic
{
    public const int MoreThanFive = 6;
    public static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(30);

    /// <summary>Unknown: giu gia tri cu.</summary>
    public static PcUnread Merge(PcUnread prev, PcUnread now) => now.Kind == PcUnreadKind.Unknown ? prev : now;

    public static bool IsWarmingUp(DateTime firstSeenUtc, DateTime nowUtc) => nowUtc - firstSeenUtc < WarmUp;

    /// <summary>true = so chua doc tang (None -> Has, hoac ca hai so da biet va tang) va nguoi dung khong dang xem, khong o giai doan vua khoi dong.</summary>
    public static bool ShouldNotify(PcUnread prev, PcUnread now, bool lookingAt, bool warmingUp)
    {
        if (now.Kind != PcUnreadKind.Has || lookingAt || warmingUp) return false;
        if (prev.Kind != PcUnreadKind.Has) return true;
        return prev.Count is int p && now.Count is int n && n > p;
    }

    /// <summary>Tin hieu bo sung: popup Zalo moi hien khi so da o "5+" (so khong the tang them).</summary>
    public static bool ShouldNotifyOnPopup(PcUnread last, bool lookingAt, bool warmingUp) =>
        last.Kind == PcUnreadKind.Has && last.Count >= MoreThanFive && !lookingAt && !warmingUp;

    /// <summary>Khi "Thong bao kin" bat va popup Zalo bi giau: moi popup moi = 1 tin hieu "co tin moi" (nhanh hon quet so chua doc).</summary>
    public static bool ShouldNotifyOnHiddenPopup(bool lookingAt, bool warmingUp) => !lookingAt && !warmingUp;

    /// <summary>Quet so chua doc ngay sau toast do popup: chi cap nhat toast im lang (cung Tag), khong bat banner lan 2.</summary>
    public static readonly TimeSpan QuietAfterPopup = TimeSpan.FromSeconds(15);
    public static bool ShouldQuietScanToast(DateTime? lastPopupToastUtc, DateTime nowUtc) =>
        lastPopupToastUtc is { } t && nowUtc >= t && nowUtc - t < QuietAfterPopup;

    /// <summary>"1".."5", "5+"; null neu khong biet so.</summary>
    public static string? CountText(int? count) =>
        count is not int c || c <= 0 ? null : c >= MoreThanFive ? "5+" : c.ToString();
}
