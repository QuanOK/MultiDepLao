using System.Text;

namespace MultiDepLao.Core;

public interface ILog
{
    void Info(string msg);
    void Warn(string msg);
    void Error(string msg);
}

public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();
    public void Info(string msg) { }
    public void Warn(string msg) { }
    public void Error(string msg) { }
}

/// <summary>Log xoay vong: app.log, app.1.log ... (tong 5 file x 2 MB). KHONG ghi tieu de cua so.</summary>
public sealed class FileLog : ILog
{
    private readonly string _dir;
    private readonly long _maxBytes;
    private readonly int _files;
    private readonly object _lock = new();

    public FileLog(AppPaths paths, long maxBytes = 2 * 1024 * 1024, int files = 5)
    { _dir = paths.LogsDir; _maxBytes = maxBytes; _files = files; }

    private string FileFor(int i) => Path.Combine(_dir, i == 0 ? "app.log" : $"app.{i}.log");

    public void Info(string msg) => Write("INFO", msg);
    public void Warn(string msg) => Write("WARN", msg);
    public void Error(string msg) => Write("ERROR", msg);

    private void Write(string level, string msg)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_dir);
                var cur = FileFor(0);
                if (File.Exists(cur) && new FileInfo(cur).Length >= _maxBytes) Rotate();
                File.AppendAllText(cur, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {msg}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* log khong duoc lam hong app */ }
    }

    private void Rotate()
    {
        var last = FileFor(_files - 1);
        if (File.Exists(last)) File.Delete(last);
        for (int i = _files - 2; i >= 0; i--)
            if (File.Exists(FileFor(i))) File.Move(FileFor(i), FileFor(i + 1));
    }
}
