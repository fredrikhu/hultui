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
            return ApertureResult<ServerInfo>.Fail(ApertureFailureKind.NotJellyfin, "That isn't an address");

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
        TaskCanceledException or TimeoutException =>
            new ApertureFailure(ApertureFailureKind.Timeout, $"{host} did not answer within {ConnectTimeout.TotalSeconds:0} s"),
        HttpRequestException http =>
            new ApertureFailure(ApertureFailureKind.Network, http.Message, (int?)http.StatusCode),
        System.Text.Json.JsonException or InvalidOperationException or FormatException =>
            new ApertureFailure(ApertureFailureKind.NotJellyfin, ex.Message),
        _ => new ApertureFailure(ApertureFailureKind.Network, ex.Message),
    };
}
