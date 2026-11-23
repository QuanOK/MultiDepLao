namespace MultiDepLao.Core;

public static class ChildEnvironment
{
    private static readonly string[] StripPrefixes = { "ELECTRON", "VSCODE", "NODE_", "CHROME" };

    public static Dictionary<string, string> FromCurrent()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            d[(string)e.Key] = (string?)e.Value ?? "";
        return d;
    }

    public static string RoamingDir(string profileDir) => Path.Combine(profileDir, "AppData", "Roaming");
    public static string LocalDir(string profileDir) => Path.Combine(profileDir, "AppData", "Local");
    public static string TempDir(string profileDir) => Path.Combine(LocalDir(profileDir), "Temp");

    /// <summary>Giong spike\launch-profile.ps1: bo ELECTRON*/VSCODE*/NODE_*/CHROME*, dat USERPROFILE/APPDATA/LOCALAPPDATA/TEMP/TMP.</summary>
    public static Dictionary<string, string> Build(IEnumerable<KeyValuePair<string, string>> currentEnv, string profileDir)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in currentEnv)
        {
            if (StripPrefixes.Any(p => kv.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            d[kv.Key] = kv.Value;
        }
        d["USERPROFILE"] = profileDir;
        d["APPDATA"] = RoamingDir(profileDir);
        d["LOCALAPPDATA"] = LocalDir(profileDir);
        d["TEMP"] = TempDir(profileDir);
        d["TMP"] = TempDir(profileDir);
        return d;
    }

    public static void EnsureDirectories(string profileDir)
    {
        Directory.CreateDirectory(RoamingDir(profileDir));
        Directory.CreateDirectory(LocalDir(profileDir));
        Directory.CreateDirectory(TempDir(profileDir));
    }
}
