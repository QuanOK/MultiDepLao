using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using static MultiDepLao.Win32.Native;

namespace MultiDepLao.App;

/// <summary>
/// Nhan + khung quanh popup thong bao Zalo: cho biet popup thuoc tai khoan nao. Khong nhan focus, chuot xuyen qua
/// (WS_EX_TRANSPARENT), nen trong suot tung diem anh (AllowsTransparency) nen khong che noi dung popup.
/// Giao dien C1: nhan gon (rong vua chu) o goc trai tren khung, khung vien 2 px mau chu dao om sat ben ngoai popup.
/// App chi ve cua so RIENG cua minh quanh popup; khong ve len / khong doc cua so Zalo.
/// </summary>
public sealed class NotifyTagWindow : Window
{
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int FramePx = 2;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);

    private readonly TextBlock _text;
    private readonly StackPanel _row;
    private readonly Border _tag, _frame;
    private readonly Canvas _canvas = new();
    private IntPtr _hwnd;

    public NotifyTagWindow(string displayName)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);

        var font = TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI");
        var icon = TryFindResource("IconFont") as FontFamily ?? new FontFamily("Segoe MDL2 Assets");
        // EA8F = Ringer (chuong). KHONG dung E7ED (RingerSilent, chuong gach cheo).
        var bell = new TextBlock { Text = "", FontFamily = icon, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 7, 0) };
        _text = new TextBlock
        {
            FontFamily = font, FontSize = 13, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Text = displayName,
        };
        foreach (var t in new[] { bell, _text }) { t.Foreground = Brushes.White; t.SetResourceReference(TextBlock.ForegroundProperty, "AccFg"); }
        _row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        _row.Children.Add(bell);
        _row.Children.Add(_text);

        var acc = new SolidColorBrush(Color.FromRgb(0x0B, 0x6E, 0x62));   // du phong neu thieu tai nguyen theme
        _tag = new Border { Background = acc, Child = _row };
        _tag.SetResourceReference(Border.BackgroundProperty, "Acc");
        _frame = new Border { BorderBrush = acc, Background = Brushes.Transparent, CornerRadius = new CornerRadius(8) };
        _frame.SetResourceReference(Border.BorderBrushProperty, "Acc");
        _canvas.Children.Add(_frame);
        _canvas.Children.Add(_tag);
        Content = _canvas;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            long ex = ExStyle(_hwnd) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };
    }

    public void SetName(string displayName) => _text.Text = displayName;

    /// <summary>
    /// popup: rect popup Zalo (px). tagY/tagH: vi tri/chieu cao nhan (px) do NotifyController tinh (tren popup, hoac duoi
    /// neu sat mep tren man hinh). Cua so phu ca nhan + khung; khung nam ngoai popup FramePx.
    /// </summary>
    public void ShowAround(RECT popup, int tagY, int tagH)
    {
        if (_hwnd == IntPtr.Zero)
        {
            Left = -32000; Top = -32000; Width = 10; Height = 10;
            Show();
        }
        bool below = tagY >= popup.Bottom;
        int left = popup.Left - FramePx, right = popup.Right + FramePx;
        int top = Math.Min(tagY, popup.Top - FramePx), bottom = Math.Max(tagY + tagH, popup.Bottom + FramePx);
        uint dpi = GetDpiForWindow(_hwnd);
        double s = (dpi == 0 ? 96 : dpi) / 96.0;

        double fw = (right - left) / s, fh = (popup.Bottom - popup.Top + 2 * FramePx) / s;
        _frame.Width = fw; _frame.Height = fh;
        _frame.BorderThickness = new Thickness(FramePx / s);
        Canvas.SetLeft(_frame, 0);
        Canvas.SetTop(_frame, (popup.Top - FramePx - top) / s);

        _row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tw = Math.Min(_row.DesiredSize.Width + _row.Margin.Left + _row.Margin.Right + 2, fw);
        _tag.Width = tw; _tag.Height = tagH / s;
        _tag.CornerRadius = below ? new CornerRadius(0, 0, 6, 6) : new CornerRadius(6, 6, 0, 0);
        Canvas.SetLeft(_tag, 8 / s);
        Canvas.SetTop(_tag, (tagY - top) / s);

        SetWindowPos(_hwnd, HWND_TOPMOST, left, top, right - left, bottom - top, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void HideTag()
    {
        if (_hwnd != IntPtr.Zero) ShowWindow(_hwnd, SW_HIDE);
    }
}
