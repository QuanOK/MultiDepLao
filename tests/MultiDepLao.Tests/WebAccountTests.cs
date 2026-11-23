using MultiDepLao.Core;

namespace MultiDepLao.Tests;

[Xunit.Collection("Loc")]
public class WebAccountTests
{
    private sealed class NullCtl : IInstanceControl, ICallPipeProbe
    {
        public int Starts;
        public Task<InstanceStatus> StartAsync(string id, CancellationToken ct = default) { Starts++; return Task.FromResult(new InstanceStatus(id, InstanceState.Starting, 1, null)); }
        public Task<StopResult> StopAsync(string id, CancellationToken ct = default) => Task.FromResult(new StopResult(StopOutcome.AlreadyStopped, null));
        public bool IsRunning(string id) => false;
        public IReadOnlyList<string> RunningOldestFirst() => Array.Empty<string>();
        public bool AnyCallPipe() => false;
        public Task<bool> WaitAsync(bool present, TimeSpan timeout, CancellationToken ct) => Task.FromResult(!present);
    }

    private static ProfileStore Store(TempRoot t) => new(t.Paths, new ConfigStore(t.Paths), _ => false);

    // ---- cau hinh
    [Fact]
    public void Config_KindWrittenAsString_AndRoundTrips()
    {
        using var t = new TempRoot();
        var cs = new ConfigStore(t.Paths);
        var cfg = new AppConfig();
        cfg.Accounts.Add(new AccountConfig { Id = "acc-01", Kind = AccountKind.Web });
        cfg.Accounts.Add(new AccountConfig { Id = "acc-02" });
        cs.Save(cfg);
        var text = File.ReadAllText(t.Paths.ConfigFile);
        Assert.Contains("\"kind\": \"Web\"", text);
        Assert.Contains("\"kind\": \"Pc\"", text);
        var back = cs.Load();
        Assert.Equal(AccountKind.Web, back.Accounts[0].Kind);
        Assert.Equal(AccountKind.Pc, back.Accounts[1].Kind);
    }

    [Fact]
    public void Config_OldFileWithoutKind_IsPc()
    {
        using var t = new TempRoot();
        Directory.CreateDirectory(t.Paths.Root);
        File.WriteAllText(t.Paths.ConfigFile, "{\"accounts\":[{\"id\":\"acc-01\",\"displayName\":\"A\"}]}");
        Assert.Equal(AccountKind.Pc, new ConfigStore(t.Paths).Load().Accounts[0].Kind);
    }

    // ---- ProfileStore
    [Fact]
    public void Add_Web_NoProfileDir_ButIdUnique()
    {
        using var t = new TempRoot();
        var s = Store(t);
        var w = s.Add("W", AccountKind.Web);
        var p = s.Add("P");
        Assert.Equal(AccountKind.Web, w.Kind);
        Assert.False(Directory.Exists(t.Paths.ProfileDir(w.Id)));
        Assert.True(Directory.Exists(t.Paths.ProfileDir(p.Id)));
        Assert.NotEqual(w.Id, p.Id);
        Assert.True(s.IsWeb(w.Id)); Assert.False(s.IsWeb(p.Id)); Assert.True(s.IsPc(p.Id));
        Assert.Equal(new[] { p.Id }, s.PcAccounts.Select(a => a.Id));
    }

    [Fact]
    public void Add_SkipsIdWithLeftoverWebView2Profile()
    {
        using var t = new TempRoot();
        Directory.CreateDirectory(Path.Combine(t.Paths.WebDataDir, "EBWebView", "acc-01"));
        Assert.Equal("acc-02", Store(t).Add("x", AccountKind.Web).Id);
    }

    [Fact]
    public void WebAccount_CannotBeCallOrCaptureOwner()
    {
        using var t = new TempRoot();
        var s = Store(t);
        var w = s.Add("W", AccountKind.Web);
        Assert.Throws<ArgumentException>(() => s.SetCallOwner(w.Id));
        Assert.Throws<ArgumentException>(() => s.SetCaptureOwner(w.Id));
        s.SetCallOwner(null);
    }

    [Fact]
    public void Delete_Web_RemovesConfigOnly()
    {
        using var t = new TempRoot();
        var s = Store(t);
        var w = s.Add("W", AccountKind.Web);
        s.Delete(w.Id);
        Assert.Empty(s.Accounts);
    }

    [Fact]
    public void WebDataDir_UnderRoot()
    {
        using var t = new TempRoot();
        Assert.Equal(Path.Combine(t.Paths.Root, "web"), t.Paths.WebDataDir);
    }

