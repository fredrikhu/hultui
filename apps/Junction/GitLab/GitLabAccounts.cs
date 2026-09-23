using System.Net.Http;
using System.Text.Json.Serialization;
using HultUI.Core.Platform;

namespace Junction.GitLab;

/// <summary>
/// One signed-in GitLab identity, as persisted. <b>No token lives here</b> — the settings
/// file carries who and where, the credential store carries the secret under
/// <see cref="GitLabAccounts.Service"/>. Everything shown on the accounts pane comes from
/// this record; everything sent to the API comes from the store.
/// </summary>
public sealed class GitLabAccountData
{
    /// <summary>Lower-cased host, the key everything joins on: <c>gitlab.com</c>,
    /// <c>gitlab.acme.internal</c>.</summary>
    public string Host { get; set; } = "";

    /// <summary>Where the instance's web and API live, scheme and port included —
    /// <c>https://gitlab.com</c>, <c>http://gitlab.lab:8080</c>.</summary>
    public string WebBaseUrl { get; set; } = "";

    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string? AvatarUrl { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<GitLabTokenKind>))]
    public GitLabTokenKind Kind { get; set; }

    /// <summary>When the token stops working; null for a PAT that never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public List<string> Scopes { get; set; } = [];
    public DateTimeOffset SignedInAt { get; set; }

    /// <summary>The OAuth application id the token was issued for (self-managed instances
    /// register their own). Empty for a PAT.</summary>
    public string ClientId { get; set; } = "";

    public bool CanWrite => Scopes.Contains("api");

    /// <summary>Days until expiry, or null when it never expires. Negative when expired.</summary>
    public int? DaysLeft => ExpiresAt is { } e ? (int)Math.Floor((e - DateTimeOffset.UtcNow).TotalDays) : null;

    public bool IsExpired => ExpiresAt is { } e && e <= DateTimeOffset.UtcNow;

    /// <summary>The design turns the expiry line amber under 14 days.</summary>
    public bool ExpiresSoon => DaysLeft is { } d && d < 14;
}

/// <summary>What a token check found out, before anything is kept. <see cref="Token"/> is the
/// secret itself — this record lives only as long as the add-host card that holds it.</summary>
public sealed record VerifiedToken(string Host, string WebBaseUrl, string Token, GlUser User,
    List<string> Scopes, DateTimeOffset? ExpiresAt);

/// <summary>
/// The accounts the app is signed in with, and the clients they yield. Owns the split the
/// design insists on: identity in settings, secret in the credential store. Sign-in
/// validates before it stores — a token that cannot name its user, or is read-only, never
/// reaches the keyring.
/// </summary>
/// <remarks>
/// A platform with no credential store keeps the token in memory for the session and reports
/// it through <see cref="StoredInKeyring"/>, so the pane can say the sign-in will not survive
/// a restart rather than pretend.
/// </remarks>
public sealed class GitLabAccounts
{
    private readonly List<GitLabAccountData> _accounts;
    private readonly ICredentialStore _store;
    private readonly Action _persist;
    private readonly Func<string, HttpMessageHandler?> _handlerFor;
    private readonly Dictionary<string, string> _sessionTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GitLabClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public const string ServicePrefix = "junction|gitlab|";
    public const string TokenAccount = "token";
    public const string RefreshAccount = "refresh";

    /// <summary>The credential-store service name for a host.</summary>
    public static string Service(string host) => ServicePrefix + host.ToLowerInvariant();

    /// <param name="accounts">The settings list; mutated in place and persisted through
    /// <paramref name="persist"/>.</param>
    /// <param name="store">Where tokens go. Tests pass an <see cref="InMemoryCredentialStore"/>.</param>
    /// <param name="handlerFor">Lets tests hand every host a recorded handler; null uses the
    /// real network.</param>
    public GitLabAccounts(List<GitLabAccountData> accounts, ICredentialStore store, Action persist,
        Func<string, HttpMessageHandler?>? handlerFor = null)
    {
        _accounts = accounts;
        _store = store;
        _persist = persist;
        _handlerFor = handlerFor ?? (_ => null);
    }

    public IReadOnlyList<GitLabAccountData> All => _accounts;

    public GitLabAccountData? For(string host)
    {
        host = host.ToLowerInvariant();
        return _accounts.FirstOrDefault(a => a.Host == host);
    }

    public bool HasAccount(string host) => For(host) is not null;

    /// <summary>The HTTP handler requests to <paramref name="host"/> go through — null for the
    /// default. For the calls that carry no token (an image a note links to), so they travel
    /// the same way as the ones that do.</summary>
    public HttpMessageHandler? HandlerFor(string host) => _handlerFor(host.ToLowerInvariant());

