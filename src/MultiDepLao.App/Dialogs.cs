using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MultiDepLao.Core;

namespace MultiDepLao.App;

/// <summary>Loai bieu tuong tren hop thoai thong bao/xac nhan.</summary>
public enum DialogKind { None, Info, Success, Warning, Question }

/// <summary>
/// Khung hop thoai chung (kieu ContentDialog Windows 11): tieu de lon, than, hang nut o day.
/// Giu thanh tieu de Windows goc cua tung Window. Mau lay qua DynamicResource (Themes/*.xaml).
/// </summary>
internal static class DlgUi
{
    public static Style StyleOf(string key) => (Style)Application.Current.FindResource(key);

    public static void Dyn(FrameworkElement e, DependencyProperty p, string key) => e.SetResourceReference(p, key);

    /// <summary>Cai dat chung cho Window hop thoai. Owner chi dat khi owner dang hien va khong thu nho (WPF nem loi neu owner chua hien).</summary>
    public static void Setup(Window w, Window? owner, string title)
    {
        ThemeManager.Attach(w);
        w.Title = title;
        w.ResizeMode = ResizeMode.NoResize;
        w.FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        w.FontSize = 14;
        w.UseLayoutRounding = true;
        Dyn(w, Control.BackgroundProperty, "Surf");
        Dyn(w, Control.ForegroundProperty, "Fg");
        if (owner is { IsVisible: true, WindowState: not WindowState.Minimized })
        {
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.ShowInTaskbar = false;
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.ShowInTaskbar = true;
        }
    }

    public static Button Btn(string text, bool primary = false, bool isDefault = false, bool isCancel = false) => new()
    {
        Content = text, IsDefault = isDefault, IsCancel = isCancel,
        Style = StyleOf(primary ? "DlgPrimaryButton" : "DlgButton"),
    };

    public static TextBlock Text(string text, double maxWidth = 0, string fgKey = "Fg", double size = 14)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size };
        if (maxWidth > 0) t.MaxWidth = maxWidth;
        Dyn(t, TextElement.ForegroundProperty, fgKey);
        return t;
    }

    /// <summary>Dung khung: tieu de lon + than (co the co icon) + hang nut. buttons null/rong = khong co hang nut.</summary>
    public static Grid Frame(string heading, UIElement body, IReadOnlyList<Button>? buttons, DialogKind kind = DialogKind.None,
        string bodyBgKey = "Surf", bool scroll = false)
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var cap = new TextBlock { Text = heading, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 22, 24, 0) };
        Grid.SetRow(cap, 0);
        g.Children.Add(cap);

        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var (glyph, key) = kind switch
        {
            DialogKind.Warning => ("", "Warn"),
            DialogKind.Success => ("", "Ok"),
            DialogKind.Info or DialogKind.Question => ("", "Acc"),
            _ => ("", ""),
        };
        if (glyph.Length > 0)
        {
            var ic = new TextBlock { Text = glyph, Style = StyleOf("Glyph"), FontSize = 28, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 16, 0) };
            Dyn(ic, TextElement.ForegroundProperty, key);
            inner.Children.Add(ic);
        }
        UIElement content = body;
        if (scroll)
        {
            var sv = new ScrollViewer { Style = StyleOf("DlgScroll"), Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            content = sv;
        }
        Grid.SetColumn(content, 1);
        inner.Children.Add(content);

        var bodyBorder = new Border { Padding = new Thickness(24, 14, 24, 22), Child = inner };
        Dyn(bodyBorder, Border.BackgroundProperty, bodyBgKey);
        Grid.SetRow(bodyBorder, 1);
        g.Children.Add(bodyBorder);

        if (buttons is { Count: > 0 })
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            for (int i = 0; i < buttons.Count; i++)
            {
                if (i > 0) buttons[i].Margin = new Thickness(8, 0, 0, 0);
                sp.Children.Add(buttons[i]);
            }
            var bar = new Border { Padding = new Thickness(24, 20, 24, 20), BorderThickness = new Thickness(0, 1, 0, 0), Child = sp };
            Dyn(bar, Border.BackgroundProperty, "Bg");
            Dyn(bar, Border.BorderBrushProperty, "Line");
            Grid.SetRow(bar, 2);
            g.Children.Add(bar);
        }
        return g;
    }
}

/// <summary>
/// Hop thoai xac nhan (Co/Khong) hoac thong bao (OK) thay cho MessageBox. Esc = Khong / Dong.
/// Dong bang nut X cua cua so = Khong.
/// </summary>
public sealed class MessageDialog : Window
{
    public bool Result { get; private set; }

    private MessageDialog(Window? owner, string title, string message, DialogKind kind, bool yesNo)
    {
        DlgUi.Setup(this, owner, title);
        SizeToContent = SizeToContent.WidthAndHeight; MinWidth = 380;
        var text = DlgUi.Text(message, 440);
        Button primary, secondary = null!;
        if (yesNo)
        {
            primary = DlgUi.Btn(Loc.T("Dlg.Yes"), primary: true, isDefault: true);
            secondary = DlgUi.Btn(Loc.T("Dlg.No"), isCancel: true);
            primary.Click += (_, _) => { Result = true; DialogResult = true; };
        }
        else
        {
            primary = DlgUi.Btn(Loc.T("Dlg.OK"), primary: true, isDefault: true, isCancel: true);
            primary.Click += (_, _) => { Result = true; DialogResult = true; };
        }
        Content = DlgUi.Frame(title, text, yesNo ? new[] { primary, secondary } : new[] { primary }, kind);
        Loaded += (_, _) => primary.Focus();
    }

