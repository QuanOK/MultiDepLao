using System.Runtime.InteropServices;
using static MultiDepLao.Win32.Native;

namespace MultiDepLao.Win32;

/// <summary>Cay tien trinh qua Toolhelp32 (khong WMI).</summary>
public static class ProcessTree
{
    /// <summary>PID goc + moi hau due (theo snapshot). Goc luon co trong ket qua.</summary>
    public static HashSet<uint> Descendants(uint root)
    {
        var parentOf = new Dictionary<uint, uint>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap != IntPtr.Zero && snap != (IntPtr)(-1))
        {
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                if (Process32FirstW(snap, ref e))
                    do { parentOf[e.th32ProcessID] = e.th32ParentProcessID; } while (Process32NextW(snap, ref e));
            }
            finally { CloseHandle(snap); }
        }
        var set = new HashSet<uint> { root };
        bool added = true;
        while (added)
        {
            added = false;
            foreach (var kv in parentOf)
                if (kv.Key != kv.Value && !set.Contains(kv.Key) && set.Contains(kv.Value)) { set.Add(kv.Key); added = true; }
        }
        return set;
    }

    /// <summary>Working set cua mot tien trinh (byte); 0 neu khong mo duoc.</summary>
    public static long WorkingSetOf(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return 0;
        try
        {
            return GetProcessMemoryInfo(h, out var c, (uint)Marshal.SizeOf<PROCESS_MEMORY_COUNTERS>())
                ? (long)c.WorkingSetSize : 0;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Tong working set (byte) cua ca cay bat dau tu root.</summary>
    public static long TotalWorkingSetBytes(uint root)
    {
        long sum = 0;
        foreach (var pid in Descendants(root)) sum += WorkingSetOf(pid);
        return sum;
    }
}
