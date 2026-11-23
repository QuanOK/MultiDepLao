using System.Runtime.InteropServices;
using static MultiDepLao.Win32.Native;

namespace MultiDepLao.Win32;

public static class IdleTime
{
    /// <summary>Thoi gian tu lan nhap chuot/ban phim cuoi cua phien (GetLastInputInfo).</summary>
    public static TimeSpan SinceLastInput()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        uint ms = unchecked(GetTickCount() - info.dwTime); // tran so 49 ngay van dung nho unsigned
        return TimeSpan.FromMilliseconds(ms);
    }
}
