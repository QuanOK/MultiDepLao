namespace MultiDepLao.Core;

/// <summary>Mot cung cua vong RAM: <see cref="Index"/> = vi tri trong danh sach dau vao; goc tinh tu 12 gio, theo chieu kim dong ho.</summary>
public readonly record struct RamArc(int Index, double Start, double Sweep);

/// <summary>Ham thuan tinh cac cung cua vong RAM o chan thanh ben (de test).</summary>
public static class RamRing
{
    /// <summary>
    /// Moi muc (bytes &gt; 0) mot cung dai bytes/scaleBytes*360 do, noi tiep tu 0 do theo thu tu dau vao;
    /// muc 0 bi bo, tong cung khong qua 360 (cat khi vuot, bo phan con lai).
    /// Thang do cua UI = tong RAM may (cung thang voi <see cref="SidebarMetrics.TotalFraction"/>).
    /// </summary>
    public static IReadOnlyList<RamArc> Arcs(IReadOnlyList<long> bytes, long scaleBytes)
    {
        var res = new List<RamArc>();
        if (scaleBytes <= 0) return res;
        double pos = 0;
        for (int i = 0; i < bytes.Count && pos < 360; i++)
        {
            if (bytes[i] <= 0) continue;
            double sweep = Math.Min((double)bytes[i] / scaleBytes * 360.0, 360.0 - pos);
            if (sweep <= 0) continue;
            res.Add(new RamArc(i, pos, sweep));
            pos += sweep;
        }
        return res;
    }
}
