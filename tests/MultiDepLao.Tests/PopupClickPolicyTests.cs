using MultiDepLao.Win32;

namespace MultiDepLao.Tests;

public class PopupClickPolicyTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1500, true)]
    [InlineData(3000, true)]
    [InlineData(3001, false)]
    public void Switches_only_inside_time_window(int agoMs, bool expected) =>
        Assert.Equal(expected, GluePolicy.ShouldSwitchOnPopupClick(T0.AddMilliseconds(agoMs), T0, false, false, false));

    [Fact] public void No_recent_popup_click_does_not_switch() =>
        Assert.False(GluePolicy.ShouldSwitchOnPopupClick(T0, null, false, false, false));

    [Fact] public void Locked_does_not_switch() =>
        Assert.False(GluePolicy.ShouldSwitchOnPopupClick(T0, T0, true, false, false));

    [Fact] public void Overlay_does_not_switch() =>
        Assert.False(GluePolicy.ShouldSwitchOnPopupClick(T0, T0, false, true, false));

    [Fact] public void Already_selected_does_not_switch() =>
        Assert.False(GluePolicy.ShouldSwitchOnPopupClick(T0, T0, false, false, true));

    private static MultiDepLao.Win32.Native.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    [Theory]
    [InlineData(150, 150, true)]
    [InlineData(95, 150, true)]
    [InlineData(91, 150, false)]
    [InlineData(150, 207, true)]
    [InlineData(150, 220, false)]
    public void PointNearRect_slack(int x, int y, bool expected) =>
        Assert.Equal(expected, GluePolicy.PointNearRect(x, y, R(100, 100, 200, 200)));

    [Theory]
    [InlineData(true, 99999, true)]
    [InlineData(false, 3000, true)]
    [InlineData(false, 3001, false)]
    public void PopupClickLikely_expiry(bool visible, int agoMs, bool expected) =>
        Assert.Equal(expected, GluePolicy.PopupClickLikely(T0.AddMilliseconds(agoMs), visible, T0, R(0, 0, 10, 10), 5, 5));

    [Fact] public void PopupClickLikely_never_seen_is_false() =>
        Assert.False(GluePolicy.PopupClickLikely(T0, false, null, R(0, 0, 10, 10), 5, 5));

    [Fact] public void PopupClickLikely_cursor_outside_is_false() =>
        Assert.False(GluePolicy.PopupClickLikely(T0, true, T0, R(0, 0, 10, 10), 50, 5));

    [Fact] public void Click_in_the_future_is_ignored() =>
        Assert.False(GluePolicy.ClickRecent(T0, T0.AddSeconds(1)));
}
