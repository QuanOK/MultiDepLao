using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class SidebarMetricsTests
{
    [Theory]
    [InlineData(180, true)]
    [InlineData(239.9, true)]
    [InlineData(240, false)]
    [InlineData(420, false)]
    public void IsCompact_Threshold240(double w, bool expected) => Assert.Equal(expected, SidebarMetrics.IsCompact(w));

    [Fact]
    public void TotalFraction_HandlesZeroMachine()
    {
        Assert.Equal(0, SidebarMetrics.TotalFraction(100, 0));
        Assert.Equal(0.25, SidebarMetrics.TotalFraction(1, 4), 5);
        Assert.Equal(1.0, SidebarMetrics.TotalFraction(10, 4));
    }
}