    /// <summary>Xac nhan Co/Khong: true = Co.</summary>
    public static bool Ask(Window owner, string title, string message, DialogKind kind = DialogKind.Warning)
    {
        var d = new MessageDialog(owner, title, message, kind, true);
        d.ShowDialog();
        return d.Result;
    }

    /// <summary>Thong bao co mot nut OK.</summary>
    public static void Notify(Window owner, string title, string message, DialogKind kind = DialogKind.Info)
        => new MessageDialog(owner, title, message, kind, false).ShowDialog();
}

/// <summary>Hop thoai nhap chu nho (them/doi ten/xac nhan xoa). validator: dieu kien bat nut OK.</summary>
public sealed class PromptDialog : Window
{
    private readonly TextBox _box = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button _ok = DlgUi.Btn(Loc.T("Dlg.Agree"), primary: true, isDefault: true);

    public string Value => _box.Text.Trim();

    public PromptDialog(Window owner, string title, string message, string initial = "", Func<string, bool>? validator = null)
    {
        DlgUi.Setup(this, owner, title);
        SizeToContent = SizeToContent.WidthAndHeight;
        _box.Text = initial;
        var cancel = DlgUi.Btn(Loc.T("Dlg.Cancel"), isCancel: true);
        _ok.Click += (_, _) => DialogResult = true;
        void Check() => _ok.IsEnabled = validator?.Invoke(_box.Text.Trim()) ?? _box.Text.Trim().Length > 0;
        _box.TextChanged += (_, _) => Check();
        Check();
        var body = new StackPanel { Width = 380 };
        body.Children.Add(DlgUi.Text(message));
        body.Children.Add(_box);
        Content = DlgUi.Frame(title, body, new[] { _ok, cancel });
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }
}

/// <summary>Chon mau tai khoan tu bang 10 mau (hoac "Mac dinh"). Value: "#RRGGBB" hoac "" = mac dinh.</summary>
public sealed class ColorDialog : Window
{
    public string Value { get; private set; } = "";

    public ColorDialog(Window owner, string name, string? current)
    {
        DlgUi.Setup(this, owner, Loc.T("Menu.Color"));
        SizeToContent = SizeToContent.WidthAndHeight;
        var grid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var hex in AccountPalette.Colors)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new Button
            {
                Width = 36, Height = 36, Margin = new Thickness(3), Background = new SolidColorBrush(c),
                BorderThickness = new Thickness(string.Equals(hex, current, StringComparison.OrdinalIgnoreCase) ? 3 : 1),
                ToolTip = hex,
            };
            DlgUi.Dyn(b, Control.BorderBrushProperty, "Fg");
            b.Click += (_, _) => { Value = hex; DialogResult = true; };
            grid.Children.Add(b);
        }
        var def = DlgUi.Btn(Loc.T("Dlg.Default"));
        def.Click += (_, _) => { Value = ""; DialogResult = true; };
        var cancel = DlgUi.Btn(Loc.T("Dlg.Cancel"), isCancel: true);
        var body = new StackPanel { Width = 300 };
        body.Children.Add(DlgUi.Text(Loc.F("Act.ColorFor", name)));
        body.Children.Add(grid);
        Content = DlgUi.Frame(Loc.T("Menu.Color"), body, new[] { def, cancel });
    }
}

/// <summary>Hop thoai nhap mat khau (1-3 o). submit tra ve thong bao loi, hoac null = thanh cong (dong hop thoai).</summary>
public sealed class PasswordDialog : Window
{
    private readonly PasswordBox[] _boxes;
    private readonly TextBlock _err = DlgUi.Text("", 0, "Err");

    public PasswordDialog(Window owner, string title, string[] labels, Func<string[], string?> submit)
    {
        DlgUi.Setup(this, owner, title);
        SizeToContent = SizeToContent.WidthAndHeight;
        var body = new StackPanel { Width = 340 };
        _boxes = labels.Select(l =>
        {
            var t = DlgUi.Text(l); t.Margin = new Thickness(0, 6, 0, 4);
            body.Children.Add(t);
            var b = new PasswordBox();
            body.Children.Add(b);
            return b;
        }).ToArray();
        _err.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(_err);
        var ok = DlgUi.Btn(Loc.T("Dlg.Agree"), primary: true, isDefault: true);
        var cancel = DlgUi.Btn(Loc.T("Dlg.Cancel"), isCancel: true);
        ok.Click += (_, _) =>
        {
            var vals = _boxes.Select(b => b.Password).ToArray();
            var e = submit(vals);
            if (e == null) { DialogResult = true; return; }
            _err.Text = e;
            foreach (var b in _boxes) b.Clear();
            _boxes[0].Focus();
        };
        Content = DlgUi.Frame(title, body, new[] { ok, cancel });
        Loaded += (_, _) => _boxes[0].Focus();
    }
}

