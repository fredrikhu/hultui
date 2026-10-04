using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HultUI.Terminal.Ssh;

/// <summary>What a host's key is to the hosts a user trusts.</summary>
public enum HostKeyStatus
{
    /// <summary>Trusted: the host's entry has this key.</summary>
    Known,

    /// <summary>Never seen: the host has no key of this kind on file. Trust on first use is the
    /// user's decision, made on the fingerprint.</summary>
    Unknown,

    /// <summary>The host has a different key of this kind on file. Either the host was
    /// reinstalled or someone is in the middle; never trust it without the user's deliberate
    /// choice.</summary>
    Changed,

    /// <summary>The key is marked <c>@revoked</c>. Never trusted, whatever the user says.</summary>
    Revoked,
}

/// <summary>
/// OpenSSH's <c>known_hosts</c>: the host keys a user has trusted, in the file <c>ssh</c> itself
/// reads and writes, so a host trusted on the command line is trusted here and the other way
/// round.
/// </summary>
/// <remarks>
/// <para>What is read: plain and hashed (<c>|1|</c>) host names, <c>*</c> and <c>?</c>
/// patterns, <c>!</c> negation, <c>[host]:port</c> for a port other than 22, and the
/// <c>@revoked</c> marker. <c>@cert-authority</c> lines are skipped: a host certificate is not
/// checked against a CA, so a host presenting one is asked about by its key like any other. Lines
/// that do not parse are left alone, in the file and in the lookup.</para>
/// <para>The file is read at every check, as <c>ssh</c> does, so an entry added or removed by
/// another program counts at once. Writes append, as <c>ssh</c> does; only
/// <see cref="Replace"/> rewrites the file, through a temporary file moved over it.</para>
/// <para>Not done: the host's IP address is not checked alongside its name
/// (<c>CheckHostIP</c>, off by default since OpenSSH 8.5).</para>
/// </remarks>
public sealed class KnownHostsFile
{
    private static readonly Lock WriteLock = new();

