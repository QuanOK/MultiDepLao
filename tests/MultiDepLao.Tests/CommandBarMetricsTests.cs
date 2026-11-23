using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class CommandBarMetricsTests
{
    [Theory]
    [InlineData(979.9, true)]
    [InlineData(980, false)]
    [InlineData(550, true)]
    public void IconOnly_Threshold980(double w, bool expected) => Assert.Equal(expected, CommandBarMetrics.IconOnly(w));

    [Theory]
    [InlineData(500, 50)]
    [InlineData(550, 90)]
    [InlineData(1400, 240)]
    public void NameMaxWidth_Clamped(double w, double expected) => Assert.Equal(expected, CommandBarMetrics.NameMaxWidth(w));

    [Fact]
    public void FlagsVisible_Threshold() { Assert.False(CommandBarMetrics.FlagsVisible(759)); Assert.True(CommandBarMetrics.FlagsVisible(760)); }

    [Fact]
    public void MinWindowHeight_AddsBarAndChrome() => Assert.Equal(680 + 52 + 39, CommandBarMetrics.MinWindowHeight(680, 39));

    [Theory]
    [InlineData(InstanceState.Stopped, true, false, true)]
    [InlineData(InstanceState.Error, true, false, true)]
    [InlineData(InstanceState.Running, false, true, true)]
    [InlineData(InstanceState.Starting, false, true, false)]
    [InlineData(InstanceState.Stopping, false, true, false)]
    public void ForState_PicksButtons(InstanceState s, bool start, bool sleep, bool enabled)
    {
        var a = CommandBarMetrics.ForState(s);
        Assert.Equal(start, a.Start); Assert.Equal(sleep, a.Sleep); Assert.Equal(enabled, a.Enabled);
        Assert.Equal(sleep, a.Restart); Assert.Equal(sleep, a.Reattach);
    }
}