/// <summary>
/// Hop thoai Cai dat. Moi muc la mot the (Surf, bo 16) trong <see cref="Sections"/> (M5 them khoa mat khau bang cach them khoi vao day).
/// </summary>
public sealed class SettingsDialog : Window
{
    private readonly RadioButton _auto, _manual, _thSystem, _thLight, _thDark, _langVi, _langEn;
    public string Lang { get; private set; } = Loc.Vi;
    private readonly TextBox _max = new() { Width = 56, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox _sleepOn = new() { Content = Loc.T("Set.AutoSleep"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _minutes = new() { Width = 56, Margin = new Thickness(8, 0, 8, 0) };
    private readonly Button _ok = DlgUi.Btn(Loc.T("Dlg.Save"), primary: true, isDefault: true);

    /// <summary>Vung chua cac khoi cai dat; them khoi moi tai day.</summary>
    public StackPanel Sections { get; } = new() { Margin = new Thickness(0, 0, 10, 0) };
    public string Theme { get; private set; } = "system";
    public string Accent { get; private set; } = "teal";
    private readonly List<RadioButton> _accents = new();
    public int? MaxRunning { get; private set; }
    public int? AutoSleepMinutes { get; private set; }
    public bool LockEnabled { get; private set; }
    public bool LockOnStart { get; private set; }
    public bool LockOnMinimize { get; private set; }
    public int LockIdleMinutes { get; private set; }
    public bool AutoStart { get; private set; }
    private readonly CheckBox _autoStart = new() { Content = Loc.T("Set.AutoStartWin") };
    public bool PcNotify { get; private set; }
    private readonly CheckBox _pcNotify = new() { Content = Loc.T("Set.PcNotify") };

    private readonly AppLock? _lock;
    private readonly CheckBox _lkOn = new() { Content = Loc.T("Lk.Enable"), Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _lkStart = new() { Content = Loc.T("Lk.OnStart"), Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _lkMin = new() { Content = Loc.T("Lk.OnMinimize") };
    private readonly TextBox _lkIdle = new() { Width = 56, Margin = new Thickness(8, 0, 8, 0) };
    private readonly TextBlock _lkStatus = DlgUi.Text("");
    private readonly Button _lkSet = new() { Style = DlgUi.StyleOf("DlgSmallButton"), Margin = new Thickness(0, 8, 8, 0) };
    private readonly Button _lkRemove = new() { Content = Loc.T("Lk.Remove"), Style = DlgUi.StyleOf("DlgSmallButton"), Margin = new Thickness(0, 8, 0, 0) };

    // ----- the cai dat -----
    private static TextBlock Desc(string text) => DlgUi.Text(text, 0, "Muted", 12);

    private static Border Card(string? glyph, string? title, params UIElement[] rows)
    {
        var sp = new StackPanel();
        if (title != null)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 6) };
            if (glyph != null)
            {
                var g = new TextBlock { Text = glyph, Style = DlgUi.StyleOf("Glyph"), FontSize = 14, Margin = new Thickness(0, 0, 8, 0) };
                DlgUi.Dyn(g, TextElement.ForegroundProperty, "Acc");
                head.Children.Add(g);
            }
            var t = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            DlgUi.Dyn(t, TextElement.ForegroundProperty, "Acc");
            head.Children.Add(t);
            sp.Children.Add(head);
        }
        for (int i = 0; i < rows.Length; i++)
        {
            bool line = title != null || i > 0;
            var r = new Border { Padding = new Thickness(0, 10, 0, 10), BorderThickness = new Thickness(0, line ? 1 : 0, 0, 0), Child = rows[i] };
            DlgUi.Dyn(r, Border.BorderBrushProperty, "Line");
            sp.Children.Add(r);
        }
        var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 0, 12), Child = sp };
        DlgUi.Dyn(card, Border.BackgroundProperty, "Surf");
        return card;
    }

