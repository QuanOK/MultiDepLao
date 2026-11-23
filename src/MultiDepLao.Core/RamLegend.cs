using System.Globalization;

namespace MultiDepLao.Core;

/// <summary>Mot dong chu thich RAM (tai khoan hoac dong gop "+N khac").</summary>
public sealed record RamLegendRow<T>(string Name, long Bytes, T? Tag);

/// <summary>Ket qua chu thich: toi da <c>max</c> dong tai khoan (RAM giam dan) + phan con lai gop lai.</summary>
public sealed record RamLegendResult<T>(IReadOnlyList<RamLegendRow<T>> Rows, int OtherCount, long OtherBytes);

/// <summary>Ham thuan cho the RAM o thanh ben (de test).</summary>
public static class RamLegend
{
    public const int MaxRows = 3;

    /// <summary>Chi lay muc co RAM &gt; 0, sap RAM giam dan (on dinh theo thu tu vao), cat top <paramref name="max"/>, gop phan con lai.</summary>
    public static RamLegendResult<T> Build<T>(IEnumerable<(string name, long bytes, T tag)> items, int max = MaxRows)
    {
        var sorted = items.Where(i => i.bytes > 0)
            .Select((i, idx) => (i, idx)).OrderByDescending(x => x.i.bytes).ThenBy(x => x.idx).Select(x => x.i).ToList();
        var rows = sorted.Take(max).Select(i => new RamLegendRow<T>(i.name, i.bytes, i.tag)).ToList();
        var rest = sorted.Skip(max).ToList();
        return new RamLegendResult<T>(rows, rest.Count, rest.Sum(i => i.bytes));
    }

    private static CultureInfo Ci(string lang)
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        bool vi = Loc.Normalize(lang) == Loc.Vi;
        c.NumberFormat.NumberDecimalSeparator = vi ? "," : ".";
        c.NumberFormat.NumberGroupSeparator = vi ? "." : ",";
        return c;
    }

    /// <summary>9,8 GB (vi) / 9.8 GB (en): 1 chu so thap phan, GB = 2^30 byte.</summary>
    public static string FormatGb(long bytes, string lang)
        => (Math.Max(bytes, 0) / (double)(1L << 30)).ToString("F1", Ci(lang)) + " GB";

    /// <summary>"2,0" (vi) / "2.0" (en): chi con so, GB = 2^30 byte.</summary>
    public static string FormatGbNumber(long bytes, string lang)
        => (Math.Max(bytes, 0) / (double)(1L << 30)).ToString("F1", Ci(lang));

    /// <summary>2.096 MB (vi) / 2,096 MB (en): so nguyen, MB = 2^20 byte.</summary>
    public static string FormatMb(long bytes, string lang)
        => (Math.Max(bytes, 0) / (1024 * 1024)).ToString("N0", Ci(lang)) + " MB";
}
