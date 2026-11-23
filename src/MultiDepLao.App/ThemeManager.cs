using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using MultiDepLao.Core;

namespace MultiDepLao.App;

/// <summary>
/// Doi giao dien sang/toi luc chay: thay bang mau (Themes/Light.xaml | Dark.xaml) trong Application.Resources
/// va dat thanh tieu de toi/sang (DWM) cho cac cua so da dang ky. "system" chi DOC registry AppsUseLightTheme.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static string _mode = "system";
    private static bool _hooked;
    private static readonly List<WeakReference<Window>> Windows = new();

    public static string Mode => _mode;
    public static bool IsDark { get; private set; }
    /// <summary>Phat sau khi doi theme (UI thread).</summary>
    public static event Action? Changed;

    /// <summary>Mau nen vung Host (resource HostBg) cua theme hien tai; khong co -> theo sang/toi.</summary>
    public static System.Drawing.Color HostBackground()
    {
        if (Application.Current?.TryFindResource("HostBg") is SolidColorBrush b)
            return System.Drawing.Color.FromArgb(b.Color.A, b.Color.R, b.Color.G, b.Color.B);
        return IsDark ? System.Drawing.Color.FromArgb(0x0B, 0x10, 0x0E) : System.Drawing.Color.FromArgb(0xE3, 0xE8, 0xE6);
    }

    private static string _accent = "teal";
    private static ResourceDictionary? _accentDict;

    // Acc, AccFg, Sec, SecFg: sang / toi
    private static readonly Dictionary<string, string[][]> Palette = new()
    {
        ["teal"] = new[] { new[] { "#0B6E62", "#FFFFFF", "#CDE8E1", "#05201B" }, new[] { "#82D5C6", "#00382F", "#2F4A44", "#CDE8E1" } },
        ["blue"] = new[] { new[] { "#005FB8", "#FFFFFF", "#D3E3F8", "#001C3A" }, new[] { "#60CDFF", "#00263D", "#1E3A5A", "#D3E3F8" } },
        ["violet"] = new[] { new[] { "#6B4FBB", "#FFFFFF", "#E4DCF7", "#21124D" }, new[] { "#C7B8F5", "#2A1B5C", "#3A2F5C", "#E4DCF7" } },
        ["amber"] = new[] { new[] { "#A34F12", "#FFFFFF", "#F7E0CE", "#3A1A03" }, new[] { "#F2B07A", "#3D1E05", "#4A2E1A", "#F7E0CE" } },
    };
    private static readonly string[] AccentKeys = { "Acc", "AccFg", "Sec", "SecFg" };

    /// <summary>Mau Acc cua bang mau nhan cho che do sang/toi (dung ve o chon).</summary>
    public static Color AccentColor(string accent, bool dark) =>
        (Color)ColorConverter.ConvertFromString(Palette[AppConfig.NormalizeAccent(accent)][dark ? 1 : 0][0]);

    /// <summary>Ghi de Acc/AccFg/Sec/SecFg bang dictionary rieng merge SAU Light/Dark (nen Refresh doi Light/Dark khong mat).</summary>
    public static void ApplyAccent(string? accent)
    {
        _accent = AppConfig.NormalizeAccent(accent);
        var app = Application.Current;
        if (app == null) return;
        if (_accentDict == null || !app.Resources.MergedDictionaries.Contains(_accentDict))
        {
            _accentDict = new ResourceDictionary();
            app.Resources.MergedDictionaries.Add(_accentDict);
        }
        var cols = Palette[_accent][IsDark ? 1 : 0];
        for (int i = 0; i < AccentKeys.Length; i++)
            _accentDict[AccentKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cols[i]));
    }

    /// <summary>Doc registry (chi doc). Thieu khoa hoac loi -> sang.</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false);
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static void Apply(string? mode)
    {
        _mode = AppConfig.NormalizeTheme(mode);
        if (!_hooked) { SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged; _hooked = true; }
        Refresh();
    }

    /// <summary>Huy dang ky su kien he thong (goi khi thoat).</summary>
    public static void Shutdown()
    {
        if (_hooked) { SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; _hooked = false; }
    }

    private static void OnUserPreferenceChanged(object? s, UserPreferenceChangedEventArgs e)
    {
        if (_mode != "system") return;
        Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }

    private static void Refresh()
    {
        bool dark = _mode == "dark" || (_mode == "system" && SystemPrefersDark());
        var app = Application.Current;
        if (app == null) { IsDark = dark; return; }
        var name = dark ? "Dark" : "Light";
        foreach (var d in app.Resources.MergedDictionaries)
        {
            var src = d.Source?.OriginalString ?? "";
            if (src.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) || src.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase))
            {
                if (!src.EndsWith($"Themes/{name}.xaml", StringComparison.OrdinalIgnoreCase) || IsDark != dark)
                    d.Source = new Uri($"pack://application:,,,/{typeof(ThemeManager).Assembly.GetName().Name};component/Themes/{name}.xaml", UriKind.Absolute);
                break;
            }
        }
        IsDark = dark;
        ApplyAccent(_accent);
        Changed?.Invoke();
        lock (Windows)
        {
            Windows.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var w in Windows.ToList()) if (w.TryGetTarget(out var win)) ApplyTitleBar(win);
        }
    }

    /// <summary>Dang ky cua so de thanh tieu de theo theme (luc SourceInitialized va moi lan doi theme).</summary>
    public static void Attach(Window w)
    {
        lock (Windows) Windows.Add(new WeakReference<Window>(w));
        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) ApplyTitleBar(w);
        w.SourceInitialized += (_, _) => ApplyTitleBar(w);
    }

    private static void ApplyTitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            int v = IsDark ? 1 : 0;
            DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));   // loi (Win10 cu) -> bo qua
        }
        catch { /* khong ho tro */ }
    }
}