    public SettingsDialog(Window owner, int? maxRunning, int autoMax, int? autoSleep, int runningNow,
        AppLock? appLock = null, LockConfig? lockCfg = null, bool focusLock = false, string? captureLine = null, string theme = "system", string accent = "teal",
        bool autoStart = false, Action<Window>? openAbout = null, string language = "vi", bool zaloRunEntry = false, bool pcNotify = false)
    {
        PcNotify = pcNotify;
        _pcNotify.IsChecked = pcNotify;
        AutoStart = autoStart;
        _autoStart.IsChecked = autoStart;
        DlgUi.Setup(this, owner, Loc.T("Cmd.Settings"));
        SizeToContent = SizeToContent.Height; Width = 560; MaxHeight = 700;

        theme = AppConfig.NormalizeTheme(theme);
        Theme = theme;
        _thSystem = new RadioButton { GroupName = "theme", Content = Loc.T("Set.ThemeSystem"), IsChecked = theme == "system", Margin = new Thickness(0, 0, 18, 0) };
        _thLight = new RadioButton { GroupName = "theme", Content = Loc.T("Set.ThemeLight"), IsChecked = theme == "light", Margin = new Thickness(0, 0, 18, 0) };
        _thDark = new RadioButton { GroupName = "theme", Content = Loc.T("Set.ThemeDark"), IsChecked = theme == "dark" };
        var radios = new StackPanel { Orientation = Orientation.Horizontal };
        radios.Children.Add(_thSystem); radios.Children.Add(_thLight); radios.Children.Add(_thDark);
        var themeRow = new StackPanel();
        themeRow.Children.Add(radios);
        accent = AppConfig.NormalizeAccent(accent);
        Accent = accent;
        var accRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var accLabel = DlgUi.Text(Loc.T("Set.Accent"));
        accLabel.VerticalAlignment = VerticalAlignment.Center; accLabel.Margin = new Thickness(0, 0, 14, 0);
        accRow.Children.Add(accLabel);
        foreach (var (key, tip) in new[] { ("teal", Loc.T("Set.AccentTeal")), ("blue", Loc.T("Set.AccentBlue")), ("violet", Loc.T("Set.AccentViolet")), ("amber", Loc.T("Set.AccentAmber")) })
        {
            var rb = new RadioButton
            {
                GroupName = "accent", Tag = key, ToolTip = tip, IsChecked = key == accent,
                Style = DlgUi.StyleOf("AccentSwatch"), Background = new SolidColorBrush(ThemeManager.AccentColor(key, ThemeManager.IsDark)),
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, tip);
            _accents.Add(rb); accRow.Children.Add(rb);
        }
        themeRow.Children.Add(accRow);
        language = Loc.Normalize(language);
        Lang = language;
        var langRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var langLabel = DlgUi.Text(Loc.T("Set.Language"));
        langLabel.VerticalAlignment = VerticalAlignment.Center; langLabel.Margin = new Thickness(0, 0, 14, 0);
        langRow.Children.Add(langLabel);
        _langVi = new RadioButton { GroupName = "language", Content = "Tiếng Việt", IsChecked = language == Loc.Vi, Margin = new Thickness(0, 0, 18, 0) };
        _langEn = new RadioButton { GroupName = "language", Content = "English", IsChecked = language == Loc.En };
        langRow.Children.Add(_langVi); langRow.Children.Add(_langEn);
        themeRow.Children.Add(langRow);
        Sections.Children.Add(Card("", Loc.T("Set.Appearance"), themeRow));

        _auto = new RadioButton { GroupName = "maxRunning", Content = Loc.F("Set.MaxAuto", autoMax), IsChecked = maxRunning == null, Margin = new Thickness(0, 0, 0, 8) };
        _manual = new RadioButton { GroupName = "maxRunning", Content = Loc.T("Set.MaxManual"), IsChecked = maxRunning != null, VerticalAlignment = VerticalAlignment.Center };
        _max.Text = (maxRunning ?? autoMax).ToString();
        var manualRow = new StackPanel { Orientation = Orientation.Horizontal };
        manualRow.Children.Add(_manual); manualRow.Children.Add(_max);
        var ramChoice = new StackPanel();
        ramChoice.Children.Add(_auto); ramChoice.Children.Add(manualRow);
        Sections.Children.Add(Card("", Loc.T("Set.MaxTitle"), ramChoice, Desc(
            Loc.T("Set.MaxDesc") + (runningNow > 0 ? Loc.F("Set.MaxRunningNow", runningNow) : ""))));

        _sleepOn.IsChecked = autoSleep is > 0;
        _minutes.Text = (autoSleep is > 0 ? autoSleep.Value : 30).ToString();
        var sleepRow = new StackPanel { Orientation = Orientation.Horizontal };
        sleepRow.Children.Add(_sleepOn); sleepRow.Children.Add(_minutes);
        sleepRow.Children.Add(DlgUi.Text(Loc.T("Set.Minutes")));
        sleepRow.Children[2].SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        Sections.Children.Add(Card(null, null, sleepRow, Desc(
            Loc.T("Set.AutoSleepDesc"))));

        var startRows = new List<UIElement> { _autoStart, Desc(Loc.T("Set.StartupDesc")) };
        if (zaloRunEntry) startRows.Add(DlgUi.Text(Loc.T("Set.ZaloRunWarn"), 0, "Warn", 12));
        Sections.Children.Add(Card("", Loc.T("Set.Startup"), startRows.ToArray()));

        var winNotif = new Button { Content = Loc.T("Set.PcNotifyOpen"), Style = DlgUi.StyleOf("DlgSmallButton"), HorizontalAlignment = HorizontalAlignment.Left };
        winNotif.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true }); }
            catch { /* bo qua */ }
        };
        Sections.Children.Add(Card(null, null, _pcNotify, Desc(Loc.T("Set.PcNotifyDesc")), winNotif));

        if (captureLine != null)
            Sections.Children.Add(Card(null, null, DlgUi.Text(captureLine)));

        _lock = appLock;
        if (appLock != null) Sections.Children.Add(BuildLockSection(appLock, lockCfg ?? new LockConfig(), focusLock));

        if (openAbout != null)
        {
            var aboutBtn = new Button { Content = Loc.T("Set.AboutButton"), Style = DlgUi.StyleOf("DlgSmallButton"), HorizontalAlignment = HorizontalAlignment.Left };
            aboutBtn.Click += (_, _) => openAbout(this);
            var donateBtn = new Button { Content = Loc.T("Donate.Title"), Style = DlgUi.StyleOf("DlgSmallButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 0, 0, 0) };
            donateBtn.Click += (_, _) => new DonateDialog(this).ShowDialog();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var relBtn = new Button { Content = Loc.T("Rel.Title"), Style = DlgUi.StyleOf("DlgSmallButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 0, 0, 0) };
            relBtn.Click += (_, _) => new WhatsNewDialog(this).ShowDialog();
            row.Children.Add(aboutBtn); row.Children.Add(relBtn); row.Children.Add(donateBtn);
            Sections.Children.Add(Card(null, null, row));
        }

        var cancel = DlgUi.Btn(Loc.T("Dlg.Cancel"), isCancel: true);

        void Check()
        {
            _max.IsEnabled = _manual.IsChecked == true;
            _minutes.IsEnabled = _sleepOn.IsChecked == true;
            bool okMax = _manual.IsChecked != true || (int.TryParse(_max.Text.Trim(), out var m) && m >= 1);
            bool okMin = _sleepOn.IsChecked != true || (int.TryParse(_minutes.Text.Trim(), out var n) && n >= 1);
            bool okLock = _lock == null || (int.TryParse(_lkIdle.Text.Trim(), out var li) && li >= 0);
            _ok.IsEnabled = okMax && okMin && okLock;
        }
        _lkIdle.TextChanged += (_, _) => Check();
        _auto.Checked += (_, _) => Check();
        _manual.Checked += (_, _) => { if (string.IsNullOrWhiteSpace(_max.Text)) _max.Text = autoMax.ToString(); Check(); };
        _sleepOn.Click += (_, _) => Check();
        _max.TextChanged += (_, _) => Check(); _minutes.TextChanged += (_, _) => Check();
        Check();
        _ok.Click += (_, _) =>
        {
            AutoStart = _autoStart.IsChecked == true;
            PcNotify = _pcNotify.IsChecked == true;
            Lang = _langEn.IsChecked == true ? Loc.En : Loc.Vi;
            Theme = _thDark.IsChecked == true ? "dark" : _thLight.IsChecked == true ? "light" : "system";
            Accent = _accents.FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "teal";
            MaxRunning = _manual.IsChecked == true ? int.Parse(_max.Text.Trim()) : null;
            AutoSleepMinutes = _sleepOn.IsChecked == true ? int.Parse(_minutes.Text.Trim()) : null;
            if (_lock != null)
            {
                LockEnabled = _lkOn.IsChecked == true; LockOnStart = _lkStart.IsChecked == true; LockOnMinimize = _lkMin.IsChecked == true;
                LockIdleMinutes = int.Parse(_lkIdle.Text.Trim());
            }
            DialogResult = true;
        };

        Content = DlgUi.Frame(Loc.T("Cmd.Settings"), Sections, new[] { _ok, cancel }, DialogKind.None, "Bg", scroll: true);
    }

    private Border BuildLockSection(AppLock lk, LockConfig cfg, bool focusLock)
    {
        _lkOn.IsChecked = cfg.Enabled; _lkStart.IsChecked = cfg.LockOnStart; _lkMin.IsChecked = cfg.LockOnMinimize;
        _lkIdle.Text = Math.Max(0, cfg.IdleMinutes).ToString();

        void Refresh()
        {
            bool has = lk.HasPassword;
            _lkStatus.Text = has ? Loc.T("Lk.StatusSet") : Loc.T("Lk.StatusNone");
            _lkSet.Content = has ? Loc.T("Lk.Change") : Loc.T("Lk.Set");
            _lkRemove.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            _lkOn.IsEnabled = _lkStart.IsEnabled = _lkMin.IsEnabled = _lkIdle.IsEnabled = has;
            if (!has) _lkOn.IsChecked = false;
        }

        string? Fail(string what, AppLock l) =>
            l.RetryAfter is { } w ? Loc.F("Lk.TooManyWrong", Math.Ceiling(w.TotalSeconds).ToString("0")) : what;

        _lkSet.Click += (_, _) =>
        {
            if (!lk.HasPassword)
            {
                var d = new PasswordDialog(this, Loc.T("Lk.Set"), new[] { Loc.T("Lk.NewPw"), Loc.T("Lk.RePw") }, v =>
                {
                    if (v[0].Length < AppLock.MinLength) return Loc.T("Lk.MinLen");
                    if (v[0] != v[1]) return Loc.T("Lk.Mismatch");
                    return lk.SetPassword(v[0]) ? null : Loc.T("Lk.SaveFail");
                });
                if (d.ShowDialog() == true) { Refresh(); _lkOn.IsChecked = true; }   // lan dau dat: tu bat khoa
            }
            else
            {
                var d = new PasswordDialog(this, Loc.T("Lk.Change"), new[] { Loc.T("Lk.CurPw"), Loc.T("Lk.NewPw"), Loc.T("Lk.ReNewPw") }, v =>
                {
                    if (v[1].Length < AppLock.MinLength) return Loc.T("Lk.NewMinLen");
                    if (v[1] != v[2]) return Loc.T("Lk.NewMismatch");
                    return lk.ChangePassword(v[0], v[1]) ? null : Fail(Loc.T("Lk.CurWrong"), lk);
                });
                d.ShowDialog();
            }
        };
        _lkRemove.Click += (_, _) =>
        {
            var d = new PasswordDialog(this, Loc.T("Lk.Remove"), new[] { Loc.T("Lk.CurPw") },
                v => lk.RemovePassword(v[0]) ? null : Fail(Loc.T("Lk.CurWrong"), lk));
            if (d.ShowDialog() == true) Refresh();
        };

        var pw = new StackPanel();
        pw.Children.Add(_lkStatus);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_lkSet); buttons.Children.Add(_lkRemove);
        pw.Children.Add(buttons);

        var checks = new StackPanel();
        checks.Children.Add(_lkOn); checks.Children.Add(_lkStart); checks.Children.Add(_lkMin);

        var idleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var t1 = DlgUi.Text(Loc.T("Lk.IdleBefore")); t1.VerticalAlignment = VerticalAlignment.Center;
        var t2 = DlgUi.Text(Loc.T("Lk.IdleAfter")); t2.VerticalAlignment = VerticalAlignment.Center;
        idleRow.Children.Add(t1); idleRow.Children.Add(_lkIdle); idleRow.Children.Add(t2);

        var card = Card("", Loc.T("Lk.Title"), pw, checks, idleRow, Desc(
            Loc.T("Lk.Desc")));
        Refresh();
        if (focusLock) Loaded += (_, _) => { _lkSet.BringIntoView(); _lkSet.Focus(); };
        return card;
    }
}

/// <summary>Hop thoai "Gioi thieu & ban quyen": phien ban, tac gia, quyen rieng tu, mien tru trach nhiem, thu muc du lieu.</summary>
public sealed class AboutDialog : Window
{
    public AboutDialog(Window? owner, string dataRoot, bool isPortable)
    {
        DlgUi.Setup(this, owner, Loc.T("About.Title"));
        SizeToContent = SizeToContent.Height; Width = 520; MaxHeight = 700;

        var body = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        try
        {
            var dec = new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/app.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var best = dec.Frames.OrderByDescending(f => f.PixelWidth).First();
            head.Children.Add(new Image { Source = best, Width = 56, Height = 56, Margin = new Thickness(0, 0, 14, 0) });
        }
        catch { /* thieu logo thi bo qua */ }
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = AppInfo.DisplayName, FontSize = 22, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(DlgUi.Text(Loc.F("About.Version", AppInfo.Version), 0, "Muted"));
        head.Children.Add(titles);
        body.Children.Add(head);

        body.Children.Add(DlgUi.Text(Loc.T("About.Tagline")));

        var author = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        DlgUi.Dyn(author, TextElement.ForegroundProperty, "Fg");
        author.Inlines.Add(new Run(Loc.F("About.Author", AppInfo.Author)));
        var link = new Hyperlink(new Run(AppInfo.AuthorEmail)) { NavigateUri = new Uri("mailto:" + AppInfo.AuthorEmail) };
        link.SetResourceReference(TextElement.ForegroundProperty, "Acc");
        link.RequestNavigate += (_, e) =>
        {
            e.Handled = true;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch { /* khong co trinh mail mac dinh */ }
        };
        author.Inlines.Add(link);
        body.Children.Add(author);
        var copy = DlgUi.Text(AppInfo.Copyright, 0, "Muted", 12); copy.Margin = new Thickness(0, 4, 0, 12);
        body.Children.Add(copy);

        body.Children.Add(Section(Loc.T("About.PrivacyTitle"),
            Loc.T("About.Privacy")));
        body.Children.Add(Section(Loc.T("About.DisclaimerTitle"), AppInfo.Disclaimer));

        var srcSec = Section(Loc.T("About.SourceTitle"), Loc.T("About.SourceNote"));
        srcSec.Children.Add(LinkLine("", AppInfo.ReleasesUrl, 6));
        srcSec.Children.Add(LinkLine(Loc.T("About.LicenseLine"), AppInfo.SourceUrl, 6));
        srcSec.Children.Add(DlgUi.Text(Loc.T("About.TrademarkNote"), 0, "Muted", 12));
        body.Children.Add(srcSec);

        var donateSec = Section(Loc.T("Donate.Section"), Loc.T("Donate.Title"));
        var donateBtn = new Button { Content = Loc.T("Donate.Title"), Style = DlgUi.StyleOf("DlgSmallButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        donateBtn.Click += (_, _) => new DonateDialog(this).ShowDialog();
        donateSec.Children.RemoveAt(1);
        var relBtn = new Button { Content = Loc.T("Rel.Title"), Style = DlgUi.StyleOf("DlgSmallButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 6, 0, 0) };
        relBtn.Click += (_, _) => new WhatsNewDialog(this).ShowDialog();
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        btnRow.Children.Add(donateBtn); btnRow.Children.Add(relBtn);
        donateSec.Children.Add(btnRow);
        body.Children.Add(donateSec);

        var info = DlgUi.Text(Loc.F("About.DataMode", dataRoot, Loc.T(isPortable ? "About.ModePortable" : "About.ModeInstalled")), 0, "Muted", 12);
        info.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(info);

        var close = DlgUi.Btn(Loc.T("Dlg.Close"), primary: true, isDefault: true, isCancel: true);
        close.Click += (_, _) => DialogResult = true;
        Content = DlgUi.Frame(Loc.T("About.Heading"), body, new[] { close }, DialogKind.None, "Surf", scroll: true);
    }

    /// <summary>Dong "prefix + lien ket" mo bang trinh duyet mac dinh.</summary>
    private static TextBlock LinkLine(string prefix, string url, double top)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, top, 0, 6) };
        DlgUi.Dyn(tb, TextElement.ForegroundProperty, "Fg");
        if (prefix.Length > 0) tb.Inlines.Add(new Run(prefix));
        var link = new Hyperlink(new Run(url)) { NavigateUri = new Uri(url) };
        link.SetResourceReference(TextElement.ForegroundProperty, "Acc");
        link.RequestNavigate += (_, e) =>
        {
            e.Handled = true;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch { /* khong mo duoc trinh duyet */ }
        };
        tb.Inlines.Add(link);
        return tb;
    }

    private static StackPanel Section(string title, string text)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var t = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
        DlgUi.Dyn(t, TextElement.ForegroundProperty, "Acc");
        sp.Children.Add(t);
        sp.Children.Add(DlgUi.Text(text, 0, "Fg", 13));
        return sp;
    }
}

/// <summary>Hop thoai "Co gi moi": nhat ky thay doi theo phien ban. sinceVersion != null: chi cac ban moi hon + nut "Xem tat ca".</summary>
public sealed class WhatsNewDialog : Window
{
    private readonly StackPanel _list = new();

    public WhatsNewDialog(Window? owner, string? sinceVersion = null, bool onlyNew = false)
    {
        DlgUi.Setup(this, owner, Loc.T("Rel.Title"));
        // Khong dat MaxHeight cho Window: cua so cat mat day ScrollViewer -> keo het thanh cuon van khong thay cuoi danh sach.
        // Chi gioi han ScrollViewer; cua so tu cao theo noi dung.
        SizeToContent = SizeToContent.Height; Width = 460;

        var body = new StackPanel();
        var scroller = new ScrollViewer { MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Padding = new Thickness(0, 0, 10, 0) };
        body.Children.Add(scroller);

        var notes = onlyNew ? ReleaseNotes.Since(sinceVersion) : ReleaseNotes.All;
        if (notes.Count == 0) { notes = ReleaseNotes.All; onlyNew = false; }
        Fill(notes);

        if (onlyNew && notes.Count < ReleaseNotes.All.Count)
        {
            var all = new Button { Content = Loc.T("Rel.ShowAll"), Style = DlgUi.StyleOf("DlgSmallButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            all.Click += (_, _) => { Fill(ReleaseNotes.All); body.Children.Remove(all); };
            body.Children.Add(all);
        }

        var close = DlgUi.Btn(Loc.T("Dlg.Close"), primary: true, isDefault: true, isCancel: true);
        close.Click += (_, _) => DialogResult = true;
        Content = DlgUi.Frame(Loc.T("Rel.Title"), body, new[] { close }, DialogKind.None, "Surf");
    }

    private void Fill(IReadOnlyList<ReleaseNote> notes)
    {
        _list.Children.Clear();
        foreach (var n in notes)
        {
            var head = new TextBlock { FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 6) };
            DlgUi.Dyn(head, TextElement.ForegroundProperty, "Acc");
            head.Text = Loc.F("Rel.Version", n.Version, n.DisplayDate) + (n.Version == AppInfo.Version ? Loc.T("Rel.Current") : "");
            _list.Children.Add(head);
            foreach (var it in n.Items)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var tagText = new TextBlock { Text = it.KindLabel, FontSize = 11.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
                string key = it.Kind switch { ReleaseKind.New => "Acc", ReleaseKind.Fixed => "Ok", _ => "Muted" };
                DlgUi.Dyn(tagText, TextElement.ForegroundProperty, key);
                var tag = new Border
                {
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 1, 6, 1),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 8, 0), Child = tagText
                };
                DlgUi.Dyn(tag, Border.BorderBrushProperty, key);
                var text = DlgUi.Text(it.Text, 0, "Fg", 13);
                Grid.SetColumn(text, 1);
                row.Children.Add(tag); row.Children.Add(text);
                _list.Children.Add(row);
            }
            _list.Children.Add(new Border { Height = 8 });
        }
    }
}

