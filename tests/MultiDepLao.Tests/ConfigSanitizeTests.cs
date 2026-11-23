using System.Text.Json;
using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class ConfigSanitizeTests
{
    private static string Cfg(string accounts, string extra = "") => "{\"accounts\":[" + accounts + "]" + extra + "}";
    private static string A(string id) => "{\"id\":" + JsonSerializer.Serialize(id) + ",\"displayName\":\"x\"}";

    private static (AppConfig Cfg, ConfigStore Store, TempRoot T) Load(string json)
    {
        var t = new TempRoot();
        var p = t.Paths;
        Directory.CreateDirectory(p.Root);
        File.WriteAllText(p.ConfigFile, json);
        var s = new ConfigStore(p);
        return (s.Load(), s, t);
    }

    [Fact]
    public void InvalidAndDuplicateIds_Dropped_OwnersCleared_BackupWritten()
    {
        var (cfg, store, t) = Load(Cfg(string.Join(",", A("ok-1"), A(@"..\..\evil"), A("a/b"), A(""), A("OK-1"), A("good_2"), A(new string('a', 41))),
            ",\"callOwnerId\":\"..\\\\x\",\"captureOwnerId\":\"gone\""));
        using var _ = t;
        Assert.Equal(new[] { "ok-1", "good_2" }, cfg.Accounts.Select(a => a.Id));
        Assert.Null(cfg.CallOwnerId); Assert.Null(cfg.CaptureOwnerId);
        var r = store.LastSanitize!;
        Assert.Equal(4, r.DroppedInvalidId); Assert.Equal(1, r.DroppedDuplicate); Assert.Equal(2, r.OwnersCleared);
        Assert.True(File.Exists(t.Paths.ConfigFile + ".bad"));
        Assert.Contains("evil", File.ReadAllText(t.Paths.ConfigFile + ".bad"));
    }

    [Fact]
    public void ValidOwners_Kept_NoBackup_NoReport()
    {
        var (cfg, store, t) = Load(Cfg(A("a1") + "," + A("b2"), ",\"callOwnerId\":\"A1\",\"captureOwnerId\":\"b2\""));
        using var _ = t;
        Assert.Equal(2, cfg.Accounts.Count);
        Assert.Equal("b2", cfg.CaptureOwnerId);
        Assert.Null(store.LastSanitize);
        Assert.False(File.Exists(t.Paths.ConfigFile + ".bad"));
    }

    [Fact]
    public void Sanitize_NullEntry_Dropped()
    {
        var cfg = new AppConfig { Accounts = new() { null!, new AccountConfig { Id = "ok" } } };
        var r = ConfigStore.Sanitize(cfg);
        Assert.Single(cfg.Accounts); Assert.Equal(1, r.DroppedInvalidId);
    }
}
