using MultiDepLao.Core;
using MultiDepLao.Win32;

namespace MultiDepLao.Tests;

public class UnreadBadgeTests
{
    [Fact]
    public void SelectStripContainer_PicksMergedNavListGroup()
    {
        var els = new List<(int, double, double, double, double)>
        {
            (50026, 837, 315, 344, 641),   // list: khong chua dai
            (50026, 1182, 219, 700, 737),  // pane rong
            (50026, 773, 219, 409, 737),   // nav + list
            (50020, 790, 250, 30, 30),
        };
        Assert.Equal(2, UnreadBadgeProbe.SelectStripContainer(els, (773, 219, 1000, 737), 1));
    }

    [Fact]
    public void SelectStripContainer_NoneWhenNoneQualifies()
    {
        var els = new List<(int, double, double, double, double)>
        {
            (50026, 837, 315, 344, 641),
            (50026, 773, 219, 900, 737),   // qua rong
            (50026, 773, 219, 409, 200),   // qua thap
            (50020, 773, 219, 409, 737),   // sai ControlType
        };
        Assert.Equal(-1, UnreadBadgeProbe.SelectStripContainer(els, (773, 219, 1000, 737), 1));
    }

    private static (int ct, double l, double t, double w, double h) Col(double s = 1) => (50026, 100, 50, 64 * s, 700 * s);

    [Theory]
    [InlineData("", true)] [InlineData("", true)] [InlineData("", true)] [InlineData("", true)]
    [InlineData("", false)] [InlineData("", false)] [InlineData("", false)] [InlineData("", false)]
    [InlineData(null, false)] [InlineData("", false)]
    public void AcceptBadge_ByFirstCodePoint(string? name, bool expected)
        => Assert.Equal(expected, UnreadBadgeProbe.AcceptBadge(name));

    [Fact]
    public void Badge17x17InColumn_Has()
    {
        var e = new List<(int, double, double, double, double)> { Col(), (50020, 134, 127, 17, 17) };
        Assert.Equal(UnreadState.Has, UnreadBadgeProbe.Classify(e, 1));
    }

    [Fact]
    public void OnlyIcon30x30_None()
    {
        var e = new List<(int, double, double, double, double)> { Col(), (50006, 117, 100, 30, 30) };
        Assert.Equal(UnreadState.None, UnreadBadgeProbe.Classify(e, 1));
    }

    [Fact]
    public void NoColumn_Unknown()
    {
        var e = new List<(int, double, double, double, double)> { (50020, 134, 127, 17, 17), (50026, 0, 0, 200, 700) };
        Assert.Equal(UnreadState.Unknown, UnreadBadgeProbe.Classify(e, 1));
        Assert.Equal(UnreadState.Unknown, UnreadBadgeProbe.Classify(new List<(int, double, double, double, double)>(), 1));
    }

    [Fact]
    public void SmallElementOutsideColumn_None()
    {
        var e = new List<(int, double, double, double, double)> { Col(), (50020, 400, 127, 17, 17) };
        Assert.Equal(UnreadState.None, UnreadBadgeProbe.Classify(e, 1));
    }

    [Fact]
    public void Scale150()
    {
        double s = 1.5;
        var has = new List<(int, double, double, double, double)> { Col(s), (50020, 100 + 51, 50 + 115, 25, 25) };
        Assert.Equal(UnreadState.Has, UnreadBadgeProbe.Classify(has, s));
        var none = new List<(int, double, double, double, double)> { Col(s), (50006, 120, 100, 45, 45) };
        Assert.Equal(UnreadState.None, UnreadBadgeProbe.Classify(none, s));
        // cot 64*1.5=96 hop le o scale 1.5 nhung khong hop le o scale 1 (>72)
        Assert.Equal(UnreadState.Unknown, UnreadBadgeProbe.Classify(new List<(int, double, double, double, double)> { Col(s) }, 1));
    }

