using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class RamLegendTests
{
    private const long Mb = 1024 * 1024;

    [Fact]
    public void Build_SortsDescending_AndSkipsZero()
    {
        var r = RamLegend.Build(new[] { ("a", 100 * Mb, 1), ("b", 0L, 2), ("c", 300 * Mb, 3), ("d", 200 * Mb, 4) });
        Assert.Equal(new[] { "c", "d", "a" }, r.Rows.Select(x => x.Name));
        Assert.Equal(0, r.OtherCount);
    }

    [Fact]
    public void Build_CutsTop3_AndMergesRest()
    {
        var r = RamLegend.Build(new[] { ("a", 10 * Mb, 0), ("b", 50 * Mb, 0), ("c", 40 * Mb, 0), ("d", 30 * Mb, 0), ("e", 20 * Mb, 0) });
        Assert.Equal(new[] { "b", "c", "d" }, r.Rows.Select(x => x.Name));
        Assert.Equal(2, r.OtherCount);
        Assert.Equal(30 * Mb, r.OtherBytes);
    }

    [Fact]
    public void Build_Empty()
    {
        var r = RamLegend.Build(Array.Empty<(string, long, int)>());
        Assert.Empty(r.Rows);
        Assert.Equal(0, r.OtherCount);
    }

    [Fact]
    public void Build_TiesKeepInputOrder()
    {
        var r = RamLegend.Build(new[] { ("x", Mb, 0), ("y", Mb, 0) });
        Assert.Equal(new[] { "x", "y" }, r.Rows.Select(x => x.Name));
    }

    [Fact]
    public void FormatGb_ByLanguage()
    {
        long b = (long)(9.84 * (1L << 30));
        Assert.Equal("9,8 GB", RamLegend.FormatGb(b, "vi"));
        Assert.Equal("9.8 GB", RamLegend.FormatGb(b, "en"));
    }

    [Fact]
    public void FormatMb_ByLanguage()
    {
        Assert.Equal("2.096 MB", RamLegend.FormatMb(2096 * Mb, "vi"));
        Assert.Equal("2,096 MB", RamLegend.FormatMb(2096 * Mb, "en"));
        Assert.Equal("0 MB", RamLegend.FormatMb(-5, "vi"));
    }

    [Fact]
    public void FormatGbNumber_OneDecimal()
    {
        Assert.Equal("2,0", RamLegend.FormatGbNumber(2L << 30, "vi"));
        Assert.Equal("1.5", RamLegend.FormatGbNumber(3L << 29, "en"));
    }

    [Fact]
    public void Config_WithLegacyDonateCardCollapsed_StillLoads()
    {
        using var t = new TempRoot();
        Directory.CreateDirectory(t.Paths.Root);
        File.WriteAllText(t.Paths.ConfigFile, "{\"accounts\":[],\"donateCardCollapsed\":true,\"sidebarCollapsed\":true}");
        var cfg = new ConfigStore(t.Paths).Load();
        Assert.True(cfg.SidebarCollapsed);
        Assert.False(File.Exists(t.Paths.ConfigFile + ".bad"));
    }
}
