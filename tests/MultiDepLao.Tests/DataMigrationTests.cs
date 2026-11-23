using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class DataMigrationTests : IDisposable
{
    private readonly string _t = Path.Combine(Path.GetTempPath(), "mdl-mig-" + Guid.NewGuid().ToString("N"));
    private string NewR => Path.Combine(_t, "new");
    private string OldR => Path.Combine(_t, "old");
    public DataMigrationTests() => Directory.CreateDirectory(_t);
    public void Dispose() { try { Directory.Delete(_t, true); } catch { } }

    [Fact]
    public void Moves_and_writes_marker()
    {
        Directory.CreateDirectory(Path.Combine(OldR, "profiles")); File.WriteAllText(Path.Combine(OldR, "config.json"), "{}");
        var r = DataMigration.Migrate(NewR, OldR);
        Assert.Equal(MigrationOutcome.Migrated, r.Outcome);
        Assert.Equal(NewR, r.Root);
        Assert.False(Directory.Exists(OldR));
        Assert.True(File.Exists(Path.Combine(NewR, "config.json")));
        Assert.True(File.Exists(Path.Combine(NewR, DataMigration.MarkerFile)));
    }

    [Fact]
    public void Both_exist_uses_new_and_leaves_old()
    {
        Directory.CreateDirectory(OldR); Directory.CreateDirectory(NewR);
        var r = DataMigration.Migrate(NewR, OldR);
        Assert.Equal(MigrationOutcome.BothExist, r.Outcome);
        Assert.Equal(NewR, r.Root);
        Assert.True(Directory.Exists(OldR));
    }

    [Fact]
    public void No_old_does_nothing()
    {
        var r = DataMigration.Migrate(NewR, OldR);
        Assert.Equal(MigrationOutcome.NothingToDo, r.Outcome);
        Assert.Equal(NewR, r.Root);
        Assert.False(Directory.Exists(NewR));
    }

    [Fact]
    public void Move_failure_falls_back_to_old()
    {
        Directory.CreateDirectory(OldR);
        var r = DataMigration.Migrate(NewR, OldR, (_, _) => throw new IOException("khoa"));
        Assert.Equal(MigrationOutcome.Failed, r.Outcome);
        Assert.Equal(OldR, r.Root);
        Assert.Equal("khoa", r.Reason);
        Assert.True(Directory.Exists(OldR));
        Assert.False(Directory.Exists(NewR));
    }
}
