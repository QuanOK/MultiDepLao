using System.Globalization;
using System.Text.Json;

namespace MultiDepLao.Core;

/// <summary>Noi dung mz.pid. Dinh dang moi: JSON {"pid":n,"createdUtcTicks":n}. Dinh dang cu (spike): chi PID.</summary>
public sealed record PidRecord(int Pid, DateTime? CreatedUtc, DateTime FileWriteUtc)
{
    private sealed class Dto { public int Pid { get; set; } public long CreatedUtcTicks { get; set; } }

    public static PidRecord? TryRead(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var text = File.ReadAllText(file).Trim().TrimStart('\uFEFF');
            var written = File.GetLastWriteTimeUtc(file);
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var legacyPid))
                return new PidRecord(legacyPid, null, written);
            var dto = JsonSerializer.Deserialize<Dto>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null || dto.Pid <= 0 || dto.CreatedUtcTicks <= 0) return null;
            return new PidRecord(dto.Pid, new DateTime(dto.CreatedUtcTicks, DateTimeKind.Utc), written);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void Write(string file, int pid, DateTime createdUtc) =>
        File.WriteAllText(file, JsonSerializer.Serialize(new Dto { Pid = pid, CreatedUtcTicks = createdUtc.ToUniversalTime().Ticks }));
}

public enum VerifyFailure { None, NoPidFile, BadPidFile, ProcessNotFound, WrongExe, OutsideRoot, ChildProcess, IdentityMismatch, BadId }

public sealed record VerifiedInstance(string Id, int Pid, DateTime CreatedUtc, string ExePath);

public sealed record Verification(VerifiedInstance? Instance, VerifyFailure Failure, string Reason)
{
    public bool Ok => Instance != null;
    public static Verification Pass(VerifiedInstance i) => new(i, VerifyFailure.None, "hop le");
    public static Verification Fail(VerifyFailure f, string reason) => new(null, f, reason);
}

/// <summary>Chot an toan truoc moi thao tac tat. Cac dieu kien: mz.pid, song, ten exe, exe duoi NoCapRoot (hoac CapRoot\<id> cua chinh id do), khong phai tien trinh con, thoi diem tao khop.</summary>
public sealed class SafetyGate
{
    public static readonly TimeSpan CreatedTolerance = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan LegacySlack = TimeSpan.FromSeconds(10);

    private readonly AppPaths _paths;
    private readonly IProcessProbe _probe;
    private readonly string _exeName;

    public SafetyGate(AppPaths paths, IProcessProbe probe, string exeName = "Zalo.exe")
    { _paths = paths; _probe = probe; _exeName = exeName; }

    public Verification Verify(string id)
    {
        string pidFile;
        try { pidFile = _paths.PidFile(id); }
        catch (ArgumentException e) { return Verification.Fail(VerifyFailure.BadId, e.Message); }

        if (!File.Exists(pidFile)) return Verification.Fail(VerifyFailure.NoPidFile, "thieu mz.pid");
        var rec = PidRecord.TryRead(pidFile);
        if (rec == null) return Verification.Fail(VerifyFailure.BadPidFile, "mz.pid khong doc duoc");

        var info = _probe.Get(rec.Pid);
        if (info == null) return Verification.Fail(VerifyFailure.ProcessNotFound, $"tien trinh {rec.Pid} khong ton tai");
        if (!string.Equals(info.Name, _exeName, StringComparison.OrdinalIgnoreCase))
            return Verification.Fail(VerifyFailure.WrongExe, $"ten exe '{info.Name}' khong phai {_exeName}");
        if (string.IsNullOrEmpty(info.ExePath) ||
            !(AppPaths.IsUnder(info.ExePath, _paths.NoCapRoot) || AppPaths.IsUnder(info.ExePath, _paths.CapDir(id))))
            return Verification.Fail(VerifyFailure.OutsideRoot, "exe nam ngoai zalo-nocap va zalo-cap\\<id>");
        if (info.CommandLine != null && info.CommandLine.Contains("--type=", StringComparison.Ordinal))
            return Verification.Fail(VerifyFailure.ChildProcess, "la tien trinh con Electron");

        if (rec.CreatedUtc is { } created)
        {
            if ((info.CreatedUtc - created).Duration() > CreatedTolerance)
                return Verification.Fail(VerifyFailure.IdentityMismatch, "thoi diem tao tien trinh lech mz.pid (PID tai su dung)");
        }
        else if (info.CreatedUtc > rec.FileWriteUtc + LegacySlack)
            return Verification.Fail(VerifyFailure.IdentityMismatch, "tien trinh tao sau mz.pid (PID tai su dung, dinh dang cu)");

        return Verification.Pass(new VerifiedInstance(id, info.Pid, info.CreatedUtc, info.ExePath!));
    }
}

