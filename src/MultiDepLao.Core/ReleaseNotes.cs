namespace MultiDepLao.Core;

public enum ReleaseKind { New, Changed, Fixed }

public sealed record ReleaseItem(ReleaseKind Kind, string Vi, string En)
{
    /// <summary>Noi dung theo ngon ngu hien hanh (khong co ban en -> ban vi).</summary>
    public string Text => Loc.Lang == Loc.En && En.Length > 0 ? En : Vi;
    public string KindLabel => ReleaseNotes.KindLabel(Kind);
}

/// <summary>Date: "yyyy-MM-dd".</summary>
public sealed record ReleaseNote(string Version, string Date, IReadOnlyList<ReleaseItem> Items)
{
    /// <summary>Ngay hien thi theo ngon ngu: vi dd/MM/yyyy, en yyyy-MM-dd.</summary>
    public string DisplayDate =>
        Loc.Lang == Loc.Vi && DateTime.TryParseExact(Date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("dd'/'MM'/'yyyy") : Date;
}

/// <summary>Nhat ky thay doi theo phien ban ("Co gi moi"). Moi nhat truoc.</summary>
public static class ReleaseNotes
{
    private static ReleaseItem N(string vi, string en) => new(ReleaseKind.New, vi, en);
    private static ReleaseItem C(string vi, string en) => new(ReleaseKind.Changed, vi, en);
    private static ReleaseItem F(string vi, string en) => new(ReleaseKind.Fixed, vi, en);

    // Ghi van tat: 1 dong / thay doi, chi nhung gi nguoi dung thay duoc. Viec nho gop thanh "Sua loi nho, cai thien giao dien".
    public static IReadOnlyList<ReleaseNote> All { get; } = new[]
    {
        new ReleaseNote("0.4.8", "2026-11-23", new[]
        {
            N("Mã nguồn mở (GPL-3.0). Chỉ tải bản chính thức ở github.com/QuanOK/MultiDepLao (xem Giới thiệu).",
              "Now open source (GPL-3.0). Download only from github.com/QuanOK/MultiDepLao (see About)."),
            C("Tăng cường bảo mật: chỉ chạy Zalo PC có chữ ký số của VNG; Zalo Web chỉ ở trang của Zalo, link ngoài mở bằng trình duyệt.",
              "Security hardening: only runs Zalo PC signed by VNG; Zalo Web stays on Zalo pages, outside links open in your browser."),
            C("Mật khẩu khóa app mới cần ít nhất 6 ký tự (mật khẩu cũ vẫn dùng được).",
              "New app-lock passwords need at least 6 characters (existing passwords still work)."),
            F("Cấu hình hỏng không còn làm app không mở được; ảnh đại diện rất lớn được thu nhỏ.",
              "A damaged config no longer stops the app from opening; very large avatars are scaled down."),
        }),
        new ReleaseNote("0.4.7", "2026-11-23", new[]
        {
            N("Chấm đỏ trên biểu tượng thanh tác vụ khi có tin chưa đọc.", "Red dot on the taskbar icon when there are unread messages."),
        }),
        new ReleaseNote("0.4.6", "2026-11-21", new[]
        {
            N("Bấm thông báo kín mở đúng cuộc trò chuyện vừa nhắn (khi Zalo có hiện popup).",
              "Clicking a private notification opens the chat that just messaged (when Zalo shows its popup)."),
            F("Bấm thông báo lúc app đang khóa: mở khóa xong chuyển đúng tài khoản.", "Clicking a notification while locked selects that account after unlocking."),
            C("Cập nhật nền tảng .NET 10 (bản vá bảo mật); gói tải về nhỏ hơn ~20 MB, tốn ít RAM hơn.",
              "Updated to .NET 10 (security updates); ~20 MB smaller download, less RAM."),
        }),
        new ReleaseNote("0.4.5", "2026-11-20", new[]
        {
            F("Mở lại app không còn bị trống vùng Zalo.", "Restoring the app no longer leaves the Zalo area blank."),
        }),
        new ReleaseNote("0.4.4", "2026-11-20", new[]
        {
            F("Bấm biểu tượng trên thanh tác vụ một lần là mở lại app.", "One taskbar click restores the app."),
        }),
        new ReleaseNote("0.4.3", "2026-11-20", new[]
        {
            C("Lưu cài đặt ổn định hơn.", "More reliable settings saving."),
        }),
        new ReleaseNote("0.4.2", "2026-11-20", new[]
        {
            F("Đóng app không còn làm hiện cửa sổ Zalo.", "Closing the app no longer flashes Zalo windows."),
        }),
        new ReleaseNote("0.4.1", "2026-11-20", new[]
        {
            N("Thông báo kín cho tài khoản Zalo PC (không hiện nội dung tin).", "Private notifications for Zalo PC accounts (no message text)."),
            C("Giảm nháy cửa sổ lúc khởi động.", "Less window flicker at startup."),
            C("Thu nhỏ vẫn giữ biểu tượng trên thanh tác vụ.", "Minimize keeps the taskbar icon."),
        }),
        new ReleaseNote("0.4.0", "2026-11-19", new[]
        {
            N("Thêm tài khoản Zalo Web.", "Zalo Web accounts."),
        }),
        new ReleaseNote("0.3.4", "2026-11-19", new[]
        {
            N("Mục \"Có gì mới\" (bạn đang xem).", "\"What's new\" window (you're reading it)."),
        }),
        new ReleaseNote("0.3.3", "2026-11-19", new[]
        {
            F("Mất biểu tượng trên taskbar khi tự mở cùng Windows.", "Missing taskbar icon when starting with Windows."),
        }),
        new ReleaseNote("0.3.2", "2026-11-19", new[]
        {
            N("Nút \"Ủng hộ tác giả\".", "\"Support the author\" button."),
            C("Xem RAM từng tài khoản ở đáy danh sách.", "Per-account RAM shown at the bottom of the list."),
        }),
        new ReleaseNote("0.3.1", "2026-11-19", new[]
        {
            N("Giao diện Tiếng Việt / English.", "Vietnamese / English interface."),
            N("Khởi động cùng Windows.", "Start with Windows."),
            C("Bản portable gọn hơn.", "Tidier portable build."),
        }),
        new ReleaseNote("0.3.0", "2026-11-18", new[]
        {
            N("Bản thử nghiệm đầu tiên.", "First test release."),
        }),
    };

    public static string KindLabel(ReleaseKind k) => Loc.T(k switch
    {
        ReleaseKind.New => "Rel.New",
        ReleaseKind.Changed => "Rel.Changed",
        _ => "Rel.Fixed",
    });

    private static bool TryParse(string? s, out Version v)
    {
        v = new Version(0, 0);
        return !string.IsNullOrWhiteSpace(s) && Version.TryParse(AppInfo.CleanVersion(s), out v!);
    }

    /// <summary>null -> chi hien khi da co tai khoan (nguoi dung cu len tu ban chua theo doi). Con lai: current &gt; lastSeen; lastSeen hong -> true.</summary>
    public static bool ShouldShow(string? lastSeen, string current, bool hasAccounts)
    {
        if (lastSeen == null) return hasAccounts;
        if (!TryParse(lastSeen, out var seen)) return true;
        if (!TryParse(current, out var cur)) return false;
        return cur > seen;
    }

    /// <summary>Cac ban moi hon lastSeen (tat ca neu null/khong doc duoc).</summary>
    public static IReadOnlyList<ReleaseNote> Since(string? lastSeen)
    {
        if (!TryParse(lastSeen, out var seen)) return All;
        return All.Where(n => TryParse(n.Version, out var v) && v > seen).ToList();
    }
}
