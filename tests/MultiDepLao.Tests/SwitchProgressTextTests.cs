using MultiDepLao.Core;

namespace MultiDepLao.Tests;

[Collection("Loc")]
public class SwitchProgressTextTests
{
    public SwitchProgressTextTests() => Loc.Set("vi");

    private static string? Name(string id) => id switch { "acc-01" => "Công Việc", "acc-02" => "Cá Nhân", _ => null };

    [Theory]
    [InlineData("tat acc-02 de giai phong quyen goi", "Đang tắt 'Cá Nhân' để nhả quyền gọi")]
    [InlineData("tat tai khoan dich de khoi dong lai", "Đang tắt 'X' để khởi động lại")]
    [InlineData("khoi dong tai khoan dich", "Đang khởi động 'X'")]
    [InlineData("mo lai acc-01", "Đang mở lại 'Công Việc'")]
    [InlineData("tai khoan nay da giu quyen goi", "Tài khoản này đã giữ quyền gọi")]
    [InlineData("thu ZaloCap cua acc-02", "Đang thu quyền chụp của 'Cá Nhân'")]
    [InlineData("khoi dong lai acc-01 de nhan quyen chup", "Đang khởi động lại 'Công Việc' để nhận quyền chụp")]
    [InlineData("da bo quyen chup", "Đã bỏ quyền chụp")]
    [InlineData("khong doi gi", "Không có gì thay đổi")]
    [InlineData("acc-01 se co quyen chup khi duoc khoi dong", "'Công Việc' sẽ có quyền chụp khi được bật")]
    [InlineData("dieu gi do la", "dieu gi do la")]
    [InlineData("mo lai zzz", "Đang mở lại 'zzz'")]
    public void Describe_Maps(string raw, string expected) =>
        Assert.Equal(expected, SwitchProgressText.Describe(raw, Name, "X"));

    [Fact]
    public void Describe_NullTarget_UsesGeneric()
    {
        Assert.Equal("Đang khởi động tài khoản đích", SwitchProgressText.Describe("khoi dong tai khoan dich", Name, null));
        Assert.Equal("Đang tắt tài khoản đích để khởi động lại", SwitchProgressText.Describe("tat tai khoan dich de khoi dong lai", Name, null));
    }
}
