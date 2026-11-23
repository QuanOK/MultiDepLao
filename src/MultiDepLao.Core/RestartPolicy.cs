namespace MultiDepLao.Core;

/// <summary>Quyet dinh thuan cho "Khoi dong lai" tai khoan PC.</summary>
public static class RestartPolicy
{
    /// <summary>Co the bat lai (instance cu da het): Stopped hoac Error.</summary>
    public static bool ReadyToStart(InstanceState s) => s is InstanceState.Stopped or InstanceState.Error;

    /// <summary>Lan bat vua roi thoat ngay khi khoi dong (khoa/tien trinh cu chua nha) -> thu lai, toi da 1 lan.</summary>
    public static bool ShouldRetry(InstanceState s, string? reason, int retriesDone) =>
        retriesDone < 1 && s == InstanceState.Error && reason != null
        && reason.Contains("thoat ngay khi khoi dong", StringComparison.OrdinalIgnoreCase);
}
