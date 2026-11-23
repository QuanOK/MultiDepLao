using MultiDepLao.Core;
using MultiDepLao.Win32;
using static MultiDepLao.Win32.Native;

namespace MultiDepLao.Tests;

public class NotifyPopupPolicyTests
{
    private const string C = "Chrome_WidgetWin_1";

    [Theory]
    [InlineData(352, 82, 96u, true)]
    [InlineData(352, 82, 144u, true)]    // popup 352x82 tho du DPI cua so cao
    [InlineData(528, 123, 144u, true)]   // 352x82 nhan 150%
    [InlineData(440, 103, 120u, true)]   // 125%
    [InlineData(528, 123, 96u, false)]
    [InlineData(400, 82, 96u, false)]    // cua so khac (vd. cuoc goi den)
    [InlineData(352, 200, 96u, false)]
    [InlineData(1000, 700, 96u, false)]  // cua so chinh
    public void Shape_by_size_and_dpi(int w, int h, uint dpi, bool expected) =>
        Assert.Equal(expected, NotifyPopupPolicy.IsPopupShape(C, true, w, h, dpi));

    [Fact]
    public void Shape_requires_class_and_topmost()
    {
        Assert.False(NotifyPopupPolicy.IsPopupShape(C, false, 352, 82, 96));
        Assert.False(NotifyPopupPolicy.IsPopupShape("Other", true, 352, 82, 96));
    }

    [Fact]
    public void Push_only_when_intersecting_virtual_screen()
    {
        var virt = RECT.FromXywh(0, 0, 1920, 1080);
        Assert.True(NotifyPopupPolicy.ShouldPush(RECT.FromXywh(1563, 993, 352, 82), virt));
        var off = NotifyPopupPolicy.Target(virt, RECT.FromXywh(1563, 993, 352, 82));
        Assert.False(NotifyPopupPolicy.ShouldPush(off, virt));   // da ngoai -> khong day lai
        Assert.Equal(352, off.Width);
        Assert.Equal(82, off.Height);
        Assert.True(off.Right < virt.Left);
    }

    [Fact]
    public void Push_with_negative_origin_monitor()
    {
        var virt = RECT.FromXywh(-1920, 0, 3840, 1080);
        var off = NotifyPopupPolicy.Target(virt, RECT.FromXywh(-400, 900, 352, 82));
        Assert.False(NotifyPopupPolicy.ShouldPush(off, virt));
        Assert.True(off.Right < virt.Left);
    }

    [Fact]
    public void Hidden_popup_notify_and_quiet_scan()
    {
        Assert.True(PcUnreadNotifyLogic.ShouldNotifyOnHiddenPopup(false, false));
        Assert.False(PcUnreadNotifyLogic.ShouldNotifyOnHiddenPopup(true, false));
        Assert.False(PcUnreadNotifyLogic.ShouldNotifyOnHiddenPopup(false, true));
        var t = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        Assert.True(PcUnreadNotifyLogic.ShouldQuietScanToast(t, t.AddSeconds(4)));
        Assert.False(PcUnreadNotifyLogic.ShouldQuietScanToast(t, t.AddSeconds(16)));
        Assert.False(PcUnreadNotifyLogic.ShouldQuietScanToast(null, t));
    }
}
