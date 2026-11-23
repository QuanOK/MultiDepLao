using MultiDepLao.Win32;
using static MultiDepLao.Win32.Native;

namespace MultiDepLao.Tests;

public class GluePolicyTests
{
    private static readonly RECT Target = RECT.FromXywh(240, 55, 1680, 1025);
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0);

    [Theory]
    [InlineData(0, 0, 1920, 1080, 844, 700)]      // 1 man hinh
    [InlineData(-1920, -200, 3840, 1280, 420, 644)] // man hinh phu ben trai/tren goc toa do
    public void OffscreenRect_is_left_of_virtual_screen_and_keeps_size(int vx, int vy, int vw, int vh, int w, int h)
    {
        var virt = RECT.FromXywh(vx, vy, vw, vh);
        var r = GluePolicy.OffscreenRect(virt, w, h);
        Assert.Equal(w, r.Width); Assert.Equal(h, r.Height);
        Assert.True(r.Right < virt.Left);           // khong cham moi man hinh
        Assert.Equal(virt.Top, r.Top);
    }

    [Theory]
    [InlineData(96u, 844)]
    [InlineData(0u, 844)]
    [InlineData(120u, 1055)]
    [InlineData(144u, 1266)]
    public void ParkWidth_scales_with_dpi(uint dpi, int expected) =>
        Assert.Equal(expected, GluePolicy.ParkWidthPx(844, dpi));

    [Theory]
    [InlineData(false, 1500, 844, true)]
    [InlineData(false, 845, 844, true)]
    [InlineData(false, 844, 844, false)]
    [InlineData(false, 600, 844, false)]
    [InlineData(true, 1500, 844, false)]
    public void ShouldPark_only_wider_nonfixed(bool isFixed, int w, int pw, bool expected) =>
        Assert.Equal(expected, GluePolicy.ShouldPark(isFixed, w, pw));

    [Fact]
    public void ShouldNudge_only_when_pending_and_on_target()
    {
        var t = RECT.FromXywh(420, 55, 1500, 1145);
        Assert.True(GluePolicy.ShouldNudge(true, t, t));
        Assert.False(GluePolicy.ShouldNudge(false, t, t));
        Assert.False(GluePolicy.ShouldNudge(true, RECT.FromXywh(420, 55, 1663, 1145), t));
    }

    [Fact]
    public void Matching_rect_needs_no_correction()
    {
        var d = GluePolicy.Decide(Target, Target, T0, DateTime.MinValue, false);
        Assert.False(d.Off); Assert.False(d.Correct);
    }

    [Fact]
    public void Size_only_difference_is_corrected()
    {
        var d = GluePolicy.Decide(RECT.FromXywh(240, 55, 1522, 1025), Target, T0, DateTime.MinValue, false);
        Assert.False(d.PosOff); Assert.True(d.SizeOff); Assert.True(d.Correct);
    }

    [Fact]
    public void Position_difference_is_corrected()
    {
        var d = GluePolicy.Decide(RECT.FromXywh(398, 55, 1680, 1025), Target, T0, DateTime.MinValue, false);
        Assert.True(d.PosOff); Assert.True(d.Correct);
    }

    [Fact]
    public void Within_throttle_not_corrected_unless_forced()
    {
        var cur = RECT.FromXywh(398, 55, 1522, 1025);
        var last = T0.AddMilliseconds(-50);
        Assert.False(GluePolicy.Decide(cur, Target, T0, last, false).Correct);
        Assert.True(GluePolicy.Decide(cur, Target, T0, last, true).Correct);
    }

    [Fact]
    public void Tolerance_of_one_pixel()
    {
        Assert.False(GluePolicy.IsOff(RECT.FromXywh(241, 56, 1681, 1024), Target));
        Assert.True(GluePolicy.IsOff(RECT.FromXywh(242, 55, 1680, 1025), Target));
    }

    [Fact]
    public void Log_limited_to_once_per_second()
    {
        Assert.False(GluePolicy.LogAllowed(T0.AddMilliseconds(500), T0));
        Assert.True(GluePolicy.LogAllowed(T0.AddMilliseconds(1000), T0));
    }
}
