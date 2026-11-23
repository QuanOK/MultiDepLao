namespace MultiDepLao.Core;

/// <summary>Logic thuan cua thanh lenh phia tren vung Zalo.</summary>
public static class CommandBarMetrics
{
    /// <summary>Chieu cao co dinh cua thanh lenh (DIP).</summary>
    public const double Height = 52;
    /// <summary>Vung Host hep hon nguong nay (DIP): nut thao tac chi con glyph + tooltip.</summary>
    public const double IconOnlyBelow = 980;

    public static bool IconOnly(double hostWidth) => hostWidth < IconOnlyBelow;

    /// <summary>Vung Host hep hon nguong nay (DIP): an het chip quyen/co de khong tran.</summary>
    public const double HideFlagsBelow = 760;

    public static bool FlagsVisible(double hostWidth) => hostWidth >= HideFlagsBelow;

    /// <summary>Gioi han chieu rong ten tai khoan (DIP) de thanh lenh khong tran; uoc luong phan con lai cua thanh.</summary>
    public static double NameMaxWidth(double hostWidth)
    {
        double fixedW = 460 + (FlagsVisible(hostWidth) ? 110 : 0) + (IconOnly(hostWidth) ? 0 : 160);
        return Math.Clamp(hostWidth - fixedW, 50, 240);
    }

    /// <summary>Chieu cao toi thieu cua cua so de Host >= zaloMinH (Root cao hon Host mot thanh lenh).</summary>
    public static double MinWindowHeight(double zaloMinH, double chromeH) => zaloMinH + Height + chromeH;

    /// <summary>Bo nut thao tac theo trang thai. Starting/Stopping: hien nut cua trang thai truoc do nhung tat het.</summary>
    public readonly record struct Actions(bool Start, bool Sleep, bool Restart, bool Reattach, bool Enabled);

    public static Actions ForState(InstanceState state) => state switch
    {
        InstanceState.Stopped or InstanceState.Error => new(true, false, false, false, true),
        InstanceState.Running => new(false, true, true, true, true),
        InstanceState.Starting => new(false, true, true, true, false),
        _ => new(false, true, true, true, false),   // Stopping
    };
}
