namespace Aperture.Core.Requests;

/// <summary>
/// From something typed to a signed-in <see cref="SeerrServer"/>: the address walk, the
/// public probe, the two ways in, and resuming a stored session. The request-server twin
/// of <see cref="JellyfinConnection"/>.
/// </summary>
public static class SeerrConnection
{
    /// <summary>The port all three servers listen on out of the box.</summary>
    public const int DefaultPort = 5055;

    /// <summary>
    /// The addresses worth trying, best first. The same walk as Jellyfin's, around 5055:
    /// a bare host is tried on the scheme's own port (a reverse proxy) and on 5055, over
    /// http and then https; an address with a scheme and a port is taken at its word.
    /// A pasted <c>/api/v1</c> is dropped rather than doubled.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string input)
    {
        var typed = (input ?? "").Trim().TrimEnd('/');
        if (typed.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase)) typed = typed[..^"/api/v1".Length];
        bool hadScheme = typed.Contains("://", StringComparison.Ordinal);
        if (typed.Length == 0 || (hadScheme && typed.EndsWith(':'))) return [];
        if (!Uri.TryCreate(hadScheme ? typed : "http://" + typed, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host) || uri.Scheme is not ("http" or "https"))
            return [];

        bool hadPort = !uri.IsDefaultPort || ExplicitPort(typed);
        var list = new List<string>();
        void Add(string scheme, int? port)
        {
            var b = new UriBuilder(scheme, uri.Host) { Path = uri.AbsolutePath, Port = port ?? -1 };
            var s = b.Uri.ToString().TrimEnd('/');
            if (!list.Contains(s, StringComparer.OrdinalIgnoreCase)) list.Add(s);
        }

