using System.Text;
using System.Xml.Linq;

namespace MultiDepLao.Core;

/// <summary>Phan thuan cua thong bao toast (khong dung WinRT/registry): dung XML, quyet dinh suppress, chuan hoa tag.</summary>
public static class ToastLogic
{
    public const string Group = "web";
    public const string GroupPc = "pc";
    /// <summary>Cung tai khoan trong khoang nay: cap nhat toast nhung khong bat banner lai.</summary>
    public static readonly TimeSpan SuppressWindow = TimeSpan.FromSeconds(5);

    /// <summary>true = cap nhat im lang (SuppressPopup). last = lan hien gan nhat cua tai khoan do (null = chua tung).</summary>
    public static bool ShouldSuppressPopup(DateTime? lastShownUtc, DateTime nowUtc) =>
        lastShownUtc is { } l && nowUtc >= l && nowUtc - l < SuppressWindow;

    /// <summary>Tag toast toi da 64 ky tu.</summary>
    public static string NormalizeTag(string id) => id.Length > 64 ? id[..64] : id;

    /// <summary>XML ToastGeneric: 2 dong chu (tieu de + noi dung do app tu dung). Escape day du, bo ky tu dieu khien.</summary>
    public static string BuildXml(string title, string line)
    {
        var doc = new XDocument(
            new XElement("toast",
                new XElement("visual",
                    new XElement("binding", new XAttribute("template", "ToastGeneric"),
                        new XElement("text", Clean(title)),
                        new XElement("text", Clean(line))))));
        return doc.Root!.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Bo ky tu khong hop le trong XML 1.0 (dieu khien, surrogate le).</summary>
    public static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); continue; }
            if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Chi ghi registry khi gia tri khac hien co.</summary>
    public static bool NeedsRegistryUpdate(string? currentDisplayName, string? currentIconUri, string displayName, string iconUri) =>
        !string.Equals(currentDisplayName, displayName, StringComparison.Ordinal) ||
        !string.Equals(currentIconUri, iconUri, StringComparison.Ordinal);
}
