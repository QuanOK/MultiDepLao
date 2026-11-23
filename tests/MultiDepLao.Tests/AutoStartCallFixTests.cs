using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class AutoStartCallFixTests
{
    private static readonly CallOwnerState None = new(CallOwnerKind.None, null);
    private static readonly CallOwnerState Unk = new(CallOwnerKind.Unknown, null);
    private static readonly CallOwnerState Ext = new(CallOwnerKind.External, null);

    [Theory]
    [InlineData(CallOwnerKind.Known, "a", true, false, CallFixAction.None)]
    [InlineData(CallOwnerKind.Known, "a", true, true, CallFixAction.None)]
    [InlineData(CallOwnerKind.Known, "b", true, false, CallFixAction.None)]   // app biet ro tai khoan khac: khong dung
    [InlineData(CallOwnerKind.None, "a", true, false, CallFixAction.Restart)]
    [InlineData(CallOwnerKind.Unknown, "a", true, false, CallFixAction.Restart)]
    [InlineData(CallOwnerKind.None, "a", true, true, CallFixAction.Notify)]
    [InlineData(CallOwnerKind.Unknown, "a", true, true, CallFixAction.Notify)]
    [InlineData(CallOwnerKind.External, "a", true, false, CallFixAction.Notify)]
    [InlineData(CallOwnerKind.External, "a", true, true, CallFixAction.Notify)]
    [InlineData(CallOwnerKind.None, "a", false, false, CallFixAction.None)]   // chu khong chay
    [InlineData(CallOwnerKind.None, null, false, false, CallFixAction.None)]   // chua cau hinh chu
    public void Decide_Branches(CallOwnerKind kind, string? owner, bool running, bool foreign, CallFixAction want)
    {
        var st = new CallOwnerState(kind, kind == CallOwnerKind.Known ? (owner == "b" ? "a" : owner) : null);
        Assert.Equal(want, AutoStartCallFix.Decide(st, owner, running, foreign));
    }

    private static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public async Task Run_Restart_Success_ReturnsNone_CallsOnce()
    {
        int calls = 0; TimeSpan waited = TimeSpan.Zero;
        var r = await AutoStartCallFix.RunAsync(() => "a", _ => true, () => None, () => false,
            id => { calls++; return Task.FromResult(new SwitchResult(true, null, Array.Empty<string>(), TimeSpan.Zero)); },
            NullLog.Instance, wait: (d, _) => { waited = d; return Task.CompletedTask; });
        Assert.Equal(CallFixAction.None, r); Assert.Equal(1, calls); Assert.Equal(AutoStartCallFix.DefaultDelay, waited);
    }

    [Fact]
    public async Task Run_Restart_Fail_ReturnsNotify()
    {
        var r = await AutoStartCallFix.RunAsync(() => "a", _ => true, () => Unk, () => false,
            id => Task.FromResult(new SwitchResult(false, "x", Array.Empty<string>(), TimeSpan.Zero)), NullLog.Instance, wait: NoWait);
        Assert.Equal(CallFixAction.Notify, r);
    }

    [Fact]
    public async Task Run_Foreign_NotifiesWithoutRestart()
    {
        int calls = 0;
        var r = await AutoStartCallFix.RunAsync(() => "a", _ => true, () => Ext, () => true,
            id => { calls++; return Task.FromResult(new SwitchResult(true, null, Array.Empty<string>(), TimeSpan.Zero)); }, NullLog.Instance, wait: NoWait);
        Assert.Equal(CallFixAction.Notify, r); Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Run_ExceptionSwallowed()
    {
        var r = await AutoStartCallFix.RunAsync(() => throw new InvalidOperationException(), _ => true, () => None, () => false,
            id => throw new InvalidOperationException(), NullLog.Instance, wait: NoWait);
        Assert.Equal(CallFixAction.None, r);
    }
}
