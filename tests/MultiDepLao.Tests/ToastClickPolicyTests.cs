using MultiDepLao.Win32;

namespace MultiDepLao.Tests;

public class ToastClickPolicyTests
{
    [Fact]
    public void ClientCenter_and_lparam()
    {
        Assert.Equal((200, 50), ToastClickPolicy.ClientCenter(400, 100));
        Assert.Equal((0, 0), ToastClickPolicy.ClientCenter(-5, 0));
        Assert.Equal((IntPtr)((50 << 16) | 200), ToastClickPolicy.MakeLParam(200, 50));
    }
}
