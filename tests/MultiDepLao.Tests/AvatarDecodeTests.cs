using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class AvatarDecodeTests
{
    [Theory]
    [InlineData(100, 100, 0, 0)]
    [InlineData(1024, 500, 0, 0)]
    [InlineData(4000, 2000, 1024, 0)]
    [InlineData(2000, 4000, 0, 1024)]
    [InlineData(5000, 5000, 1024, 0)]
    [InlineData(0, 10, 0, 0)]
    public void DecodeSize(int w, int h, int dw, int dh) => Assert.Equal((dw, dh), AvatarMath.DecodeSize(w, h));
}