    /// <summary>Whether the token for <paramref name="host"/> is in the OS store, as opposed
    /// to only in this process's memory.</summary>
    public bool StoredInKeyring(string host) => _store.Read(Service(host), TokenAccount) is not null;

    public string? TokenFor(string host)
    {
        lock (_lock)
            if (_sessionTokens.TryGetValue(host, out var t)) return t;
        return _store.Read(Service(host), TokenAccount);
    }

    /// <summary>A client for the host, or null when there is no account or no token. Cached
    /// per host; invalidated by sign-in, refresh and sign-out.</summary>
    public GitLabClient? ClientFor(string host)
    {
        var account = For(host);
        if (account is null) return null;
        lock (_lock)
        {
            if (_clients.TryGetValue(host, out var cached)) return cached;
            var token = TokenFor(host);
            if (token is null) return null;
            var client = new GitLabClient(account.WebBaseUrl, token, account.Kind, _handlerFor(host));
            _clients[host] = client;
            return client;
        }
    }

    public GitLabClient? ClientFor(GitLabRemote remote) => ClientFor(remote.Host);

    /// <summary>
    /// A client whose token is good for at least a minute — an OAuth token near its expiry
    /// is refreshed first. Falls back to the stale client when the refresh fails, so a flaky
    /// network surfaces as the call's own failure rather than as "no account".
    /// </summary>
    public async Task<GitLabClient?> FreshClientForAsync(string host, CancellationToken ct = default)
    {
        var account = For(host);
        if (account is null) return null;
        if (account.Kind == GitLabTokenKind.OAuth && account.ExpiresAt is { } exp && exp <= DateTimeOffset.UtcNow.AddMinutes(1))
            await RefreshAsync(host, ct).ConfigureAwait(false);
        return ClientFor(host);
    }

    // ── Sign in ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks a pasted personal access token without keeping it — the accounts pane's
    /// <b>Test</b>. Three outcomes the design shows as the failed row: the host is not GitLab
    /// (<see cref="GitLabFailureKind.Invalid"/> / <see cref="GitLabFailureKind.Network"/>),
    /// the token is refused (401), the token is read-only (<see cref="GitLabFailureKind.Forbidden"/>,
    /// since Junction creates and acts on merge requests). The verified outcome names the
    /// user, expiry and scopes; <see cref="AddVerified"/> turns it into an account.
    /// </summary>
    public async Task<GitLabResult<VerifiedToken>> VerifyTokenAsync(string webBaseUrl, string token, CancellationToken ct = default)
    {
        token = token.Trim();
        if (token.Length == 0)
            return GitLabResult<VerifiedToken>.Fail(GitLabFailureKind.Unauthorized, "Paste a token first");

        string apiBase;
        try { apiBase = GitLabClient.NormalizeApiBase(webBaseUrl); }
        catch (UriFormatException)
        {
            return GitLabResult<VerifiedToken>.Fail(GitLabFailureKind.Invalid, $"\"{webBaseUrl}\" is not a host URL");
        }
        var host = new Uri(apiBase).Host;

        using var probe = new GitLabClient(apiBase, token, GitLabTokenKind.Pat, _handlerFor(host));
        var user = await probe.GetCurrentUserAsync(ct).ConfigureAwait(false);
        if (!user.Ok)
            return GitLabResult<VerifiedToken>.Fail(DescribeSignInFailure(user.Failure!, host));

        var info = await probe.ProbeTokenAsync(ct).ConfigureAwait(false);
        List<string> scopes;
        DateTimeOffset? expires = null;
        if (info.Ok)
        {
            scopes = info.Value.Scopes;
            if (info.Value.Expires is { } d) expires = new DateTimeOffset(d, TimeSpan.Zero);
            if (info.Value.Revoked || !info.Value.Active)
                return GitLabResult<VerifiedToken>.Fail(GitLabFailureKind.Unauthorized,
                    $"That token has been revoked on {host} — create a new one at {probe.WebBaseUrl}/-/user_settings/personal_access_tokens");
        }
        else if (info.Failure!.Kind is GitLabFailureKind.NotFound or GitLabFailureKind.Forbidden)
        {
            // An OAuth access token, or an instance too old to answer: the user is known,
            // the scopes are not. Assume write access and let the first 403 say otherwise.
            scopes = ["api"];
        }
        else
        {
            return GitLabResult<VerifiedToken>.Fail(DescribeSignInFailure(info.Failure, host));
        }

        if (!scopes.Contains("api"))
            return GitLabResult<VerifiedToken>.Fail(GitLabFailureKind.Forbidden,
                $"The token has {string.Join(", ", scopes)} only. Reading works, but creating, approving and merging requests need the api scope.");

        return GitLabResult<VerifiedToken>.Success(new VerifiedToken(host, probe.WebBaseUrl, token, user.Value, scopes, expires));
    }

