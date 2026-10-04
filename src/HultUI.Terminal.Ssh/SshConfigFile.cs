using System.Text;

namespace HultUI.Terminal.Ssh;

/// <summary>What OpenSSH's client configuration says about one host.</summary>
/// <param name="Alias">The name as given: what <c>Host</c> patterns match.</param>
/// <param name="HostName">The host to connect to: <c>HostName</c>, or the alias.</param>
/// <param name="Port"><c>Port</c>, or null for the default.</param>
/// <param name="User"><c>User</c>, or null.</param>
/// <param name="IdentityFiles">Every <c>IdentityFile</c> that applies, in order, paths expanded.
/// Empty when none does: <c>ssh</c> then tries <see cref="SshConfigFile.DefaultIdentityFiles"/>.</param>
/// <param name="IdentitiesOnly"><c>IdentitiesOnly yes</c>: only those files, not every key the
/// agent holds.</param>
/// <param name="UserKnownHostsFile">The first <c>UserKnownHostsFile</c>, expanded, or null.</param>
/// <param name="ProxyJump"><c>ProxyJump</c> as written, or null. Not something this package does:
/// an application can say so rather than connect directly.</param>
public sealed record SshHostConfig(
    string Alias, string HostName, int? Port, string? User, IReadOnlyList<string> IdentityFiles,
    bool IdentitiesOnly, string? UserKnownHostsFile, string? ProxyJump);

/// <summary>
/// OpenSSH's client configuration (<c>~/.ssh/config</c>), read as <c>ssh</c> reads it for the
/// settings a connection needs: which name to connect to, as whom, on which port, with which key
/// files. A user who has set up <c>ssh</c> has set up this too.
/// </summary>
/// <remarks>
/// <para>Read as <c>ssh_config(5)</c> says: keywords in any case, <c>Keyword value</c> or
/// <c>Keyword=value</c>, double quotes around a value with spaces; <c>Host</c> patterns with
/// <c>*</c>, <c>?</c> and <c>!</c>, matched against the name given; the first value obtained for
/// each keyword wins, except <c>IdentityFile</c>, which adds up. <c>Include</c> is followed
/// (relative to <c>~/.ssh</c>, <c>*</c> in the file name). Tokens are expanded: <c>~</c>,
/// <c>%h</c>, <c>%n</c>, <c>%p</c>, <c>%r</c>, <c>%u</c>, <c>%d</c>, <c>%l</c>, <c>%%</c>.</para>
/// <para><c>Match</c> blocks are not evaluated, and apply to nothing. Other keywords are
/// ignored. The file is read at every <see cref="Resolve"/>, as <c>ssh</c> does.</para>
/// </remarks>
public sealed class SshConfigFile
{
    private const int MostIncludes = 16;

