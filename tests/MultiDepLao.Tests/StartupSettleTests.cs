using MultiDepLao.Core;

namespace MultiDepLao.Tests;

public class StartupSettleTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact] public void RecentChange_NotSettled() => Assert.False(StartupSettle.IsSettled(T0, T0.AddSeconds(10.5), T0.AddSeconds(11.5)));
    [Fact] public void QuietLongEnoughAfterMin_Settled() => Assert.True(StartupSettle.IsSettled(T0, T0.AddSeconds(8.5), T0.AddSeconds(11)));
    [Fact] public void ExactlyQuietAfterMin_Settled() => Assert.True(StartupSettle.IsSettled(T0, T0.Add(StartupSettle.Min) - StartupSettle.Quiet, T0.Add(StartupSettle.Min)));
    [Fact] public void KeepsChangingButMaxReached_Settled() => Assert.True(StartupSettle.IsSettled(T0, T0.AddSeconds(19.5), T0.AddSeconds(20)));
    [Fact] public void KeepsChangingBeforeMax_NotSettled() => Assert.False(StartupSettle.IsSettled(T0, T0.AddSeconds(19), T0.AddSeconds(19.5)));

    // Thu that 2026-11-20: Zalo da dang nhap doi style ~5,5 s va ~8,5 s sau khi gan; yen 3 s giua chung.
    [Fact] public void QuietGapBeforeSecondStyleChange_NotSettled() => Assert.False(StartupSettle.IsSettled(T0, T0.AddSeconds(5.5), T0.AddSeconds(8.4)));
    [Fact] public void QuietFromStartButBeforeMin_NotSettled() => Assert.False(StartupSettle.IsSettled(T0, T0, T0.AddSeconds(5)));
}
