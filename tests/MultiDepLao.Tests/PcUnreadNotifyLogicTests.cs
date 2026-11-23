using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class PcUnreadNotifyLogicTests
{
    private static PcUnread Has(int? c) => new(PcUnreadKind.Has, c);
    private static readonly PcUnread None = new(PcUnreadKind.None, null);
    private static readonly PcUnread Unk = new(PcUnreadKind.Unknown, null);

    [Fact] public void NoneToHas_Notifies() => Assert.True(PcUnreadNotifyLogic.ShouldNotify(None, Has(1), false, false));
    [Fact] public void NoneToHasUnknownCount_Notifies() => Assert.True(PcUnreadNotifyLogic.ShouldNotify(None, Has(null), false, false));
    [Fact] public void CountIncrease_Notifies() => Assert.True(PcUnreadNotifyLogic.ShouldNotify(Has(2), Has(3), false, false));
    [Fact] public void FiveToMore_Notifies() => Assert.True(PcUnreadNotifyLogic.ShouldNotify(Has(5), Has(6), false, false));
    [Fact] public void SameOrLower_NoNotify()
    {
        Assert.False(PcUnreadNotifyLogic.ShouldNotify(Has(3), Has(3), false, false));
        Assert.False(PcUnreadNotifyLogic.ShouldNotify(Has(3), Has(2), false, false));
        Assert.False(PcUnreadNotifyLogic.ShouldNotify(Has(6), Has(6), false, false));
    }
    [Fact] public void UnknownCount_NoNotifyWhenAlreadyHas()
    {
        Assert.False(PcUnreadNotifyLogic.ShouldNotify(Has(null), Has(3), false, false));
        Assert.False(PcUnreadNotifyLogic.ShouldNotify(Has(3), Has(null), false, false));
    }
    [Fact] public void UnknownOrNoneNow_NoNotify()
    {
        Assert.False(PcUnreadNotifyLogic.ShouldNotify(Has(1), Unk, false, false));
        Assert.False(PcUnreadNotifyLogic.ShouldNotify(Has(1), None, false, false));
    }
    [Fact] public void LookingOrWarmUp_NoNotify()
    {
        Assert.False(PcUnreadNotifyLogic.ShouldNotify(None, Has(1), true, false));
        Assert.False(PcUnreadNotifyLogic.ShouldNotify(None, Has(1), false, true));
    }
    [Fact] public void Merge_UnknownKeepsPrev()
    {
        Assert.Equal(Has(2), PcUnreadNotifyLogic.Merge(Has(2), Unk));
        Assert.Equal(None, PcUnreadNotifyLogic.Merge(Has(2), None));
        Assert.Equal(Has(4), PcUnreadNotifyLogic.Merge(Has(2), Has(4)));
    }
    [Fact] public void UnknownThenIncrease_StillCompares()
    {
        var p = PcUnreadNotifyLogic.Merge(Has(2), Unk);
        Assert.True(PcUnreadNotifyLogic.ShouldNotify(p, Has(3), false, false));
    }
    [Fact] public void WarmUp_Window()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(PcUnreadNotifyLogic.IsWarmingUp(t, t.AddSeconds(29)));
        Assert.False(PcUnreadNotifyLogic.IsWarmingUp(t, t.AddSeconds(30)));
    }
    [Fact] public void Popup_OnlyWhenFivePlus()
    {
        Assert.True(PcUnreadNotifyLogic.ShouldNotifyOnPopup(Has(6), false, false));
        Assert.False(PcUnreadNotifyLogic.ShouldNotifyOnPopup(Has(3), false, false));
        Assert.False(PcUnreadNotifyLogic.ShouldNotifyOnPopup(Has(6), true, false));
        Assert.False(PcUnreadNotifyLogic.ShouldNotifyOnPopup(Has(6), false, true));
        Assert.False(PcUnreadNotifyLogic.ShouldNotifyOnPopup(None, false, false));
    }
    [Theory]
    [InlineData(null, null)] [InlineData(0, null)] [InlineData(1, "1")] [InlineData(5, "5")] [InlineData(6, "5+")]
    public void CountText(int? c, string? exp) => Assert.Equal(exp, PcUnreadNotifyLogic.CountText(c));
}
