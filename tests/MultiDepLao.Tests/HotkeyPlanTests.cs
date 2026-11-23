using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class HotkeyPlanTests
{
    [Fact]
    public void TabChiKhiCuaSoChinhLaForeground()
    {
        Assert.True(HotkeyPlan.For(true, true, false).TabSwitch);
        Assert.False(HotkeyPlan.For(true, false, false).TabSwitch);   // foreground la cua so Zalo PC hoac hop thoai
        Assert.False(HotkeyPlan.For(false, false, true).TabSwitch);
    }

    [Fact]
    public void ChonVaKhoa()
    {
        var p = HotkeyPlan.For(true, false, true);
        Assert.True(p.Select); Assert.True(p.Lock);
        Assert.False(HotkeyPlan.For(true, true, false).Lock);
        var off = HotkeyPlan.For(false, false, true);
        Assert.False(off.Select); Assert.False(off.Lock);
    }
}
