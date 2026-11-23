namespace MultiDepLao.Core;

/// <summary>Ham thuan cho giao dien thanh ben (de test).</summary>
public static class SidebarMetrics
{
    /// <summary>Duoi nguong nay (DIP) thanh ben o che do "chat": an ghi chu, hang RAM.</summary>
    public const double CompactBelow = 240;

    public static bool IsCompact(double width) => width < CompactBelow;

    /// <summary>Ti le 0..1 theo tong RAM may.</summary>
    public static double TotalFraction(long ramBytes, long machineBytes)
        => ramBytes <= 0 || machineBytes <= 0 ? 0 : Math.Min(1.0, (double)ramBytes / machineBytes);
}
