using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MultiDepLao.Core;
using MultiDepLao.Win32;

namespace MultiDepLao.App;

/// <summary>
/// Toast Windows chuan cho tai khoan Web. Dang ky AUMID "MultiDepLao" o HKCU (chi DisplayName + IconUri, khong COM),
/// khong dung SetCurrentProcessExplicitAppUserModelID. Loi o bat ky buoc nao -> Send tra false (goi noi quay ve Balloon).
/// Chay tren UI thread (Activated duoc chuyen ve qua <c>onActivated</c> do goi noi tu Dispatcher).
/// </summary>
public sealed class ToastNotifier
{
    private const string RegSubKey = @"Software\Classes\AppUserModelId\" + AppInfo.ToastAumid;

    private readonly string _iconPath;
    private readonly Action<string> _log;
    private readonly Action<string> _onActivated;
    private readonly Dictionary<string, DateTime> _lastShown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WinRtToast> _live = new(StringComparer.OrdinalIgnoreCase);
    private WinRtToastNotifier? _notifier;
    private bool _failed;

    /// <param name="onActivated">Goi (co the tu thread bat ky) voi id tai khoan khi nguoi dung bam toast.</param>
    public ToastNotifier(string dataRoot, Action<string> log, Action<string> onActivated)
    {
        _iconPath = Path.Combine(dataRoot, "toast-icon.png");
        _log = log; _onActivated = onActivated;
    }

    /// <summary>true = da gui. false = khong dung duoc toast (da log 1 lan) -> goi noi dung Balloon.</summary>
    /// <summary>Lan Send gan nhat co SuppressPopup (cap nhat im lang) khong.</summary>
    public bool LastSuppressed { get; private set; }

    public bool Send(string accountId, string title, string line, string group = ToastLogic.Group, bool forceQuiet = false)
    {
        if (_failed) return false;
        try
        {
            EnsureReady();
            var tag = ToastLogic.NormalizeTag(accountId);
            var now = DateTime.UtcNow;
            bool quiet = forceQuiet || ToastLogic.ShouldSuppressPopup(_lastShown.TryGetValue(tag, out var l) ? l : null, now);
            LastSuppressed = quiet;
            var toast = WinRtToastNotifier.CreateToast(ToastLogic.BuildXml(title, line), tag, group, quiet,
                () => _onActivated(accountId));
            _live[tag] = toast;   // giu tham chieu de toast/handler khong bi GC
            _notifier!.Show(toast);
            if (!quiet) _lastShown[tag] = now;
            return true;
        }
        catch (Exception ex)
        {
            _failed = true;
            _log($"[web] toast khong dung duoc, dung balloon: {ex.GetType().Name} 0x{ex.HResult:X8}");
            return false;
        }
    }

    /// <summary>Xoa toast cua tai khoan khoi trung tam thong bao (im lang neu loi).</summary>
    public void Remove(string accountId)
    {
        if (_notifier == null) return;
        try
        {
            var tag = ToastLogic.NormalizeTag(accountId);
            _live.Remove(tag); _lastShown.Remove(tag);
            WinRtToastNotifier.RemoveFromHistory(tag, ToastLogic.Group, AppInfo.ToastAumid);
            WinRtToastNotifier.RemoveFromHistory(tag, ToastLogic.GroupPc, AppInfo.ToastAumid);
        }
        catch { /* bo qua */ }
    }

    private void EnsureReady()
    {
        if (_notifier != null) return;
        WriteIcon();
        using (var k = Registry.CurrentUser.CreateSubKey(RegSubKey))
        {
            var curName = k.GetValue("DisplayName") as string;
            var curIcon = k.GetValue("IconUri") as string;
            if (ToastLogic.NeedsRegistryUpdate(curName, curIcon, AppInfo.DisplayName, _iconPath))
            {
                k.SetValue("DisplayName", AppInfo.DisplayName, RegistryValueKind.String);
                k.SetValue("IconUri", _iconPath, RegistryValueKind.String);
            }
        }
        _notifier = WinRtToastNotifier.Create(AppInfo.ToastAumid);
    }

    /// <summary>Ghi toast-icon.png tu khung lon nhat cua Assets/app.ico (neu chua co).</summary>
    private void WriteIcon()
    {
        if (File.Exists(_iconPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_iconPath)!);
        var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using var s = res.Stream;
        var dec = new IconBitmapDecoder(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var best = dec.Frames.OrderByDescending(f => f.PixelWidth).First();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(best));
        var tmp = _iconPath + ".tmp";
        using (var fs = File.Create(tmp)) enc.Save(fs);
        File.Move(tmp, _iconPath, true);
    }
}
