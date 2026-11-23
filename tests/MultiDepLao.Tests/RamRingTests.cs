using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class RamRingTests
{
    [Fact]
    public void Arcs_ChainFromTop_InOrder()
    {
        var a = RamRing.Arcs(new long[] { 25, 50 }, 100);
        Assert.Equal(2, a.Count);
        Assert.Equal(0, a[0].Start, 6); Assert.Equal(90, a[0].Sweep, 6);
        Assert.Equal(90, a[1].Start, 6); Assert.Equal(180, a[1].Sweep, 6);
    }

    [Fact]
    public void Arcs_SkipZeroAndNegative_KeepIndex()
    {
        var a = RamRing.Arcs(new long[] { 0, 10, -5, 10 }, 100);
        Assert.Equal(new[] { 1, 3 }, a.Select(x => x.Index).ToArray());
        Assert.Equal(36, a[1].Start, 6);
    }

    [Fact]
    public void Arcs_TotalNeverExceeds360_CutsOverflow()
    {
        var a = RamRing.Arcs(new long[] { 80, 80, 80 }, 100);
        Assert.Equal(2, a.Count);
        Assert.Equal(360, a.Sum(x => x.Sweep), 6);
        Assert.Equal(72, a[1].Sweep, 6);
    }

    [Fact]
    public void Arcs_EmptyOrBadScale()
    {
        Assert.Empty(RamRing.Arcs(Array.Empty<long>(), 100));
        Assert.Empty(RamRing.Arcs(new long[] { 5 }, 0));
    }
}
