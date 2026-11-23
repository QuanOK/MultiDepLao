using System.Runtime.InteropServices;
using static MultiDepLao.Win32.Native;

namespace MultiDepLao.Win32;

public enum UnreadState { Unknown, None, Has }

/// <summary>Count = so tin (1..9) neu doc duoc, RawCodePoint = ky tu huy hieu (chi khi la 1 ky tu).</summary>
public readonly record struct UnreadResult(UnreadState State, int? Count, int? RawCodePoint);

/// <summary>
/// Phat hien huy hieu "chua doc" tren cot dieu huong cua Zalo qua UI Automation (COM). CHI doc BoundingRectangle va
/// ControlType (hinh hoc); Name CHI doc cho dung phan tu huy hieu (1 ky tu dem), khong bao gio doc Name/Value/text cua phan tu khac. Goi tu luong MTA nen.
/// </summary>
public sealed class UnreadBadgeProbe : IDisposable
{
    private sealed class NavCache { public IUIAEl El = null!; public DateTime Built; public bool StripMode; }
    private static readonly TimeSpan NavMaxAge = TimeSpan.FromSeconds(60);
    private readonly Dictionary<IntPtr, NavCache> _nav = new();

    private void DropNav(IntPtr render)
    {
        if (_nav.Remove(render, out var n)) { try { Marshal.FinalReleaseComObject(n.El); } catch { } }
    }

    public void Dispose()
    {
        foreach (var k in _nav.Keys.ToList()) DropNav(k);
        _uia = null; _true = null; _cache = null;
    }

    public const string RenderClass = "Chrome_RenderWidgetHostHWND";
    private const int ControlTypeGroup = 50026;

    private IUIA? _uia;
    private object? _true;
    private object? _cache;

    /// <summary>Loi COM gan nhat (de log), null neu khong co.</summary>
    public string? LastError { get; private set; }

    public UnreadResult Probe(IntPtr mainHwnd)
    {
        LastError = null;
        try
        {
            if (mainHwnd == IntPtr.Zero || !IsWindow(mainHwnd)) return new(UnreadState.Unknown, null, null);
            IntPtr render = FindRender(mainHwnd);
            if (render == IntPtr.Zero) return new(UnreadState.Unknown, null, null);
            double scale = Math.Max(1.0, GetDpiForWindow(mainHwnd) / 96.0);
            foreach (var k in _nav.Keys.ToList()) if (k != render && !IsWindow(k)) DropNav(k);
            UnreadState s = UnreadState.Unknown; string? name = null;
            bool done = false;
            if (_nav.TryGetValue(render, out var nc))
            {
                if (DateTime.UtcNow - nc.Built > NavMaxAge) DropNav(render);
                else
                {
                    try { var r = nc.StripMode ? ScanStripContainer(nc.El, render, scale) : ScanNav(nc.El, scale); if (r != null) { s = r.Value.State; name = r.Value.Name; done = s != UnreadState.Unknown; } }
                    catch (COMException) { }
                    if (!done) DropNav(render);   // loi / cot khong con dung -> quet day du lai
                }
            }
            if (!done)
            {
                (s, name) = Scan(render, scale);
            }
            // Chromium chi bat cay tro nang khi duoc hoi qua cua so CHINH (da kiem thu): hoi thang cua so con
            // truoc do tra cay rong. Cay rong -> danh thuc 1 lan qua cua so chinh (toi da 10 s/lan/cua so), lan quet sau se co.
            if (s == UnreadState.Unknown) Wake(mainHwnd);
            if (s != UnreadState.Has) return new(s, null, null);
            int? raw = name is { Length: > 0 } && char.ConvertToUtf32(name, 0) is var cp && (name.Length == 1 || (name.Length == 2 && char.IsSurrogatePair(name, 0))) ? cp : null;
            // Ten la 1 ky tu font icon ngoai EA05..EA0A (vd. EC05/EC88) = bieu tuong nav tam thu nho lai, khong phai huy hieu -> Unknown (UI giu nguyen cham).
            if (!AcceptBadge(name)) return new(UnreadState.Unknown, null, raw);
            return new(s, CountFromBadgeName(name), raw);
        }
        catch (Exception e)
        {
            LastError = e.GetType().Name + " 0x" + (e is COMException c ? c.HResult.ToString("X8") : "-");
            return new(UnreadState.Unknown, null, null);
        }
    }

