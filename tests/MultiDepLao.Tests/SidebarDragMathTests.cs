using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class SidebarDragMathTests
{
    [Theory]
    [InlineData(100, 180, 420, 1100, 550, 180)]
    [InlineData(300, 180, 420, 1100, 550, 300)]
    [InlineData(900, 180, 420, 1100, 550, 420)]
    [InlineData(900, 180, 420, 800, 550, 250)]
    [InlineData(300, 180, 420, 600, 550, 180)]
    public void Clamp(double cur, double min, double max, double root, double zmin, double expected)
        => Assert.Equal(expected, SidebarDragMath.ComputeWidth(cur, min, max, root, zmin));
}
