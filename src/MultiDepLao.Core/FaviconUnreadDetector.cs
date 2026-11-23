namespace MultiDepLao.Core;

/// <summary>
/// Nhan biet "co tin chua doc" cua Zalo Web qua favicon (do 2026-11-19, ban thu w3):
/// - khong co tin chua doc: favicon la file "https://.../favicon-96x96.v1.png";
/// - co tin chua doc: favicon la anh nhung "data:image/png;base64,..." (Zalo ve so do len logo), moi lan so doi la anh khac;
/// - doc het: tro lai file .png thuong.
/// Chi xem DANG cua dia chi favicon (data: hay khong) va no co doi hay khong; khong giai ma anh, khong doc noi dung tin.
/// </summary>
public sealed class FaviconUnreadDetector
{
    private readonly object _l = new();
    private string? _last;
    private bool _hasUnread;

    public bool HasUnread { get { lock (_l) return _hasUnread; } }

    /// <summary>Favicon vua doi sang mot anh "co tin chua doc" moi (so tin doi). Cung luong goi OnFavicon.</summary>
    public event Action? NewMessage;
    /// <summary>Trang thai co/khong tin chua doc vua doi (false = da doc het, ke ca doc tren thiet bi khac).</summary>
    public event Action<bool>? UnreadChanged;
    /// <summary>Moi lan favicon doi (sau UnreadChanged/NewMessage): de cap nhat so tin chua doc.</summary>
    public event Action? Updated;

    public static bool IsBadge(string? uri) => uri != null && uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

    /// <summary>"5+" — cung gia tri voi UnreadBadgeProbe.MoreThanFive (the tai khoan hien "5+").</summary>
    public const int MoreThanFive = 6;

    /// <summary>
    /// Anh favicon co so do Zalo Web ve (do 2026-11-19, ban thu w6, Windows 11 100% DPI): ma bam -> so tin chua doc.
    /// Zalo ve 1..5 va "5+". Anh ve tren may khac (font/DPI khac) co the cho ma khac -> khong biet so, chi hien cham.
    /// </summary>
    private static readonly Dictionary<string, int> KnownBadges = new(StringComparer.Ordinal)
    {
        ["eb03ac"] = 1, ["350909"] = 2, ["16d2d9"] = 3, ["d7c078"] = 4, ["d5f9c5"] = 5, ["61e5e6"] = MoreThanFive,
        // ve luc trang vua mo, chua tai xong font (chu co chan): da gap "1" va "5+"
        ["8d5fdc"] = 1, ["9b6008"] = MoreThanFive,
    };

    /// <summary>So tin chua doc tu anh favicon da biet; null = khong co tin hoac anh la (chi hien cham).</summary>
    public static int? CountOf(string? uri) => IsBadge(uri) && KnownBadges.TryGetValue(ShortHash(uri), out var n) ? n : null;

    /// <summary>FNV-1a 32 bit, 24 bit thap, 6 hex. Dung chung cho log chan doan (khong dich nguoc ra noi dung).</summary>
    public static string ShortHash(string? v)
    {
        uint h = 2166136261;
        foreach (char c in v ?? "") { h ^= c; h *= 16777619; }
        return (h & 0xFFFFFF).ToString("x6");
    }

    /// <summary>So tin chua doc theo favicon gan nhat (null = khong biet so / khong co tin).</summary>
    public int? Count { get { lock (_l) return CountOf(_last); } }

    public void OnFavicon(string? uri)
    {
        bool badge = IsBadge(uri), changed, isNew;
        lock (_l)
        {
            isNew = badge && !string.Equals(uri, _last, StringComparison.Ordinal)
                    // so giam (doc bot o thiet bi khac) thi khong phai tin moi
                    && !(CountOf(uri) is int now && CountOf(_last) is int before && now < before);
            changed = badge != _hasUnread;
            _hasUnread = badge;
            _last = uri;
        }
        if (changed) UnreadChanged?.Invoke(badge);
        if (isNew) NewMessage?.Invoke();
        Updated?.Invoke();
    }
}