    /// <summary>Keeps a token <see cref="VerifyTokenAsync"/> accepted: the identity into
    /// settings, the secret into the store. Replaces any account on the same host.</summary>
    public GitLabAccountData AddVerified(VerifiedToken verified)
    {
        var account = new GitLabAccountData
        {
            Host = verified.Host,
            WebBaseUrl = verified.WebBaseUrl,
            UserId = verified.User.Id,
            Username = verified.User.Username,
            Name = verified.User.Name,
            AvatarUrl = verified.User.AvatarUrl,
            Kind = GitLabTokenKind.Pat,
            ExpiresAt = verified.ExpiresAt,
            Scopes = verified.Scopes,
            SignedInAt = DateTimeOffset.UtcNow,
        };
        Store(verified.Host, verified.Token, refresh: null);
        Upsert(account);
        return account;
    }

    /// <summary>Verify and keep in one step.</summary>
    public async Task<GitLabResult<GitLabAccountData>> SignInWithTokenAsync(string webBaseUrl, string token, CancellationToken ct = default)
    {
        var verified = await VerifyTokenAsync(webBaseUrl, token, ct).ConfigureAwait(false);
        return verified.Ok
            ? GitLabResult<GitLabAccountData>.Success(AddVerified(verified.Value))
            : verified.As<GitLabAccountData>();
    }

    /// <summary>
    /// Replaces a PAT with a fresh one from the instance (<c>/personal_access_tokens/self/rotate</c>);
    /// GitLab revokes the old one in the same call, so the new value goes to the store before
    /// anything else is asked. Only a PAT rotates; an OAuth account refreshes instead.
    /// </summary>
    public async Task<GitLabResult<GitLabAccountData>> RotateTokenAsync(string host, CancellationToken ct = default)
    {
        var account = For(host);
        if (account is null)
            return GitLabResult<GitLabAccountData>.Fail(GitLabFailureKind.Unauthorized, $"Not signed in to {host}");
        if (account.Kind != GitLabTokenKind.Pat)
            return GitLabResult<GitLabAccountData>.Success(account);
        var client = ClientFor(host);
        if (client is null)
            return GitLabResult<GitLabAccountData>.Fail(GitLabFailureKind.Unauthorized, "Sign in again");

        var rotated = await client.RotateTokenAsync(ct).ConfigureAwait(false);
        if (!rotated.Ok) return rotated.As<GitLabAccountData>();
        if (string.IsNullOrEmpty(rotated.Value.Token))
            return GitLabResult<GitLabAccountData>.Fail(GitLabFailureKind.Invalid, $"{host} rotated the token but did not return the new one");

        Store(host, rotated.Value.Token, refresh: null);
        account.ExpiresAt = rotated.Value.Expires is { } d ? new DateTimeOffset(d, TimeSpan.Zero) : null;
        if (rotated.Value.Scopes.Count > 0) account.Scopes = rotated.Value.Scopes;
        _persist();
        return GitLabResult<GitLabAccountData>.Success(account);
    }

    /// <summary>Sign in through the browser. <paramref name="clientId"/> defaults to the
    /// gitlab.com application; a self-managed instance passes its own.</summary>
    public async Task<GitLabResult<GitLabAccountData>> SignInWithOAuthAsync(string webBaseUrl, Func<string, bool> openBrowser,
        string? clientId = null, int port = GitLabOAuth.DefaultPort, CancellationToken ct = default)
    {
        string apiBase;
        try { apiBase = GitLabClient.NormalizeApiBase(webBaseUrl); }
        catch (UriFormatException)
        {
            return GitLabResult<GitLabAccountData>.Fail(GitLabFailureKind.Invalid, $"\"{webBaseUrl}\" is not a host URL");
        }
        var host = new Uri(apiBase).Host;
        var web = apiBase[..^"/api/v4".Length];
        clientId ??= GitLabOAuth.GitLabComClientId;

        var tokens = await GitLabOAuth.SignInAsync(web, clientId, openBrowser, _handlerFor(host), port, ct: ct).ConfigureAwait(false);
        if (!tokens.Ok) return tokens.As<GitLabAccountData>();

        using var probe = new GitLabClient(apiBase, tokens.Value.AccessToken, GitLabTokenKind.OAuth, _handlerFor(host));
        var user = await probe.GetCurrentUserAsync(ct).ConfigureAwait(false);
        if (!user.Ok) return GitLabResult<GitLabAccountData>.Fail(DescribeSignInFailure(user.Failure!, host));

        var account = new GitLabAccountData
        {
            Host = host,
            WebBaseUrl = web,
            UserId = user.Value.Id,
            Username = user.Value.Username,
            Name = user.Value.Name,
            AvatarUrl = user.Value.AvatarUrl,
            Kind = GitLabTokenKind.OAuth,
            ExpiresAt = tokens.Value.ExpiresAt,
            Scopes = [.. tokens.Value.Scopes],
            SignedInAt = DateTimeOffset.UtcNow,
            ClientId = clientId,
        };
        Store(host, tokens.Value.AccessToken, tokens.Value.RefreshToken);
        Upsert(account);
        return GitLabResult<GitLabAccountData>.Success(account);
    }

