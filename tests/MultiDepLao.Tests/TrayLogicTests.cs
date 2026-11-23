using MultiDepLao.Core;
using Xunit;

namespace MultiDepLao.Tests;

public class TrayLogicTests
{
    [Fact]
    public void ClampTip_cat_o_63()
    {
        Assert.Equal(63, TrayLogic.ClampTip(new string('a', 100)).Length);
        Assert.Equal("abc", TrayLogic.ClampTip("abc"));
        Assert.Equal("", TrayLogic.ClampTip(null));
    }

    [Theory]
    [InlineData(16, 0)]
    [InlineData(20, 1)]   // 24 la nho nhat >= 20 (index 1 trong {16,24,32,256})? xem mang ben duoi
    [InlineData(32, 2)]
    [InlineData(40, 3)]
    [InlineData(300, 3)]
    public void PickIconIndex_nho_nhat_khong_nho_hon_dich(int target, int expected)
    {
        Assert.Equal(expected, TrayLogic.PickIconIndex(new[] { 16, 24, 32, 256 }, target));
    }

    [Fact]
    public void PickIconIndex_rong_va_chi_co_anh_nho()
    {
        Assert.Equal(-1, TrayLogic.PickIconIndex(Array.Empty<int>(), 16));
        Assert.Equal(1, TrayLogic.PickIconIndex(new[] { 16, 24 }, 48));
    }
}
