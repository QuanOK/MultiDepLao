namespace MultiDepLao.Win32;

/// <summary>Tinh toan thuan cho "bam toast -> mo chat" (gui chuot vao popup Zalo da an). Khong P/Invoke.</summary>
public static class ToastClickPolicy
{
    /// <summary>Tam vung client (toa do client). Rong/cao &lt;= 0 -> (0,0).</summary>
    public static (int X, int Y) ClientCenter(int width, int height) => (Math.Max(0, width) / 2, Math.Max(0, height) / 2);

    /// <summary>MAKELPARAM(x,y) (16 bit moi nua, co the am).</summary>
    public static IntPtr MakeLParam(int x, int y) => (IntPtr)(int)(((uint)(ushort)y << 16) | (ushort)x);
}
