namespace MultiDepLao.Core;

/// <summary>Tinh chieu rong sidebar khi keo bang tay (pure, de test).</summary>
public static class SidebarDragMath
{
    /// <param name="cursorDip">X con tro (DIP, tuong doi Root).</param>
    /// <param name="rootWidth">Rong Root (DIP).</param>
    /// <param name="zaloMin">Rong toi thieu cot Zalo.</param>
    public static double ComputeWidth(double cursorDip, double minW, double maxW, double rootWidth, double zaloMin)
    {
        double hi = Math.Min(maxW, rootWidth - zaloMin);
        if (hi < minW) hi = minW;
        if (double.IsNaN(cursorDip)) return minW;
        return Math.Clamp(cursorDip, minW, hi);
    }
}
