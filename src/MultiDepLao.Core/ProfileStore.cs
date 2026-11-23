namespace MultiDepLao.Core;

public sealed class ProfileStore
{
    private readonly AppPaths _paths;
    private readonly ConfigStore _store;
    private readonly Func<string, bool> _isRunning;
    private readonly object _lock = new();
    public AppConfig Config { get; private set; }

    public ProfileStore(AppPaths paths, ConfigStore store, Func<string, bool> isRunning)
    {
        _paths = paths; _store = store; _isRunning = isRunning;
        Config = store.Load();
    }

    public IReadOnlyList<AccountConfig> Accounts
    {
        get { lock (_lock) return Config.Accounts.OrderBy(a => a.Order).ThenBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToList(); }
    }

    /// <summary>Tai khoan Pc (co the khoi chay Zalo.exe). Id khong co trong config -> false.</summary>
    public bool IsPc(string? id)
    {
        if (id == null) return false;
        lock (_lock) return Config.Accounts.Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && a.Kind == AccountKind.Pc);
    }

    /// <summary>Tai khoan Web (WebView2). Id la/khong co trong config -> false.</summary>
    public bool IsWeb(string? id)
    {
        if (id == null) return false;
        lock (_lock) return Config.Accounts.Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && a.Kind == AccountKind.Web);
    }

    /// <summary>Chi cac tai khoan Pc, cung thu tu nhu <see cref="Accounts"/>.</summary>
    public IReadOnlyList<AccountConfig> PcAccounts => Accounts.Where(a => a.Kind == AccountKind.Pc).ToList();

    public AccountConfig Add(string displayName, AccountKind kind = AccountKind.Pc)
    {
        lock (_lock)
        {
            string id = "";
            for (int i = 1; i < 1000; i++)
            {
                var c = $"acc-{i:00}";
                if (Config.Accounts.Any(a => a.Id.Equals(c, StringComparison.OrdinalIgnoreCase))) continue;
                if (Directory.Exists(_paths.ProfileDir(c))) continue;
                if (Directory.Exists(Path.Combine(_paths.WebDataDir, "EBWebView", c))) continue;   // profile WebView2 con sot
                id = c; break;
            }
            if (id == "") throw new InvalidOperationException("Het id profile.");
            // Web: khong tao profiles\<id> (khong chay Zalo.exe); WebView2 tu tao du lieu duoi WebDataDir.
            if (kind == AccountKind.Pc) Directory.CreateDirectory(_paths.ProfileDir(id));
            var acc = new AccountConfig
            {
                Id = id,
                Kind = kind,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim(),
                Order = Config.Accounts.Count == 0 ? 0 : Config.Accounts.Max(a => a.Order) + 1,
            };
            Config.Accounts.Add(acc);
            _store.Save(Config);
            return acc;
        }
    }

    private AccountConfig Get(string id) =>
        Config.Accounts.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Khong co tai khoan '{id}'");

    public void Rename(string id, string displayName)
    {
        lock (_lock) { Get(id).DisplayName = displayName.Trim(); _store.Save(Config); }
    }

    public void SetPinned(string id, bool pinned)
    {
        lock (_lock) { Get(id).Pinned = pinned; _store.Save(Config); }
    }

    public void SetAutoStart(string id, bool value)
    {
        lock (_lock) { Get(id).AutoStart = value; _store.Save(Config); }
    }

    /// <summary>hex "#RRGGBB" hoac null/rong = mau mac dinh.</summary>
    public void SetColor(string id, string? hex)
    {
        if (!string.IsNullOrEmpty(hex) && !AccountPalette.IsValidHex(hex)) throw new ArgumentException("Mau khong hop le", nameof(hex));
        lock (_lock) { Get(id).Color = string.IsNullOrEmpty(hex) ? null : hex!.ToUpperInvariant(); _store.Save(Config); }
    }

    public void SetSidebarCollapsed(bool value)
    {
        lock (_lock) { Config.SidebarCollapsed = value; _store.Save(Config); }
    }

    public void SetSidebarWidth(double value)
    {
        lock (_lock) { Config.SidebarWidth = AppConfig.ClampSidebarWidth(value); _store.Save(Config); }
    }

    public void SetTheme(string? value)
    {
        lock (_lock) { Config.Theme = AppConfig.NormalizeTheme(value); _store.Save(Config); }
    }

    public void SetAccent(string? value)
    {
        lock (_lock) { Config.Accent = AppConfig.NormalizeAccent(value); _store.Save(Config); }
    }

    public void SetLastSeenVersion(string? value)
    {
        lock (_lock) { if (Config.LastSeenVersion == value) return; Config.LastSeenVersion = value; _store.Save(Config); }
    }

    public void SetLanguage(string? value)
    {
        lock (_lock) { Config.Language = Loc.Normalize(value); _store.Save(Config); }
    }

    public void SetMaxRunning(int? value)
    {
        lock (_lock) { Config.MaxRunning = value is { } v ? Math.Max(1, v) : null; _store.Save(Config); }
    }

    public void SetPcPrivateNotify(bool on)
    {
        lock (_lock) { Config.PcPrivateNotify = on; _store.Save(Config); }
    }

    public void SetAutoSleepMinutes(int? minutes)
    {
        lock (_lock) { Config.AutoSleepMinutes = minutes is > 0 ? minutes : null; _store.Save(Config); }
    }

    public void SetLock(bool enabled, bool lockOnStart, bool lockOnMinimize, int idleMinutes)
    {
        lock (_lock)
        {
            var l = Config.Lock;
            l.Enabled = enabled; l.LockOnStart = lockOnStart; l.LockOnMinimize = lockOnMinimize; l.IdleMinutes = Math.Max(0, idleMinutes);
            _store.Save(Config);
        }
    }

    /// <summary>Tai khoan Web khong the giu quyen goi (ArgumentException).</summary>
    public void SetCallOwner(string? id)
    {
        if (IsWeb(id)) throw new ArgumentException("Tai khoan Web khong giu quyen goi.", nameof(id));
        lock (_lock) { Config.CallOwnerId = id; _store.Save(Config); }
    }

    /// <summary>Tai khoan Web khong the giu quyen chup (ArgumentException).</summary>
    public void SetCaptureOwner(string? id)
    {
        if (IsWeb(id)) throw new ArgumentException("Tai khoan Web khong giu quyen chup.", nameof(id));
        lock (_lock) { Config.CaptureOwnerId = id; _store.Save(Config); }
    }

    /// <summary>Dat thu tu theo danh sach id (id thieu giu nguyen o cuoi).</summary>
    public void Reorder(IEnumerable<string> orderedIds)
    {
        lock (_lock)
        {
            int n = 0;
            foreach (var id in orderedIds) Get(id).Order = n++;
            foreach (var a in Config.Accounts.Where(a => !orderedIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase)).OrderBy(a => a.Order))
                a.Order = n++;
            _store.Save(Config);
        }
    }

    public void Delete(string id)
    {
        lock (_lock)
        {
            // TODO (tang App): voi tai khoan Web, truoc khi goi Delete phai xoa profile WebView2
            // (CoreWebView2Profile.Delete()) de bo cookie/dang nhap; o day chi bo cau hinh (khong co profiles\<id>).
            var dir = _paths.ProfileDir(id);   // kiem tra id + nam duoi ProfilesRoot
            if (_isRunning(id)) throw new InvalidOperationException($"Tai khoan '{id}' dang chay, khong the xoa.");
            if (!AppPaths.IsUnder(dir, _paths.ProfilesRoot)) throw new InvalidOperationException("Duong dan ngoai goc profiles.");
            if (Directory.Exists(dir))
            {
                if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Thu muc profile la lien ket (reparse point), tu choi xoa.");
                Directory.Delete(dir, recursive: true);
            }
            Config.Accounts.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(Config.CallOwnerId, id, StringComparison.OrdinalIgnoreCase)) Config.CallOwnerId = null;
            if (string.Equals(Config.CaptureOwnerId, id, StringComparison.OrdinalIgnoreCase)) Config.CaptureOwnerId = null;
            _store.Save(Config);
        }
    }

    /// <summary>Thu muc trong profiles\ co id hop le ma chua co trong config (chi doc, khong ghi).</summary>
    public IReadOnlyList<string> Discover()
    {
        lock (_lock)
        {
            if (!Directory.Exists(_paths.ProfilesRoot)) return Array.Empty<string>();
            return Directory.EnumerateDirectories(_paths.ProfilesRoot)
                .Select(d => Path.GetFileName(d))
                .Where(n => AppPaths.IsValidId(n) && !Config.Accounts.Any(a => a.Id.Equals(n, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Nhan cac thu muc da co (spike-e, spike-f, acc-01...) vao config. Tra ve cac tai khoan moi.</summary>
    public IReadOnlyList<AccountConfig> ImportExisting()
    {
        lock (_lock)
        {
            var added = new List<AccountConfig>();
            int order = Config.Accounts.Count == 0 ? 0 : Config.Accounts.Max(a => a.Order) + 1;
            foreach (var id in Discover())
            {
                var acc = new AccountConfig { Id = id, DisplayName = id, Order = order++ };
                Config.Accounts.Add(acc); added.Add(acc);
            }
            if (added.Count > 0) _store.Save(Config);
            return added;
        }
    }
}


