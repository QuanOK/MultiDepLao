using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using MultiDepLao.Core;

namespace MultiDepLao.App;

public sealed class AccountViewModel : INotifyPropertyChanged
{
    private static readonly Brush Gray = Frozen(Colors.Gray), Orange = Frozen(Colors.DarkOrange),
        Green = Frozen(Colors.ForestGreen), Red = Frozen(Colors.Firebrick);
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private string _displayName = "";
    private InstanceState _state = InstanceState.Stopped;
    private int? _pid;
    private string? _reason;
    private long _ramBytes;
    private bool _hasWindow;

    public AccountViewModel(string id, string displayName, string? color = null, AccountKind kind = AccountKind.Pc)
    { Id = id; _displayName = displayName; _color = color; Kind = kind; }

    public string Id { get; }
    public AccountKind Kind { get; }
    /// <summary>Tai khoan Zalo Web (WebView2), khong chay Zalo.exe.</summary>
    public bool IsWeb => Kind == AccountKind.Web;
    public string KindText => Loc.T(IsWeb ? "Kind.WebShort" : "Kind.PcShort");
    private bool _showKind;
    /// <summary>Hien nhan PC/Web tren the (chi khi danh sach co tai khoan Web, de khong roi voi nguoi chi dung PC).</summary>
    public bool ShowKind { get => _showKind; set { if (_showKind != value) { _showKind = value; Raise(nameof(ShowKind), nameof(KindVisibility)); } } }
    public System.Windows.Visibility KindVisibility => _showKind ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(params string[] names) { foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }

    public string DisplayName { get => _displayName; set { if (_displayName != value) { _displayName = value; Raise(nameof(DisplayName), nameof(Initial), nameof(ItemTip)); } } }

    private string? _color;
    /// <summary>Mau tuy chon (null = mac dinh theo id).</summary>
    public string? CustomColor { get => _color; set { _color = value; _avatar = null; Raise(nameof(CustomColor), nameof(AvatarBrush)); } }
    private Brush? _avatar;
    public Brush AvatarBrush => _avatar ??= Frozen((Color)ColorConverter.ConvertFromString(AccountPalette.Effective(Id, _color)));
    public string Initial => AccountPalette.Initial(_displayName);

