using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Platform;

namespace HultUI;

/// <summary>The platform's secret store: the Windows Credential Manager on Windows, nothing
/// elsewhere yet (macOS Keychain and libsecret are the obvious next two). Apps take an
/// <see cref="ICredentialStore"/> in their constructor and default to this, so tests can hand
/// in an <see cref="InMemoryCredentialStore"/> and never touch the developer's machine.</summary>
public static class CredentialStore
{
    public static ICredentialStore Default { get; } =
        OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new NoCredentialStore();
}

/// <summary>
/// The Windows Credential Manager, through <c>CredReadW</c> / <c>CredWriteW</c> /
/// <c>CredDeleteW</c> — the store the OS itself uses for saved passwords, visible to the
/// user under "Windows Credentials" as generic entries named <c>service|account</c>.
/// </summary>
/// <remarks>
/// Straight P/Invoke, no COM, so it survives NativeAOT. Secrets are stored as UTF-16 blobs,
/// which is what the Credential Manager UI expects when it shows them. A generic credential's
/// blob is capped at 2560 bytes (1280 UTF-16 characters) by the OS; API tokens are a tenth
/// of that, but a value that long is refused rather than truncated. Entries persist for the
/// local machine profile (<c>CRED_PERSIST_LOCAL_MACHINE</c>), which is the setting that
/// survives a log-out without roaming the secret to another PC.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsCredentialStore : ICredentialStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int MaxBlobBytes = 5 * 512;
    private const int ErrorNotFound = 1168;

    public string? Read(string service, string account)
    {
        if (!CredReadW(Target(service, account), CredTypeGeneric, 0, out var credPtr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<Credential>(credPtr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0) return "";
            return Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public bool Write(string service, string account, string secret)
    {
        if (secret.Length * 2 > MaxBlobBytes) return false;
        var blob = Marshal.StringToHGlobalUni(secret);
        var target = Marshal.StringToHGlobalUni(Target(service, account));
        var user = Marshal.StringToHGlobalUni(account);
        try
        {
            var cred = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlob = blob,
                CredentialBlobSize = (uint)(secret.Length * 2),
                Persist = CredPersistLocalMachine,
                UserName = user,
            };
            return CredWriteW(ref cred, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(target);
            Marshal.FreeHGlobal(user);
        }
    }

    public bool Delete(string service, string account)
    {
        if (CredDeleteW(Target(service, account), CredTypeGeneric, 0)) return true;
        // Nothing to delete is the outcome the caller wanted.
        return Marshal.GetLastPInvokeError() == ErrorNotFound;
    }

    private static string Target(string service, string account) => $"{service}|{account}";

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredReadW(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDeleteW(string target, int type, int flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr buffer);
}
