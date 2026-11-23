using System.Diagnostics;
using MultiDepLao.Core;

namespace MultiDepLao.Win32;

/// <summary>ICaptureProcessOps that: liet ke ZaloCap.exe, cay con qua Toolhelp32, tat dung mot tien trinh (khong cay).</summary>
public sealed class WinCaptureProcessOps : ICaptureProcessOps
{
    private readonly WinProcessProbe _probe = new();

    public IReadOnlyList<ProcessInfo> FindCapProcesses()
    {
        var list = new List<ProcessInfo>();
        foreach (var p in Process.GetProcessesByName("ZaloCap"))
        {
            using (p)
            {
                var info = _probe.Get(p.Id);
                if (info != null) list.Add(info);
            }
        }
        return list;
    }

    public ISet<int> DescendantsOf(int pid)
    {
        var s = ProcessTree.Descendants((uint)pid).Select(x => (int)x).ToHashSet();
        s.Remove(pid);
        return s;
    }

    public bool Kill(ProcessInfo target)
    {
        var now = _probe.Get(target.Pid);
        if (now == null) return true;   // da thoat
        if (!string.Equals(now.ExePath, target.ExePath, StringComparison.OrdinalIgnoreCase)
            || (now.CreatedUtc - target.CreatedUtc).Duration() > TimeSpan.FromSeconds(2)) return false;   // PID tai su dung
        try { using var p = Process.GetProcessById(target.Pid); p.Kill(false); return p.WaitForExit(3000); }
        catch (ArgumentException) { return true; }
        catch (Exception) { return false; }
    }
}
