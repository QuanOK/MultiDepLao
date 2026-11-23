using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class AppInfoLinkTests
{
    [Theory]
    [InlineData(AppInfo.SourceUrl)]
    [InlineData(AppInfo.ReleasesUrl)]
    public void Urls_AreAbsoluteHttps(string url)
    {
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var u));
        Assert.Equal("https", u!.Scheme);
        Assert.Equal("github.com", u.Host);
    }

    [Fact]
    public void ReleasesUrl_IsUnderSource() => Assert.StartsWith(AppInfo.SourceUrl + "/", AppInfo.ReleasesUrl);

    [Fact]
    public void AboutStrings_HaveBothLanguages()
    {
        foreach (var k in new[] { "About.SourceTitle", "About.SourceNote", "About.LicenseLine", "About.TrademarkNote" })
            Assert.False(string.IsNullOrWhiteSpace(Loc.T(k)));
    }
}
