using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class PathsAndEnvTests
{
    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("..\\..\\x")]
    [InlineData("a b")]
    public void ProfileDir_RejectsBadIds(string id)
    {
        var p = new AppPaths(Path.Combine(Path.GetTempPath(), "mz-x"));
        Assert.Throws<ArgumentException>(() => p.ProfileDir(id));
        Assert.Throws<ArgumentException>(() => p.PidFile(id));
    }

    [Fact]
    public void ProfileDir_Rejects41Chars_Accepts40()
    {
        var p = new AppPaths(Path.Combine(Path.GetTempPath(), "mz-x"));
        Assert.Throws<ArgumentException>(() => p.ProfileDir(new string('a', 41)));
        Assert.StartsWith(p.ProfilesRoot, p.ProfileDir(new string('a', 40)));
        Assert.EndsWith("mz.pid", p.PidFile("acc-01"));
    }

    [Fact]
    public void ChildEnvironment_StripsGroupsAndSetsPaths()
    {
        var cur = new Dictionary<string, string>
        {
            ["ELECTRON_RUN_AS_NODE"] = "1", ["vscode_pid"] = "5", ["NODE_OPTIONS"] = "x", ["Chrome_Foo"] = "1",
            ["NODEJS_KEEP"] = "keep", ["PATH"] = "p", ["USERPROFILE"] = "C:\\real", ["TEMP"] = "C:\\real\\tmp",
        };
        var env = ChildEnvironment.Build(cur, @"C:\d\profiles\acc-01");
        Assert.False(env.ContainsKey("ELECTRON_RUN_AS_NODE"));
        Assert.False(env.ContainsKey("VSCODE_PID"));
        Assert.False(env.ContainsKey("NODE_OPTIONS"));
        Assert.False(env.ContainsKey("CHROME_FOO"));
        Assert.Equal("keep", env["NODEJS_KEEP"]);   // NODEJS khong bat dau bang NODE_
        Assert.Equal("p", env["PATH"]);
        Assert.Equal(@"C:\d\profiles\acc-01", env["USERPROFILE"]);
        Assert.Equal(@"C:\d\profiles\acc-01\AppData\Roaming", env["APPDATA"]);
        Assert.Equal(@"C:\d\profiles\acc-01\AppData\Local", env["LOCALAPPDATA"]);
        Assert.Equal(@"C:\d\profiles\acc-01\AppData\Local\Temp", env["TEMP"]);
        Assert.Equal(env["TEMP"], env["TMP"]);
    }
}

public class ConfigAndProfileTests
{
    [Fact]
    public void Config_RoundTrip()
    {
        using var t = new TempRoot(); var paths = t.Paths; var s = new ConfigStore(paths);
        var cfg = new AppConfig { CallOwnerId = "acc-01", MaxRunning = 3 };
        cfg.Accounts.Add(new AccountConfig { Id = "acc-01", DisplayName = "Tôi", Order = 2, Pinned = true, AutoStart = true });
        cfg.Lock.Enabled = true; cfg.Lock.IdleMinutes = 7;
        s.Save(cfg);
        Assert.Empty(Directory.GetFiles(paths.Root, "*.tmp"));
        var back = s.Load();
        Assert.Equal("acc-01", back.CallOwnerId); Assert.Equal(3, back.MaxRunning);
        Assert.Equal("Tôi", back.Accounts[0].DisplayName); Assert.True(back.Accounts[0].Pinned);
        Assert.True(back.Lock.Enabled); Assert.Equal(7, back.Lock.IdleMinutes); Assert.True(back.Lock.LockOnStart);
    }