        if (hadScheme && hadPort) { Add(uri.Scheme, uri.Port); return list; }
        if (hadPort) { Add("http", uri.Port); Add("https", uri.Port); return list; }
        if (hadScheme) { Add(uri.Scheme, null); Add(uri.Scheme, DefaultPort); return list; }
        Add("http", null);
        Add("http", DefaultPort);
        Add("https", null);
        Add("https", DefaultPort);
        return list;
    }

    private static bool ExplicitPort(string input)
    {
        int slashes = input.IndexOf("://", StringComparison.Ordinal);
        var rest = slashes >= 0 ? input[(slashes + 3)..] : input;
        int slash = rest.IndexOf('/');
        if (slash >= 0) rest = rest[..slash];
        return rest.Contains(':');
    }

    /// <summary>
    /// Asks one address what it is. Two public calls: <c>/status</c> for the version (and
    /// to tell this API from anything else that answers on the port), and
    /// <c>/settings/public</c> for the name and the switches the request form depends on.
    /// </summary>
    public static async Task<ApertureResult<RequestServerInfo>> ProbeAsync(string address, CancellationToken ct = default,
        HttpMessageHandler? handler = null)
    {
        using var http = new SeerrHttp(address, new SeerrAuth.None(), handler);
        var status = await http.GetAsync("status", SeerrJson.Default.SeerrStatusDto, ct).ConfigureAwait(false);
        if (!status.Ok) return status.As<RequestServerInfo>();
        if (string.IsNullOrWhiteSpace(status.Value.Version))
            return ApertureResult<RequestServerInfo>.Fail(ApertureFailureKind.NotJellyfin, $"{http.Host} answered without a version");

        var settings = await http.GetAsync("settings/public", SeerrJson.Default.SeerrPublicSettingsDto, ct).ConfigureAwait(false);
        var s = settings.Ok ? settings.Value : new SeerrPublicSettingsDto();
        if (s.Initialized == false)
            return ApertureResult<RequestServerInfo>.Fail(ApertureFailureKind.Invalid,
                $"{http.Host} has not been set up yet — finish its setup in a browser first");

        return ApertureResult<RequestServerInfo>.Success(new RequestServerInfo(
            http.Address,
            string.IsNullOrWhiteSpace(s.ApplicationTitle) ? "Jellyseerr" : s.ApplicationTitle!,
            status.Value.Version!,
            s.Movie4kEnabled ?? false,
            s.Series4kEnabled ?? false,
            s.PartialRequestsEnabled ?? true,
            JellyfinBacked: s.MediaServerType is null or JellyfinServerType,
            CacheImages: s.CacheImages ?? false,
            HideAvailable: s.HideAvailable ?? false,
            HideBlocklisted: s.HideBlocklisted ?? false));
    }

    /// <summary><c>mediaServerType</c> for Jellyfin (1 is Plex, 3 Emby, 4 not configured).</summary>
    private const int JellyfinServerType = 2;

    /// <summary>Tries every candidate and returns the first that answers as a request
    /// server; the failure reported is the first candidate's.</summary>
    public static async Task<ApertureResult<RequestServerInfo>> DiscoverAsync(string input, CancellationToken ct = default,
        HttpMessageHandler? handler = null)
    {
        var candidates = Candidates(input);
        if (candidates.Count == 0)
            return ApertureResult<RequestServerInfo>.Fail(ApertureFailureKind.Invalid, "That isn't an address");
        ApertureFailure? first = null;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var result = await ProbeAsync(candidate, ct, handler).ConfigureAwait(false);
            if (result.Ok) return result;
            // A server that answered and said "not set up" is the answer, not a miss.
            if (result.Failure!.Kind == ApertureFailureKind.Invalid) return result;
            first ??= result.Failure;
        }
        return ApertureResult<RequestServerInfo>.Fail(first!);
    }

    /// <summary>
    /// Signs in with a Jellyfin user's own name and password, which is how Jellyseerr signs
    /// Jellyfin users in. The password is sent in this one request and not kept; what comes
    /// back — the session cookie — is the credential from here on.
    /// </summary>
    public static async Task<ApertureResult<SeerrServer>> SignInAsync(RequestServerInfo server, string jellyfinServerId,
        string username, string password, CancellationToken ct = default, HttpMessageHandler? handler = null)
    {
        var http = new SeerrHttp(server.Address, new SeerrAuth.None(), handler);
        string? cookie = null;
        var body = new SeerrJellyfinAuthDto { Username = username, Password = password };
        var signIn = await http.SendAsync(HttpMethod.Post, "auth/jellyfin",
            SeerrHttp.JsonContent(body, SeerrJson.Default.SeerrJellyfinAuthDto),
            SeerrJson.Default.SeerrUserDto, ct, c => cookie = c).ConfigureAwait(false);

        return Session(server, jellyfinServerId, http, signIn, cookie);
    }

    /// <summary>
    /// Signs in without a password, through Jellyfin's Quick Connect (Seerr 3.4 and later):
    /// the request server opens a Quick Connect request against Jellyfin and hands back its
    /// code; <paramref name="approve"/> approves that code on Jellyfin with the session this
    /// app already holds; the request server then trades its secret for a session of its own.
    ///
    /// <para>Nothing secret passes through Aperture's hands but the request server's secret,
    /// which is only good for this one exchange. It needs Quick Connect switched on in
    /// Jellyfin (it is by default), and a user the request server knows or may import.</para>
    /// </summary>
    public static async Task<ApertureResult<SeerrServer>> SignInWithJellyfinAsync(RequestServerInfo server,
        string jellyfinServerId, Func<string, CancellationToken, Task<ApertureResult<bool>>> approve,
        CancellationToken ct = default, HttpMessageHandler? handler = null)
    {
        if (!server.SupportsQuickConnect)
            return ApertureResult<SeerrServer>.Fail(ApertureFailureKind.Invalid,
                $"{server.Title} {server.Version} can't sign in through Jellyfin — it needs Seerr 3.4 or later");

        var http = new SeerrHttp(server.Address, new SeerrAuth.None(), handler);
        var initiate = await http.PostAsync("auth/jellyfin/quickconnect/initiate",
            SeerrJson.Default.SeerrQuickConnectCodeDto, ct).ConfigureAwait(false);
        if (!initiate.Ok || initiate.Value.Code is not { Length: > 0 } code || initiate.Value.Secret is not { Length: > 0 } secret)
        {
            http.Dispose();
            return initiate.Ok
                ? ApertureResult<SeerrServer>.Fail(ApertureFailureKind.NotJellyfin, $"{server.Host} started Quick Connect but sent no code")
                // Its own 500 here almost always means Jellyfin refused to open one: Quick
                // Connect is switched off in Jellyfin's dashboard.
                : ApertureResult<SeerrServer>.Fail(initiate.Failure! with
                {
                    Kind = ApertureFailureKind.Forbidden,
                    Message = "Jellyfin's Quick Connect is switched off — use your password instead",
                });
        }

        var approved = await approve(code, ct).ConfigureAwait(false);
        if (!approved.Ok || !approved.Value)
        {
            http.Dispose();
            return ApertureResult<SeerrServer>.Fail(approved.Failure ?? new ApertureFailure(ApertureFailureKind.Forbidden,
                "Jellyfin did not approve the sign-in"));
        }

        string? cookie = null;
        var body = new SeerrQuickConnectSecretDto { Secret = secret };
        var signIn = await http.SendAsync(HttpMethod.Post, "auth/jellyfin/quickconnect/authenticate",
            SeerrHttp.JsonContent(body, SeerrJson.Default.SeerrQuickConnectSecretDto),
            SeerrJson.Default.SeerrUserDto, ct, c => cookie = c).ConfigureAwait(false);
        return Session(server, jellyfinServerId, http, signIn, cookie);
    }

    /// <summary>The end of both Jellyfin sign-ins: a user and a session cookie, or why not.</summary>
    private static ApertureResult<SeerrServer> Session(RequestServerInfo server, string jellyfinServerId, SeerrHttp http,
        ApertureResult<SeerrUserDto> signIn, string? cookie)
    {
        if (!signIn.Ok)
        {
            http.Dispose();
            return ApertureResult<SeerrServer>.Fail(SignInFailure(signIn.Failure!, server));
        }
        if (cookie is null)
        {
            http.Dispose();
            return ApertureResult<SeerrServer>.Fail(ApertureFailureKind.NotJellyfin,
                $"{server.Host} accepted the sign-in but opened no session");
        }

        http.Auth = new SeerrAuth.Session(cookie);
        var user = SeerrServer.MapUser(signIn.Value);
        var account = new RequestServerAccountData
        {
            JellyfinServerId = jellyfinServerId,
            Address = server.Address,
            Auth = RequestServerAuth.Session,
            UserId = user.Id,
            UserName = user.DisplayName,
            SignedInAt = DateTimeOffset.UtcNow,
        };
        return ApertureResult<SeerrServer>.Success(new SeerrServer(server, user, http, account, cookie));
    }

    /// <summary>
    /// A sign-in the server refused, in words. A 401 is the password; a 403 is an account
    /// the request server does not know and will not import; a 500 carries the server's
    /// own reason ("Jellyfin login is disabled") and that sentence is the message.
    /// </summary>
    private static ApertureFailure SignInFailure(ApertureFailure failure, RequestServerInfo server) => failure.Kind switch
    {
        ApertureFailureKind.Unauthorized => failure with { Kind = ApertureFailureKind.BadCredentials },
        ApertureFailureKind.Forbidden => failure with
        {
            Message = $"{server.Title} doesn't know your Jellyfin account and won't add it — an administrator can import it under Users",
        },
        ApertureFailureKind.Server when failure.Message is { Length: > 0 } said && !char.IsDigit(said[0]) =>
            failure with { Kind = ApertureFailureKind.Forbidden },
        _ => failure,
    };

    /// <summary>
    /// Connects with the server's API key. The key is the administrator's, so the Jellyfin
    /// user this app is signed in as is looked up among the request server's users, and —
    /// when found — named on every call (<c>X-API-User</c>), so a request lands in that
    /// person's name and counts against their quota. Not found, the key acts as the
    /// administrator, and the account row says so.
    /// </summary>
    public static async Task<ApertureResult<SeerrServer>> SignInWithApiKeyAsync(RequestServerInfo server,
        string jellyfinServerId, string apiKey, Guid? jellyfinUserId, CancellationToken ct = default,
        HttpMessageHandler? handler = null)
    {
        var http = new SeerrHttp(server.Address, new SeerrAuth.ApiKey(apiKey, null), handler);
        var admin = await http.GetAsync("auth/me", SeerrJson.Default.SeerrUserDto, ct).ConfigureAwait(false);
        if (!admin.Ok)
        {
            http.Dispose();
            var failure = admin.Failure!;
            if (failure.Kind is ApertureFailureKind.Unauthorized or ApertureFailureKind.Forbidden)
                failure = failure with { Kind = ApertureFailureKind.BadCredentials, Message = "That API key was refused" };
            return ApertureResult<SeerrServer>.Fail(failure);
        }

        var me = admin.Value;
        int? asUser = null;
        if (jellyfinUserId is { } wanted)
        {
            var match = await FindJellyfinUserAsync(http, wanted, ct).ConfigureAwait(false);
            if (match is not null && match.Id != me.Id)
            {
                asUser = match.Id;
                http.Auth = new SeerrAuth.ApiKey(apiKey, asUser);
                // Asked as them, so the permissions are theirs rather than the admin's.
                var them = await http.GetAsync("auth/me", SeerrJson.Default.SeerrUserDto, ct).ConfigureAwait(false);
                me = them.Ok ? them.Value : match;
            }
        }

        var user = SeerrServer.MapUser(me);
        var account = new RequestServerAccountData
        {
            JellyfinServerId = jellyfinServerId,
            Address = server.Address,
            Auth = RequestServerAuth.ApiKey,
            UserId = user.Id,
            UserName = user.DisplayName,
            SignedInAt = DateTimeOffset.UtcNow,
        };
        return ApertureResult<SeerrServer>.Success(new SeerrServer(server, user, http, account, apiKey));
    }

    /// <summary>The request server's user for a Jellyfin user, by the Jellyfin id it
    /// imported them with. Paged, because a household server can have more users than one
    /// page holds.</summary>
    private static async Task<SeerrUserDto?> FindJellyfinUserAsync(SeerrHttp http, Guid jellyfinUserId, CancellationToken ct)
    {
        var wanted = jellyfinUserId.ToString("N");
        // Seerr 3.3 answers the question directly; older servers 404 the route, and the
        // user list is walked instead.
        var direct = await http.GetAsync($"user/jellyfin/{wanted}", SeerrJson.Default.SeerrUserDto, ct).ConfigureAwait(false);
        if (direct.Ok && direct.Value.Id > 0) return direct.Value;

        const int take = 100;
        for (int skip = 0; skip < 2000; skip += take)
        {
            var page = await http.GetAsync($"user?take={take}&skip={skip}", SeerrJson.Default.SeerrUserPageDto, ct)
                .ConfigureAwait(false);
            if (!page.Ok) return null;
            var users = page.Value.Results ?? [];
            var match = users.FirstOrDefault(u =>
                SeerrServer.JellyfinId(u.JellyfinUserId) is { } id && id.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
            if (users.Count < take) return null;
        }
        return null;
    }

    /// <summary>
    /// Re-opens a stored session: probes the address again (for the name and switches,
    /// which an administrator may have changed) and asks who the secret belongs to. A
    /// session the server has ended comes back as <see cref="ApertureFailureKind.Unauthorized"/>.
    /// </summary>
    public static async Task<ApertureResult<SeerrServer>> ResumeAsync(RequestServerAccountData account, string secret,
        CancellationToken ct = default, HttpMessageHandler? handler = null)
    {
        var probe = await ProbeAsync(account.Address, ct, handler).ConfigureAwait(false);
        if (!probe.Ok) return probe.As<SeerrServer>();

        SeerrAuth auth = account.Auth == RequestServerAuth.ApiKey
            ? new SeerrAuth.ApiKey(secret, account.UserId > 0 ? account.UserId : null)
            : new SeerrAuth.Session(secret);
        var http = new SeerrHttp(probe.Value.Address, auth, handler);
        var me = await http.GetAsync("auth/me", SeerrJson.Default.SeerrUserDto, ct).ConfigureAwait(false);
        if (!me.Ok)
        {
            http.Dispose();
            return me.As<SeerrServer>();
        }
        var user = SeerrServer.MapUser(me.Value);
        var refreshed = new RequestServerAccountData
        {
            JellyfinServerId = account.JellyfinServerId,
            Address = probe.Value.Address,
            Auth = account.Auth,
            UserId = account.Auth == RequestServerAuth.ApiKey ? account.UserId : user.Id,
            UserName = user.DisplayName,
            SignedInAt = account.SignedInAt,
        };
        return ApertureResult<SeerrServer>.Success(new SeerrServer(probe.Value, user, http, refreshed, secret));
    }
}