    // ---- khong bao gio khoi chay Zalo.exe cho Web
    [Fact]
    public async Task CallOwnerService_NeverStartsWeb()
    {
        using var t = new TempRoot();
        var s = Store(t);
        var w = s.Add("W", AccountKind.Web);
        s.SetAutoStart(w.Id, true);
        var ctl = new NullCtl();
        var svc = new CallOwnerService(ctl, s, ctl);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.StartAsync(w.Id));
        await svc.StartAutoStartAccountsAsync();
        Assert.Empty(svc.IdsThatWillStart(w.Id));
        var r = await svc.SwitchToAsync(w.Id);
        Assert.False(r.Success);
        Assert.Equal(0, ctl.Starts);
    }

    [Fact]
    public async Task CallOwnerService_StartsPcOwnerOnly_NotWeb()
    {
        using var t = new TempRoot();
        var s = Store(t);
        var w = s.Add("W", AccountKind.Web);
        s.SetAutoStart(w.Id, true);
        var p = s.Add("P"); s.SetAutoStart(p.Id, true);
        var ctl = new NullCtl();
        var svc = new CallOwnerService(ctl, s, ctl, new CallOwnerOptions { PipeAppearTimeout = TimeSpan.FromMilliseconds(20) });
        await svc.StartAutoStartAccountsAsync();
        Assert.Equal(1, ctl.Starts);
    }

    [Fact]
    public void Strings_NewKeysInBothLanguages()
    {
        foreach (var k in new[] { "Kind.Pc", "Kind.Web", "Kind.PcDesc", "Kind.WebDesc", "Web.InstallAsk", "Web.NewMessage", "Web.InstallFail" })
        {
            Assert.True(Strings.Vi.ContainsKey(k) && Strings.En.ContainsKey(k));
        }
        Assert.Contains("{0}", Strings.Vi["Web.NewMessage"]);
    }

    // ---- WebView2Runtime (phan thuan)
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("0.0.0.0", null)]
    [InlineData("130.0.2849.80", "130.0.2849.80")]
    public void NormalizeVersion(string? pv, string? expected) => Assert.Equal(expected, WebView2Runtime.NormalizeVersion(pv));

    [Fact]
    public void FirstValid_PicksFirstGood() =>
        Assert.Equal("1.2.3.4", WebView2Runtime.FirstValid(new string?[] { null, "0.0.0.0", "1.2.3.4", "9.9" }));

    [Fact]
    public void FirstValid_NoneIsNull() => Assert.Null(WebView2Runtime.FirstValid(new string?[] { null, "" }));

    [Fact]
    public void Decide_Cases()
    {
        Assert.Equal(InstallFailure.Timeout, WebView2Runtime.Decide(true, 0, "1.0").Failure);
        Assert.Equal(InstallFailure.ExitCode, WebView2Runtime.Decide(false, 5, "1.0").Failure);
        Assert.Equal(InstallFailure.NotDetected, WebView2Runtime.Decide(false, 0, "0.0.0.0").Failure);
        var ok = WebView2Runtime.Decide(false, 0, "130.1");
        Assert.True(ok.Success); Assert.Equal("130.1", ok.Detail);
    }

    [Theory]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", true)]
    [InlineData("O=\"Microsoft Corporation\", C=US", true)]
    [InlineData("CN=Microsoft Corporation, O=Evil Corp", false)]
    [InlineData("CN=Evil, O=Microsoft Corporation Fake", false)]
    [InlineData("CN=Microsoft Corporation", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsMicrosoftSubject(string? subject, bool expected) => Assert.Equal(expected, WebView2Runtime.IsMicrosoftSubject(subject));

    [Fact]
    public async Task Install_DownloadFailure_IsNoNetwork_AndCleansUp()
    {
        using var t = new TempRoot();
        var r = await WebView2Runtime.InstallAsync(t.Paths, null, default,
            (_, _) => throw new HttpRequestException("x"), _ => true);
        Assert.Equal(InstallFailure.NoNetwork, r.Failure);
        Assert.Empty(Directory.GetFiles(t.Paths.TmpDir));
    }

    [Fact]
    public async Task Install_BadSignature_NotExecuted_AndDeleted()
    {
        using var t = new TempRoot();
        var r = await WebView2Runtime.InstallAsync(t.Paths, null, default,
            (f, _) => { File.WriteAllText(f, "not an exe"); return Task.CompletedTask; }, _ => false);
        Assert.Equal(InstallFailure.BadSignature, r.Failure);
        Assert.Empty(Directory.GetFiles(t.Paths.TmpDir));
    }

    [Fact]
    public void VerifySignature_NonSignedFile_False()
    {
        using var t = new TempRoot();
        var f = t.Sub("x.exe"); File.WriteAllText(f, "hello");
        Assert.False(WebView2Runtime.VerifyMicrosoftSignature(f));
    }


    // ---- FaviconUnreadDetector (do that 2026-11-19, ban thu w3)
    private const string Plain = "https://chat.zalo.me/favicon-96x96.v1.png";
    private const string PlainCdn = "https://zalo-chat-static.zadn.vn/v1/favicon-96x96.v1.png";
    private const string Badge1 = "data:image/png;base64,AAAA1";
    private const string Badge2 = "data:image/png;base64,BBBB2";

    private static (FaviconUnreadDetector d, List<string> ev) NewFav()
    {
        var d = new FaviconUnreadDetector(); var ev = new List<string>();
        d.NewMessage += () => ev.Add("new");
        d.UnreadChanged += h => ev.Add(h ? "unread" : "read");
        return (d, ev);
    }

    [Fact]
    public void Favicon_PlainFiles_NoUnread()
    {
        var (d, ev) = NewFav();
        d.OnFavicon(Plain); d.OnFavicon(PlainCdn); d.OnFavicon(Plain);
        Assert.False(d.HasUnread); Assert.Empty(ev);
    }

    [Fact]
    public void Favicon_BadgeThenNewBadge_TwoNewMessages_ThenReadClears()
    {
        var (d, ev) = NewFav();
        d.OnFavicon(PlainCdn);
        d.OnFavicon(Badge1);
        Assert.True(d.HasUnread);
        d.OnFavicon(Badge2);
        d.OnFavicon(PlainCdn);
        Assert.False(d.HasUnread);
        Assert.Equal(new[] { "unread", "new", "new", "read" }, ev);
    }

    [Fact]
    public void Favicon_SameBadgeRepeated_NotNew()
    {
        var (d, ev) = NewFav();
        d.OnFavicon(Badge1); d.OnFavicon(Badge1);
        Assert.Equal(new[] { "unread", "new" }, ev);
    }

    [Fact]
    public void Favicon_NullOrEmpty_IsNotBadge()
    {
        Assert.False(FaviconUnreadDetector.IsBadge(null));
        Assert.False(FaviconUnreadDetector.IsBadge(""));
        Assert.True(FaviconUnreadDetector.IsBadge("DATA:image/png;base64,x"));
    }

    // Anh favicon that cua Zalo Web (thu 2026-11-19, ban w6): ten file = ma bam cua dia chi data: tuong ung.
    private static string FavUri(string hash) =>
        "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "zalo-web-favicons", hash + ".png")));

    [Theory]
    [InlineData("eb03ac", 1)] [InlineData("8d5fdc", 1)] [InlineData("350909", 2)] [InlineData("16d2d9", 3)]
    [InlineData("d7c078", 4)] [InlineData("d5f9c5", 5)] [InlineData("61e5e6", FaviconUnreadDetector.MoreThanFive)] [InlineData("9b6008", FaviconUnreadDetector.MoreThanFive)]
    public void Favicon_RealZaloBadges_CountKnown(string hash, int count)
    {
        var u = FavUri(hash);
        Assert.Equal(hash, FaviconUnreadDetector.ShortHash(u));
        Assert.Equal(count, FaviconUnreadDetector.CountOf(u));
    }

    [Fact]
    public void Favicon_UnknownBadge_CountNull_PlainNull()
    {
        Assert.Null(FaviconUnreadDetector.CountOf("data:image/png;base64,QUJD"));
        Assert.Null(FaviconUnreadDetector.CountOf(Plain));
    }

    [Fact]
    public void Favicon_CountDecrease_IsNotNewMessage()
    {
        var (d, ev) = NewFav();
        d.OnFavicon(FavUri("d5f9c5"));   // 5
        d.OnFavicon(FavUri("16d2d9"));   // 3: doc bot o dien thoai
        Assert.Equal(new[] { "unread", "new" }, ev);
        Assert.Equal(3, d.Count);
        d.OnFavicon(FavUri("d7c078"));   // 4: tin moi
        Assert.Equal(new[] { "unread", "new", "new" }, ev);
    }}
