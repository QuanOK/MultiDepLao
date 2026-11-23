using System.Security;

namespace MultiDepLao.Core;

public sealed record DataRootInfo(string Root, bool IsPortable, string? Error);

/// <summary>Noi DUY NHAT tinh thu muc du lieu cua app (thuong hoac portable).</summary>
public static class DataRoot
{
    public const string PortableMarker = "portable.txt";
    public const string PortableDataDir = "data";
    public const string AppDirName = "MultiDepLao";
    public const string OldAppDirName = "MultiZalo";

    public static string LocalRoot(string name = AppDirName, string? localAppData = null) =>
        Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name);

    public static DataRootInfo Resolve(string exeDir, string? localAppData = null, Action<string>? writeProbe = null)
    {
        if (!File.Exists(Path.Combine(exeDir, PortableMarker)))
            return new(LocalRoot(AppDirName, localAppData), false, null);

        var root = Path.GetFullPath(Path.Combine(exeDir, PortableDataDir));
        writeProbe ??= DefaultWriteProbe;
        try
        {
            writeProbe(root);
            return new(root, true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new(root, true, Loc.F("Boot.DataRootError", root, ex.Message));
        }
    }

    /// <summary>Portable thi bo qua chuyen du lieu MultiZalo cu va kiem tra mutex ban cu.</summary>
    public static bool ShouldMigrate(DataRootInfo info) => !info.IsPortable;

    private static void DefaultWriteProbe(string root)
    {
        Directory.CreateDirectory(root);
        var f = Path.Combine(root, ".write-test-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(f, "x");
        File.Delete(f);
    }
}