    /// <summary>Trades the refresh token for a new pair. Only meaningful for OAuth accounts;
    /// a PAT returns its account unchanged.</summary>
    public async Task<GitLabResult<GitLabAccountData>> RefreshAsync(string host, CancellationToken ct = default)
    {
        var account = For(host);
        if (account is null)
            return GitLabResult<GitLabAccountData>.Fail(GitLabFailureKind.Unauthorized, $"Not signed in to {host}");
        if (account.Kind != GitLabTokenKind.OAuth)
            return GitLabResult<GitLabAccountData>.Success(account);

        var refresh = _store.Read(Service(host), RefreshAccount);
        if (string.IsNullOrEmpty(refresh))
            return GitLabResult<GitLabAccountData>.Fail(GitLabFailureKind.Unauthorized, "Sign in again");

        var tokens = await GitLabOAuth.RefreshAsync(account.WebBaseUrl, account.ClientId, refresh, _handlerFor(host), ct).ConfigureAwait(false);
        if (!tokens.Ok) return tokens.As<GitLabAccountData>();

        Store(host, tokens.Value.AccessToken, tokens.Value.RefreshToken);
        account.ExpiresAt = tokens.Value.ExpiresAt;
        if (tokens.Value.Scopes.Count > 0) account.Scopes = [.. tokens.Value.Scopes];
        _persist();
        return GitLabResult<GitLabAccountData>.Success(account);
    }

    /// <summary>Revokes the host's OAuth token on the instance, best effort — the network
    /// half of a sign-out, kept apart from <see cref="Forget"/> so the app can run it off
    /// the UI thread and do the forgetting on it. A PAT is not revoked: it may be in use
    /// elsewhere, and GitLab has a page for that.</summary>
    public async Task RevokeAsync(string host, CancellationToken ct = default)
    {
        host = host.ToLowerInvariant();
        var account = For(host);
        var token = TokenFor(host);
        if (account is { Kind: GitLabTokenKind.OAuth } && token is not null && account.ClientId.Length > 0)
            await GitLabOAuth.RevokeAsync(account.WebBaseUrl, account.ClientId, token, _handlerFor(host), ct).ConfigureAwait(false);
    }

    /// <summary>Drops the account, its secrets and its client. Synchronous, so the settings
    /// list and the store change on whichever thread the caller owns.</summary>
    public void Forget(string host)
    {
        host = host.ToLowerInvariant();
        var account = For(host);
        _store.Delete(Service(host), TokenAccount);
        _store.Delete(Service(host), RefreshAccount);
        lock (_lock)
        {
            _sessionTokens.Remove(host);
            if (_clients.Remove(host, out var client)) client.Dispose();
        }
        if (account is not null)
        {
            _accounts.Remove(account);
            _persist();
        }
    }

    /// <summary>Revoke, then forget.</summary>
    public async Task SignOutAsync(string host, CancellationToken ct = default)
    {
        await RevokeAsync(host, ct).ConfigureAwait(false);
        Forget(host);
    }

    // ── Internals ────────────────────────────────────────────────────────────────────

    private static GitLabFailure DescribeSignInFailure(GitLabFailure f, string host) => f.Kind switch
    {
        GitLabFailureKind.Unauthorized => f with { Message = $"{host} did not accept that token" },
        GitLabFailureKind.Invalid => f with { Message = $"{host} does not look like a GitLab instance" },
        GitLabFailureKind.NotFound => f with { Kind = GitLabFailureKind.Invalid, Message = $"{host} does not look like a GitLab instance (no /api/v4)" },
        _ => f,
    };

    private void Store(string host, string token, string? refresh)
    {
        var service = Service(host);
        bool stored = _store.Write(service, TokenAccount, token);
        if (refresh is not null) _store.Write(service, RefreshAccount, refresh);
        else _store.Delete(service, RefreshAccount);
        lock (_lock)
        {
            if (stored) _sessionTokens.Remove(host);
            else _sessionTokens[host] = token;
            if (_clients.Remove(host, out var old)) old.Dispose();
        }
    }

    private void Upsert(GitLabAccountData account)
    {
        var existing = For(account.Host);
        if (existing is not null) _accounts.Remove(existing);
        _accounts.Add(account);
        _persist();
    }
}
