namespace MultiDepLao.Core;

public enum CallFixAction { None, Restart, Notify }

/// <summary>
/// Phien mo cung Windows: Zalo that cung tu mo co the gianh pipe cuoc goi/ZaloCap truoc tai khoan chu quyen goi.
/// Doi mot luc roi xet lai; chi doc trang thai, khong dung vao Zalo ngoai app.
/// </summary>
public static class AutoStartCallFix
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(25);

    /// <param name="foreignRunning">Co Zalo ngoai app dang chay (ZaloCap ngoai CapRoot).</param>
    public static CallFixAction Decide(CallOwnerState state, string? ownerId, bool ownerRunning, bool foreignRunning)
    {
        if (string.IsNullOrEmpty(ownerId) || !ownerRunning) return CallFixAction.None;
        if (state.Kind == CallOwnerKind.Known) return CallFixAction.None;   // dung chu, hoac app da biet ro mot tai khoan khac: khong dung
        if (foreignRunning || state.Kind == CallOwnerKind.External) return CallFixAction.Notify;
        return CallFixAction.Restart;   // None / Unknown, khong con Zalo ngoai
    }

    /// <summary>Chay 1 lan. Tra ve Notify neu UI can hien thong bao; None neu khong can / da tu sua duoc.</summary>
    public static async Task<CallFixAction> RunAsync(
        Func<string?> ownerId, Func<string, bool> isRunning, Func<CallOwnerState> getState, Func<bool> foreignRunning,
        Func<string, Task<SwitchResult>> restart, ILog log, TimeSpan? delay = null,
        Func<TimeSpan, CancellationToken, Task>? wait = null, CancellationToken ct = default)
    {
        try
        {
            await (wait ?? Task.Delay)(delay ?? DefaultDelay, ct);
            var owner = ownerId();
            var action = Decide(getState(), owner, owner != null && isRunning(owner), foreignRunning());
            if (action == CallFixAction.Restart)
            {
                log.Info($"mo cung Windows: quyen goi cua {owner} chua dung, khoi dong lai de lay lai");
                var r = await restart(owner!);
                if (r.Success) { log.Info("mo cung Windows: da lay lai quyen goi"); return CallFixAction.None; }
                log.Warn("mo cung Windows: khong lay lai duoc quyen goi: " + r.Reason);
                return CallFixAction.Notify;
            }
            if (action == CallFixAction.Notify) log.Warn("mo cung Windows: Zalo ngoai app giu quyen goi/chup, khong dung vao");
            return action;
        }
        catch (OperationCanceledException) { return CallFixAction.None; }
        catch (Exception ex) { log.Error("mo cung Windows: kiem tra quyen goi loi: " + ex.GetType().Name); return CallFixAction.None; }
    }
}