    private System.Windows.Media.ImageSource? _avatarImage;
    private System.Windows.Media.ImageBrush? _avatarImageBrush;
    /// <summary>Anh dai dien tuy chon (dong bang, null = dung chu cai + mau).</summary>
    public System.Windows.Media.ImageSource? AvatarImage => _avatarImage;
    public bool HasAvatarImage => _avatarImage != null;
    public System.Windows.Media.ImageBrush? AvatarImageBrush => _avatarImageBrush;
    public System.Windows.Visibility AvatarImageVisibility => _avatarImage != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility InitialVisibility => _avatarImage != null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    /// <summary>Nap lai anh tu file da chuan hoa (doc vao bo nho, khong giu khoa file). File thieu/hong -> khong anh.</summary>
    public void LoadAvatar(string file)
    {
        System.Windows.Media.ImageSource? img = null;
        try
        {
            if (File.Exists(file))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                bmp.DecodePixelWidth = AvatarMath.Size;   // anh da chuan hoa 128; file la bi thay bang anh khong lo cung khong an het RAM
                bmp.UriSource = new Uri(file);
                bmp.EndInit();
                bmp.Freeze();
                img = bmp;
            }
        }
        catch { img = null; }
        _avatarImage = img;
        if (img == null) _avatarImageBrush = null;
        else
        {
            _avatarImageBrush = new System.Windows.Media.ImageBrush(img) { Stretch = System.Windows.Media.Stretch.UniformToFill };
            _avatarImageBrush.Freeze();
        }
        Raise(nameof(AvatarImage), nameof(HasAvatarImage), nameof(AvatarImageBrush), nameof(AvatarImageVisibility), nameof(InitialVisibility));
    }

    private bool _narrow;
    /// <summary>Sidebar thu gon: mau item chi con o tron + cham trang thai.</summary>
    public bool Narrow { get => _narrow; set { if (_narrow != value) { _narrow = value; Raise(nameof(Narrow), nameof(FullVisibility), nameof(NarrowVisibility), nameof(ItemTip)); } } }
    public System.Windows.Visibility FullVisibility => _narrow ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public System.Windows.Visibility NarrowVisibility => _narrow ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    /// <summary>Tooltip cho item (chi dung khi thu gon, de biet ten).</summary>
    public string? ItemTip => _narrow ? $"{_displayName} · {KindText} - {StateText}" : null;
    public InstanceState State { get => _state; private set { _state = value; Raise(nameof(State), nameof(StateText), nameof(StateBrush), nameof(StateNote), nameof(ItemTip), nameof(Dim), nameof(NoteText)); } }
    public int? Pid { get => _pid; private set { _pid = value; Raise(nameof(Pid)); } }
    public string? Reason { get => _reason; private set { _reason = value; Raise(nameof(Reason), nameof(NoteText)); } }
    public long RamBytes { get => _ramBytes; set { _ramBytes = value; Raise(nameof(RamBytes)); } }
    /// <summary>Cua so Zalo cua tai khoan nay dang duoc dan (GlueWindow.IsAttached).</summary>
    private bool _opening;
    /// <summary>Zalo dang khoi dong (cua so duoc giu ngoai man hinh cho den khi on dinh): vung Host hien lop cho.</summary>
    public bool Opening { get => _opening; set { if (_opening != value) { _opening = value; Raise(nameof(Opening)); } } }
    public bool HasWindow { get => _hasWindow; set { if (_hasWindow != value) { _hasWindow = value; Raise(nameof(HasWindow)); } } }

    private bool _isCallOwner;
    /// <summary>Tai khoan dang giu quyen goi dien thuc te (pipe).</summary>
    public bool IsCallOwner { get => _isCallOwner; set { if (_isCallOwner != value) { _isCallOwner = value; Raise(nameof(IsCallOwner), nameof(CallIcon), nameof(HasFlags)); } } }
    public string CallIcon => _isCallOwner ? "📞" : "";

    private bool _isCaptureOwner, _captureActive;
    /// <summary>Tai khoan giu quyen chup man hinh (cau hinh); active = dang chay kem ZaloCap.</summary>
    public void SetCapture(bool owner, bool active)
    {
        if (_isCaptureOwner == owner && _captureActive == active) return;
        _isCaptureOwner = owner; _captureActive = active;
        Raise(nameof(IsCaptureOwner), nameof(CaptureActive), nameof(CaptureIcon), nameof(CaptureTip), nameof(CaptureOpacity), nameof(HasFlags));
    }
    public bool IsCaptureOwner => _isCaptureOwner;
    public bool CaptureActive => _captureActive;
    public string CaptureIcon => _isCaptureOwner ? "📷" : "";
    public double CaptureOpacity => _captureActive || !_isCaptureOwner ? 1.0 : 0.4;
    public string CaptureTip => !_isCaptureOwner ? "" : _captureActive ? Loc.T("Tip.CaptureOn") : Loc.T("Tip.CaptureOff");

    /// <summary>Doi ngon ngu: lam moi moi chuoi hien thi sinh ra tu <see cref="Loc"/>.</summary>
    public void RefreshLanguage() => Raise(nameof(KindText), nameof(StateText), nameof(StateNote), nameof(NoteText), nameof(UnreadText), nameof(CaptureTip), nameof(ItemTip));

    private bool _hasNotify;
    /// <summary>Popup thong bao cua Zalo da hien cho tai khoan nay ke tu lan cuoi nguoi dung chon no.</summary>
    public bool HasNotify { get => _hasNotify; set { if (_hasNotify != value) { _hasNotify = value; Raise(nameof(HasNotify), nameof(NotifyIcon)); } } }
    public string NotifyIcon => _hasNotify ? "🔔" : "";

    private bool _hasUnread;
    /// <summary>Zalo cua tai khoan nay dang co huy hieu tin chua doc (UI Automation, theo hinh hoc).</summary>
    public bool HasUnread { get => _hasUnread; set { if (_hasUnread != value) { _hasUnread = value; Raise(nameof(HasUnread), nameof(UnreadDotVisibility), nameof(UnreadNumberVisibility), nameof(UnreadTextVisibility), nameof(NoteText)); } } }
        private int? _unreadCount;
    /// <summary>So tin chua doc (1..9) neu doc duoc tu huy hieu.</summary>
    public int? UnreadCount { get => _unreadCount; set { if (_unreadCount != value) { _unreadCount = value; Raise(nameof(UnreadCount), nameof(UnreadDotVisibility), nameof(UnreadNumberVisibility), nameof(UnreadText), nameof(UnreadCountText), nameof(NoteText)); } } }
    public string UnreadCountText => _unreadCount is int n ? (n >= MultiDepLao.Win32.UnreadBadgeProbe.MoreThanFive ? "5+" : n.ToString()) : "";
    public string UnreadText => _unreadCount != null ? Loc.F("State.UnreadCount", UnreadCountText) : Loc.T("State.UnreadSome");
    /// <summary>Cham tron 9x9 khi chua doc nhung khong co so.</summary>
    public System.Windows.Visibility UnreadDotVisibility => _hasUnread && _unreadCount == null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    /// <summary>Badge tron co so.</summary>
    public System.Windows.Visibility UnreadNumberVisibility => _hasUnread && _unreadCount != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility UnreadTextVisibility => _hasUnread ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    private bool _needsUpdate;
    /// <summary>Dang chay ban Zalo cu hon ban sao moi nhat (Zalo da tu cap nhat).</summary>
    public bool NeedsUpdate { get => _needsUpdate; set { if (_needsUpdate != value) { _needsUpdate = value; Raise(nameof(NeedsUpdate), nameof(UpdateIcon), nameof(HasFlags)); } } }
    public string UpdateIcon => _needsUpdate ? "⟳" : "";

    private bool _pinned;
    public bool Pinned { get => _pinned; set { if (_pinned != value) { _pinned = value; Raise(nameof(Pinned), nameof(PinIcon), nameof(HasFlags)); } } }
    public string PinIcon => _pinned ? "📌" : "";
    public string StateNote => State == InstanceState.Stopped ? Loc.T("State.NoMessages") : "";

    public bool IsActive => State is InstanceState.Starting or InstanceState.Running or InstanceState.Stopping;
    public bool CanStart => State is InstanceState.Stopped or InstanceState.Error;

    public string StateText => State switch
    {
        InstanceState.Stopped => Loc.T("State.Stopped"),
        InstanceState.Starting => Loc.T("State.Starting"),
        InstanceState.Running => Loc.T("State.Running"),
        InstanceState.Stopping => Loc.T("State.Stopping"),
        _ => Loc.T("State.Error"),
    };

    public Brush StateBrush => State switch
    {
        InstanceState.Running => Green,
        InstanceState.Starting or InstanceState.Stopping => Orange,
        InstanceState.Error => Red,
        _ => Gray,
    };

    /// <summary>Cho/ngu va loi thi avatar mo.</summary>
    public bool Dim => State is InstanceState.Stopped or InstanceState.Error;
    /// <summary>Dong ghi chu duoi chip: tin chua doc > ly do loi > ghi chu trang thai.</summary>
    public string NoteText => _hasUnread ? UnreadText : State == InstanceState.Error ? (_reason ?? "") : StateNote;
    public bool HasFlags => _needsUpdate || _pinned || _isCallOwner || _isCaptureOwner;

    private bool _compact;
    /// <summary>Sidebar hep (&lt; 240 DIP): an ghi chu va thanh RAM, o co nho.</summary>
    public bool Compact { get => _compact; set { if (_compact != value) { _compact = value; Raise(nameof(Compact), nameof(WideDetailVisibility)); } } }
    public System.Windows.Visibility WideDetailVisibility => _compact ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public void Apply(InstanceStatus st)
    {
        State = st.State; Pid = st.Pid; Reason = st.Reason;
        if (st.Pid == null && (!IsWeb || st.State is InstanceState.Stopped or InstanceState.Error)) RamBytes = 0;   // Web khong co pid: RAM do rieng
        Raise(nameof(IsActive), nameof(CanStart));
    }
}

