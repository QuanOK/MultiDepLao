namespace MultiDepLao.Core;

public sealed record ZaloVersion(Version Version, string DirName, string SourceDir)
{
    public string ExePath => Path.Combine(SourceDir, "Zalo.exe");
}

public sealed class ZaloInstall
{
    public const string CompleteMarker = ".mz-complete";
    public const string CopyingMarker = ".mz-copying";
    public const string CapExeName = "ZaloCap.exe";

    private readonly AppPaths _paths;
    private readonly ILog _log;
    public string ZaloRoot { get; }

    private readonly Func<string, bool> _verifyExe;
    private readonly Dictionary<(string Path, long Len, long Ticks), bool> _verified = new();

    /// <summary>Mac dinh: Authenticode hop le + chu the O= chua "VNG" (<see cref="Authenticode.ZaloSignerOrg"/>). Test co the truyen ham gia.</summary>
    public static bool VerifyZaloSignature(string exe) =>
        Authenticode.VerifySigned(exe, s => Authenticode.SubjectOrgContains(s, Authenticode.ZaloSignerOrg));

    public ZaloInstall(AppPaths paths, string? zaloRoot = null, ILog? log = null, Func<string, bool>? verifyExe = null)
    {
        _paths = paths;
        _log = log ?? NullLog.Instance;
        _verifyExe = verifyExe ?? VerifyZaloSignature;
        ZaloRoot = zaloRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Zalo");
    }

    public static Version? ParseVersion(string dirName)
    {
        if (!dirName.StartsWith("Zalo-", StringComparison.OrdinalIgnoreCase)) return null;
        return Version.TryParse(dirName.Substring(5), out var v) ? v : null;
    }