    [Fact]
    public void Config_ConcurrentSaves_NoError_NoLeftoverTmp_FileValid()
    {
        // Truoc day: nhieu lan Save chong nhau dung chung "config.json.tmp" -> UnauthorizedAccessException / mat file tam.
        using var t = new TempRoot(); var paths = t.Paths;
        Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            new ConfigStore(paths).Save(new AppConfig { MaxRunning = i % 9 + 1 }));
        Assert.Empty(Directory.GetFiles(paths.Root, "*.tmp"));
        Assert.InRange(new ConfigStore(paths).Load().MaxRunning ?? 0, 1, 9);
    }

    [Fact]
    public void Config_Missing_IsDefaultAndNoWrite()
    {
        using var t = new TempRoot(); var paths = t.Paths;
        var cfg = new ConfigStore(paths).Load();
        Assert.Empty(cfg.Accounts); Assert.Equal(10, cfg.Lock.IdleMinutes); Assert.Null(cfg.MaxRunning);
        Assert.False(Directory.Exists(paths.Root));
    }

    [Fact]
    public void Config_Corrupt_BackedUpAndDefault()
    {
        using var t = new TempRoot(); var paths = t.Paths;
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.ConfigFile, "{ not json");
        var cfg = new ConfigStore(paths).Load();
        Assert.Empty(cfg.Accounts);
        Assert.True(File.Exists(paths.ConfigFile + ".bad"));
        Assert.Equal("{ not json", File.ReadAllText(paths.ConfigFile + ".bad"));
    }

    private static ProfileStore Store(AppPaths p, Func<string, bool>? running = null) => new(p, new ConfigStore(p), running ?? (_ => false));

    [Fact]
    public void Add_GeneratesUniqueIds_SkippingExistingDirs()
    {
        using var t = new TempRoot(); var p = t.Paths;
        Directory.CreateDirectory(p.ProfileDir("acc-01"));    // thu muc co san chua trong config
        var s = Store(p);
        var a = s.Add("A"); var b = s.Add("B");
        Assert.Equal("acc-02", a.Id); Assert.Equal("acc-03", b.Id);
        Assert.True(Directory.Exists(p.ProfileDir("acc-03")));
        Assert.Equal(2, new ConfigStore(p).Load().Accounts.Count);
    }

    [Fact]
    public void RenamePinReorder_Persist()
    {
        using var t = new TempRoot(); var p = t.Paths; var s = Store(p);
        var a = s.Add("A"); var b = s.Add("B");
        s.Rename(a.Id, "AA"); s.SetPinned(b.Id, true); s.Reorder(new[] { b.Id, a.Id });
        var back = Store(p).Accounts;
        Assert.Equal(new[] { b.Id, a.Id }, back.Select(x => x.Id));
        Assert.Equal("AA", back[1].DisplayName); Assert.True(back[0].Pinned);
    }

    [Fact]
    public void OldConfig_WithoutNewFields_LoadsWithDefaults()
    {
        using var t = new TempRoot(); var paths = t.Paths;
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.ConfigFile, "{\"accounts\":[{\"id\":\"acc-01\",\"displayName\":\"A\",\"order\":0,\"pinned\":true,\"autoStart\":false}],\"lock\":{\"enabled\":false}}");
        var cfg = new ConfigStore(paths).Load();
        Assert.Single(cfg.Accounts);
        Assert.Null(cfg.Accounts[0].Color); Assert.True(cfg.Accounts[0].Pinned);
        Assert.False(cfg.SidebarCollapsed);
        Assert.Null(cfg.SidebarWidth);
        Assert.Equal(240, cfg.EffectiveSidebarWidth);
    }

    [Fact]
    public void Theme_DefaultsToSystem_PersistsAndNormalizes()
    {
        using var t = new TempRoot(); var p = t.Paths; var s = Store(p);
        Assert.Equal("system", new AppConfig().Theme);
        s.SetTheme("dark");
        Assert.Equal("dark", new ConfigStore(p).Load().Theme);
        s.SetTheme("Light");
        Assert.Equal("light", new ConfigStore(p).Load().Theme);
        s.SetTheme("neon");
        Assert.Equal("system", new ConfigStore(p).Load().Theme);
        File.WriteAllText(p.ConfigFile, "{\"theme\":\"bogus\"}");
        Assert.Equal("system", new ConfigStore(p).Load().Theme);
    }

    [Fact]
    public void Accent_DefaultsToTeal_PersistsAndNormalizes()
    {
        using var t = new TempRoot(); var p = t.Paths; var s = Store(p);
        Assert.Equal("teal", new AppConfig().Accent);
        s.SetAccent("Violet");
        Assert.Equal("violet", new ConfigStore(p).Load().Accent);
        s.SetAccent("blue");
        Assert.Equal("blue", new ConfigStore(p).Load().Accent);
        s.SetAccent("neon");
        Assert.Equal("teal", new ConfigStore(p).Load().Accent);
        File.WriteAllText(p.ConfigFile, "{\"accent\":\"bogus\"}");
        Assert.Equal("teal", new ConfigStore(p).Load().Accent);
        File.WriteAllText(p.ConfigFile, "{\"accent\":null}");
        Assert.Equal("teal", new ConfigStore(p).Load().Accent);
    }

    [Fact]
    public void SidebarWidth_PersistsAndClamps()
    {
        using var t = new TempRoot(); var p = t.Paths; var s = Store(p);
        s.SetSidebarWidth(300);
        Assert.Equal(300, new ConfigStore(p).Load().SidebarWidth);
        s.SetSidebarWidth(50);
        Assert.Equal(180, new ConfigStore(p).Load().SidebarWidth);
        s.SetSidebarWidth(9999);
        Assert.Equal(420, new ConfigStore(p).Load().SidebarWidth);
        Assert.Equal(240, AppConfig.ClampSidebarWidth(double.NaN));
        Assert.Equal(420, new AppConfig { SidebarWidth = 1000 }.EffectiveSidebarWidth);
    }

    [Fact]
    public void ColorAndSidebarCollapsed_Persist_InvalidColorRejected()
    {
        using var t = new TempRoot(); var p = t.Paths; var s = Store(p);
        var a = s.Add("A");
        s.SetColor(a.Id, "#1e88e5"); s.SetSidebarCollapsed(true);
        var back = new ConfigStore(p).Load();
        Assert.Equal("#1E88E5", back.Accounts[0].Color); Assert.True(back.SidebarCollapsed);
        Assert.Throws<ArgumentException>(() => s.SetColor(a.Id, "red"));
        s.SetColor(a.Id, null);
        Assert.Null(new ConfigStore(p).Load().Accounts[0].Color);
    }

    [Fact]
    public void Reorder_UnknownAndMissingIds()
    {
        using var t = new TempRoot(); var p = t.Paths; var s = Store(p);
        var a = s.Add("A"); var b = s.Add("B"); var c = s.Add("C");
        s.Reorder(new[] { c.Id });   // id thieu giu nguyen thu tu o cuoi
        Assert.Equal(new[] { c.Id, a.Id, b.Id }, Store(p).Accounts.Select(x => x.Id));
        Assert.Throws<KeyNotFoundException>(() => s.Reorder(new[] { "zzz" }));
    }

    [Fact]
    public void AccountPalette_StableColorAndInitial()
    {
        Assert.Equal(AccountPalette.DefaultFor("acc-01"), AccountPalette.DefaultFor("ACC-01"));
        Assert.Contains(AccountPalette.DefaultFor("acc-07"), AccountPalette.Colors);
        Assert.Equal(10, AccountPalette.Colors.Length);
        Assert.Equal("#112233", AccountPalette.Effective("x", "#112233"));
        Assert.Equal(AccountPalette.DefaultFor("x"), AccountPalette.Effective("x", "bad"));
        Assert.Equal("E", AccountPalette.Initial("  Édith"));
        Assert.Equal("D", AccountPalette.Initial("đạt"));
        Assert.Equal("A", AccountPalette.Initial("Ánh"));
        Assert.Equal("?", AccountPalette.Initial("  "));
        Assert.Equal("5", AccountPalette.Initial("-5"));
    }

    [Fact]
    public void Delete_RefusesWhenRunning_RemovesWhenStopped()
    {
        using var t = new TempRoot(); var p = t.Paths; bool running = true;
        var s = Store(p, _ => running);
        var a = s.Add("A");
        File.WriteAllText(Path.Combine(p.ProfileDir(a.Id), "x.txt"), "x");
        Assert.Throws<InvalidOperationException>(() => s.Delete(a.Id));
        Assert.True(Directory.Exists(p.ProfileDir(a.Id)));
        running = false;
        s.Delete(a.Id);
        Assert.False(Directory.Exists(p.ProfileDir(a.Id)));
        Assert.Empty(s.Accounts);
        Assert.Throws<ArgumentException>(() => s.Delete(".."));
    }

    [Fact]
    public void ImportExisting_PicksUpUnknownDirs()
    {
        using var t = new TempRoot(); var p = t.Paths;
        foreach (var n in new[] { "spike-e", "spike-f", "acc-01", "bad name" }) Directory.CreateDirectory(Path.Combine(p.ProfilesRoot, n));
        File.WriteAllText(Path.Combine(p.ProfilesRoot, "call-owner.txt"), "x");
        var s = Store(p);
        Assert.Equal(3, s.Discover().Count);
        Assert.False(File.Exists(p.ConfigFile));   // Discover chi doc
        var added = s.ImportExisting();
        Assert.Equal(new[] { "acc-01", "spike-e", "spike-f" }, added.Select(x => x.Id));
        Assert.Empty(s.ImportExisting());
        Assert.Equal(3, new ConfigStore(p).Load().Accounts.Count);
    }
}
