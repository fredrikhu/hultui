using System.Runtime.InteropServices;
using System.Text;

namespace Meridian.Core;

/// <summary>
/// Linux's keyring: the freedesktop.org Secret Service (GNOME Keyring, KWallet, KeePassXC), through
/// libsecret, the library those desktops' own apps use. An entry is an item of the default
/// collection — the "login" keyring, unlocked at sign-in — with three attributes: the collection's
/// folder, the environment (<see cref="Shared"/> for the shared value; no environment's name holds a
/// <c>*</c>) and the key, under the schema <see cref="SchemaName"/>. Its label names all three, for
/// Seahorse or KWallet Manager.
///
/// <para>libsecret is loaded when Meridian starts, not linked: a machine without it (a server, a
/// minimal container) has no store, and <see cref="TryCreate"/> says why. One with it but no Secret
/// Service to reach (no session bus, nothing providing org.freedesktop.secrets) learns that on the
/// first call, and <see cref="Unavailable"/> then answers every later one without asking again.</para>
///
/// <para>libsecret's synchronous calls go over D-Bus, a few milliseconds each, and a locked keyring
/// prompts to unlock. The table asks for a value at every build, so answers are remembered for the
/// process (<see cref="KeyringMemo"/>): a change made in Seahorse shows after Meridian restarts.</para>
/// </summary>
public sealed unsafe class SecretServiceKeyring : IKeyring
{
    /// <summary>The schema every Meridian item carries (libsecret's <c>xdg:schema</c> attribute).</summary>
    public const string SchemaName = "io.github.fredrikhu.Meridian";

    /// <summary>The <c>environment</c> attribute of the shared value, which every environment
    /// without its own uses.</summary>
    public const string Shared = "*";

    private const int GIoErrorCancelled = 19;   // G_IO_ERROR_CANCELLED: a dismissed prompt, not a broken store

    private readonly nint _schema;
    private readonly nint _strHash, _strEqual;
    private readonly uint _ioErrorQuark;
    private readonly delegate* unmanaged<nint, nint, nint> _hashTableNew;
    private readonly delegate* unmanaged<nint, nint, nint, int> _hashTableInsert;
    private readonly delegate* unmanaged<nint, void> _hashTableUnref;
    private readonly delegate* unmanaged<nint, void> _errorFree;
    private readonly delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint*, int> _store;
    private readonly delegate* unmanaged<nint, nint, nint, nint*, nint> _lookup;
    private readonly delegate* unmanaged<nint, nint, nint, nint*, int> _clear;
    private readonly delegate* unmanaged<nint, void> _passwordFree;

    private readonly KeyringMemo _memo = new();
    private readonly Lock _native = new();

    /// <summary>Why this machine's Secret Service can't be reached, once a call has found out; null
    /// while it can (or before anything has asked).</summary>
    public string? Unavailable { get; private set; }

    private SecretServiceKeyring(nint secret, nint glib)
    {
        _hashTableNew = (delegate* unmanaged<nint, nint, nint>)NativeLibrary.GetExport(glib, "g_hash_table_new");
        _hashTableInsert = (delegate* unmanaged<nint, nint, nint, int>)NativeLibrary.GetExport(glib, "g_hash_table_insert");
        _hashTableUnref = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(glib, "g_hash_table_unref");
        _errorFree = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(glib, "g_error_free");
        _strHash = NativeLibrary.GetExport(glib, "g_str_hash");
        _strEqual = NativeLibrary.GetExport(glib, "g_str_equal");
        var quarkFromString = (delegate* unmanaged<nint, uint>)NativeLibrary.GetExport(glib, "g_quark_from_string");
        _store = (delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint*, int>)NativeLibrary.GetExport(secret, "secret_password_storev_sync");
        _lookup = (delegate* unmanaged<nint, nint, nint, nint*, nint>)NativeLibrary.GetExport(secret, "secret_password_lookupv_sync");
        _clear = (delegate* unmanaged<nint, nint, nint, nint*, int>)NativeLibrary.GetExport(secret, "secret_password_clearv_sync");
        _passwordFree = (delegate* unmanaged<nint, void>)NativeLibrary.GetExport(secret, "secret_password_free");

        var quarkName = Marshal.StringToCoTaskMemUTF8("g-io-error-quark");
        _ioErrorQuark = quarkFromString(quarkName);
        Marshal.FreeCoTaskMem(quarkName);
        _schema = BuildSchema();
    }

