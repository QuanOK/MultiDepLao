namespace MultiDepLao.Core;

/// <summary>Noi luu lenh khoi dong cung Windows (that su la registry HKCU\...\Run; test dung gia).</summary>
public interface IRunKeyReader
{
    /// <summary>true neu HKCU\...\Run co gia tri ten Zalo. CHI DOC.</summary>
    bool HasZaloEntry();
}

public interface IAutoStartStore
{
    string? Read();
    void Write(string command);
    void Delete();
}

/// <summary>Logic bat/tat khoi dong cung Windows. Registry la nguon su that, khong luu vao config.</summary>
public sealed class AutoStart
{
    public const string ValueName = "MultiDepLao";
    private readonly IAutoStartStore _store;
    private readonly Func<string, bool> _fileExists;

    public AutoStart(IAutoStartStore store, Func<string, bool>? fileExists = null)
    {
        _store = store;
        _fileExists = fileExists ?? File.Exists;
    }

    /// <summary>Lay duong dan exe tu lenh da dang ky ("\"exe\" --tray" hoac exe tran). Null neu khong doc duoc.</summary>
    public static string? ExeOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }
        int sp = c.IndexOf(' ');
        return sp < 0 ? c : c[..sp];
    }

    public const string Arg = "--autostart";

    public static string BuildCommand(string exePath) => $"\"{exePath}\" {Arg}";

    private static bool SameExe(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_store.Read());

    public void Enable(string exePath) => _store.Write(BuildCommand(exePath));

    public void Disable()
    {
        if (_store.Read() != null) _store.Delete();
    }

    /// <summary>
    /// Dang bat ma exe da dang ky KHONG CON TON TAI (ban nay bi chuyen cho/xoa) thi tro sang exe hien tai. Tra ve true neu da sua.
    /// Exe da dang ky van con (vd. ban cai dat) ma dang mo ban khac (portable, ban dev) thi GIU NGUYEN: khong lang le cuop muc khoi dong.
    /// </summary>
    public bool RefreshPath(string exePath)
    {
        var cur = _store.Read();
        if (string.IsNullOrWhiteSpace(cur)) return false;
        var want = BuildCommand(exePath);
        if (string.Equals(cur, want, StringComparison.Ordinal)) return false;
        var registered = ExeOf(cur);
        if (registered != null && SameExe(registered, exePath)) { _store.Write(want); return true; }   // cung exe, lenh cu (--tray)
        if (registered != null && _fileExists(registered)) return false;
        _store.Write(want);
        return true;
    }
}
