using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class RestartPolicyTests
{
    [Theory]
    [InlineData(InstanceState.Stopped, true)]
    [InlineData(InstanceState.Error, true)]
    [InlineData(InstanceState.Stopping, false)]
    [InlineData(InstanceState.Running, false)]
    [InlineData(InstanceState.Starting, false)]
    public void ReadyToStart(InstanceState s, bool exp) => Assert.Equal(exp, RestartPolicy.ReadyToStart(s));

    [Fact]
    public void RetryOnlyOnceOnQuickExit()
    {
        Assert.True(RestartPolicy.ShouldRetry(InstanceState.Error, "thoat ngay khi khoi dong", 0));
        Assert.True(RestartPolicy.ShouldRetry(InstanceState.Error, "tien trinh thoat ngay khi khoi dong", 0));
        Assert.False(RestartPolicy.ShouldRetry(InstanceState.Error, "thoat ngay khi khoi dong", 1));
        Assert.False(RestartPolicy.ShouldRetry(InstanceState.Error, "loi khac", 0));
        Assert.False(RestartPolicy.ShouldRetry(InstanceState.Running, null, 0));
    }
}
