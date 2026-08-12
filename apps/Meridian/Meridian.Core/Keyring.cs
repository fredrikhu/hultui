using System.Runtime.InteropServices;

namespace Meridian.Core;

/// <summary>
/// Where secret values live. The <c>.env</c> file stores only the
/// <c>secret key</c> flag; the value comes from here at resolution time and is
/// never written to the collection.
/// </summary>
public interface IKeyring
{
    /// <summary>The stored value, or null when this machine has none.</summary>
    string? Get(string workspaceRoot, string key);

    /// <summary>Stores (or replaces) a value for this machine. False when the
    /// platform has no store to write to.</summary>
    bool Set(string workspaceRoot, string key, string value);
}

/// <summary>
/// The default chain: the OS credential store first, then a plain process
/// environment variable with the key's own name — which is how CI injects
/// secrets (the mock's "injected by CI" cell) without any keyring at all.
/// </summary>
public sealed class DefaultKeyring : IKeyring
{
    private readonly IKeyring? _store = OperatingSystem.IsWindows() ? new WindowsCredentialKeyring() : null;

    public string? Get(string workspaceRoot, string key) =>
        _store?.Get(workspaceRoot, key) ?? Environment.GetEnvironmentVariable(key);

    public bool Set(string workspaceRoot, string key, string value) =>
        _store?.Set(workspaceRoot, key, value) ?? false;
}

/// <summary>
/// Windows Credential Manager, via CredReadW/CredWriteW — the same store the
/// OS uses for saved passwords. Entries are scoped per workspace:
/// <c>meridian|&lt;workspace-root&gt;|&lt;key&gt;</c>, so two collections'
/// <c>client_secret</c>s never collide. AOT-safe straight P/Invoke.
/// </summary>
public sealed partial class WindowsCredentialKeyring : IKeyring
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public string? Get(string workspaceRoot, string key)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!CredReadW(Target(workspaceRoot, key), CredTypeGeneric, 0, out var credPtr)) return null;
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

    public bool Set(string workspaceRoot, string key, string value)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var blob = Marshal.StringToHGlobalUni(value);
        var target = Marshal.StringToHGlobalUni(Target(workspaceRoot, key));
        try
        {
            var cred = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlob = blob,
                CredentialBlobSize = (uint)(value.Length * 2),
                Persist = CredPersistLocalMachine,
            };
            return CredWriteW(ref cred, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(target);
        }
    }

    private static string Target(string workspaceRoot, string key) => $"meridian|{workspaceRoot}|{key}";

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

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(IntPtr buffer);
}