/// <summary>Hop thoai "Ung ho tac gia": ma QR chuyen khoan + so tai khoan (khong ket noi mang).</summary>
public sealed class DonateDialog : Window
{
    public DonateDialog(Window? owner)
    {
        DlgUi.Setup(this, owner, Loc.T("Donate.Title"));
        SizeToContent = SizeToContent.Height; Width = 440; MaxHeight = 760;

        var body = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        body.Children.Add(DlgUi.Text(Loc.T("Donate.Text")));

        // Nen trang co dinh de quet duoc ca khi theme toi. HighQuality (Fant) cho ket qua min khi thu nho 625px -> ~240 DIP.
        try
        {
            var img = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/donate-qr.png")),
                Width = 240, Stretch = Stretch.Uniform
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            body.Children.Add(new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 10), Child = img
            });
        }
        catch { /* thieu anh thi chi hien so tai khoan */ }

        var bank = DlgUi.Text($"{AppInfo.DonateBank} · {AppInfo.DonateHolder}", 0, "Muted");
        bank.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(bank);
        var num = new TextBox
        {
            Text = AppInfo.FormatAccount(AppInfo.DonateAccount), IsReadOnly = true, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, FontSize = 24, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 8)
        };
        DlgUi.Dyn(num, Control.ForegroundProperty, "Fg");
        body.Children.Add(num);

        var copy = new Button { Content = Loc.T("Donate.Copy"), Style = DlgUi.StyleOf("DlgSmallButton"), HorizontalAlignment = HorizontalAlignment.Center };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { timer.Stop(); copy.Content = Loc.T("Donate.Copy"); };
        Closed += (_, _) => timer.Stop();
        copy.Click += (_, _) =>
        {
            bool ok = false;
            for (int i = 0; i < 3 && !ok; i++)
            {
                try { Clipboard.SetText(AppInfo.DonateAccount); ok = true; }
                catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(80); }
            }
            copy.Content = Loc.T(ok ? "Donate.Copied" : "Donate.CopyFail");
            timer.Stop(); timer.Start();
        };
        body.Children.Add(copy);

        var close = DlgUi.Btn(Loc.T("Dlg.Close"), primary: true, isDefault: true, isCancel: true);
        close.Click += (_, _) => DialogResult = true;
        Content = DlgUi.Frame(Loc.T("Donate.Title"), body, new[] { close }, DialogKind.None, "Surf", scroll: true);
    }
}

