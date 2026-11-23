namespace MultiDepLao.Core;

public enum WebLinkAction { Default, OpenExternal, Block }

/// <summary>Logic thuan cua tai khoan Zalo Web (khong phu thuoc WebView2/WPF) de test duoc.</summary>
public static class WebAccountLogic
{
    /// <summary>
    /// Quyet dinh cho yeu cau mo cua so moi (target=_blank, window.open): link http/https NGOAI cac mien noi bo
    /// -> mo bang trinh duyet mac dinh (neu do nguoi dung bam; con lai chan); link noi bo / about:blank -> de mac dinh
    /// (cua so popup cua WebView2, can cho cac luong can window.opener); scheme khac (file, javascript, zalo...) -> chan.
    /// </summary>
    public static WebLinkAction ClassifyNewWindow(string? uri, IReadOnlyList<string> internalDomains, bool userInitiated)
    {
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var u)) return WebLinkAction.Block;
        if (u.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase)) return WebLinkAction.Default;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return WebLinkAction.Block;
        if (IsInternalHost(u.Host, internalDomains)) return WebLinkAction.Default;
        return userInitiated ? WebLinkAction.OpenExternal : WebLinkAction.Block;
    }

    /// <summary>host trung mot mien noi bo hoac la mien con cua no ("a.zalo.me" thuoc "zalo.me"; "evilzalo.me" thi khong).</summary>
    public static bool IsInternalHost(string? host, IReadOnlyList<string> internalDomains)
    {
        if (string.IsNullOrEmpty(host)) return false;
        foreach (var d in internalDomains)
            if (host.Equals(d, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Origin (e.Uri cua PermissionRequested) la http(s) tren mien noi bo thi moi duoc tu dong cap quyen.</summary>
    public static bool IsInternalOrigin(string? uri, IReadOnlyList<string> internalDomains) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
        && IsInternalHost(u.Host, internalDomains);

    public enum NavAction { Allow, Cancel, CancelAndOpenExternal }

    /// <summary>
    /// Dieu huong cap cao nhat cua trang chinh: http(s) toi host ngoai mien noi bo -> huy (mo trinh duyet neu do nguoi dung bam);
    /// scheme khac ngoai about/data/blob (file, ftp, ...) -> huy. Khong parse duoc / about / data / blob -> cho qua (chi huy cai ro rang la ben ngoai).
    /// </summary>
    public static NavAction ClassifyNavigation(string? uri, IReadOnlyList<string> internalDomains, bool userInitiated)
    {
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var u)) return NavAction.Allow;
        var s = u.Scheme;
        if (s.Equals("about", StringComparison.OrdinalIgnoreCase) || s.Equals("data", StringComparison.OrdinalIgnoreCase)
            || s.Equals("blob", StringComparison.OrdinalIgnoreCase)) return NavAction.Allow;
        if (s != Uri.UriSchemeHttp && s != Uri.UriSchemeHttps) return NavAction.Cancel;
        if (IsInternalHost(u.Host, internalDomains)) return NavAction.Allow;
        return userInitiated ? NavAction.CancelAndOpenExternal : NavAction.Cancel;
    }

    /// <summary>Khoa chuoi (Strings) mo ta ly do cai WebView2 that bai; FailExit nhan {0} = ma thoat.</summary>
    public static string InstallFailureKey(InstallFailure f) => f switch
    {
        InstallFailure.NoNetwork => "Web.FailNoNetwork",
        InstallFailure.BadSignature => "Web.FailSignature",
        InstallFailure.ExitCode => "Web.FailExit",
        InstallFailure.Timeout => "Web.FailTimeout",
        InstallFailure.NotDetected => "Web.FailNotDetected",
        _ => "Web.FailOther",
    };

    /// <summary>Lay ma thoat tu Detail "ma thoat N" cua InstallResult (khong co -> "?").</summary>
    public static string ExitCodeOf(string? detail)
    {
        var last = detail?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return last != null && int.TryParse(last, out var n) ? n.ToString() : "?";
    }

    /// <summary>
    /// WebView2 la HWND con (airspace): chi hien khi tai khoan Web dang chon va co view, app khong khoa
    /// (man hinh khoa ve trong cua so, WebView2 se de len no) va cua so khong thu nho/an khay.
    /// </summary>
    public static bool ShouldShow(bool selected, bool hasView, bool locked, bool minimizedOrHidden) =>
        selected && hasView && !locked && !minimizedOrHidden;

    /// <summary>
    /// Chia RAM cua cac tien trinh WebView2 cho cac tai khoan Web dang chay: moi tai khoan nhan phan renderer da xac dinh
    /// (own) cong phan chia deu cua phan con lai (tien trinh chung browser/GPU/utility va renderer khong xac dinh duoc).
    /// Tong cac phan = total (chia du, phan le don vao tai khoan dau tien).
    /// </summary>
    public static IReadOnlyDictionary<string, long> SplitRam(IReadOnlyList<string> ids, IReadOnlyDictionary<string, long> own, long total)
    {
        var res = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return res;
        long matched = 0;
        foreach (var id in ids) if (own.TryGetValue(id, out var b) && b > 0) matched += b;
        long shared = Math.Max(0, total - matched);
        long each = shared / ids.Count, rest = shared - each * ids.Count;
        for (int i = 0; i < ids.Count; i++)
        {
            long mine = own.TryGetValue(ids[i], out var b) && b > 0 ? b : 0;
            res[ids[i]] = mine + each + (i == 0 ? rest : 0);
        }
        return res;
    }
}
