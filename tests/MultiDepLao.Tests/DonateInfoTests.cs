using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class DonateInfoTests
{
    [Fact]
    public void FormatAccount_GroupsByFour() => Assert.Equal("7820 7088 88", AppInfo.FormatAccount(AppInfo.DonateAccount));

    [Theory]
    [InlineData("", "")]
    [InlineData("123", "123")]
    [InlineData("12345", "1234 5")]
    public void FormatAccount_Edge(string input, string expected) => Assert.Equal(expected, AppInfo.FormatAccount(input));
}
