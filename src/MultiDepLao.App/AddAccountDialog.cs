using System.Windows;
using System.Windows.Controls;
using MultiDepLao.Core;

namespace MultiDepLao.App;

/// <summary>Them tai khoan: nhap ten + chon loai bang hai the (Zalo PC mac dinh / Zalo Web).</summary>
public sealed class AddAccountDialog : Window
{
    private readonly TextBox _box = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly RadioButton _pc, _web;
    private readonly Button _ok = DlgUi.Btn(Loc.T("Dlg.Agree"), primary: true, isDefault: true);

    public string Value => _box.Text.Trim();
    public AccountKind Kind => _web.IsChecked == true ? AccountKind.Web : AccountKind.Pc;

    public AddAccountDialog(Window owner)
    {
        var title = Loc.T("Sidebar.Add");
        DlgUi.Setup(this, owner, title);
        SizeToContent = SizeToContent.WidthAndHeight;
        var cancel = DlgUi.Btn(Loc.T("Dlg.Cancel"), isCancel: true);
        _ok.Click += (_, _) => DialogResult = true;
        void Check() => _ok.IsEnabled = _box.Text.Trim().Length > 0;
        _box.TextChanged += (_, _) => Check();
        Check();

        _pc = Card("Kind.Pc", "Kind.PcDesc", true);
        _web = Card("Kind.Web", "Kind.WebDesc", false);
        var kindLabel = DlgUi.Text(Loc.T("Kind.Choose"));
        kindLabel.Margin = new Thickness(0, 16, 0, 6);
        var body = new StackPanel { Width = 420 };
        body.Children.Add(DlgUi.Text(Loc.T("Act.AddPrompt")));
        body.Children.Add(_box);
        body.Children.Add(kindLabel);
        body.Children.Add(_pc);
        body.Children.Add(_web);
        Content = DlgUi.Frame(title, body, new[] { _ok, cancel });
        Loaded += (_, _) => _box.Focus();
    }

    private static RadioButton Card(string titleKey, string descKey, bool isChecked)
    {
        var sp = new StackPanel();
        var t = DlgUi.Text(Loc.T(titleKey), 0, "Fg", 14);
        t.FontWeight = FontWeights.SemiBold;
        var d = DlgUi.Text(Loc.T(descKey), 0, "Muted", 12);
        d.Margin = new Thickness(0, 3, 0, 0);
        sp.Children.Add(t); sp.Children.Add(d);
        return new RadioButton
        {
            GroupName = "kind", IsChecked = isChecked, Content = sp, Style = DlgUi.StyleOf("KindCard"),
            Margin = new Thickness(0, 0, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
    }
}

/// <summary>Cua so tien do cai WebView2 (khong treo UI), co nut Huy. X cua cua so = Huy.</summary>
public sealed class WebInstallWindow : Window
{
    private readonly TextBlock _detail = DlgUi.Text("", 0, "Muted", 12.5);
    private readonly CancellationTokenSource _cts = new();
    private bool _allowClose;

    public CancellationToken Token => _cts.Token;

    public WebInstallWindow(Window owner)
    {
        var title = Loc.T("Web.InstallTitle");
        DlgUi.Setup(this, owner, title);
        SizeToContent = SizeToContent.Height; Width = 420;
        var text = DlgUi.Text(Loc.T("Web.Installing"));
        text.FontWeight = FontWeights.SemiBold;
        _detail.Margin = new Thickness(0, 3, 0, 0);
        var body = new StackPanel();
        body.Children.Add(text);
        body.Children.Add(_detail);
        body.Children.Add(new ProgressBar { Style = DlgUi.StyleOf("DlgProgress"), Margin = new Thickness(0, 12, 0, 0), IsIndeterminate = true });
        var cancel = DlgUi.Btn(Loc.T("Dlg.Cancel"), isCancel: true);
        cancel.Click += (_, _) => { cancel.IsEnabled = false; _cts.Cancel(); };
        Content = DlgUi.Frame(title, body, new[] { cancel });
        Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; _cts.Cancel(); } };
        SetStage("download");
    }

    /// <summary>Giai doan tu <see cref="WebView2Runtime.InstallAsync(AppPaths, IProgress{string}?, CancellationToken)"/>: download / verify / install.</summary>
    public void SetStage(string stage) => _detail.Text = Loc.T(stage switch
    {
        "verify" => "Web.InstallDetailVerify",
        "install" => "Web.InstallDetailInstall",
        _ => "Web.InstallDetailDownload",
    });

    public void CloseNow() { _allowClose = true; Close(); }
}
