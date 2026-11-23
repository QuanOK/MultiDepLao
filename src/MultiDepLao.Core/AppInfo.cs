using System.Reflection;

namespace MultiDepLao.Core;

public static class AppInfo
{
    /// <summary>Ten hien thi cua app (chi de nhin thay; khong dung cho duong dan/mutex/registry).</summary>
    public const string DisplayName = "Multi DépLào";
    /// <summary>AppUserModelID dung cho toast (HKCU\Software\Classes\AppUserModelId\MultiDepLao). Khong doi AUMID cua tien trinh.</summary>
    public const string ToastAumid = "MultiDepLao";
    public const string Author ="QuânOK";
    public const string AuthorEmail = "okiemquan@gmail.com";
    public const string Copyright = "Copyright © 2026 QuânOK";

    /// <summary>Kho ma nguon cong khai, trang ban chinh thuc va giay phep.</summary>
    public const string SourceUrl = "https://github.com/QuanOK/MultiDepLao";
    public const string ReleasesUrl = SourceUrl + "/releases";
    public const string License = "GPL-3.0";

    public const string DonateBank ="Techcombank";
    public const string DonateHolder = "O Kiem Quan";
    public const string DonateAccount = "7820708888";

    /// <summary>"7820708888" -> "7820 7088 88" (nhom 4 so).</summary>
    public static string FormatAccount(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>Mien tru trach nhiem theo ngon ngu hien hanh (bang chuoi: key About.Disclaimer).</summary>
    public static string Disclaimer => Loc.T("About.Disclaimer");

    /// <summary>Phien ban, vd "0.3.1" (lay tu assembly dang chay).</summary>
    public static string Version { get; } = ReadVersion(Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly);

    /// <summary>"0.3.1+abc123" -> "0.3.1" (bo phan metadata sau dau +); rong/null -> null.</summary>
    public static string? CleanVersion(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        int i = s.IndexOf('+');
        var r = (i >= 0 ? s[..i] : s).Trim();
        return r.Length == 0 ? null : r;
    }

    public static string ReadVersion(Assembly asm)
    {
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var v = CleanVersion(info);
        if (v != null) return v;
        var av = asm.GetName().Version;
        return av == null ? "0.0.0" : $"{av.Major}.{av.Minor}.{Math.Max(av.Build, 0)}";
    }
}
