using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class AvatarTests
{
    [Theory]
    [InlineData(400, 200, 100, 0, 200)]
    [InlineData(200, 400, 0, 100, 200)]
    [InlineData(300, 300, 0, 0, 300)]
    [InlineData(5, 4, 0, 0, 4)]
    public void CenterSquare_Ok(int w, int h, int x, int y, int s)
        => Assert.Equal((x, y, s), AvatarMath.CenterSquare(w, h));

    [Fact]
    public void CenterSquare_Invalid() => Assert.Equal((0, 0, 0), AvatarMath.CenterSquare(0, 10));

    [Fact]
    public void AvatarFile_OutsideProfiles()
    {
        var p = new AppPaths(Path.Combine(Path.GetTempPath(), "mz-avatar-test"));
        var f = p.AvatarFile("acc-01");
        Assert.EndsWith(Path.Combine("avatars", "acc-01.png"), f);
        Assert.False(AppPaths.IsUnder(f, p.ProfilesRoot));
        Assert.Throws<ArgumentException>(() => p.AvatarFile("..\\x"));
    }
}
