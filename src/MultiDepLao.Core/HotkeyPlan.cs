namespace MultiDepLao.Core;

/// <summary>Phim tat nao cua app nen dang ky luc nay (ham thuan, de test).</summary>
public readonly record struct HotkeyPlan(bool Select, bool Lock, bool TabSwitch)
{
    /// <param name="mineForeground">Foreground la cua so cua MultiDepLao hoac cua so Zalo cua tai khoan.</param>
    /// <param name="mainForeground">Foreground chinh la cua so chinh (ke ca khi focus nam trong WebView2).</param>
    /// <param name="wantLock">Da dat mat khau khoa.</param>
    public static HotkeyPlan For(bool mineForeground, bool mainForeground, bool wantLock) =>
        new(mineForeground, mineForeground && wantLock, mainForeground);
}
