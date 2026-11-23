using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class WebAccountLogicTests
{
    private static readonly string[] Internal = { "zalo.me" };

    [Theory]
    [InlineData("https://chat.zalo.me/x", true, WebLinkAction.Default)]
    [InlineData("https://zalo.me/abc", true, WebLinkAction.Default)]
    [InlineData("https://CHAT.ZALO.ME/", false, WebLinkAction.Default)]
    [InlineData("about:blank", false, WebLinkAction.Default)]
    [InlineData("https://example.com/a", true, WebLinkAction.OpenExternal)]
    [InlineData("http://example.com/a", true, WebLinkAction.OpenExternal)]
    [InlineData("https://example.com/a", false, WebLinkAction.Block)]       // khong do nguoi dung bam: chan
    [InlineData("https://evilzalo.me/", true, WebLinkAction.OpenExternal)]  // khong phai mien con
    [InlineData("https://zalo.me.evil.com/", true, WebLinkAction.OpenExternal)]
    [InlineData("file:///C:/Windows/notepad.exe", true, WebLinkAction.Block)]
    [InlineData("javascript:alert(1)", true, WebLinkAction.Block)]
    [InlineData("zalo://open", true, WebLinkAction.Block)]
    [InlineData("", true, WebLinkAction.Block)]
    [InlineData(null, true, WebLinkAction.Block)]
    [InlineData("khong-phai-uri", true, WebLinkAction.Block)]
    public void ClassifyNewWindow(string? uri, bool user, WebLinkAction expected)
        => Assert.Equal(expected, WebAccountLogic.ClassifyNewWindow(uri, Internal, user));

    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, true, false, false, false)]   // khong phai tai khoan dang chon
    [InlineData(true, false, false, false, false)]   // chua co view / dang loi
    [InlineData(true, true, true, false, false)]     // khoa app: tuyet doi an
    [InlineData(true, true, false, true, false)]     // thu nho / khay
    [InlineData(true, true, true, true, false)]
    public void ShouldShow(bool sel, bool view, bool locked, bool hidden, bool expected)
        => Assert.Equal(expected, WebAccountLogic.ShouldShow(sel, view, locked, hidden));

    [Fact]
    public void SplitRam_OwnPlusEvenShare_SumsToTotal()
    {
        var ids = new[] { "a", "b", "c" };
        var own = new Dictionary<string, long> { ["a"] = 300, ["b"] = 100 };
        var r = WebAccountLogic.SplitRam(ids, own, 1000);   // chung = 1000 - 400 = 600 -> 200/tai khoan
        Assert.Equal(500, r["a"]);
        Assert.Equal(300, r["b"]);
        Assert.Equal(200, r["c"]);
        Assert.Equal(1000, r.Values.Sum());
    }

    [Fact]
    public void SplitRam_NoOwnData_SplitsEvenly_RemainderToFirst()
    {
        var r = WebAccountLogic.SplitRam(new[] { "a", "b", "c" }, new Dictionary<string, long>(), 1000);
        Assert.Equal(334, r["a"]);
        Assert.Equal(333, r["b"]);
        Assert.Equal(333, r["c"]);
    }

    [Fact]
    public void SplitRam_OwnLargerThanTotal_NoNegativeShare()
    {
        var r = WebAccountLogic.SplitRam(new[] { "a" }, new Dictionary<string, long> { ["a"] = 900 }, 500);
        Assert.Equal(900, r["a"]);
    }

    [Fact]
    public void SplitRam_Empty_ReturnsEmpty() =>
        Assert.Empty(WebAccountLogic.SplitRam(Array.Empty<string>(), new Dictionary<string, long>(), 123));

    [Fact]
    public void SplitRam_IgnoresOwnOfOtherAccounts()
    {
        // renderer cua tai khoan da tat khong duoc tinh vao phan cua tai khoan dang chay
        var r = WebAccountLogic.SplitRam(new[] { "a" }, new Dictionary<string, long> { ["x"] = 400 }, 1000);
        Assert.Equal(1000, r["a"]);
    }

    [Theory]
    [InlineData(InstallFailure.NoNetwork, "Web.FailNoNetwork")]
    [InlineData(InstallFailure.BadSignature, "Web.FailSignature")]
    [InlineData(InstallFailure.ExitCode, "Web.FailExit")]
    [InlineData(InstallFailure.Timeout, "Web.FailTimeout")]
    [InlineData(InstallFailure.NotDetected, "Web.FailNotDetected")]
    [InlineData(InstallFailure.Other, "Web.FailOther")]
    public void InstallFailureKey_ExistsInStrings(InstallFailure f, string key)
    {
        Assert.Equal(key, WebAccountLogic.InstallFailureKey(f));
        Assert.NotEqual(key, Loc.T(key));   // chuoi co that, khong tra lai khoa
    }

    [Theory]
    [InlineData("ma thoat 5", "5")]
    [InlineData("ma thoat -2147219416", "-2147219416")]
    [InlineData(null, "?")]
    [InlineData("khong co so", "?")]
    public void ExitCodeOf(string? detail, string expected) => Assert.Equal(expected, WebAccountLogic.ExitCodeOf(detail));

    [Fact]
    public void WebStrings_KeysPresent()
    {
        foreach (var k in new[] { "Web.NoRuntime", "Web.InitFail", "Web.Crashed", "Web.AddFailHint", "Web.InstallTitle",
                     "Web.InstallDetailDownload", "Web.InstallDetailVerify", "Web.InstallDetailInstall" })
            Assert.NotEqual(k, Loc.T(k));   // (khong doi ngon ngu toan cuc trong test: chay song song voi test khac)
    }

    [Theory]
    [InlineData("https://chat.zalo.me/x", true)]
    [InlineData("https://id.zalo.me/", true)]
    [InlineData("http://zalo.me/", true)]
    [InlineData("https://evilzalo.me/", false)]
    [InlineData("https://zalo.me.evil.com/", false)]
    [InlineData("file:///C:/x", false)]
    [InlineData("about:blank", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsInternalOrigin(string? uri, bool expected)
        => Assert.Equal(expected, WebAccountLogic.IsInternalOrigin(uri, Internal));

    [Theory]
    [InlineData("https://chat.zalo.me/", false, WebAccountLogic.NavAction.Allow)]
    [InlineData("https://id.zalo.me/login", true, WebAccountLogic.NavAction.Allow)]
    [InlineData("about:blank", false, WebAccountLogic.NavAction.Allow)]
    [InlineData("data:text/html,hi", false, WebAccountLogic.NavAction.Allow)]
    [InlineData("blob:https://chat.zalo.me/abc", false, WebAccountLogic.NavAction.Allow)]
    [InlineData("https://example.com/a?q=1", true, WebAccountLogic.NavAction.CancelAndOpenExternal)]
    [InlineData("https://example.com/a", false, WebAccountLogic.NavAction.Cancel)]
    [InlineData("https://evilzalo.me/", true, WebAccountLogic.NavAction.CancelAndOpenExternal)]
    [InlineData("file:///C:/Windows/notepad.exe", true, WebAccountLogic.NavAction.Cancel)]
    [InlineData("ftp://example.com/", false, WebAccountLogic.NavAction.Cancel)]
    [InlineData("zalo://open", true, WebAccountLogic.NavAction.Cancel)]
    [InlineData("", true, WebAccountLogic.NavAction.Allow)]
    [InlineData(null, true, WebAccountLogic.NavAction.Allow)]
    public void ClassifyNavigation(string? uri, bool user, WebAccountLogic.NavAction expected)
        => Assert.Equal(expected, WebAccountLogic.ClassifyNavigation(uri, Internal, user));

    [Fact]
    public void IsInternalHost_Basics()
    {
        Assert.True(WebAccountLogic.IsInternalHost("Chat.Zalo.Me", Internal));
        Assert.False(WebAccountLogic.IsInternalHost("", Internal));
        Assert.False(WebAccountLogic.IsInternalHost("zalo.me", Array.Empty<string>()));
    }

    // Dang nhap QR: trang chinh chuyen qua jr.zaloapp.com (khong tu nguoi dung bam) -> phai cho phep.
    [Theory]
    [InlineData("https://jr.zaloapp.com/x", WebAccountLogic.NavAction.Allow)]
    [InlineData("https://id.zalo.me/account", WebAccountLogic.NavAction.Allow)]
    [InlineData("https://zaloapp.com.evil.vn/", WebAccountLogic.NavAction.Cancel)]
    [InlineData("https://evilzaloapp.com/", WebAccountLogic.NavAction.Cancel)]
    public void ZaloDomains_LoginRedirect(string uri, WebAccountLogic.NavAction expected)
        => Assert.Equal(expected, WebAccountLogic.ClassifyNavigation(uri, WebAccountLogic.ZaloDomains, false));

    // L6: giu file FileShare.Read tu luc kiem chu ky den khi bo cai thoat; bo cai co chu ky that van chay duoc.
    [Fact]
    public async Task InstallAsync_HoldsFileDuringVerifyAndStart_SignedExeStillRuns()
    {
        var signed = @"C:\Program Files\dotnet\dotnet.exe";   // ky nhung Authenticode nhung (khong phai catalog)
        if (!File.Exists(signed) || !WebView2Runtime.VerifyMicrosoftSignature(signed)) return;
        using var t = new TempRoot();
        var p = t.Paths;
        bool heldWrite = false, heldDelete = false, trustedWhileHeld = false;
        var res = await WebView2Runtime.InstallAsync(p, null, default,
            (f, _) => { File.Copy(signed, f, true); return Task.CompletedTask; },
            f =>
            {
                try { using var _ = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None); } catch (IOException) { heldWrite = true; }
                try { File.Delete(f); } catch (IOException) { heldDelete = true; } catch (UnauthorizedAccessException) { heldDelete = true; }
                trustedWhileHeld = WebView2Runtime.VerifyMicrosoftSignature(f);
                return trustedWhileHeld;
            });
        Assert.True(heldWrite); Assert.True(heldDelete); Assert.True(trustedWhileHeld);
        // da khoi chay duoc (thoat ma khac 0 vi doi so la) chu khong phai loi khoi chay
        Assert.Equal(InstallFailure.ExitCode, res.Failure);
        Assert.False(File.Exists(Path.Combine(p.TmpDir, "MicrosoftEdgeWebview2Setup.exe")));   // da xoa sau cung
    }
}