    /// <summary>The store, or null with the reason when libsecret can't be loaded here.</summary>
    public static SecretServiceKeyring? TryCreate(out string? reason)
    {
        reason = null;
        if (!OperatingSystem.IsLinux() || !Environment.Is64BitProcess)
        {
            reason = "the Secret Service store is for 64-bit Linux";
            return null;
        }
        if (!NativeLibrary.TryLoad("libsecret-1.so.0", out var secret))
        {
            reason = "libsecret isn't installed (the libsecret or libsecret-1-0 package)";
            return null;
        }
        if (!NativeLibrary.TryLoad("libglib-2.0.so.0", out var glib))
        {
            reason = "GLib isn't installed";
            return null;
        }
        try
        {
            return new SecretServiceKeyring(secret, glib);
        }
        catch (EntryPointNotFoundException e)
        {
            reason = $"this libsecret is too old ({e.Message})";
            return null;
        }
    }

    public string? Get(string workspaceRoot, string? environment, string key)
    {
        if (_memo.TryRecall(workspaceRoot, environment, key, out var known)) return known;
        if (Unavailable != null) return null;
        lock (_native)
        {
            using var attributes = new Attributes(this, workspaceRoot, environment, key);
            nint error = 0;
            nint found = _lookup(_schema, attributes.Table, 0, &error);
            if (Failed(error)) return null;
            string? value = found == 0 ? null : Marshal.PtrToStringUTF8(found);
            if (found != 0) _passwordFree(found);   // zeroes it as it frees
            _memo.Remember(workspaceRoot, environment, key, value);
            return value;
        }
    }

    public bool Set(string workspaceRoot, string? environment, string key, string value)
    {
        if (Unavailable != null) return false;
        lock (_native)
        {
            using var attributes = new Attributes(this, workspaceRoot, environment, key);
            var label = Marshal.StringToCoTaskMemUTF8(
                $"Meridian: {key} ({environment ?? "every environment"}) in {workspaceRoot}");
            int length = Encoding.UTF8.GetByteCount(value);
            var password = Marshal.StringToCoTaskMemUTF8(value);
            try
            {
                nint error = 0;
                // A null collection is the default one; an item with the same attributes is replaced.
                bool stored = _store(_schema, attributes.Table, 0, label, password, 0, &error) != 0;
                if (Failed(error) || !stored) return false;
                _memo.Remember(workspaceRoot, environment, key, value);
                return true;
            }
            finally
            {
                new Span<byte>((void*)password, length).Clear();
                Marshal.FreeCoTaskMem(password);
                Marshal.FreeCoTaskMem(label);
            }
        }
    }

    public bool Delete(string workspaceRoot, string? environment, string key)
    {
        if (Unavailable != null) return false;
        lock (_native)
        {
            using var attributes = new Attributes(this, workspaceRoot, environment, key);
            nint error = 0;
            bool removed = _clear(_schema, attributes.Table, 0, &error) != 0;
            if (Failed(error))
            {
                _memo.Forget(workspaceRoot, environment, key);   // unknown now: ask again next time
                return false;
            }
            _memo.Remember(workspaceRoot, environment, key, null);
            return removed;
        }
    }

    /// <summary>Frees a GError and says whether there was one. A dismissed prompt fails the one call;
    /// anything else means the store can't be reached, and <see cref="Unavailable"/> keeps the reason.</summary>
    private bool Failed(nint error)
    {
        if (error == 0) return false;
        uint domain = *(uint*)error;
        int code = *(int*)(error + 4);
        string message = Marshal.PtrToStringUTF8(*(nint*)(error + 8)) ?? "unknown error";
        _errorFree(error);
        if (!(domain == _ioErrorQuark && code == GIoErrorCancelled))
            Unavailable = $"no Secret Service to reach: {message}";
        return true;
    }

