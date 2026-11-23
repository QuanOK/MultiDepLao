using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MultiDepLao.Core;

/// <summary>Thong tin mot tien trinh. CommandLine co the null (khong dung WMI).</summary>
public sealed record ProcessInfo(int Pid, string Name, string? ExePath, DateTime CreatedUtc, string? CommandLine = null);

public interface IProcessProbe
{
    /// <summary>null neu tien trinh khong ton tai / da thoat.</summary>
    ProcessInfo? Get(int pid);
    /// <summary>Ket thuc tien trinh va ca cay con (taskkill /T /F).</summary>
    void KillTree(int pid);
}

public sealed class WinProcessProbe : IProcessProbe
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, char[] buf, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr h, out uint code);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint STILL_ACTIVE = 259;

    public ProcessInfo? Get(int pid)
    {
        if (pid <= 0) return null;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == 5) return new ProcessInfo(pid, "", null, DateTime.MinValue);   // ton tai nhung khong doc duoc -> cong an toan se tu choi
            return null;                                                                  // 87 = khong ton tai
        }
        try
        {
            if (!GetExitCodeProcess(h, out var code) || code != STILL_ACTIVE) return null;
            if (!GetProcessTimes(h, out var creation, out _, out _, out _)) return null;
            var buf = new char[1024]; int size = buf.Length;
            string? path = QueryFullProcessImageNameW(h, 0, buf, ref size) ? new string(buf, 0, size) : null;
            return new ProcessInfo(pid, path == null ? "" : Path.GetFileName(path), path, DateTime.FromFileTimeUtc(creation));
        }
        finally { CloseHandle(h); }
    }

    public void KillTree(int pid)
    {
        try { using var p = Process.GetProcessById(pid); p.Kill(entireProcessTree: true); }
        catch (ArgumentException) { /* da thoat */ }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
}
