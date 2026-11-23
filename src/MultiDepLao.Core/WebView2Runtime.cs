using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace MultiDepLao.Core;

public enum InstallFailure { None, NoNetwork, BadSignature, ExitCode, Timeout, NotDetected, Cancelled, Other }

public sealed record InstallResult(bool Success, InstallFailure Failure, string? Detail)
{
    public static InstallResult Ok(string? version = null) => new(true, InstallFailure.None, version);
}

/// <summary>
/// Phat hien / cai WebView2 Evergreen Runtime (khong can admin). Chi goi InstallAsync sau khi nguoi dung dong y.
/// Khong gui thong tin nguoi dung di dau; chi tai bo cai tu Microsoft va kiem tra chu ky.
/// </summary>
public static class WebView2Runtime
{
    public const string BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    public const string ClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    public static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(5);

    // ------------------------------------------------------------ phat hien

    /// <summary>pv hop le (khong rong, khong "0.0.0.0") -> chinh no; nguoc lai null.</summary>
    public static string? NormalizeVersion(string? pv)
    {
        var v = pv?.Trim();
        return string.IsNullOrEmpty(v) || v == "0.0.0.0" ? null : v;
    }

    /// <summary>Phien ban dau tien hop le trong danh sach (thu tu uu tien) hoac null.</summary>
    public static string? FirstValid(IEnumerable<string?> pvs)
    {
        foreach (var p in pvs) if (NormalizeVersion(p) is { } v) return v;
        return null;
    }

    /// <summary>Doc registry: HKLM WOW6432Node, HKLM, HKCU. Null = chua cai.</summary>
    public static string? InstalledVersion()
    {
        try
        {
            return FirstValid(new[]
            {
                ReadPv(RegistryHive.LocalMachine, RegistryView.Registry32),
                ReadPv(RegistryHive.LocalMachine, RegistryView.Registry64),
                ReadPv(RegistryHive.CurrentUser, RegistryView.Default),
            });
        }
        catch { return null; }
    }

    private static string? ReadPv(RegistryHive hive, RegistryView view)
    {
        try
        {
            using var b = RegistryKey.OpenBaseKey(hive, view);
            // Registry32 tren HKLM = SOFTWARE\WOW6432Node\... (tu dong chuyen huong)
            using var k = b.OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + ClientGuid);
            return k?.GetValue("pv") as string;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------ ket qua (thuan, test duoc)

    /// <summary>Quyet dinh ket qua sau khi chay bo cai.</summary>
    public static InstallResult Decide(bool timedOut, int exitCode, string? versionAfter)
    {
        if (timedOut) return new(false, InstallFailure.Timeout, "het gio (5 phut)");
        if (exitCode != 0) return new(false, InstallFailure.ExitCode, $"ma thoat {exitCode}");
        var v = NormalizeVersion(versionAfter);
        return v != null ? InstallResult.Ok(v) : new(false, InstallFailure.NotDetected, "cai xong nhung chua nhan ra thanh phan");
    }

    /// <summary>Chu the chung chi phai la Microsoft Corporation (truong O=).</summary>
    public static bool IsMicrosoftSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        foreach (var part in subject.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("O=", StringComparison.OrdinalIgnoreCase))
                return p.Substring(2).Trim().Trim('"').Equals("Microsoft Corporation", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    // ------------------------------------------------------------ cai dat

    /// <summary>Tai bo cai, kiem tra chu ky, chay /silent /install (khong admin), doi toi da 5 phut, xoa file tam.</summary>
    public static Task<InstallResult> InstallAsync(AppPaths paths, IProgress<string>? progress = null, CancellationToken ct = default) =>
        InstallAsync(paths, progress, ct, null, null);

    /// <summary>Co the thay buoc tai va buoc kiem tra chu ky (test); null = dung that.</summary>
    public static async Task<InstallResult> InstallAsync(AppPaths paths, IProgress<string>? progress, CancellationToken ct,
        Func<string, CancellationToken, Task>? download, Func<string, bool>? verify)
    {
        Directory.CreateDirectory(paths.TmpDir);
        var file = Path.Combine(paths.TmpDir, "MicrosoftEdgeWebview2Setup.exe");
        FileStream? hold = null;   // giu file (FileShare.Read) tu luc kiem chu ky den khi bo cai thoat: khong ai thay/xoa/ghi duoc
        try
        {
            progress?.Report("download");
            try
            {
                if (download != null) await download(file, ct);
                else await DownloadAsync(file, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new(false, InstallFailure.Cancelled, null); }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or System.Net.Sockets.SocketException)
            { return new(false, InstallFailure.NoNetwork, e.GetType().Name); }

            progress?.Report("verify");
            try { hold = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(false, InstallFailure.Other, "khong khoa duoc file bo cai"); }
            if (!(verify ?? VerifyMicrosoftSignature)(file)) return new(false, InstallFailure.BadSignature, "chu ky khong hop le");

            progress?.Report("install");
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = file, Arguments = "/silent /install", UseShellExecute = false, CreateNoWindow = true,
            });
            if (p == null) return new(false, InstallFailure.Other, "khong khoi chay duoc bo cai");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(InstallTimeout);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) return new(false, InstallFailure.Cancelled, null);
                return Decide(true, 0, null);
            }
            return Decide(false, p.ExitCode, InstalledVersion());
        }
        catch (Win32Exception e) { return new(false, InstallFailure.Other, e.Message); }
        finally
        {
            hold?.Dispose();
            try { File.Delete(file); } catch { /* bo qua */ }
        }
    }

    private static async Task DownloadAsync(string file, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        using var resp = await http.GetAsync(BootstrapperUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(file);
        await src.CopyToAsync(dst, ct);
    }

    /// <summary>Authenticode hop le (WinVerifyTrust) VA chu the la Microsoft Corporation.</summary>
    public static bool VerifyMicrosoftSignature(string file) => Authenticode.VerifySigned(file, IsMicrosoftSubject);
}
