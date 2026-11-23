using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class AutoStartTests
{
    private sealed class FakeStore : IAutoStartStore
    {
        public string? Value; public int Writes;
        public string? Read() => Value;
        public void Write(string command) { Value = command; Writes++; }
        public void Delete() => Value = null;
    }

    [Fact]
    public void BuildCommand_QuotesPathWithSpaces()
        => Assert.Equal("\"C:\\Program Files\\Multi DepLao\\app.exe\" --autostart", AutoStart.BuildCommand(@"C:\Program Files\Multi DepLao\app.exe"));

    [Fact]
    public void EnableDisable_TogglesIsEnabled()
    {
        var s = new FakeStore(); var a = new AutoStart(s);
        Assert.False(a.IsEnabled);
        a.Enable(@"C:\x\a.exe");
        Assert.True(a.IsEnabled);
        Assert.Equal("\"C:\\x\\a.exe\" --autostart", s.Value);
        a.Disable();
        Assert.False(a.IsEnabled);
        a.Disable(); // khong loi khi da tat
    }

    [Fact]
    public void RefreshPath_FixesMovedExe_NoWriteWhenCorrect_NothingWhenOff()
    {
        var s = new FakeStore(); var a = new AutoStart(s, _ => false);   // exe da dang ky khong con
        Assert.False(a.RefreshPath(@"C:\x\a.exe"));
        Assert.Equal(0, s.Writes); Assert.False(a.IsEnabled);

        a.Enable(@"C:\old\a.exe"); s.Writes = 0;
        Assert.True(a.RefreshPath(@"C:\new\a.exe"));
        Assert.Equal("\"C:\\new\\a.exe\" --autostart", s.Value);
        Assert.Equal(1, s.Writes);
        Assert.False(a.RefreshPath(@"C:\new\a.exe"));
        Assert.Equal(1, s.Writes);
    }

    [Fact]
    public void RefreshPath_KeepsOtherCopyThatStillExists()
    {
        // Bat o ban cai dat, roi mo ban portable/dev: khong duoc cuop muc khoi dong
        var s = new FakeStore();
        var a = new AutoStart(s, p => p == @"C:\Users\u\AppData\Local\Programs\MultiDepLao\MultiDepLao.App.exe");
        a.Enable(@"C:\Users\u\AppData\Local\Programs\MultiDepLao\MultiDepLao.App.exe"); s.Writes = 0;
        Assert.False(a.RefreshPath(@"D:\Portable\MultiDepLao\MultiDepLao.App.exe"));
        Assert.Equal(0, s.Writes);
        Assert.Equal("\"C:\\Users\\u\\AppData\\Local\\Programs\\MultiDepLao\\MultiDepLao.App.exe\" --autostart", s.Value);
    }

    [Fact]
    public void RefreshPath_SameExeOldCommand_Rewrites_CaseInsensitive()
    {
        var s = new FakeStore { Value = "\"C:\\X\\A.exe\" --tray" };
        var a = new AutoStart(s, _ => true);   // exe con ton tai nhung la CHINH exe hien tai
        Assert.True(a.RefreshPath(@"c:\x\a.exe"));
        Assert.Equal("\"c:\\x\\a.exe\" --autostart", s.Value);
        Assert.False(a.RefreshPath(@"c:\x\a.exe"));
    }

    [Theory]
    [InlineData("\"C:\\a b\\x.exe\" --tray", "C:\\a b\\x.exe")]
    [InlineData("C:\\ab\\x.exe --tray", "C:\\ab\\x.exe")]
    [InlineData("C:\\ab\\x.exe", "C:\\ab\\x.exe")]
    [InlineData("", null)]
    [InlineData("\"", null)]
    public void ExeOf_ParsesCommand(string cmd, string? exe) => Assert.Equal(exe, AutoStart.ExeOf(cmd));

    [Theory]
    [InlineData("0.3.1+abc", "0.3.1")]
    [InlineData("0.3.1", "0.3.1")]
    [InlineData("0.3.1-beta+1f2e", "0.3.1-beta")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("+abc", null)]
    public void CleanVersion_StripsBuildMetadata(string? input, string? expected)
        => Assert.Equal(expected, AppInfo.CleanVersion(input));
}

