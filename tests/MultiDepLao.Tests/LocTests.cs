using System.Text.RegularExpressions;
using MultiDepLao.Core;

namespace MultiDepLao.Tests;

[Collection("Loc")]
public sealed class LocTests : IDisposable
{
    public LocTests() { Loc.Set("vi"); Loc.AssertOnMissing = true; }
    public void Dispose() { Loc.Set("vi"); Loc.AssertOnMissing = true; }

    private static readonly Regex Ph = new(@"\{(\d+)\}");
    private static int[] Placeholders(string s) => Ph.Matches(s).Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(x => x).ToArray();

    [Fact]
    public void EveryKey_HasBothLanguages_NonEmpty()
    {
        Assert.Equal(Strings.Vi.Keys.OrderBy(k => k), Strings.En.Keys.OrderBy(k => k));
        foreach (var (k, v) in Strings.Vi) Assert.False(string.IsNullOrWhiteSpace(v), "vi rong: " + k);
        foreach (var (k, v) in Strings.En) Assert.False(string.IsNullOrWhiteSpace(v), "en rong: " + k);
    }

    [Fact]
    public void Placeholders_MatchBetweenLanguages()
    {
        foreach (var k in Strings.Vi.Keys)
            Assert.True(Placeholders(Strings.Vi[k]).SequenceEqual(Placeholders(Strings.En[k])), "placeholder lech: " + k);
    }

    [Fact]
    public void F_Formats_InBothLanguages()
    {
        Assert.Equal("Tài khoản (3)", Loc.F("Header.Accounts", 3));
        Loc.Set("en");
        Assert.Equal("Accounts (3)", Loc.F("Header.Accounts", 3));
        Assert.Equal("Open X", Loc.F("Tray.Open", "X"));
    }

    [Fact]
    public void Set_UnknownLanguage_FallsBackToVi()
    {
        Loc.Set("en");
        Loc.Set("xx");
        Assert.Equal("vi", Loc.Lang);
        Loc.Set(null);
        Assert.Equal("vi", Loc.Lang);
        Assert.Equal("Bật", Loc.T("Cmd.Start"));
    }

    [Fact]
    public void Set_RaisesChanged_OnlyWhenChanged()
    {
        int n = 0;
        void H() => n++;
        Loc.Changed += H;
        try
        {
            Loc.Set("en"); Assert.Equal(1, n);
            Loc.Set("en"); Assert.Equal(1, n);
            Loc.Set("vi"); Assert.Equal(2, n);
        }
        finally { Loc.Changed -= H; }
    }

    [Fact]
    public void T_MissingKey_ReturnsKey()
    {
        Loc.AssertOnMissing = false;
        Assert.Equal("No.Such.Key", Loc.T("No.Such.Key"));
    }

    [Fact]
    public void T_ByLanguage()
    {
        Assert.Equal("Bật", Loc.T("Cmd.Start"));
        Loc.Set("en");
        Assert.Equal("Start", Loc.T("Cmd.Start"));
    }

    [Fact]
    public void Config_Language_DefaultsAndNormalizes()
    {
        var c = new AppConfig();
        Assert.Equal("vi", c.Language);
        c.Language = "EN"; Assert.Equal("en", c.Language);
        c.Language = "fr"; Assert.Equal("vi", c.Language);
        c.Language = null!; Assert.Equal("vi", c.Language);
    }

    [Fact]
    public void SwitchProgressText_English()
    {
        Loc.Set("en");
        Assert.Equal("Stopping 'X' to restart it", SwitchProgressText.Describe("tat tai khoan dich de khoi dong lai", _ => null, "X"));
        Assert.Equal("Starting the target account", SwitchProgressText.Describe("khoi dong tai khoan dich", _ => null, null));
    }

    [Fact]
    public void Disclaimer_FollowsLanguage()
    {
        Assert.StartsWith("PHẦN MỀM", AppInfo.Disclaimer);
        Loc.Set("en");
        Assert.StartsWith("THE SOFTWARE IS PROVIDED \"AS IS\"", AppInfo.Disclaimer);
    }
}
