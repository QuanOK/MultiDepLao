using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiDepLao.Core;

/// <summary>Pc = chay Zalo.exe co lap; Web = chat.zalo.me trong WebView2 (khong bao gio khoi chay Zalo.exe).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccountKind { Pc, Web }

public sealed class AccountConfig
{
    public string Id { get; set; } = "";
    /// <summary>Thieu trong config cu -> Pc. Ghi dang chuoi "Pc"/"Web".</summary>
    public AccountKind Kind { get; set; } = AccountKind.Pc;
    public string DisplayName { get; set; } = "";
    public int Order { get; set; }
    public bool Pinned { get; set; }
    public bool AutoStart { get; set; }
    /// <summary>Mau tuy chon "#RRGGBB"; null = mau mac dinh theo id (<see cref="AccountPalette"/>).</summary>
    public string? Color { get; set; }
}

/// <summary>Bang 10 mau cho o tai khoan trong sidebar + chu cai dau (bo dau tieng Viet).</summary>
public static class AccountPalette
{
    public static readonly string[] Colors =
    {
        "#E53935", "#D81B60", "#8E24AA", "#5E35B1", "#3949AB",
        "#1E88E5", "#00897B", "#43A047", "#F4511E", "#6D4C41",
    };

    public static bool IsValidHex(string? s) =>
        s is { Length: 7 } && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);

    /// <summary>Mau mac dinh on dinh theo id (FNV-1a; khong dung string.GetHashCode vi bi ngau nhien hoa moi lan chay).</summary>
    public static string DefaultFor(string id)
    {
        uint h = 2166136261;
        foreach (var c in id.ToLowerInvariant()) { h ^= c; h *= 16777619; }
        return Colors[(int)(h % (uint)Colors.Length)];
    }

    public static string Effective(string id, string? custom) => IsValidHex(custom) ? custom!.ToUpperInvariant() : DefaultFor(id);

    /// <summary>Chu cai/chu so dau tien, viet hoa, bo dau; khong co thi "?".</summary>
    public static string Initial(string? name)
    {
        foreach (var c in (name ?? "").Trim().Normalize(System.Text.NormalizationForm.FormD))
        {
            if (c is 'đ' or 'Đ') return "D";
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        }
        return "?";
    }
}

public sealed class LockConfig
{
    public bool Enabled { get; set; }
    public int IdleMinutes { get; set; } = 10;
    public bool LockOnStart { get; set; } = true;
    public bool LockOnMinimize { get; set; }
}

public sealed class AppConfig
{
    public List<AccountConfig> Accounts { get; set; } = new();
    public string? CallOwnerId { get; set; }
    /// <summary>Tai khoan duy nhat chay Zalo kem ZaloCap.exe (chup man hinh); null = khong ai.</summary>
    public string? CaptureOwnerId { get; set; }
    /// <summary>null = tu tinh theo RAM.</summary>
    public int? MaxRunning { get; set; }
    /// <summary>null/0 = tat tu dong cho ngu.</summary>
    public int? AutoSleepMinutes { get; set; }
    public LockConfig Lock { get; set; } = new();
    /// <summary>Sidebar thu gon (cot bieu tuong hep).</summary>
    public bool SidebarCollapsed { get; set; }
    /// <summary>Chieu rong sidebar khi mo rong (DIP). null = mac dinh.</summary>
    public double? SidebarWidth { get; set; }

    private string _theme = "system";
    /// <summary>"system" | "light" | "dark"; gia tri la -> "system".</summary>
    public string Theme { get => _theme; set => _theme = NormalizeTheme(value); }

    public static string NormalizeTheme(string? v) => v?.Trim().ToLowerInvariant() switch
    {
        "light" => "light",
        "dark" => "dark",
        _ => "system",
    };

    private string _accent = "teal";
    /// <summary>"teal" | "blue" | "violet" | "amber"; gia tri la/null -> "teal".</summary>
    public string Accent { get => _accent; set => _accent = NormalizeAccent(value); }

    public static string NormalizeAccent(string? v) => v?.Trim().ToLowerInvariant() switch
    {
        "blue" => "blue",
        "violet" => "violet",
        "amber" => "amber",
        _ => "teal",
    };

