using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class AppLockTests
{
    private sealed class Clock { public DateTime T = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc); }

    private static AppLock Make(TempRoot t, Clock? c = null, ILog? log = null) =>
        new(t.Paths, log, 10, c == null ? null : () => c.T);

    private sealed class CaptureLog : ILog
    {
        public List<string> Lines = new();
        public void Info(string m) => Lines.Add(m);
        public void Warn(string m) => Lines.Add(m);
        public void Error(string m) => Lines.Add(m);
    }

    [Fact]
    public void Set_Verify_Change_Remove()
    {
        using var t = new TempRoot();
        var l = Make(t);
        Assert.False(l.HasPassword);
        Assert.False(l.SetPassword("abc"));          // qua ngan
        Assert.False(l.SetPassword("abcde"));        // 5 < 6
        Assert.True(l.SetPassword("abcdef"));
        Assert.True(l.HasPassword);
        Assert.False(l.SetPassword("other"));        // da co
        Assert.True(l.Verify("abcdef"));
        Assert.False(l.Verify("abce"));
        Assert.False(l.ChangePassword("sai", "newpw1"));
        Assert.False(l.ChangePassword("abcdef", "ab"));
        Assert.True(l.ChangePassword("abcdef", "newpw1"));
        Assert.False(l.Verify("abcdef"));
        Assert.True(l.Verify("newpw1"));
        Assert.False(l.RemovePassword("abcdef"));
        Assert.True(l.RemovePassword("newpw1"));
        Assert.False(l.HasPassword);
        Assert.False(File.Exists(t.Paths.LockFile));
    }

    [Fact]
    public void Persists_Across_Instances_And_NoPlaintext()
    {
        using var t = new TempRoot();
        Make(t).SetPassword("matkhau-bi-mat");
        Assert.DoesNotContain("matkhau-bi-mat", File.ReadAllText(t.Paths.LockFile));
        Assert.True(Make(t).Verify("matkhau-bi-mat"));
    }

    [Fact]
    public void CorruptFile_TreatedAsNoPassword_WithWarning()
    {
        using var t = new TempRoot();
        Directory.CreateDirectory(t.Paths.Root);
        File.WriteAllText(t.Paths.LockFile, "{ khong phai json");
        var log = new CaptureLog();
        var l = Make(t, null, log);
        Assert.False(l.HasPassword);
        Assert.True(l.Verify("bat ky"));
        Assert.Contains(log.Lines, s => s.Contains("app.lock"));
        Assert.True(l.SetPassword("abcdef"));          // ghi de file hong
        Assert.True(l.HasPassword);
    }

    [Fact]
    public void FiveFailures_Block30s_ThenRetry()
    {
        using var t = new TempRoot();
        var c = new Clock();
        var l = Make(t, c);
        l.SetPassword("abcdef");
        for (int i = 0; i < 4; i++) { Assert.False(l.Verify("x")); Assert.Null(l.RetryAfter); }
        Assert.False(l.Verify("x"));
        Assert.NotNull(l.RetryAfter);
        Assert.Equal(30, l.RetryAfter!.Value.TotalSeconds, 0.5);
        Assert.False(l.Verify("abcdef"));              // dung nhung dang bi chan
        c.T += TimeSpan.FromSeconds(31);
        Assert.Null(l.RetryAfter);
        Assert.False(l.Verify("x"));                 // moi lan sai tiep theo lai 30 giay
        Assert.NotNull(l.RetryAfter);
        c.T += TimeSpan.FromSeconds(31);
        Assert.True(l.Verify("abcdef"));
        Assert.Null(l.RetryAfter);
    }

    [Fact]
    public void SamePassword_DifferentSalt()
    {
        using var t1 = new TempRoot(); using var t2 = new TempRoot();
        Make(t1).SetPassword("abcdef"); Make(t2).SetPassword("abcdef");
        Assert.NotEqual(File.ReadAllText(t1.Paths.LockFile), File.ReadAllText(t2.Paths.LockFile));
    }

    [Fact]
    public void OldConfig_WithoutLockOnMinimize_Loads()
    {
        using var t = new TempRoot();
        var p = t.Paths;
        Directory.CreateDirectory(p.Root);
        File.WriteAllText(p.ConfigFile, "{\"accounts\":[],\"lock\":{\"enabled\":true,\"idleMinutes\":5,\"lockOnStart\":false}}");
        var cfg = new ConfigStore(p).Load();
        Assert.True(cfg.Lock.Enabled);
        Assert.Equal(5, cfg.Lock.IdleMinutes);
        Assert.False(cfg.Lock.LockOnStart);
        Assert.False(cfg.Lock.LockOnMinimize);
    }
}