    /// <summary>A SecretSchema, never freed: LP64 layout — name, flags, 32 {name, type} attributes,
    /// then a reserved int and seven reserved pointers (592 bytes). Zeroed, so the attribute list
    /// ends at the first null name and the flags are SECRET_SCHEMA_NONE (the schema name must match).</summary>
    private static nint BuildSchema()
    {
        const int attributeSize = 16, attributesAt = 16, size = attributesAt + 32 * attributeSize + 8 + 7 * 8;
        var schema = (byte*)NativeMemory.AllocZeroed(size);
        *(nint*)schema = Marshal.StringToCoTaskMemUTF8(SchemaName);
        string[] names = ["collection", "environment", "key"];
        for (int i = 0; i < names.Length; i++)
        {
            *(nint*)(schema + attributesAt + i * attributeSize) = Marshal.StringToCoTaskMemUTF8(names[i]);
            *(int*)(schema + attributesAt + i * attributeSize + 8) = 0;   // SECRET_SCHEMA_ATTRIBUTE_STRING
        }
        return (nint)schema;
    }

    /// <summary>One entry's attributes as the GHashTable of UTF-8 strings libsecret's *v calls take.
    /// The table owns none of them; disposing frees it and them.</summary>
    private readonly struct Attributes : IDisposable
    {
        private readonly SecretServiceKeyring _owner;
        private readonly nint[] _strings;
        public nint Table { get; }

        public Attributes(SecretServiceKeyring owner, string workspaceRoot, string? environment, string key)
        {
            _owner = owner;
            Table = owner._hashTableNew(owner._strHash, owner._strEqual);
            string[] pairs = ["collection", workspaceRoot, "environment", environment ?? Shared, "key", key];
            _strings = new nint[pairs.Length];
            for (int i = 0; i < pairs.Length; i++) _strings[i] = Marshal.StringToCoTaskMemUTF8(pairs[i]);
            for (int i = 0; i < pairs.Length; i += 2) owner._hashTableInsert(Table, _strings[i], _strings[i + 1]);
        }

        public void Dispose()
        {
            _owner._hashTableUnref(Table);
            foreach (var s in _strings) Marshal.FreeCoTaskMem(s);
        }
    }
}

/// <summary>A store's answers for the life of the process — the value, or that there is none — so a
/// table that asks at every build costs the platform's keyring one call per entry. Only answers
/// are kept: a call that failed is asked again. An entry is its three parts, not the name the store
/// knows it by, so an answer recalled allocates nothing (the Request tab asks for every secret of
/// every environment at each build of the response pane, 59 times a frame of a drag with 12 secrets
/// × 3 environments). <paramref name="ignoreCase"/> is for a store that matches names in any letter
/// case (Windows): the memo then answers for the same entries the store would.</summary>
internal sealed class KeyringMemo(bool ignoreCase = false)
{
    private readonly Dictionary<(string Root, string? Environment, string Key), string?> _known =
        new(new EntryComparer(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
    private readonly Lock _lock = new();

    public bool TryRecall(string workspaceRoot, string? environment, string key, out string? value)
    {
        lock (_lock) return _known.TryGetValue((workspaceRoot, environment, key), out value);
    }

    public void Remember(string workspaceRoot, string? environment, string key, string? value)
    {
        lock (_lock) _known[(workspaceRoot, environment, key)] = value;
    }

    public void Forget(string workspaceRoot, string? environment, string key)
    {
        lock (_lock) _known.Remove((workspaceRoot, environment, key));
    }

    private sealed class EntryComparer(StringComparer names) : IEqualityComparer<(string Root, string? Environment, string Key)>
    {
        public bool Equals((string Root, string? Environment, string Key) x, (string Root, string? Environment, string Key) y) =>
            names.Equals(x.Key, y.Key) && names.Equals(x.Environment, y.Environment) && names.Equals(x.Root, y.Root);

        public int GetHashCode((string Root, string? Environment, string Key) entry) =>
            HashCode.Combine(names.GetHashCode(entry.Root),
                entry.Environment is null ? 0 : names.GetHashCode(entry.Environment),
                names.GetHashCode(entry.Key));
    }
}