    private static IntPtr FindRender(IntPtr main)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(main, (h, _) =>
        {
            if (ClassOf(h) == RenderClass) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private readonly Dictionary<IntPtr, DateTime> _lastWake = new();

    /// <summary>Hoi UIA tu cua so chinh (FindAll con chau, khong doc thuoc tinh nao) de Chromium bat tro nang.</summary>
    private void Wake(IntPtr main)
    {
        var now = DateTime.UtcNow;
        if (_lastWake.TryGetValue(main, out var t) && now - t < TimeSpan.FromSeconds(10)) return;
        if (_lastWake.Count > 32) _lastWake.Clear();
        _lastWake[main] = now;
        Init();
        IUIAEl? root = null; IUIAArr? arr = null;
        try
        {
            if (_uia!.ElementFromHandle(main, out root) != 0 || root == null) return;
            root.FindAll(4, _true!, out arr);
        }
        finally
        {
            if (arr != null) Marshal.FinalReleaseComObject(arr);
            if (root != null) Marshal.FinalReleaseComObject(root);
        }
    }

    private void Init()
    {
        if (_uia != null) return;
        var u = (IUIA)new CUIAutomation();
        u.CreateTrueCondition(out var t);
        u.CreateCacheRequest(out var cr);
        _true = t; _cache = cr;
        _uia = u;
    }

    private (UnreadState, string?) Scan(IntPtr render, double scale)
    {
        Init();
        var res = new List<(int, double, double, double, double)>();
        IUIAEl? root = null; IUIAArr? arr = null;
        var live = new List<IUIAEl>();
        try
        {
            if (_uia!.ElementFromHandle(render, out root) != 0 || root == null) return (UnreadState.Unknown, null);
            if (!FindAllRect(root, 4, out arr, out var cached) || arr == null) return (UnreadState.Unknown, null);
            if (arr.get_Length(out int n) != 0) return (UnreadState.Unknown, null);
            for (int i = 0; i < n; i++)
            {
                if (arr.GetElement(i, out var e) != 0 || e == null) continue;
                live.Add(e);
                res.Add(ReadRect(e, cached));
            }
            // Name: CHI cua phan tu huy hieu do Classify chon (live va res cung chi so)
            string? name = null;
            int ni = FindNavIndex(res, scale);
            if (ni < 0)
            {
                // Cua so rong: Chromium gop cot dieu huong vao Group lon -> quet theo dai hinh hoc ben trai vung render.
                GetWindowRect(render, out var rr);
                var (fs, fi) = ClassifyStrip((rr.Left, rr.Top, 72 * scale, rr.Bottom - rr.Top), res, scale);
                if (fi >= 0 && fi < live.Count && live[fi].GetCurrentPropertyValue(30005, out var fv) == 0) name = fv as string;
                int ci = SelectStripContainer(res, (rr.Left, rr.Top, rr.Right - rr.Left, rr.Bottom - rr.Top), scale);
                if (ci >= 0 && ci < live.Count)
                {
                    var keep = live[ci]; live.RemoveAt(ci);
                    DropNav(render); _nav[render] = new NavCache { El = keep, Built = DateTime.UtcNow, StripMode = true };
                }
                return (fs, name);
            }
            int bi = FindBadgeIndex(res, scale).Index;
            if (bi >= 0 && bi < live.Count && live[bi].GetCurrentPropertyValue(30005, out var nv) == 0) name = nv as string;
            // Giu phan tu cot dieu huong de lan sau chi quet con chau cua no
            if (ni < live.Count)
            {
                var keep = live[ni]; live.RemoveAt(ni);
                DropNav(render); _nav[render] = new NavCache { El = keep, Built = DateTime.UtcNow };
            }
            return (FindBadgeIndex(res, scale).State, name);
        }
        finally
        {
            foreach (var e in live) Marshal.FinalReleaseComObject(e);
            if (arr != null) Marshal.FinalReleaseComObject(arr);
            if (root != null) Marshal.FinalReleaseComObject(root);
        }
    }

    private bool FindAllRect(IUIAEl el, int scope, out IUIAArr? arr, out bool cached)
    {
        arr = null; cached = false;
        if (_cache is IUIACacheRequest cr && cr.AddProperty(30001) == 0 && cr.AddProperty(30003) == 0
            && el.FindAllBuildCache(scope, _true!, cr, out arr) == 0 && arr != null) { cached = true; return true; }
        return el.FindAll(scope, _true!, out arr) == 0 && arr != null;
    }

    private static (int, double, double, double, double) ReadRect(IUIAEl e, bool cached)
    {
        object? v; double l = 0, t = 0, w = 0, h = 0;
        int hr = cached ? e.GetCachedPropertyValue(30001, out v) : e.GetCurrentPropertyValue(30001, out v);
        if (hr == 0 && v is double[] r && r.Length == 4) { l = r[0]; t = r[1]; w = r[2]; h = r[3]; }
        hr = cached ? e.GetCachedPropertyValue(30003, out v) : e.GetCurrentPropertyValue(30003, out v);
        int ct = hr == 0 && v is int ci ? ci : 0;
        return (ct, l, t, w, h);
    }

    /// <summary>Quet nhanh: chi con chau cua phan tu cot dieu huong da giu. null = loi (huy cache).</summary>
    private (UnreadState State, string? Name)? ScanNav(IUIAEl nav, double scale)
    {
        IUIAArr? arr = null; var live = new List<IUIAEl>();
        try
        {
            if (nav.GetCurrentPropertyValue(30001, out var v) != 0 || v is not double[] r || r.Length != 4) return null;
            if (!FindAllRect(nav, 4, out arr, out var cached) || arr == null) return null;
            if (arr.get_Length(out int n) != 0) return null;
            var kids = new List<(int, double, double, double, double)>(n);
            for (int i = 0; i < n; i++)
            {
                if (arr.GetElement(i, out var e) != 0 || e == null) continue;
                live.Add(e); kids.Add(ReadRect(e, cached));
            }
            var (st, bi) = ClassifyInNav((r[0], r[1], r[2], r[3]), kids, scale);
            string? name = null;
            if (bi >= 0 && bi < live.Count && live[bi].GetCurrentPropertyValue(30005, out var nv) == 0) name = nv as string;
            return (st, name);
        }
        finally
        {
            foreach (var e in live) Marshal.FinalReleaseComObject(e);
            if (arr != null) Marshal.FinalReleaseComObject(arr);
        }
    }

    /// <summary>Quet nhanh cho cua so rong: con chau cua Group chua dai nav, roi ClassifyStrip. null = loi (huy cache).</summary>
    private (UnreadState State, string? Name)? ScanStripContainer(IUIAEl container, IntPtr render, double scale)
    {
        IUIAArr? arr = null; var live = new List<IUIAEl>();
        try
        {
            if (!GetWindowRect(render, out var rr)) return null;
            if (!FindAllRect(container, 4, out arr, out var cached) || arr == null) return null;
            if (arr.get_Length(out int n) != 0) return null;
            var kids = new List<(int, double, double, double, double)>(n);
            for (int i = 0; i < n; i++)
            {
                if (arr.GetElement(i, out var e) != 0 || e == null) continue;
                live.Add(e); kids.Add(ReadRect(e, cached));
            }
            var (st, bi) = ClassifyStrip((rr.Left, rr.Top, 72 * scale, rr.Bottom - rr.Top), kids, scale);
            string? name = null;
            if (bi >= 0 && bi < live.Count && live[bi].GetCurrentPropertyValue(30005, out var nv) == 0) name = nv as string;
            return (st, name);
        }
        finally
        {
            foreach (var e in live) Marshal.FinalReleaseComObject(e);
            if (arr != null) Marshal.FinalReleaseComObject(arr);
        }
    }

    /// <summary>
    /// Chon Group nho nhat chua tron dai nav (trai render) theo chieu ngang, cao &gt; 300, rong &lt;= 500*scale. -1 neu khong co.
    /// </summary>
    public static int SelectStripContainer(IReadOnlyList<(int ct, double l, double t, double w, double h)> elems,
        (double l, double t, double w, double h) render, double scale)
    {
        int best = -1; double bestArea = double.MaxValue;
        for (int i = 0; i < elems.Count; i++)
        {
            var e = elems[i];
            if (e.ct != ControlTypeGroup || e.h <= 300 || e.w > 500 * scale) continue;
            if (e.l > render.l + 2 * scale || e.l + e.w < render.l + 72 * scale) continue;
            double a = e.w * e.h;
            if (a < bestArea) { bestArea = a; best = i; }
        }
        return best;
    }

    /// <summary>Chi so Group dau tien dung kich thuoc cot dieu huong, -1 neu khong co.</summary>
    public static int FindNavIndex(IReadOnlyList<(int ct, double l, double t, double w, double h)> elems, double scale)
    {
        for (int i = 0; i < elems.Count; i++)
            if (IsNavRect(elems[i].ct, elems[i].w, elems[i].h, scale)) return i;
        return -1;
    }

    private static bool IsNavRect(int ct, double w, double h, double scale)
        => ct == ControlTypeGroup && w >= 56 * scale && w <= 72 * scale && h > 300;

    /// <summary>
    /// Du phong khi khong co Group cot dieu huong (cua so rong): dai (l,t,w,h) ben trai vung render. Phan tu co tam trong dai:
    /// 0 &lt; W,H &lt;= 24*scale = huy hieu (Has); neu khong, co phan tu 24*scale &lt; W,H &lt;= 48*scale (bieu tuong) = None; con lai Unknown.
    /// </summary>
    public static (UnreadState State, int Index) ClassifyStrip((double l, double t, double w, double h) strip,
        IReadOnlyList<(int ct, double l, double t, double w, double h)> elems, double scale)
    {
        bool icon = false;
        for (int i = 0; i < elems.Count; i++)
        {
            var e = elems[i];
            if (e.w <= 0 || e.h <= 0) continue;
            double cx = e.l + e.w / 2, cy = e.t + e.h / 2;
            if (cx < strip.l || cx > strip.l + strip.w || cy < strip.t || cy > strip.t + strip.h) continue;
            if (e.w <= 24 * scale && e.h <= 24 * scale) return (UnreadState.Has, i);
            if (e.w <= 48 * scale && e.h <= 48 * scale) icon = true;
        }
        return (icon ? UnreadState.None : UnreadState.Unknown, -1);
    }

    /// <summary>Nhu FindBadgeIndex nhung cot da biet (nav) va chi xet cac con. nav sai kich thuoc = Unknown.</summary>
    public static (UnreadState State, int Index) ClassifyInNav((double l, double t, double w, double h) nav,
        IReadOnlyList<(int ct, double l, double t, double w, double h)> kids, double scale)
    {
        if (!IsNavRect(ControlTypeGroup, nav.w, nav.h, scale)) return (UnreadState.Unknown, -1);
        int inside = 0;
        for (int i = 0; i < kids.Count; i++)
        {
            var e = kids[i];
            if (e.w <= 0 || e.h <= 0) continue;
            double cx = e.l + e.w / 2, cy = e.t + e.h / 2;
            if (cx < nav.l || cx > nav.l + nav.w || cy < nav.t || cy > nav.t + nav.h) continue;
            inside++;
            if (e.w <= 24 * scale && e.h <= 24 * scale) return (UnreadState.Has, i);
        }
        // Cot luon co bieu tuong; cay con rong (Chromium tam thoi chua co) = chua biet, khong duoc tat cham.
        return (inside == 0 ? UnreadState.Unknown : UnreadState.None, -1);
    }
    /// <summary>
    /// Quyet dinh thuan hinh hoc. Cot dieu huong = Group rong 56..72*scale, cao &gt; 300. Huy hieu = phan tu 0 &lt; W,H &lt;= 24*scale
    /// co tam nam trong cot. Khong thay cot = Unknown.
    /// </summary>
    public static UnreadState Classify(IReadOnlyList<(int ct, double l, double t, double w, double h)> elems, double scale)
        => FindBadgeIndex(elems, scale).State;

    /// <summary>Nhu Classify, kem chi so phan tu huy hieu (-1 neu khong co).</summary>
    public static (UnreadState State, int Index) FindBadgeIndex(IReadOnlyList<(int ct, double l, double t, double w, double h)> elems, double scale)
    {
        bool anyCol = false;
        foreach (var c in elems)
        {
            if (c.ct != ControlTypeGroup || c.w < 56 * scale || c.w > 72 * scale || c.h <= 300) continue;
            anyCol = true;
            for (int i = 0; i < elems.Count; i++)
            {
                var e = elems[i];
                if (e.w <= 0 || e.h <= 0 || e.w > 24 * scale || e.h > 24 * scale) continue;
                double cx = e.l + e.w / 2, cy = e.t + e.h / 2;
                if (cx >= c.l && cx <= c.l + c.w && cy >= c.t && cy <= c.t + c.h) return (UnreadState.Has, i);
            }
        }
        return (anyCol ? UnreadState.None : UnreadState.Unknown, -1);
    }

    /// <summary>Huy hieu la 1 ky tu font icon: U+EA05..U+EA0D = 1..9 tin. Khac -> null.</summary>
    /// <summary>Ky tu font icon cua huy hieu Zalo (do 2026-11-18): U+EA05..U+EA09 = 1..5, U+EA0A = "5+" (tra 6 = "tren 5").</summary>
    public const int MoreThanFive = 6;
    /// <summary>
    /// Chi nhan huy hieu khi ky tu dau trong U+EA05..U+EA0A. Ten rong/null (khong doc duoc) = KHONG nhan: log 10:26:43
    /// cho thay phan tu nho khong ten (bieu tuong thu nho) gay cham do sai ~5 s.
    /// </summary>
    public static bool AcceptBadge(string? name)
        => !string.IsNullOrEmpty(name) && name[0] is >= '\uEA05' and <= '\uEA0A';
    public static int? CountFromBadgeName(string? name)
        => name is { Length: 1 } && name[0] >= '\uEA05' && name[0] <= '\uEA0A' ? name[0] - 0xEA04 : null;
}

// IUIAutomation (COM, CUIAutomation) toi thieu: chi khai bao den slot can dung, cac slot khac la giu cho (thu tu theo UIAutomationClient.h).
[ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIA
{
    void _0(); void _1(); void _2();
    [PreserveSig] int ElementFromHandle(IntPtr h, out IUIAEl el);
    void _4(); void _5(); void _6(); void _7(); void _8(); void _9(); void _10(); void _11(); void _12(); void _13();
    void _14(); void _15(); void _16();
    [PreserveSig] int CreateCacheRequest([MarshalAs(UnmanagedType.IUnknown)] out object c);   // index 17
    [PreserveSig] int CreateTrueCondition([MarshalAs(UnmanagedType.IUnknown)] out object c);   // index 18
}

[ComImport, Guid("b32a92b5-bc25-4078-9c08-d7ee95c48e03"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIACacheRequest
{
    [PreserveSig] int AddProperty(int propertyId);   // index 0
}

[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAEl
{
    void _0(); void _1(); void _2();
    [PreserveSig] int FindAll(int scope, [MarshalAs(UnmanagedType.IUnknown)] object cond, out IUIAArr arr);   // 3
    void _4();
    [PreserveSig] int FindAllBuildCache(int scope, [MarshalAs(UnmanagedType.IUnknown)] object cond,
        [MarshalAs(UnmanagedType.IUnknown)] object cache, out IUIAArr arr);   // 5
    void _6();
    [PreserveSig] int GetCurrentPropertyValue(int id, [MarshalAs(UnmanagedType.Struct)] out object v);   // 7
    void _8();
    [PreserveSig] int GetCachedPropertyValue(int id, [MarshalAs(UnmanagedType.Struct)] out object v);   // 9
}

[ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAArr
{
    [PreserveSig] int get_Length(out int n);
    [PreserveSig] int GetElement(int i, out IUIAEl e);
}

[ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
internal class CUIAutomation { }



