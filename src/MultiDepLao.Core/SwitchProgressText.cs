namespace MultiDepLao.Core;

/// <summary>Doi chuoi log tien trinh (khong dau) cua CallOwnerService/CaptureOwnerService thanh van ban theo ngon ngu hien hanh (<see cref="Loc"/>) cho nguoi dung.</summary>
public static class SwitchProgressText
{
    public static string Describe(string raw, Func<string, string?> nameOf, string? targetName)
    {
        if (string.IsNullOrEmpty(raw)) return raw ?? "";
        string Q(string id) => "'" + (nameOf(id) ?? id) + "'";
        string target = targetName != null ? "'" + targetName + "'" : Loc.T("Prog.TargetGeneric");

        switch (raw)
        {
            case "tai khoan nay da giu quyen goi": return Loc.T("Prog.AlreadyCall");
            case "tat tai khoan dich de khoi dong lai": return Loc.F("Prog.StopTargetRestart", target);
            case "khoi dong tai khoan dich": return Loc.F("Prog.StartTarget", target);
            case "da bo quyen chup": return Loc.T("Prog.CapRemoved");
            case "khong doi gi": return Loc.T("Prog.NoChange");
        }

        if (TryId(raw, "mo lai ", "", out var id)) return Loc.F("Prog.Reopen", Q(id));
        if (TryId(raw, "tat ", " de giai phong quyen goi", out id)) return Loc.F("Prog.StopRelease", Q(id));
        if (TryId(raw, "thu ZaloCap cua ", "", out id)) return Loc.F("Prog.ReclaimCap", Q(id));
        if (TryId(raw, "khoi dong lai ", " de nhan quyen chup", out id)) return Loc.F("Prog.RestartCap", Q(id));
        if (TryId(raw, "", " se co quyen chup khi duoc khoi dong", out id)) return Loc.F("Prog.WillCap", Q(id));
        return raw;
    }

    private static bool TryId(string raw, string prefix, string suffix, out string id)
    {
        id = "";
        if (raw.Length <= prefix.Length + suffix.Length) return false;
        if (!raw.StartsWith(prefix, StringComparison.Ordinal) || !raw.EndsWith(suffix, StringComparison.Ordinal)) return false;
        id = raw.Substring(prefix.Length, raw.Length - prefix.Length - suffix.Length);
        return id.Length > 0 && !id.Contains(' ');
    }
}
