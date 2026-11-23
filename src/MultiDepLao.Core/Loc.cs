using System.Diagnostics;
using System.Globalization;

namespace MultiDepLao.Core;

/// <summary>
/// Ngon ngu giao dien (vi | en). Bang chuoi nam trong code (<see cref="Strings"/>), khong dung .resx/CultureInfo.
/// Khong co ban en -> tra ban vi; khong co ca hai -> tra chinh key.
/// </summary>
public static class Loc
{
    public const string Vi = "vi", En = "en";

    private static volatile string _lang = Vi;

    /// <summary>Ngon ngu hien hanh: "vi" | "en".</summary>
    public static string Lang => _lang;

    /// <summary>Phat sau khi <see cref="Set"/> doi ngon ngu (goi tren thread da goi Set, thuong la UI thread).</summary>
    public static event Action? Changed;

    /// <summary>Test dat false de thu duong "thieu key" ma khong dung Debug.Fail.</summary>
    public static bool AssertOnMissing { get; set; } = true;

    /// <summary>"en" -> "en"; moi gia tri khac (null, la) -> "vi".</summary>
    public static string Normalize(string? lang) => lang?.Trim().ToLowerInvariant() == En ? En : Vi;

    /// <summary>Doi ngon ngu. Chi phat <see cref="Changed"/> khi that su doi.</summary>
    public static void Set(string? lang)
    {
        var n = Normalize(lang);
        if (n == _lang) return;
        _lang = n;
        Changed?.Invoke();
    }

    /// <summary>Tat ca key (cho nap resource XAML).</summary>
    public static IEnumerable<string> Keys => Strings.Vi.Keys;

    public static string T(string key)
    {
        if (_lang == En && Strings.En.TryGetValue(key, out var e) && e.Length > 0) return e;
        if (Strings.Vi.TryGetValue(key, out var v)) return v;
#if DEBUG
        if (AssertOnMissing) Debug.Fail("Loc: thieu key " + key);
#endif
        return key;
    }

    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
}
