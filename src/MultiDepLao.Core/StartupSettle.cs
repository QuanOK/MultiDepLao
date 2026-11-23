namespace MultiDepLao.Core;

/// <summary>Giai doan "dang mo" cua tai khoan PC: giu cua so Zalo ngoai man hinh cho den khi no het tu doi style/vi tri/hien.</summary>
public static class StartupSettle
{
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(1500);
    /// <summary>Thu that 2026-11-20 (tai khoan da dang nhap): Zalo tu doi style 2 lan, ~5,5 s va ~8,5 s sau khi gan lan dau
    /// (giua chung yen ~3 s) -> nguong "yen 2,5 s" dua vao qua som, nguoi dung thay nhay 2 lan. Cho toi thieu 11 s.</summary>
    public static readonly TimeSpan Min = TimeSpan.FromSeconds(11);
    public static readonly TimeSpan Max = TimeSpan.FromSeconds(20);

    /// <summary>true = da qua Min va khong co thay doi nao trong Quiet, hoac da cho qua Max ke tu luc gan lan dau.</summary>
    public static bool IsSettled(DateTime startUtc, DateTime lastChangeUtc, DateTime nowUtc) =>
        (nowUtc - startUtc >= Min && nowUtc - lastChangeUtc >= Quiet) || nowUtc - startUtc >= Max;
}