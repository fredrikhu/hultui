namespace Junction.GitLab;

/// <summary>
/// What a remote URL says about where a repository is hosted: the host, how it was reached,
/// and the project's path on that host (<c>group/subgroup/project</c>). Whether the host is
/// actually a GitLab instance is a separate question answered by the accounts — this record
/// only reads the URL.
/// </summary>
/// <param name="Host">Lower-cased host name, no port.</param>
/// <param name="Scheme"><c>https</c>, <c>http</c> or <c>ssh</c>.</param>
/// <param name="Port">The port in the URL, if any. For <c>ssh</c> it is the SSH port and
/// says nothing about where the API listens.</param>
/// <param name="ProjectPath">Path without a leading slash or <c>.git</c>, at least two
/// segments.</param>
public sealed record GitLabRemote(string Host, string Scheme, int? Port, string ProjectPath)
{
    /// <summary>The project path as a single URL segment, the form the API addresses a
    /// project by: <c>acme%2Fplatform%2Finfra</c>.</summary>
    public string EncodedProjectPath => Uri.EscapeDataString(ProjectPath);

    /// <summary>The last segment: the project's own name.</summary>
    public string ProjectName => ProjectPath[(ProjectPath.LastIndexOf('/') + 1)..];

    /// <summary>
    /// Where the REST API lives for this host. An HTTP(S) remote keeps its scheme and port
    /// (a self-managed instance on <c>http://gitlab.lab:8080</c> serves its API there too);
    /// an SSH remote says nothing about the web side, so it is assumed to be plain
    /// <c>https</c> on the default port, which is what every GitLab install defaults to.
    /// </summary>
    public string ApiBaseUrl => Scheme == "ssh"
        ? $"https://{Host}/api/v4"
        : Port is { } p ? $"{Scheme}://{Host}:{p}/api/v4" : $"{Scheme}://{Host}/api/v4";

    /// <summary>The instance's web root, same reasoning as <see cref="ApiBaseUrl"/>.</summary>
    public string WebBaseUrl => Scheme == "ssh"
        ? $"https://{Host}"
        : Port is { } p ? $"{Scheme}://{Host}:{p}" : $"{Scheme}://{Host}";

    /// <summary>
    /// Reads a git remote URL. Handles the three spellings git accepts for a hosted
    /// repository — <c>https://host/group/project.git</c> (with or without credentials, port
    /// or the <c>.git</c>), <c>ssh://git@host[:port]/group/project.git</c>, and the scp-like
    /// <c>git@host:group/project.git</c> — and returns <c>null</c> for anything else: local
    /// paths, <c>file://</c>, a Windows drive letter that happens to look like <c>host:path</c>,
    /// or a URL with fewer than two path segments (GitLab projects always live in a group).
    /// </summary>
    public static GitLabRemote? TryParse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();

        if (url.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            var scheme = uri.Scheme.ToLowerInvariant();
            if (scheme is not ("https" or "http" or "ssh")) return null;
            if (string.IsNullOrEmpty(uri.Host)) return null;
            var path = CleanPath(uri.AbsolutePath);
            if (path is null) return null;
            int? port = uri.IsDefaultPort ? null : uri.Port;
            return new GitLabRemote(uri.Host.ToLowerInvariant(), scheme, port, path);
        }

        // scp-like: [user@]host:path — but not C:\repo or C:/repo.
        int colon = url.IndexOf(':');
        if (colon <= 0 || colon == url.Length - 1) return null;
        var hostPart = url[..colon];
        var rest = url[(colon + 1)..];
        if (rest.StartsWith('/') || rest.StartsWith('\\')) return null;
        if (hostPart.Length == 1 && char.IsLetter(hostPart[0])) return null;   // drive letter
        int at = hostPart.LastIndexOf('@');
        var host = at >= 0 ? hostPart[(at + 1)..] : hostPart;
        if (host.Length == 0 || host.Contains('/') || host.Contains('\\') || host.Contains(' ')) return null;
        var scpPath = CleanPath(rest);
        if (scpPath is null) return null;
        return new GitLabRemote(host.ToLowerInvariant(), "ssh", null, scpPath);
    }

    private static string? CleanPath(string raw)
    {
        var path = raw.Replace('\\', '/').Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        path = path.TrimEnd('/');
        if (path.Length == 0) return null;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return null;
        foreach (var s in segments)
            if (s is "." or "..") return null;
        return string.Join('/', segments);
    }
}