    private string _language = "vi";
    /// <summary>"vi" | "en"; gia tri la/null -> "vi".</summary>
    public string Language { get => _language; set => _language = Loc.Normalize(value); }

    public const double SidebarMinWidth = 180, SidebarMaxWidth = 420, SidebarDefaultWidth = 240;

    public static double ClampSidebarWidth(double? w) =>
        w is double v && !double.IsNaN(v) && !double.IsInfinity(v)
            ? Math.Clamp(v, SidebarMinWidth, SidebarMaxWidth) : SidebarDefaultWidth;
    [JsonIgnore] public double EffectiveSidebarWidth => ClampSidebarWidth(SidebarWidth);
    /// <summary>Bao "co tin chua doc" tren sidebar bang UI Automation (cong tac tat).</summary>
    public bool UnreadBadge { get; set; } = true;
    /// <summary>"Thong bao kin": app tu gui toast "N tin nhan moi" (khong noi dung) cho tai khoan Zalo PC.</summary>
    public bool PcPrivateNotify { get; set; }
    /// <summary>Phien ban app lan cuoi da hien "Co gi moi"; null = chua tung ghi.</summary>
    public string? LastSeenVersion { get; set; }
}

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly AppPaths _paths;
    public ConfigStore(AppPaths paths) => _paths = paths;

    /// <summary>Ket qua lam sach cua lan <see cref="Load"/> gan nhat (chi so luong, khong co noi dung).</summary>
    public sealed record SanitizeReport(int DroppedInvalidId, int DroppedDuplicate, int OwnersCleared)
    {
        public bool Changed => DroppedInvalidId + DroppedDuplicate + OwnersCleared > 0;
    }

    /// <summary>Lan Load gan nhat co bo/sua muc nao khong (null = khong).</summary>
    public SanitizeReport? LastSanitize { get; private set; }

    /// <summary>
    /// Bo tai khoan co Id khong hop le (<see cref="AppPaths.IsValidId"/>) hoac trung (khong phan biet hoa thuong, giu muc dau);
    /// CallOwnerId/CaptureOwnerId khong hop le hoac khong con trong danh sach -> null. Sua truc tiep cfg.
    /// </summary>
    public static SanitizeReport Sanitize(AppConfig cfg)
    {
        int bad = 0, dup = 0, owners = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keep = new List<AccountConfig>();
        foreach (var a in cfg.Accounts)
        {
            if (a == null || !AppPaths.IsValidId(a.Id)) { bad++; continue; }
            if (!seen.Add(a.Id)) { dup++; continue; }
            keep.Add(a);
        }
        if (keep.Count != cfg.Accounts.Count) cfg.Accounts = keep;
        if (cfg.CallOwnerId != null && !seen.Contains(cfg.CallOwnerId)) { cfg.CallOwnerId = null; owners++; }
        if (cfg.CaptureOwnerId != null && !seen.Contains(cfg.CaptureOwnerId)) { cfg.CaptureOwnerId = null; owners++; }
        return new SanitizeReport(bad, dup, owners);
    }

    /// <summary>
    /// Doc cau hinh. File thieu -> mac dinh (khong ghi dia). File hong -> sao luu .bad + mac dinh.
    /// Id tai khoan sai/trung duoc bo (sao luu file goc sang .bad, xem <see cref="LastSanitize"/>).
    /// </summary>
    public AppConfig Load()
    {
        LastSanitize = null;
        var f = _paths.ConfigFile;
        if (!File.Exists(f)) return new AppConfig();
        try
        {
            var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(f), Json);
            if (cfg == null) throw new JsonException("null");
            cfg.Accounts ??= new();
            cfg.Lock ??= new();
            var rep = Sanitize(cfg);
            if (rep.Changed)
            {
                LastSanitize = rep;
                try { File.Copy(f, f + ".bad", true); } catch { /* bo qua */ }
            }
            return cfg;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try { File.Copy(f, f + ".bad", true); } catch { /* bo qua */ }
            return new AppConfig();
        }
    }

    /// <summary>Ghi an toan (file tam ten rieng + xep hang + thu lai): xem <see cref="AtomicFile"/>.</summary>
    public void Save(AppConfig cfg)
    {
        Directory.CreateDirectory(_paths.Root);
        AtomicFile.WriteAllText(_paths.ConfigFile, JsonSerializer.Serialize(cfg, Json));
    }
}



