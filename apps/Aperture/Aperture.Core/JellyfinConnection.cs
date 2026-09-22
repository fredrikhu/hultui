using Jellyfin.Sdk;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Aperture.Core;

/// <summary>
/// Supplies the <c>Authorization</c> header on every request. Jellyfin's scheme is not
/// bearer: client, device and version ride along with the token, and the same header goes
/// out before sign-in (with no <c>Token=</c>) because that is how the server learns which
/// device is asking. The token is settable so one adapter survives a sign-in — the
/// anonymous probe and the authenticated session are the same connection.
/// </summary>
internal sealed class JellyfinAuthProvider(DeviceIdentity device, string? token = null) : IAuthenticationProvider
{
    public string? Token { get; set; } = token;
    public DeviceIdentity Device { get; } = device;

    public Task AuthenticateRequestAsync(RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Remove("Authorization");
        request.Headers.Add("Authorization", Device.AuthorizationHeader(Token));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Getting from something the user typed to a signed-in <see cref="IMediaLibrary"/>:
/// address guessing, the public probe, sign-in, and resuming a stored token.
/// </summary>
public static class JellyfinConnection
{
    /// <summary>How long a probe or a sign-in waits before it calls the server unreachable.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(12);

    /// <summary>Jellyfin's own port, tried whenever the user gave no explicit one — which
    /// is most of the time, because "jellyfin.home" is what people type and
    /// <c>http://jellyfin.home/</c> is a connection refused on a box that only listens on
    /// 8096.</summary>
    public const int DefaultPort = 8096;

    /// <summary>
    /// The addresses worth trying for what the user typed, best first. A bare host becomes
    /// four candidates (http and https, given port and 8096); an address with a scheme and
    /// a port is taken at its word and tried alone.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string input)
    {
        var typed = (input ?? "").Trim();
        // The scheme is read before the trailing slashes go, or "http://" arrives below as
        // "http:/" and is taken for a hostname.
        bool hadScheme = typed.Contains("://", StringComparison.Ordinal);
        input = typed.TrimEnd('/');
        if (input.Length == 0) return [];
        // A scheme and nothing else is not an address.
        if (hadScheme && input.EndsWith(':')) return [];
        var withScheme = hadScheme ? input : "http://" + input;
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return [];

        bool hadPort = !uri.IsDefaultPort || HasExplicitPort(input);
        var list = new List<string>();

        void Add(string scheme, int? port)
        {
            var b = new UriBuilder(scheme, uri.Host) { Path = uri.AbsolutePath };
            if (port is { } p) b.Port = p; else b.Port = -1;
            var s = b.Uri.ToString().TrimEnd('/');
            if (!list.Contains(s, StringComparer.OrdinalIgnoreCase)) list.Add(s);
        }

        if (hadScheme && hadPort) { Add(uri.Scheme, uri.Port); return list; }
        if (hadPort) { Add("http", uri.Port); Add("https", uri.Port); return list; }

        // No port given: the scheme's own port first (a reverse proxy is the common
        // deployment), then Jellyfin's.
        if (hadScheme)
        {
            Add(uri.Scheme, null);
            Add(uri.Scheme, DefaultPort);
            return list;
        }
        Add("http", null);
        Add("http", DefaultPort);
        Add("https", null);
        Add("https", DefaultPort);
        return list;
    }

    private static bool HasExplicitPort(string input)
    {
        int slashes = input.IndexOf("://", StringComparison.Ordinal);
        var rest = slashes >= 0 ? input[(slashes + 3)..] : input;
        int slash = rest.IndexOf('/');
        if (slash >= 0) rest = rest[..slash];
        return rest.Contains(':');
    }

    /// <summary>
    /// Asks one address who it is. Public endpoint: no token, no user — this is what turns
    /// a typed address into a named server before anyone is asked for a password.
    /// </summary>
    public static async Task<ApertureResult<ServerInfo>> ProbeAsync(string address, DeviceIdentity device,
        CancellationToken ct = default)
    {
        using var http = NewHttpClient();
        var auth = new JellyfinAuthProvider(device);
        using var adapter = NewAdapter(auth, http, address);
        var client = new JellyfinApiClient(adapter);
        try
        {
            var info = await client.System.Info.Public.GetAsync(cancellationToken: ct).ConfigureAwait(false);
            if (info is null || string.IsNullOrEmpty(info.Id))
                return ApertureResult<ServerInfo>.Fail(ApertureFailureKind.NotJellyfin,
                    $"{address} answered without a server id");
            return ApertureResult<ServerInfo>.Success(new ServerInfo(
                address, info.ServerName ?? address, info.Version ?? "unknown", info.Id!));
        }
        catch (Exception ex)
        {
            return ApertureResult<ServerInfo>.Fail(Classify(ex, address));
        }
    }

    /// <summary>
    /// Tries every candidate for what the user typed and returns the first that answers as
    /// a Jellyfin server. The failure returned is the <i>first</i> candidate's — "can't
    /// reach jellyfin.home" reads better than whatever the last guess said.
    /// </summary>
    public static async Task<ApertureResult<ServerInfo>> DiscoverAsync(string input, DeviceIdentity device,
        CancellationToken ct = default)
    {
        var candidates = Candidates(input);
        if (candidates.Count == 0)
            return ApertureResult<ServerInfo>.Fail(ApertureFailureKind.Invalid, "That isn't an address");

        ApertureFailure? first = null;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var result = await ProbeAsync(candidate, device, ct).ConfigureAwait(false);
            if (result.Ok) return result;
            first ??= result.Failure;
        }
        return ApertureResult<ServerInfo>.Fail(first!);
    }

    /// <summary>
    /// Signs in with a username and password and hands back a library bound to the session.
    /// The password is used for this one request and never stored; what comes back — and
    /// what is worth keeping — is the access token.
    /// </summary>
    public static async Task<ApertureResult<IMediaLibrary>> SignInAsync(ServerInfo server, string username,
        string password, DeviceIdentity device, CancellationToken ct = default)
    {
        var http = NewHttpClient();
        var auth = new JellyfinAuthProvider(device);
        var adapter = NewAdapter(auth, http, server.Address);
        var client = new JellyfinApiClient(adapter);
        try
        {
            var result = await client.Users.AuthenticateByName
                .PostAsync(new AuthenticateUserByName { Username = username, Pw = password }, cancellationToken: ct)
                .ConfigureAwait(false);

            if (result?.AccessToken is not { Length: > 0 } token || result.User?.Id is not { } userId)
            {
                adapter.Dispose(); http.Dispose();
                return ApertureResult<IMediaLibrary>.Fail(ApertureFailureKind.NotJellyfin,
                    "The server accepted the sign-in but sent no token");
            }

            auth.Token = token;
            var user = new SignedInUser(userId, result.User.Name ?? username, token,
                result.User.PrimaryImageTag);
            return ApertureResult<IMediaLibrary>.Success(
                new JellyfinLibrary(server, user, device, client, adapter, http));
        }
        catch (Exception ex)
        {
            adapter.Dispose(); http.Dispose();
            var failure = Classify(ex, server.Host);
            // A rejected password is a 401 here, but it is not an expired session: the
            // sign-in form needs different copy from the rest of the app.
            if (failure.Kind == ApertureFailureKind.Unauthorized)
                failure = failure with { Kind = ApertureFailureKind.BadCredentials };
            return ApertureResult<IMediaLibrary>.Fail(failure);
        }
    }

    /// <summary>
    /// Connects with an API key from the server's dashboard rather than a user's password.
    /// An API key authenticates as the server, not as a person, so the user to browse as
    /// has to be named: <paramref name="userId"/> when the caller knows it, otherwise the
    /// only user on the server (and an error when there are several, because guessing
    /// which one's watch history to write into is not a decision a client should make).
    ///
    /// <para>This is how a headless run signs in — the screenshot driver and the live
    /// smoke tests — without a password ever being typed or stored.</para>
    /// </summary>
    public static async Task<ApertureResult<IMediaLibrary>> SignInWithApiKeyAsync(ServerInfo server, string apiKey,
        string? userId, DeviceIdentity device, CancellationToken ct = default)
    {
        var http = NewHttpClient();
        var auth = new JellyfinAuthProvider(device, apiKey);
        var adapter = NewAdapter(auth, http, server.Address);
        var client = new JellyfinApiClient(adapter);
        try
        {
            var users = await client.Users.GetAsync(cancellationToken: ct).ConfigureAwait(false) ?? [];
            var match = userId is { Length: > 0 }
                ? users.FirstOrDefault(u => u.Id?.ToString("N").Equals(userId.Replace("-", ""), StringComparison.OrdinalIgnoreCase) == true
                                         || string.Equals(u.Name, userId, StringComparison.OrdinalIgnoreCase))
                : users.Count == 1 ? users[0] : null;

            if (match?.Id is not { } id)
            {
                adapter.Dispose(); http.Dispose();
                return ApertureResult<IMediaLibrary>.Fail(ApertureFailureKind.NotFound,
                    userId is { Length: > 0 }
                        ? $"{server.Host} has no user '{userId}'"
                        : $"{server.Host} has {users.Count} users — name the one to browse as");
            }

            var user = new SignedInUser(id, match.Name ?? "", apiKey, match.PrimaryImageTag);
            return ApertureResult<IMediaLibrary>.Success(
                new JellyfinLibrary(server, user, device, client, adapter, http));
        }
        catch (Exception ex)
        {
            adapter.Dispose(); http.Dispose();
            return ApertureResult<IMediaLibrary>.Fail(Classify(ex, server.Host));
        }
    }

    // ── Quick Connect ────────────────────────────────────────────────────

    /// <summary>How often the client asks the server whether the code has been approved.
    /// Two seconds: the person is standing at another screen typing six characters, and
    /// the wait between finishing and this window moving on is the whole experience.</summary>
    public static readonly TimeSpan QuickConnectPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>How long a code is waited on before the attempt is abandoned. The server
    /// expires its own codes — this is only so a window left open overnight stops asking.</summary>
    public static readonly TimeSpan QuickConnectTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Opens a Quick Connect request: the server hands back a code to type into a screen
    /// that is <i>already</i> signed in, and a secret this client keeps to itself. The
    /// secret is the credential — it is what turns into an access token once a signed-in
    /// user approves the code — so it never leaves the process.
    /// </summary>
    public static async Task<ApertureResult<QuickConnectCode>> StartQuickConnectAsync(ServerInfo server,
        DeviceIdentity device, CancellationToken ct = default)
    {
        using var http = NewHttpClient();
        var auth = new JellyfinAuthProvider(device);
        using var adapter = NewAdapter(auth, http, server.Address);
        var client = new JellyfinApiClient(adapter);
        try
        {
            // Asked first, because a server with it switched off answers the initiate call
            // with a 401 — which would otherwise be reported as a rejected sign-in.
            var enabled = await client.QuickConnect.Enabled.GetAsync(cancellationToken: ct).ConfigureAwait(false);
            if (enabled != true)
                return ApertureResult<QuickConnectCode>.Fail(ApertureFailureKind.Forbidden,
                    $"{server.Host} has Quick Connect switched off");

            var result = await client.QuickConnect.Initiate.PostAsync(cancellationToken: ct).ConfigureAwait(false);
            if (result?.Code is not { Length: > 0 } code || result.Secret is not { Length: > 0 } secret)
                return ApertureResult<QuickConnectCode>.Fail(ApertureFailureKind.NotJellyfin,
                    "The server started Quick Connect but sent no code");

            return ApertureResult<QuickConnectCode>.Success(new QuickConnectCode(code, secret));
        }
        catch (Exception ex)
        {
            return ApertureResult<QuickConnectCode>.Fail(Classify(ex, server.Host));
        }
    }

    /// <summary>
    /// One look at whether the code has been approved yet. A single short request, so the
    /// caller owns the waiting: a client that blocks for minutes inside one call has no
    /// way to draw the code it is waiting on.
    /// </summary>
    public static async Task<ApertureResult<bool>> PollQuickConnectAsync(ServerInfo server,
        QuickConnectCode code, DeviceIdentity device, CancellationToken ct = default)
    {
        using var http = NewHttpClient();
        var auth = new JellyfinAuthProvider(device);
        using var adapter = NewAdapter(auth, http, server.Address);
        var client = new JellyfinApiClient(adapter);
        try
        {
            var state = await client.QuickConnect.Connect
                .GetAsync(r => r.QueryParameters.Secret = code.Secret, cancellationToken: ct)
                .ConfigureAwait(false);
            return ApertureResult<bool>.Success(state?.Authenticated == true);
        }
        catch (Exception ex)
        {
            return ApertureResult<bool>.Fail(Classify(ex, server.Host));
        }
    }

    /// <summary>
    /// Trades an approved secret for a session. Called once <see cref="PollQuickConnectAsync"/>
    /// has said yes; a secret that is not approved comes back as
    /// <see cref="ApertureFailureKind.Unauthorized"/>.
    /// </summary>
    public static async Task<ApertureResult<IMediaLibrary>> CompleteQuickConnectAsync(ServerInfo server,
        QuickConnectCode code, DeviceIdentity device, CancellationToken ct = default)
    {
        var http = NewHttpClient();
        var auth = new JellyfinAuthProvider(device);
        var adapter = NewAdapter(auth, http, server.Address);
        var client = new JellyfinApiClient(adapter);
        try
        {
            var result = await client.Users.AuthenticateWithQuickConnect
                .PostAsync(new QuickConnectDto { Secret = code.Secret }, cancellationToken: ct)
                .ConfigureAwait(false);

            if (result?.AccessToken is not { Length: > 0 } token || result.User?.Id is not { } userId)
            {
                adapter.Dispose(); http.Dispose();
                return ApertureResult<IMediaLibrary>.Fail(ApertureFailureKind.NotJellyfin,
                    "The code was approved but the server sent no token");
            }

            auth.Token = token;
            var user = new SignedInUser(userId, result.User.Name ?? "", token, result.User.PrimaryImageTag);
            return ApertureResult<IMediaLibrary>.Success(
                new JellyfinLibrary(server, user, device, client, adapter, http));
        }
        catch (Exception ex)
        {
            adapter.Dispose(); http.Dispose();
            return ApertureResult<IMediaLibrary>.Fail(Classify(ex, server.Host));
        }
    }

    /// <summary>
    /// Poll and complete in one call, for a caller that can afford to sit and wait — a
    /// test, a script. The app does not use it: it draws the code while it waits, which
    /// means it owns the clock.
    /// </summary>
    public static async Task<ApertureResult<IMediaLibrary>> WaitForQuickConnectAsync(ServerInfo server,
        QuickConnectCode code, DeviceIdentity device, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? QuickConnectTimeout);
        while (true)
        {
            var polled = await PollQuickConnectAsync(server, code, device, ct).ConfigureAwait(false);
            if (!polled.Ok) return ApertureResult<IMediaLibrary>.Fail(polled.Failure!);
            if (polled.Value) return await CompleteQuickConnectAsync(server, code, device, ct).ConfigureAwait(false);

            if (DateTimeOffset.UtcNow >= deadline)
                return ApertureResult<IMediaLibrary>.Fail(ApertureFailureKind.Timeout,
                    $"Nobody approved the code on {server.Host}");
            await Task.Delay(QuickConnectPollInterval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Re-opens a session from a token that was kept in the credential store, checking it
    /// still works. A revoked token comes back as <see cref="ApertureFailureKind.Unauthorized"/>,
    /// which is the app's signal to show the sign-in form again.
    /// </summary>
    public static async Task<ApertureResult<IMediaLibrary>> ResumeAsync(ServerInfo server, string token,
        DeviceIdentity device, CancellationToken ct = default)
    {
        var http = NewHttpClient();
        var auth = new JellyfinAuthProvider(device, token);
        var adapter = NewAdapter(auth, http, server.Address);
        var client = new JellyfinApiClient(adapter);
        try
        {
            var me = await client.Users.Me.GetAsync(cancellationToken: ct).ConfigureAwait(false);
            if (me?.Id is not { } id)
            {
                adapter.Dispose(); http.Dispose();
                return ApertureResult<IMediaLibrary>.Fail(ApertureFailureKind.Unauthorized,
                    "The stored token no longer identifies a user");
            }
            var user = new SignedInUser(id, me.Name ?? "", token, me.PrimaryImageTag);
            return ApertureResult<IMediaLibrary>.Success(
                new JellyfinLibrary(server, user, device, client, adapter, http));
        }
        catch (Exception ex)
        {
            adapter.Dispose(); http.Dispose();
            return ApertureResult<IMediaLibrary>.Fail(Classify(ex, server.Host));
        }
    }

    internal static HttpClient NewHttpClient() => new() { Timeout = ConnectTimeout };

    /// <summary>
    /// The Kiota adapter every connection here is built on — with Aperture's own JSON
    /// factories, which is what makes the server's dash-less ids readable (see
    /// <see cref="JellyfinSerialization"/>).
    /// </summary>
    private static HttpClientRequestAdapter NewAdapter(JellyfinAuthProvider auth, HttpClient http, string baseUrl) =>
        new(auth, JellyfinSerialization.ParseNodeFactory, JellyfinSerialization.SerializationWriterFactory, http)
        {
            BaseUrl = baseUrl,
        };

    /// <summary>
    /// Turns whatever the SDK threw into the app's vocabulary. Kiota raises
    /// <see cref="ApiException"/> for an HTTP status it was not told to expect and a
    /// <see cref="HttpRequestException"/> when the socket never got that far; a body that
    /// does not parse means the address is something other than a Jellyfin server.
    /// </summary>
    internal static ApertureFailure Classify(Exception ex, string host) => ex switch
    {
        ApiException api => api.ResponseStatusCode switch
        {
            401 => new ApertureFailure(ApertureFailureKind.Unauthorized, api.Message, 401),
            403 => new ApertureFailure(ApertureFailureKind.Forbidden, api.Message, 403),
            404 => new ApertureFailure(ApertureFailureKind.NotFound, api.Message, 404),
            >= 500 => new ApertureFailure(ApertureFailureKind.Server, api.Message, api.ResponseStatusCode),
            _ => new ApertureFailure(ApertureFailureKind.NotJellyfin, api.Message, api.ResponseStatusCode),
        },
        OperationCanceledException or TimeoutException =>
            new ApertureFailure(ApertureFailureKind.Timeout, $"{host} did not answer within {ConnectTimeout.TotalSeconds:0} s"),
        HttpRequestException http =>
            new ApertureFailure(ApertureFailureKind.Network, http.Message, (int?)http.StatusCode),
        System.Text.Json.JsonException or InvalidOperationException or FormatException =>
            new ApertureFailure(ApertureFailureKind.NotJellyfin, ex.Message),
        _ => new ApertureFailure(ApertureFailureKind.Network, ex.Message),
    };
}
