namespace MultiDepLao.Core;

/// <summary>Tinh thuan cho anh dai dien: o cat vuong o giua.</summary>
public static class AvatarMath
{
    public const int Size = 128;
    /// <summary>Anh nguon toi da 20 MB (tranh nap file khong lo).</summary>
    public const long MaxSourceBytes = 20L * 1024 * 1024;
    /// <summary>Canh dai nhat khi giai ma anh nguon (giu ti le); lon hon se bi thu nho ngay luc giai ma.</summary>
    public const int MaxDecodeSide = 1024;

    /// <summary>
    /// (DecodePixelWidth, DecodePixelHeight) can dat cho anh nguon w x h: 0 = khong gioi han. Chi dat canh dai (de WPF giu ti le);
    /// anh khong lon hon <see cref="MaxDecodeSide"/> -> (0, 0).
    /// </summary>
    public static (int Width, int Height) DecodeSize(int w, int h)
    {
        if (w <= 0 || h <= 0 || Math.Max(w, h) <= MaxDecodeSide) return (0, 0);
        return w >= h ? (MaxDecodeSide, 0) : (0, MaxDecodeSide);
    }

    /// <summary>Hinh vuong lon nhat nam giua anh (x, y, canh). Kich thuoc khong hop le -> (0,0,0).</summary>
    public static (int X, int Y, int Side) CenterSquare(int width, int height)
    {
        if (width <= 0 || height <= 0) return (0, 0, 0);
        int side = Math.Min(width, height);
        return ((width - side) / 2, (height - side) / 2, side);
    }
}
