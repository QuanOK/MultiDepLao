using System.Security.Cryptography;
using System.Text.Json;

namespace MultiDepLao.Core;

/// <summary>
/// Khoa giao dien bang mat khau (SPEC 4.7). Luu PBKDF2-SHA256 trong app.lock. Chi la khoa giao dien:
/// file hong/thieu -> coi nhu khong co mat khau. KHONG bao gio ghi mat khau/bam vao log.
/// </summary>
public sealed class AppLock
{
    public const int MinLength = 6;
    public const int MaxFailures = 5;
    public static readonly TimeSpan Penalty = TimeSpan.FromSeconds(30);

    private sealed class LockFileData
    {
        public string Salt { get; set; } = "";
        public string Hash { get; set; } = "";
        public int Iterations { get; set; }
    }

    private readonly AppPaths _paths;
    private readonly ILog _log;
    private readonly int _iterations;
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private int _failures;
    private DateTime _blockedUntil = DateTime.MinValue;

    public AppLock(AppPaths paths, ILog? log = null, int iterations = 600_000, Func<DateTime>? now = null)
    {
        _paths = paths; _log = log ?? NullLog.Instance; _iterations = iterations; _now = now ?? (() => DateTime.UtcNow);
    }

    private LockFileData? Read()
    {
        var f = _paths.LockFile;
        if (!File.Exists(f)) return null;
        try
        {
            var d = JsonSerializer.Deserialize<LockFileData>(File.ReadAllText(f));
            if (d == null || d.Iterations < 1 || Convert.FromBase64String(d.Salt).Length != 16 || Convert.FromBase64String(d.Hash).Length != 32)
                throw new JsonException("sai dinh dang");
            return d;
        }
        catch (Exception e) when (e is JsonException or FormatException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _log.Warn("app.lock hong, coi nhu chua dat mat khau");
            return null;
        }
    }

    public bool HasPassword => Read() != null;

    /// <summary>Con bao lau nua moi duoc thu lai (null = duoc thu ngay).</summary>
    public TimeSpan? RetryAfter
    {
        get
        {
            lock (_gate)
            {
                var left = _blockedUntil - _now();
                return left > TimeSpan.Zero ? left : null;
            }
        }
    }

    private static byte[] Derive(string pw, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(pw, salt, iterations, HashAlgorithmName.SHA256, 32);

    private void Write(string pw)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var data = new LockFileData { Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(Derive(pw, salt, _iterations)), Iterations = _iterations };
        Directory.CreateDirectory(_paths.Root);
        AtomicFile.WriteAllText(_paths.LockFile, JsonSerializer.Serialize(data));   // file tam ten rieng + xep hang + thu lai
    }

    private static bool Valid(string? pw) => pw != null && pw.Length >= MinLength;

    public bool SetPassword(string newPw)
    {
        if (!Valid(newPw) || HasPassword) return false;
        Write(newPw);
        _log.Info("da dat mat khau khoa app");
        return true;
    }

    public bool ChangePassword(string oldPw, string newPw)
    {
        if (!Valid(newPw) || !Verify(oldPw)) return false;
        Write(newPw);
        _log.Info("da doi mat khau khoa app");
        return true;
    }

    public bool RemovePassword(string oldPw)
    {
        if (!Verify(oldPw)) return false;
        try { File.Delete(_paths.LockFile); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        _log.Info("da bo mat khau khoa app");
        return true;
    }

    /// <summary>Kiem tra mat khau. Khong co mat khau -> true. Dang bi chan (RetryAfter) -> false, khong tinh them.</summary>
    public bool Verify(string? pw)
    {
        var d = Read();
        if (d == null) return true;
        lock (_gate)
        {
            if (_blockedUntil - _now() > TimeSpan.Zero) return false;
            bool ok = false;
            try
            {
                var actual = Derive(pw ?? "", Convert.FromBase64String(d.Salt), d.Iterations);
                ok = CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(d.Hash));
            }
            catch (Exception e) when (e is FormatException or ArgumentException) { }
            if (ok) { _failures = 0; _blockedUntil = DateTime.MinValue; return true; }
            if (++_failures >= MaxFailures)
            {
                _blockedUntil = _now() + Penalty;
                _log.Warn($"sai mat khau {_failures} lan lien tiep, tam khoa {Penalty.TotalSeconds:0} giay");
            }
            return false;
        }
    }
}
