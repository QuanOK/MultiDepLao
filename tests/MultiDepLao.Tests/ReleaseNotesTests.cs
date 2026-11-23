using System.Globalization;
using System.Text.RegularExpressions;
using MultiDepLao.Core;

namespace MultiDepLao.Tests;

[Collection("Loc")]
public sealed class ReleaseNotesTests
{
    [Theory]
    [InlineData(null, "0.3.4", true, true)]
    [InlineData(null, "0.3.4", false, false)]
    [InlineData("0.3.3", "0.3.4", false, true)]
    [InlineData("0.3.4", "0.3.4", true, false)]
    [InlineData("0.4.0", "0.3.4", true, false)]
    [InlineData("abc", "0.3.4", false, true)]
    [InlineData("", "0.3.4", false, true)]
    public void ShouldShow(string? seen, string cur, bool accounts, bool expected) =>
        Assert.Equal(expected, ReleaseNotes.ShouldShow(seen, cur, accounts));

    [Fact]
    public void Since_FiltersNewer()
    {
        Assert.Equal(new[] { "0.4.8", "0.4.7", "0.4.6", "0.4.5", "0.4.4", "0.4.3", "0.4.2", "0.4.1", "0.4.0", "0.3.4", "0.3.3" }, ReleaseNotes.Since("0.3.2").Select(n => n.Version));
        Assert.Empty(ReleaseNotes.Since("0.4.8"));
        Assert.Equal(ReleaseNotes.All.Count, ReleaseNotes.Since(null).Count);
        Assert.Equal(ReleaseNotes.All.Count, ReleaseNotes.Since("garbage").Count);
    }

    [Fact]
    public void Data_IsSane()
    {
        var vs = ReleaseNotes.All.Select(n => Version.Parse(n.Version)).ToList();
        for (int i = 1; i < vs.Count; i++) Assert.True(vs[i - 1] > vs[i]);
        foreach (var n in ReleaseNotes.All)
        {
            Assert.True(DateTime.TryParseExact(n.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), n.Version);
            Assert.NotEmpty(n.Items);
            foreach (var it in n.Items)
            {
                Assert.False(string.IsNullOrWhiteSpace(it.Vi));
                Assert.False(string.IsNullOrWhiteSpace(it.En));
            }
        }
    }

    [Fact]
    public void FirstEntry_MatchesCsprojVersion()
    {
        var dir = AppContext.BaseDirectory;
        string? csproj = null;
        for (var d = new DirectoryInfo(dir); d != null && csproj == null; d = d.Parent)
        {
            var f = Path.Combine(d.FullName, "src", "MultiDepLao.App", "MultiDepLao.App.csproj");
            if (File.Exists(f)) csproj = f;
        }
        Assert.NotNull(csproj);
        var m = Regex.Match(File.ReadAllText(csproj!), @"<Version>([^<]+)</Version>");
        Assert.True(m.Success);
        Assert.Equal(m.Groups[1].Value, ReleaseNotes.All[0].Version);
    }

    [Fact]
    public void Text_FollowsLanguage()
    {
        try
        {
            var it = ReleaseNotes.All[0].Items[0];
            var (vi, en) = it.Kind switch { ReleaseKind.New => ("Mới", "New"), ReleaseKind.Fixed => ("Sửa lỗi", "Fixed"), _ => ("Thay đổi", "Changed") };
            Loc.Set("vi"); Assert.Equal(it.Vi, it.Text); Assert.Equal(vi, it.KindLabel);
            Loc.Set("en"); Assert.Equal(it.En, it.Text); Assert.Equal(en, it.KindLabel);
        }
        finally { Loc.Set("vi"); }
    }

    [Fact]
    public void LastSeenVersion_RoundTrips()
    {
        using var t = new TempRoot();
        var store = new ConfigStore(t.Paths);
        Assert.Null(store.Load().LastSeenVersion);
        store.Save(new AppConfig { LastSeenVersion = "0.3.4" });
        Assert.Equal("0.3.4", store.Load().LastSeenVersion);
    }
}