    /// <summary>The file at <paramref name="path"/>; it need not exist.</summary>
    public SshConfigFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
    }

    /// <summary>The user's own, <c>~/.ssh/config</c>.</summary>
    public static string UserFilePath => System.IO.Path.Combine(SshFolder, "config");

    /// <summary>The key files <c>ssh</c> tries when no <c>IdentityFile</c> applies, the ones this
    /// package can read: <c>~/.ssh/id_rsa</c>, <c>id_ecdsa</c>, <c>id_ed25519</c>.</summary>
    public static IReadOnlyList<string> DefaultIdentityFiles =>
        [System.IO.Path.Combine(SshFolder, "id_rsa"), System.IO.Path.Combine(SshFolder, "id_ecdsa"), System.IO.Path.Combine(SshFolder, "id_ed25519")];

    /// <summary>The file.</summary>
    public string Path { get; }

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
    private static string SshFolder => System.IO.Path.Combine(Home, ".ssh");

    /// <summary>What applies to <paramref name="host"/>, the name as the user gave it.</summary>
    public SshHostConfig Resolve(string host)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var identities = new List<string>();
        Read(Path, host, values, identities, depth: 0, applies: true);

        string Get(string key) => values.TryGetValue(key, out var v) ? v : "";
        int? port = int.TryParse(Get("port"), out var p) && p is >= 1 and <= 65535 ? p : null;
        string? user = Get("user") is { Length: > 0 } u ? u : null;
        // HostName may itself say %h, the name given.
        var hostName = Get("hostname") is { Length: > 0 } h ? Expand(h, host, host, port, user) : host;
        return new SshHostConfig(
            host, hostName, port, user,
            identities.Select(f => Expand(f, host, hostName, port, user)).ToList(),
            Get("identitiesonly").Equals("yes", StringComparison.OrdinalIgnoreCase),
            Get("userknownhostsfile") is { Length: > 0 } k ? Expand(k.Split(' ')[0], host, hostName, port, user) : null,
            Get("proxyjump") is { Length: > 0 } j && !j.Equals("none", StringComparison.OrdinalIgnoreCase) ? j : null);
    }

    /// <summary>Reads one file into what applies. <paramref name="applies"/> is whether the block
    /// holding an <c>Include</c> did.</summary>
    private static void Read(string path, string host, Dictionary<string, string> values, List<string> identities, int depth, bool applies)
    {
        if (depth > MostIncludes || !File.Exists(path)) return;
        bool inBlock = applies;
        foreach (var raw in File.ReadLines(path))
        {
            var (keyword, args) = Split(raw);
            if (keyword.Length == 0) continue;
            switch (keyword.ToLowerInvariant())
            {
                case "host":
                    inBlock = applies && HostMatches(args, host);
                    continue;
                case "match":
                    inBlock = false; // not evaluated: applies to nothing
                    continue;
            }
            if (!inBlock || args.Count == 0) continue;
            switch (keyword.ToLowerInvariant())
            {
                case "include":
                    foreach (var pattern in args)
                        foreach (var file in Included(pattern))
                            Read(file, host, values, identities, depth + 1, applies: true);
                    break;
                case "identityfile":
                    identities.Add(args[0]);
                    break;
                default:
                    values.TryAdd(keyword, string.Join(' ', args)); // the first obtained value wins
                    break;
            }
        }
    }

    /// <summary>A line's keyword and arguments: <c>Keyword value</c>, <c>Keyword=value</c>, quotes
    /// around values with spaces, <c>#</c> to the end of the line a comment.</summary>
    private static (string Keyword, List<string> Args) Split(string line)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (!quoted && c == '#') break;
            // The first '=' separates the keyword, as a space does, with or without spaces round it.
            bool equals = c == '=' && (words.Count == 0 || (words.Count == 1 && !any));
            if (!quoted && (char.IsWhiteSpace(c) || equals))
            {
                if (any) { words.Add(word.ToString()); word.Clear(); any = false; }
                continue;
            }
            word.Append(c);
            any = true;
        }
        if (any) words.Add(word.ToString());
        return words.Count == 0 ? ("", []) : (words[0], words.GetRange(1, words.Count - 1));
    }

    /// <summary>Whether any pattern matches and no negated one does.</summary>
    private static bool HostMatches(List<string> patterns, string host)
    {
        bool any = false;
        foreach (var entry in patterns)
        {
            foreach (var pattern in entry.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                bool negated = pattern[0] == '!';
                if (!Glob(negated ? pattern[1..] : pattern, host)) continue;
                if (negated) return false;
                any = true;
            }
        }
        return any;
    }

    private static bool Glob(string pattern, string text)
    {
        pattern = pattern.ToLowerInvariant();
        text = text.ToLowerInvariant();
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

    /// <summary>The files an <c>Include</c> names, in name order: relative to <c>~/.ssh</c>, with
    /// <c>*</c> and <c>?</c> in the file name.</summary>
    private static IEnumerable<string> Included(string pattern)
    {
        var path = pattern.StartsWith('~') ? Home + pattern[1..] : pattern;
        if (!System.IO.Path.IsPathRooted(path)) path = System.IO.Path.Combine(SshFolder, path);
        var dir = System.IO.Path.GetDirectoryName(path);
        var name = System.IO.Path.GetFileName(path);
        if (dir is null || !Directory.Exists(dir)) return [];
        if (name.IndexOfAny(['*', '?']) < 0) return File.Exists(path) ? [path] : [];
        return Directory.EnumerateFiles(dir).Where(f => Glob(name, System.IO.Path.GetFileName(f))).Order(StringComparer.Ordinal);
    }

    /// <summary>Expands <c>~</c> and the tokens a path or name may hold.</summary>
    private static string Expand(string value, string alias, string hostName, int? port, string? user)
    {
        if (value.StartsWith('~')) value = Home + value[1..];
        if (!value.Contains('%')) return value;
        var sb = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '%' || i + 1 == value.Length) { sb.Append(value[i]); continue; }
            sb.Append(value[++i] switch
            {
                '%' => "%",
                'h' => hostName,
                'n' => alias,
                'p' => (port ?? 22).ToString(System.Globalization.CultureInfo.InvariantCulture),
                'r' => user ?? Environment.UserName,
                'u' => Environment.UserName,
                'd' => Home,
                'l' => Environment.MachineName,
                var other => "%" + other,
            });
        }
        return sb.ToString();
    }
}
