using System.Runtime.InteropServices;

namespace Meridian.Core;

/// <summary>
/// Where secret values live. The <c>.env</c> file stores only the
/// <c>secret key</c> flag; the value comes from here at resolution time and is
/// never written to the collection.
///
/// <para>An entry belongs to one collection and either one environment or
/// every environment (<c>environment</c> null): the shared value, which an
/// environment without a value of its own uses (<see cref="Secrets.Resolve"/>).
/// Entries written before values were per environment are shared ones, so they
/// keep serving every environment as they always did.</para>
/// </summary>
public interface IKeyring
{
    /// <summary>The stored value, or null when this machine has none. Exactly this entry:
    /// an environment's own value, or (<paramref name="environment"/> null) the shared one.</summary>
    string? Get(string workspaceRoot, string? environment, string key);

    /// <summary>Stores (or replaces) a value for this machine. False when the
    /// platform has no store to write to.</summary>
    bool Set(string workspaceRoot, string? environment, string key, string value);

    /// <summary>Forgets this machine's value, so nothing will offer it again. True when an
    /// entry was there and is gone; false when there was none, or the platform has no store.</summary>
    bool Delete(string workspaceRoot, string? environment, string key);
}

/// <summary>The platform's credential store, or none: only Windows has one so far, and
/// elsewhere a secret comes from an environment variable alone (<see cref="Secrets.Resolve"/>).</summary>
public sealed class DefaultKeyring : IKeyring
{
    private readonly IKeyring? _store = OperatingSystem.IsWindows() ? new WindowsCredentialKeyring() : null;

    public string? Get(string workspaceRoot, string? environment, string key) =>
        _store?.Get(workspaceRoot, environment, key);

    public bool Set(string workspaceRoot, string? environment, string key, string value) =>
        _store?.Set(workspaceRoot, environment, key, value) ?? false;

    public bool Delete(string workspaceRoot, string? environment, string key) =>
        _store?.Delete(workspaceRoot, environment, key) ?? false;
}

/// <summary>Where a secret's value came from: the keyring or a process environment variable,
/// and for this environment alone or for every environment.</summary>
public enum SecretSource { Keyring, SharedKeyring, Variable, SharedVariable }

public readonly record struct SecretValue(string Value, SecretSource Source)
{
    /// <summary>The environment's own value — not one it shares with the others.</summary>
    public bool IsOwn => Source is SecretSource.Keyring or SecretSource.Variable;
}

/// <summary>
/// How a secret key resolves in one environment — the same in the app and the CLI. The most
/// specific value wins:
/// <list type="number">
/// <item>this environment's keyring entry;</item>
/// <item>the process environment variable <c>&lt;env&gt;__&lt;key&gt;</c> (<c>staging__client_secret</c>);</item>
/// <item>the shared keyring entry, for every environment;</item>
/// <item>the process environment variable <c>&lt;key&gt;</c> (<c>client_secret</c>) — how CI injects
/// a secret without any keyring.</item>
/// </list>
/// The variables are what a machine with no keyring has (CI, and Linux and macOS today); the
/// <c>&lt;env&gt;__</c> one lets such a machine hold a different value per environment.
/// </summary>
public static class Secrets
{
    /// <summary>The environment variable that holds <paramref name="key"/> for one environment.</summary>
    public static string VariableName(string environment, string key) => $"{environment}__{key}";

    /// <param name="variables">The process environment, or a stand-in for a test.</param>
    public static SecretValue? Resolve(IKeyring keyring, string workspaceRoot, string environment, string key,
        Func<string, string?>? variables = null)
    {
        variables ??= Environment.GetEnvironmentVariable;
        if (keyring.Get(workspaceRoot, environment, key) is { } own) return new(own, SecretSource.Keyring);
        if (variables(VariableName(environment, key)) is { } ownVariable) return new(ownVariable, SecretSource.Variable);
        if (keyring.Get(workspaceRoot, null, key) is { } shared) return new(shared, SecretSource.SharedKeyring);
        if (variables(key) is { } variable) return new(variable, SecretSource.SharedVariable);
        return null;
    }
}

/// <summary>
/// Windows Credential Manager, via CredReadW/CredWriteW — the same store the
/// OS uses for saved passwords. Entries are scoped per workspace, so two
/// collections' <c>client_secret</c>s never collide: <c>meridian|&lt;workspace-root&gt;|&lt;key&gt;</c>
/// for the shared value (the one name entries have always had), and
/// <c>meridian:&lt;env&gt;|&lt;workspace-root&gt;|&lt;key&gt;</c> for one environment's — an
/// environment's name is a Windows file name, so it never holds the <c>|</c> a key may.
/// AOT-safe straight P/Invoke.
/// </summary>
public sealed partial class WindowsCredentialKeyring : IKeyring
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    public string? Get(string workspaceRoot, string? environment, string key)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!CredReadW(Target(workspaceRoot, environment, key), CredTypeGeneric, 0, out var credPtr)) return null;
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

    public bool Set(string workspaceRoot, string? environment, string key, string value)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var blob = Marshal.StringToHGlobalUni(value);
        var target = Marshal.StringToHGlobalUni(Target(workspaceRoot, environment, key));
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

    public bool Delete(string workspaceRoot, string? environment, string key) =>
        OperatingSystem.IsWindows() && CredDeleteW(Target(workspaceRoot, environment, key), CredTypeGeneric, 0);

    internal static string Target(string workspaceRoot, string? environment, string key) =>
        environment == null ? $"meridian|{workspaceRoot}|{key}" : $"meridian:{environment}|{workspaceRoot}|{key}";

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
