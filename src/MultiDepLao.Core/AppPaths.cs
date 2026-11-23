using System.Text.RegularExpressions;

namespace MultiDepLao.Core;

/// <summary>Moi duong dan du lieu cua app. Khong tao thu muc; goc co the thay bang thu muc tam (test).</summary>
public sealed class AppPaths
{
    public const string IdPattern = "^[A-Za-z0-9_-]{1,40}$";
    private static readonly Regex IdRegex = new(IdPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Root { get; }
    public string ProfilesRoot { get; }
    public string NoCapRoot { get; }
    /// <summary>Ban sao co ZaloCap.exe cua tai khoan giu quyen chup man hinh: zalo-cap\<id>\Zalo-<ver>.</summary>
    public string CapRoot { get; }
    public string ConfigFile { get; }
    public string LogsDir { get; }
    public string LockFile { get; }

    public AppPaths(string? root = null)
    {
        root ??= DataRoot.LocalRoot();
        Root = Path.GetFullPath(root);
        ProfilesRoot = Path.Combine(Root, "profiles");
        NoCapRoot = Path.Combine(Root, "zalo-nocap");
        CapRoot = Path.Combine(Root, "zalo-cap");
        ConfigFile = Path.Combine(Root, "config.json");
        LogsDir = Path.Combine(Root, "logs");
        LockFile = Path.Combine(Root, "app.lock");
    }

    public static bool IsValidId(string? id) => id != null && IdRegex.IsMatch(id);

    public string ProfileDir(string id)
    {
        if (!IsValidId(id)) throw new ArgumentException($"Id profile khong hop le: '{id}'", nameof(id));
        var dir = Path.GetFullPath(Path.Combine(ProfilesRoot, id));
        if (!IsUnder(dir, ProfilesRoot)) throw new ArgumentException($"Duong dan profile nam ngoai goc: '{id}'", nameof(id));
        return dir;
    }

    /// <summary>zalo-cap\<id> (kiem tra id + nam duoi CapRoot).</summary>
    public string CapDir(string id)
    {
        if (!IsValidId(id)) throw new ArgumentException($"Id profile khong hop le: '{id}'", nameof(id));
        var dir = Path.GetFullPath(Path.Combine(CapRoot, id));
        if (!IsUnder(dir, CapRoot)) throw new ArgumentException($"Duong dan cap nam ngoai goc: '{id}'", nameof(id));
        return dir;
    }

    /// <summary>Anh dai dien da chuan hoa (nam ngoai profiles\): avatars\&lt;id&gt;.png.</summary>
    public string AvatarsDir => Path.Combine(Root, "avatars");

    public string AvatarFile(string id)
    {
        if (!IsValidId(id)) throw new ArgumentException($"Id profile khong hop le: '{id}'", nameof(id));
        return Path.Combine(AvatarsDir, id + ".png");
    }

    /// <summary>userDataFolder chung cua WebView2 (moi tai khoan Web la mot ProfileName = id). Khong lien quan ZaloData.</summary>
    public string WebDataDir => Path.Combine(Root, "web");

    /// <summary>Thu muc tam cua app (tai bo cai WebView2...).</summary>
    public string TmpDir => Path.Combine(Root, "tmp");

    public string PidFile(string id) => Path.Combine(ProfileDir(id), "mz.pid");

    /// <summary>true neu path (sau chuan hoa) nam NGHIEM NGAT duoi root.</summary>
    public static bool IsUnder(string path, string root)
    {
        var p = Path.GetFullPath(path);
        var r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return p.Length > r.Length && p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }
}