    private static IEnumerable<ZaloVersion> List(string root, bool requireExe)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var d in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(d);
            var v = ParseVersion(name);
            if (v == null) continue;
            if (requireExe && !File.Exists(Path.Combine(d, "Zalo.exe"))) continue;
            yield return new ZaloVersion(v, name, d);
        }
    }

    /// <summary>Ban cai goc moi nhat (so sanh theo so). Chi doc.</summary>
    public ZaloVersion? FindLatest() => List(ZaloRoot, true).OrderByDescending(x => x.Version).FirstOrDefault();

    public string NoCapDirFor(ZaloVersion v) => Path.Combine(_paths.NoCapRoot, v.DirName);

    private static bool HasFile(string dir, string name) =>
        Directory.EnumerateFiles(dir, name, SearchOption.AllDirectories).Any();

    /// <summary>Kiem chu ky Zalo.exe, nho ket qua THANH CONG theo (duong dan, kich thuoc, gio ghi) vi kiem ton ~1.2 giay.</summary>
    private bool Verified(string exe)
    {
        FileInfo fi;
        try { fi = new FileInfo(exe); if (!fi.Exists) return false; } catch { return false; }
        var key = (fi.FullName.ToLowerInvariant(), fi.Length, fi.LastWriteTimeUtc.Ticks);
        lock (_verified) { if (_verified.ContainsKey(key)) return true; }
        if (!_verifyExe(exe)) return false;
        lock (_verified) _verified[key] = true;
        return true;
    }

    /// <summary>Ban cai goc phai co chu ky hop le truoc khi dung lam nguon chep; neu khong thi khong khoi chay.</summary>
    private void RequireOriginalVerified(ZaloVersion src)
    {
        if (Verified(src.ExePath)) return;
        _log.Warn($"zalo: Zalo.exe goc {src.DirName} khong qua kiem tra chu ky, khong khoi chay");
        throw new InvalidOperationException("Zalo.exe cua ban cai goc khong co chu ky hop le cua VNG; tu choi khoi chay.");
    }

    /// <summary>Ban sao hop le: co file danh dau hoan tat, hoac (ban cu cua spike) co Zalo.exe, khong ZaloCap.exe, khong co danh dau dang chep.</summary>
    public bool IsCopyValid(string dstDir)
    {
        if (!File.Exists(Path.Combine(dstDir, "Zalo.exe"))) return false;
        if (File.Exists(Path.Combine(dstDir, CopyingMarker))) return false;
        if (File.Exists(Path.Combine(dstDir, CompleteMarker))) return true;
        if (HasFile(dstDir, CapExeName)) return false;
        File.WriteAllText(Path.Combine(dstDir, CompleteMarker), "adopted " + DateTime.UtcNow.ToString("o"));
        _log.Info($"nocap: nhan ban sao cu {Path.GetFileName(dstDir)} (them file danh dau)");
        return true;
    }

    /// <summary>Ban sao nocap hop le (da chep xong) co phien ban cao nhat; null neu chua co.</summary>
    public ZaloVersion? FindLatestValidCopy() =>
        List(_paths.NoCapRoot, true).Where(c => IsCopyValid(c.SourceDir)).OrderByDescending(x => x.Version).FirstOrDefault();

    // Chi mot lan chep tai mot thoi diem (watcher nen + khoi dong tai khoan): neu khong, lan thu hai thay
    // CopyingMarker se tuong ban sao do va xoa di lam lai.
    private readonly SemaphoreSlim _copyLock = new(1, 1);

    /// <summary>Dam bao co ban sao nocap cua ban cai moi nhat. Tra ve duong dan Zalo.exe trong ban sao.</summary>
    public async Task<string> EnsureNoCapCopyAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await _copyLock.WaitAsync(ct);
        try { return await EnsureCoreAsync(progress, ct); }
        finally { _copyLock.Release(); }
    }

    private async Task<string> EnsureCoreAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var src = FindLatest() ?? throw new FileNotFoundException($"Khong thay Zalo PC duoi {ZaloRoot}");
        var dst = NoCapDirFor(src);
        var dstExe = Path.Combine(dst, "Zalo.exe");
        if (Directory.Exists(dst) && IsCopyValid(dst))
        {
            if (Verified(dstExe)) { progress?.Report(1); return dstExe; }
            _log.Warn($"nocap: Zalo.exe trong ban sao {src.DirName} khong qua kiem tra chu ky, chep lai");
        }
        RequireOriginalVerified(src);

        await Task.Run(() =>
        {
            if (Directory.Exists(dst))
            {
                if (!AppPaths.IsUnder(dst, _paths.NoCapRoot)) throw new InvalidOperationException("Ngoai NoCapRoot");
                _log.Warn($"nocap: ban sao do {src.DirName}, lam lai");
                Directory.Delete(dst, true);
            }
            Directory.CreateDirectory(dst);
            File.WriteAllText(Path.Combine(dst, CopyingMarker), DateTime.UtcNow.ToString("o"));

            var files = Directory.EnumerateFiles(src.SourceDir, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).Equals(CapExeName, StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f)).ToList();
            long total = Math.Max(1, files.Sum(f => f.Length)), done = 0;
            foreach (var dir in Directory.EnumerateDirectories(src.SourceDir, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src.SourceDir, dir)));
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                File.Copy(f.FullName, Path.Combine(dst, Path.GetRelativePath(src.SourceDir, f.FullName)), true);
                done += f.Length;
                progress?.Report((double)done / total);
            }
            File.Delete(Path.Combine(dst, CopyingMarker));
            File.WriteAllText(Path.Combine(dst, CompleteMarker), DateTime.UtcNow.ToString("o"));
            _log.Info($"nocap: xong {src.DirName}, {files.Count} file");
        }, ct);

        if (!File.Exists(dstExe)) throw new FileNotFoundException("Ban sao thieu Zalo.exe", dstExe);
        if (!Verified(dstExe)) throw new InvalidOperationException("Ban sao Zalo.exe khong qua kiem tra chu ky sau khi chep.");
        return dstExe;
    }

    /// <summary>Xoa ban sao cu khong con dung. Chi xoa duoi NoCapRoot; luon giu ban moi nhat. Nhan "Zalo-x.y.z" hoac "x.y.z".</summary>
    public IReadOnlyList<string> PruneOldCopies(IEnumerable<string> versionsInUse)
    {
        var keep = new HashSet<Version>();
        foreach (var s in versionsInUse)
        {
            var v = ParseVersion(s.StartsWith("Zalo-", StringComparison.OrdinalIgnoreCase) ? s : "Zalo-" + s);
            if (v != null) keep.Add(v);
        }
        var copies = List(_paths.NoCapRoot, false).ToList();
        if (copies.Count > 0) keep.Add(copies.Select(c => c.Version).Max()!);
        var removed = new List<string>();
        foreach (var c in copies.Where(c => !keep.Contains(c.Version)))
        {
            if (!AppPaths.IsUnder(c.SourceDir, _paths.NoCapRoot)) continue;
            try { Directory.Delete(c.SourceDir, true); removed.Add(c.DirName); _log.Info($"nocap: xoa ban cu {c.DirName}"); }
            catch (Exception e) { _log.Warn($"nocap: khong xoa duoc {c.DirName}: {e.Message}"); }
        }
        return removed;
    }

    // ------------------------------------------------------------ ban sao co ZaloCap (quyen chup man hinh)

    public const string CapOffSuffix = ".off";
    private static readonly string[] RealCopyExt = { ".db", ".json", ".dat", ".log", ".ini" };
    private static readonly string CapRel = Path.Combine("plugins", "capture", CapExeName);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newFile, string existing, IntPtr securityAttributes);

    /// <summary>Duong dan ZaloCap.exe trong ban sao cap cua mot tai khoan (dir = zalo-cap\id\Zalo-ver).</summary>
    public static string CapExeIn(string capVersionDir) => Path.Combine(capVersionDir, CapRel);

    private static void LinkOrCopy(string src, string dst)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        if (File.Exists(dst)) File.Delete(dst);
        if (RealCopyExt.Contains(Path.GetExtension(src), StringComparer.OrdinalIgnoreCase)) { File.Copy(src, dst, true); return; }
        if (!CreateHardLink(dst, src, IntPtr.Zero)) File.Copy(src, dst, true);   // khac o dia -> chep that
    }

    private bool IsCapCopyValid(string dir) =>
        File.Exists(Path.Combine(dir, "Zalo.exe")) && File.Exists(Path.Combine(dir, CompleteMarker)) && !File.Exists(Path.Combine(dir, CopyingMarker));

    /// <summary>
    /// Dam bao ban sao cap (hardlink, kem ZaloCap.exe) cua tai khoan <paramref name="id"/> theo ban cai goc moi nhat,
    /// dat lai ZaloCap.exe neu dang la .off. Tra ve duong dan Zalo.exe. Don cac phien ban cu cua chinh id nay.
    /// </summary>
    public async Task<string> EnsureCapCopyAsync(string id, CancellationToken ct = default)
    {
        await _copyLock.WaitAsync(ct);
        try { return await Task.Run(() => EnsureCapCore(id, ct), ct); }
        finally { _copyLock.Release(); }
    }

    private string EnsureCapCore(string id, CancellationToken ct)
    {
        var src = FindLatest() ?? throw new FileNotFoundException($"Khong thay Zalo PC duoi {ZaloRoot}");
        var idDir = _paths.CapDir(id);
        var dst = Path.Combine(idDir, src.DirName);
        if (!AppPaths.IsUnder(dst, _paths.CapRoot)) throw new InvalidOperationException("Ngoai CapRoot");
        bool build = !(Directory.Exists(dst) && IsCapCopyValid(dst));
        if (!build && !Verified(Path.Combine(dst, "Zalo.exe")))
        {
            _log.Warn($"cap: Zalo.exe trong ban sao {id}/{src.DirName} khong qua kiem tra chu ky, lam lai");
            build = true;
        }
        if (build)
        {
            RequireOriginalVerified(src);
            if (Directory.Exists(dst)) { _log.Warn($"cap: ban sao do {id}/{src.DirName}, lam lai"); Directory.Delete(dst, true); }
            Directory.CreateDirectory(dst);
            File.WriteAllText(Path.Combine(dst, CopyingMarker), DateTime.UtcNow.ToString("o"));
            int n = 0;
            foreach (var dir in Directory.EnumerateDirectories(src.SourceDir, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src.SourceDir, dir)));
            foreach (var f in Directory.EnumerateFiles(src.SourceDir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                LinkOrCopy(f, Path.Combine(dst, Path.GetRelativePath(src.SourceDir, f))); n++;
            }
            File.Delete(Path.Combine(dst, CopyingMarker));
            File.WriteAllText(Path.Combine(dst, CompleteMarker), DateTime.UtcNow.ToString("o"));
            _log.Info($"cap: xong {id}/{src.DirName}, {n} file");
        }

        // ZaloCap.exe phai co mat: doi lai tu .off, hoac tao lai tu ban cai goc
        var cap = CapExeIn(dst);
        if (!File.Exists(cap))
        {
            if (File.Exists(cap + CapOffSuffix)) File.Move(cap + CapOffSuffix, cap);
            else
            {
                var orig = CapExeIn(src.SourceDir);
                if (!File.Exists(orig)) throw new FileNotFoundException("Ban cai goc khong co ZaloCap.exe", orig);
                LinkOrCopy(orig, cap);
            }
        }

        // don phien ban cu cua chinh id nay
        foreach (var d in Directory.EnumerateDirectories(idDir))
        {
            if (string.Equals(Path.GetFileName(d), src.DirName, StringComparison.OrdinalIgnoreCase) || !AppPaths.IsUnder(d, _paths.CapRoot)) continue;
            try { Directory.Delete(d, true); _log.Info($"cap: xoa ban cu {id}/{Path.GetFileName(d)}"); }
            catch (Exception e) { _log.Warn($"cap: khong xoa duoc {id}/{Path.GetFileName(d)}: {e.Message}"); }
        }
        var exe = Path.Combine(dst, "Zalo.exe");
        if (!Verified(exe)) throw new InvalidOperationException("Ban sao Zalo.exe khong qua kiem tra chu ky sau khi tao.");
        return exe;
    }

    /// <summary>Xoa zalo-cap\&lt;id&gt; cua moi id KHAC keepId va khong dang chay. Chi xoa duoi CapRoot.</summary>
    public IReadOnlyList<string> PruneCapCopies(string? keepId, Func<string, bool> isRunning)
    {
        var removed = new List<string>();
        if (!Directory.Exists(_paths.CapRoot)) return removed;
        foreach (var d in Directory.EnumerateDirectories(_paths.CapRoot))
        {
            var name = Path.GetFileName(d);
            if (!AppPaths.IsValidId(name) || !AppPaths.IsUnder(d, _paths.CapRoot)) continue;
            if (string.Equals(name, keepId, StringComparison.OrdinalIgnoreCase) || isRunning(name)) continue;
            if ((new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) != 0) continue;
            try { Directory.Delete(d, true); removed.Add(name); _log.Info($"cap: xoa ban sao cua {name}"); }
            catch (Exception e) { _log.Warn($"cap: khong xoa duoc {name}: {e.Message}"); }
        }
        return removed;
    }
}