    [Fact]
    public void Config_DefaultsTrue_AndMissingKeyStaysTrue()
    {
        Assert.True(new AppConfig().UnreadBadge);
        using var t = new TempRoot(); var paths = t.Paths;
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.ConfigFile, "{\"sidebarCollapsed\": true}");
        Assert.True(new ConfigStore(paths).Load().UnreadBadge);
        File.WriteAllText(paths.ConfigFile, "{\"unreadBadge\": false}");
        Assert.False(new ConfigStore(paths).Load().UnreadBadge);
    }
    [Theory]
    [InlineData("\uEA05", 1)]
    [InlineData("\uEA07", 3)]
    [InlineData("\uEA09", 5)]
    [InlineData("\uEA0A", 6)]   // "5+"
    [InlineData("\uEA0B", null)]
    [InlineData("\uEA0D", null)]
    [InlineData("\uEA04", null)]
    [InlineData("3", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("\uEA05\uEA05", null)]
    public void CountFromBadgeName_Cases(string? name, int? expected)
        => Assert.Equal(expected, UnreadBadgeProbe.CountFromBadgeName(name));

    [Fact]
    public void FindBadgeIndex_ReturnsBadgeElement()
    {
        var e = new List<(int, double, double, double, double)> { (50006, 117, 100, 30, 30), Col(), (50020, 134, 127, 17, 17) };
        var (s, i) = UnreadBadgeProbe.FindBadgeIndex(e, 1);
        Assert.Equal(UnreadState.Has, s); Assert.Equal(2, i);   // icon 30x30 > 24 bi loai
    }

    [Fact]
    public void ClassifyStrip_Cases()
    {
        var strip = (773.0, 219.0, 72.0, 700.0);
        var has = new List<(int, double, double, double, double)> { (50020, 790, 300, 30, 30), (50020, 807, 296, 17, 17) };
        Assert.Equal((UnreadState.Has, 1), UnreadBadgeProbe.ClassifyStrip(strip, has, 1));
        var icons = new List<(int, double, double, double, double)> { (50020, 790, 300, 30, 30), (50020, 789, 676, 32, 32), (50020, 790, 356, 0, 0) };
        Assert.Equal(UnreadState.None, UnreadBadgeProbe.ClassifyStrip(strip, icons, 1).State);
        Assert.Equal(UnreadState.Unknown, UnreadBadgeProbe.ClassifyStrip(strip, new List<(int, double, double, double, double)>(), 1).State);
        Assert.Equal(UnreadState.Unknown, UnreadBadgeProbe.ClassifyStrip(strip,
            new List<(int, double, double, double, double)> { (50020, 790, 300, 0, 0), (50020, 800, 400, 0, 20) }, 1).State);
        // phan tu ngoai dai bi bo qua
        var outside = new List<(int, double, double, double, double)> { (50020, 1000, 296, 17, 17), (50020, 790, 300, 30, 30) };
        Assert.Equal(UnreadState.None, UnreadBadgeProbe.ClassifyStrip(strip, outside, 1).State);
    }

    [Fact]
    public void ClassifyInNav_Cases()
    {
        var nav = (100.0, 50.0, 64.0, 700.0);
        var has = new List<(int, double, double, double, double)> { (50006, 117, 100, 30, 30), (50020, 134, 127, 17, 17) };
        Assert.Equal((UnreadState.Has, 1), UnreadBadgeProbe.ClassifyInNav(nav, has, 1));
        var none = new List<(int, double, double, double, double)> { (50006, 117, 100, 30, 30) };
        Assert.Equal(UnreadState.None, UnreadBadgeProbe.ClassifyInNav(nav, none, 1).State);
        Assert.Equal(UnreadState.Unknown, UnreadBadgeProbe.ClassifyInNav((100, 50, 200, 700), has, 1).State);
        Assert.Equal(UnreadState.Unknown, UnreadBadgeProbe.ClassifyInNav((100, 50, 64, 100), has, 1).State);
        // cay con rong / chi co phan tu ngoai cot -> chua biet (khong tat cham)
        Assert.Equal(UnreadState.Unknown, UnreadBadgeProbe.ClassifyInNav(nav, new List<(int, double, double, double, double)>(), 1).State);
        Assert.Equal(UnreadState.Unknown, UnreadBadgeProbe.ClassifyInNav(nav,
            new List<(int, double, double, double, double)> { (50006, 900, 100, 30, 30) }, 1).State);
        Assert.Equal(1, UnreadBadgeProbe.FindNavIndex(new List<(int, double, double, double, double)> { (50020, 0, 0, 5, 5), Col() }, 1));
    }
}