/// <summary>Cua so tien do khong xac dinh (chuyen quyen goi). Khong dong duoc bang tay.</summary>
public sealed class BusyWindow : Window
{
    private readonly TextBlock _text = DlgUi.Text("");
    private readonly TextBlock _detail = DlgUi.Text("", 0, "Muted", 12.5);
    private bool _allowClose;

    public BusyWindow(Window owner, string title, string text)
    {
        DlgUi.Setup(this, owner, title);
        SizeToContent = SizeToContent.Height; Width = 420;
        _text.Text = text;
        _text.FontWeight = FontWeights.SemiBold;
        _detail.Visibility = Visibility.Collapsed;
        _detail.Margin = new Thickness(0, 3, 0, 0);

        var spinner = new Grid { Width = 32, Height = 32, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(0.5, 0.5) };
        var track = new System.Windows.Shapes.Ellipse { StrokeThickness = 3 };
        DlgUi.Dyn(track, System.Windows.Shapes.Shape.StrokeProperty, "Cont2");
        var arc = new System.Windows.Shapes.Path
        {
            StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(1.5),
            Data = Geometry.Parse("M 14.5,0 A 14.5,14.5 0 0 1 29,14.5"),
        };
        DlgUi.Dyn(arc, System.Windows.Shapes.Shape.StrokeProperty, "Acc");
        spinner.Children.Add(track); spinner.Children.Add(arc);
        if (SystemParameters.ClientAreaAnimation)
        {
            var rot = new RotateTransform();
            spinner.RenderTransform = rot;
            rot.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(1))
            { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        }

        var right = new StackPanel();
        right.Children.Add(_text);
        right.Children.Add(_detail);
        right.Children.Add(new ProgressBar { Style = DlgUi.StyleOf("DlgProgress"), Margin = new Thickness(0, 12, 0, 0), IsIndeterminate = true });
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        row.Children.Add(spinner); row.Children.Add(right);
        Content = DlgUi.Frame(title, row, null);
        Closing += (_, e) => { if (!_allowClose) e.Cancel = true; };
    }

    public void SetText(string text) => _text.Text = text;
    public void SetDetail(string text) { _detail.Text = text; _detail.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible; }
    public void CloseNow() { _allowClose = true; Close(); }
}

/// <summary>Cua so tien do nho khi tat cac tai khoan luc dong app.</summary>
public sealed class ProgressWindow : Window
{
    private readonly TextBlock _text = DlgUi.Text("");
    private readonly ProgressBar _bar = new() { Style = DlgUi.StyleOf("DlgProgress"), Margin = new Thickness(0, 14, 0, 0), Minimum = 0 };

    public ProgressWindow(Window owner, string title, int total)
    {
        DlgUi.Setup(this, owner, title);
        SizeToContent = SizeToContent.Height; Width = 420;
        _bar.Maximum = Math.Max(1, total);
        var body = new StackPanel();
        body.Children.Add(_text); body.Children.Add(_bar);
        Content = DlgUi.Frame(title, body, null);
        Report(0, total);
    }

    public void Report(int done, int total)
    {
        _text.Text = Loc.F("Act.StoppingProgress", done, total);
        _bar.Value = done;
    }
}