    /// <summary>The file at <paramref name="path"/>; it need not exist yet.</summary>
    public KnownHostsFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
    }

    /// <summary>The user's own file, <c>~/.ssh/known_hosts</c>, where <c>ssh</c> keeps it on
    /// every platform (Windows' OpenSSH included).</summary>
    public static string UserFilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
        ".ssh", "known_hosts");

    /// <summary>The file.</summary>
    public string Path { get; }

    /// <summary>Whether entries this adds have their host names hashed, as OpenSSH's
    /// <c>HashKnownHosts</c> does: someone who reads the file learns the keys, not the hosts. Off
    /// by default, as in OpenSSH; Debian and Ubuntu turn it on.</summary>
    public bool HashNewEntries { get; init; }

    /// <summary>Looks a host's key up.</summary>
    /// <param name="host">The name connected to, as typed: entries are by name.</param>
    /// <param name="port">The port; entries for any port but 22 are <c>[host]:port</c>.</param>
    /// <param name="key">The key as the host sent it (SSH wire format).</param>
    /// <param name="onFile">The SHA-256 fingerprints of the host's keys of the same kind when the
    /// key <see cref="HostKeyStatus.Changed"/>; empty otherwise.</param>
    public HostKeyStatus Check(string host, int port, ReadOnlySpan<byte> key, out IReadOnlyList<string> onFile)
    {
        onFile = [];
        var type = KeyType(key);
        var name = HostName(host, port);
        var known = new List<string>();
        bool match = false;
        foreach (var entry in Read())
        {
            if (entry.Revoked)
            {
                if (entry.Key.AsSpan().SequenceEqual(key)) return HostKeyStatus.Revoked;
                continue;
            }
            if (!entry.Matches(name)) continue;
            if (entry.Key.AsSpan().SequenceEqual(key)) match = true;
            else if (entry.Type == type) known.Add(Fingerprint(entry.Key));
        }
        if (match) return HostKeyStatus.Known;
        if (known.Count == 0) return HostKeyStatus.Unknown;
        onFile = known;
        return HostKeyStatus.Changed;
    }

    /// <summary>Adds the host's key, appending a line as <c>ssh</c> does. The folder is made if
    /// it is missing.</summary>
    public void Add(string host, int port, ReadOnlySpan<byte> key)
    {
        var line = Line(host, port, key);
        lock (WriteLock)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            // A file whose last line has no newline would have this one joined onto it.
            var lead = File.Exists(Path) && !EndsWithNewline(Path) ? "\n" : "";
            File.AppendAllText(Path, lead + line + "\n");
        }
    }

    /// <summary>Removes the host's keys of this key's kind and adds this one: what trusting a
    /// <see cref="HostKeyStatus.Changed"/> key means. Every other line is kept as it was.</summary>
    public void Replace(string host, int port, ReadOnlySpan<byte> key)
    {
        var type = KeyType(key);
        var name = HostName(host, port);
        var line = Line(host, port, key);
        lock (WriteLock)
        {
            var kept = new StringBuilder();
            if (File.Exists(Path))
            {
                foreach (var text in File.ReadAllLines(Path))
                {
                    if (Entry.Parse(text) is { Revoked: false } entry && entry.Type == type && entry.Matches(name))
                        continue;
                    kept.Append(text).Append('\n');
                }
            }
            kept.Append(line).Append('\n');
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, kept.ToString());
            File.Move(temporary, Path, overwrite: true);
        }
    }

    /// <summary>A key's fingerprint as <c>ssh</c> shows it: <c>SHA256:</c> and unpadded base64.</summary>
    public static string Fingerprint(ReadOnlySpan<byte> key) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=');

    /// <summary>The key's type, from the key itself (its first string): <c>ssh-ed25519</c>,
    /// <c>ssh-rsa</c>, <c>ecdsa-sha2-nistp256</c>. Not the signature algorithm the connection
    /// agreed, which for an RSA key is <c>rsa-sha2-512</c> while the file says <c>ssh-rsa</c>.</summary>
    public static string KeyType(ReadOnlySpan<byte> key)
    {
        if (key.Length < 4) return "";
        int length = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(key), (uint)(key.Length - 4));
        return Encoding.ASCII.GetString(key.Slice(4, length));
    }

    private string Line(string host, int port, ReadOnlySpan<byte> key)
    {
        var name = HostName(host, port);
        if (HashNewEntries)
        {
            var salt = RandomNumberGenerator.GetBytes(20);
            name = $"|1|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(name)))}";
        }
        return $"{name} {KeyType(key)} {Convert.ToBase64String(key)}";
    }

    /// <summary>The name an entry is under: the host, lower case, or <c>[host]:port</c>.</summary>
    private static string HostName(string host, int port)
    {
        host = host.ToLowerInvariant();
        return port == 22 ? host : $"[{host}]:{port}";
    }

    private static bool EndsWithNewline(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length == 0) return true;
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() == '\n';
    }

    private IEnumerable<Entry> Read()
    {
        if (!File.Exists(Path)) yield break;
        foreach (var text in File.ReadLines(Path))
            if (Entry.Parse(text) is { } entry) yield return entry;
    }

    /// <summary>One line: who it is for, and the key.</summary>
    private sealed class Entry
    {
        private Entry(string hosts, string type, byte[] key, bool revoked) => (Hosts, Type, Key, Revoked) = (hosts, type, key, revoked);

        public string Hosts { get; }
        public string Type { get; }
        public byte[] Key { get; }
        public bool Revoked { get; }

        /// <summary>A line, or null for a comment, a blank, a CA line, or one that does not
        /// parse.</summary>
        public static Entry? Parse(string line)
        {
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0 || fields[0].StartsWith('#')) return null;
            bool revoked = false;
            int at = 0;
            if (fields[0].StartsWith('@'))
            {
                if (fields[0] != "@revoked") return null; // @cert-authority, or a marker this does not know
                revoked = true;
                at = 1;
            }
            if (fields.Length < at + 3) return null;
            byte[] key;
            try { key = Convert.FromBase64String(fields[at + 2]); }
            catch (FormatException) { return null; }
            // The type field must agree with the key it names; a line where they differ is not
            // trusted for either.
            if (KeyType(key) != fields[at + 1]) return null;
            return new Entry(fields[at], fields[at + 1], key, revoked);
        }

        /// <summary>Whether the line is for <paramref name="name"/>: any pattern matches it and no
        /// negated one does.</summary>
        public bool Matches(string name)
        {
            if (Hosts.StartsWith("|1|", StringComparison.Ordinal)) return HashMatches(Hosts, name);
            bool any = false;
            foreach (var pattern in Hosts.Split(','))
            {
                if (pattern.Length == 0) continue;
                bool negated = pattern[0] == '!';
                var body = negated ? pattern[1..] : pattern;
                if (!Glob(body.ToLowerInvariant(), name)) continue;
                if (negated) return false;
                any = true;
            }
            return any;
        }

        private static bool HashMatches(string hashed, string name)
        {
            var parts = hashed.Split('|'); // "", "1", salt, hash
            if (parts.Length != 4) return false;
            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var hash = Convert.FromBase64String(parts[3]);
                return CryptographicOperations.FixedTimeEquals(HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(name)), hash);
            }
            catch (FormatException) { return false; }
        }

        /// <summary><c>*</c> any run, <c>?</c> any one character, as OpenSSH's match_pattern.</summary>
        private static bool Glob(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text)
        {
            int p = 0, t = 0, star = -1, resume = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t])) { p++; t++; }
                else if (p < pattern.Length && pattern[p] == '*') { star = p++; resume = t; }
                else if (star >= 0) { p = star + 1; t = ++resume; }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }
    }
}
