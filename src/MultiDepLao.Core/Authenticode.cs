using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace MultiDepLao.Core;

/// <summary>Kiem tra chu ky Authenticode (WinVerifyTrust) + doc chu the chung chi nguoi ky. Dung chung cho bo cai WebView2 va Zalo.exe.</summary>
public static class Authenticode
{
    /// <summary>Chu the chung chi cua Zalo that: "CN=VNG GROUP JSC, O=VNG GROUP JSC, ...". Chi so khop truong O=.</summary>
    public const string ZaloSignerOrg = "VNG";

    /// <summary>Gia tri truong O= cua chu the (bo ngoac kep) hoac null.</summary>
    public static string? OrganizationOf(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;
        foreach (var part in subject.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("O=", StringComparison.OrdinalIgnoreCase)) return p.Substring(2).Trim().Trim('"');
        }
        return null;
    }

    /// <summary>Truong O= chua <paramref name="token"/> (khong phan biet hoa thuong).</summary>
    public static bool SubjectOrgContains(string? subject, string token) =>
        OrganizationOf(subject) is { } o && o.Contains(token, StringComparison.OrdinalIgnoreCase);

    /// <summary>Chu ky hop le (WinVerifyTrust) va chu the thoa <paramref name="subjectOk"/>.</summary>
    public static bool VerifySigned(string file, Func<string?, bool> subjectOk)
    {
        try
        {
            if (!WinVerifyTrustOk(file)) return false;
            return subjectOk(SignerSubject(file));
        }
        catch { return false; }
    }

    /// <summary>Chi kiem WinVerifyTrust (khong xet chu the).</summary>
    public static bool IsTrusted(string file)
    {
        try { return WinVerifyTrustOk(file); } catch { return false; }
    }

    private static string SignerSubject(string file)
    {
        // SYSLIB0057 (.NET 9+): X509CertificateLoader khong co ham lay chung chi nguoi ky Authenticode tu file da ky;
        // ham cu van chay dung tren .NET 10. Chu ky da duoc WinVerifyTrust kiem, day chi doc ten chu the.
#pragma warning disable SYSLIB0057
        using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
        return cert.Subject;
    }

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private static bool WinVerifyTrustOk(string file)
    {
        var fi = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = file };
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fi, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,            // WTD_UI_NONE
                fdwRevocationChecks = 0,   // WTD_REVOKE_NONE (tranh phu thuoc mang them)
                dwUnionChoice = 1,         // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 0,
                dwProvFlags = 0x00000010,  // WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            var action = GenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        finally { Marshal.FreeHGlobal(pFile); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);
}
