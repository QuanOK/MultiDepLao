using MultiDepLao.Core;

namespace MultiDepLao.Tests;

[Collection("Loc")]
public sealed class DataRootTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "mdl-dr-" + Guid.NewGuid().ToString("N"));

    public DataRootTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    [Fact]
    public void Portable_UsesDataDir_AndLeavesNoProbe()
    {
        var exe = Path.Combine(_tmp, "app"); Directory.CreateDirectory(exe);
        File.WriteAllText(Path.Combine(exe, DataRoot.PortableMarker), "");
        var r = DataRoot.Resolve(exe, Path.Combine(_tmp, "local"));
        Assert.True(r.IsPortable);
        Assert.Null(r.Error);
        Assert.Equal(Path.Combine(exe, "data"), r.Root);
        Assert.True(Directory.Exists(r.Root));
        Assert.Empty(Directory.GetFileSystemEntries(r.Root));
        Assert.False(DataRoot.ShouldMigrate(r));
    }

    [Fact]
    public void NotPortable_UsesLocalRoot()
    {
        var exe = Path.Combine(_tmp, "app"); Directory.CreateDirectory(exe);
        var local = Path.Combine(_tmp, "local");
        var r = DataRoot.Resolve(exe, local);
        Assert.False(r.IsPortable);
        Assert.Null(r.Error);
        Assert.Equal(Path.Combine(local, "MultiDepLao"), r.Root);
        Assert.False(Directory.Exists(r.Root));
        Assert.True(DataRoot.ShouldMigrate(r));
    }

    [Fact]
    public void Portable_NotWritable_ReturnsErrorWithPath()
    {
        var exe = Path.Combine(_tmp, "app"); Directory.CreateDirectory(exe);
        File.WriteAllText(Path.Combine(exe, DataRoot.PortableMarker), "");
        var r = DataRoot.Resolve(exe, null, _ => throw new UnauthorizedAccessException("deny"));
        Assert.NotNull(r.Error);
        Assert.Contains(Path.Combine(exe, "data"), r.Error);
        Assert.Contains("Program Files", r.Error);
    }
}
