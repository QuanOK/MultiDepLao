using Microsoft.Win32;
using MultiDepLao.Core;

namespace MultiDepLao.App;

/// <summary>Luu lenh khoi dong cung Windows o HKCU\Software\Microsoft\Windows\CurrentVersion\Run (REG_SZ, khong can admin).</summary>
internal sealed class RegistryAutoStartStore : IAutoStartStore
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return k?.GetValue(AutoStart.ValueName) as string;
    }

    public void Write(string command)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
        k.SetValue(AutoStart.ValueName, command, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        k?.DeleteValue(AutoStart.ValueName, false);
    }
}

/// <summary>Chi DOC: Zalo co tu khoi dong cung Windows khong. Khong bao gio ghi/xoa muc cua Zalo.</summary>
internal sealed class RegistryRunKeyReader : IRunKeyReader
{
    public bool HasZaloEntry()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
        return k?.GetValue("Zalo") != null;
    }
}
