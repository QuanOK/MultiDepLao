namespace MultiDepLao.Core;

/// <summary>Quyet dinh thuan cua bieu tuong khay (khong Win32): cat tooltip, chon anh trong .ico.</summary>
public static class TrayLogic
{
    /// <summary>szTip toi da 127 ky tu (NOTIFYICONDATA v4 + NIF_SHOWTIP); giu gioi han 63 nhu ban WinForms cu.</summary>
    public const int MaxTip = 63;

    public static string ClampTip(string? text)
    {
        text ??= "";
        return text.Length > MaxTip ? text[..MaxTip] : text;
    }

    /// <summary>Chon chi so anh trong .ico: nho nhat co canh &gt;= target; neu khong co thi lon nhat. -1 neu rong.</summary>
    public static int PickIconIndex(IReadOnlyList<int> sizes, int target)
    {
        int best = -1;
        for (int i = 0; i < sizes.Count; i++)
        {
            int s = sizes[i];
            if (best < 0) { best = i; continue; }
            int b = sizes[best];
            bool sOk = s >= target, bOk = b >= target;
            if (sOk && !bOk) best = i;
            else if (sOk && bOk && s < b) best = i;
            else if (!sOk && !bOk && s > b) best = i;
        }
        return best;
    }
}
