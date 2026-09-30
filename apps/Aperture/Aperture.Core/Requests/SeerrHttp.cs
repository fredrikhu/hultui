using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Aperture.Core.Requests;

/// <summary>How a request to the request server proves who it is.</summary>
internal abstract record SeerrAuth
{
    /// <summary>A session cookie from a password sign-in.</summary>
    public sealed record Session(string Cookie) : SeerrAuth;

    /// <summary>The server's API key, optionally acting as one user (<c>X-API-User</c>).
    /// Without a user it acts as the administrator.</summary>
    public sealed record ApiKey(string Key, int? AsUser) : SeerrAuth;

    public sealed record None : SeerrAuth;

    /// <summary>The headers this way of signing in adds. A session's cookie is not one of
    /// them: <see cref="SeerrHttp"/> sends every cookie in one header of its own.</summary>
    public void Apply(HttpRequestMessage request)
    {
        switch (this)
        {
            case ApiKey k:
                request.Headers.TryAddWithoutValidation("X-Api-Key", k.Key);
                if (k.AsUser is { } user)
                    request.Headers.TryAddWithoutValidation("X-API-User", user.ToString(CultureInfo.InvariantCulture));
                break;
        }
    }
}

/// <summary>
/// The HTTP underneath <see cref="SeerrServer"/> and <see cref="SeerrConnection"/>: one base
/// address, one way of authenticating, JSON in and out through the source-generated
/// <see cref="SeerrJson"/> context (no reflection, so it survives Native AOT). Every call
/// returns an <see cref="ApertureResult{T}"/>; nothing throws but the caller's own
/// cancellation.
/// </summary>
internal sealed class SeerrHttp : IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;

    public SeerrHttp(string address, SeerrAuth auth, HttpMessageHandler? handler = null)
    {
        Address = address.TrimEnd('/');
        Auth = auth;
        // No cookie container: the session cookie is sent by hand, from the credential
        // store, and nothing the server sets is kept by accident. No redirects either — an
        // address that redirects is answered by whatever it redirects to, and a login page
        // behind a proxy must read as "not Jellyseerr", not be parsed.
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout;
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Aperture/1.0");
    }

    /// <summary><c>http://jellyseerr.home:5055</c> — no trailing slash, no <c>/api/v1</c>.</summary>
    public string Address { get; }

    public SeerrAuth Auth { get; set; }

    public string Host => Uri.TryCreate(Address, UriKind.Absolute, out var u) ? u.Authority : Address;

    public Task<ApertureResult<T>> GetAsync<T>(string pathAndQuery, JsonTypeInfo<T> type, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, pathAndQuery, null, type, ct);

    public Task<ApertureResult<T>> PostAsync<TBody, T>(string path, TBody body, JsonTypeInfo<TBody> bodyType,
        JsonTypeInfo<T> type, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, path, JsonContent(body, bodyType), type, ct);

    public Task<ApertureResult<T>> PostAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, path, null, type, ct);

    /// <summary>A call whose answer is only its status — a delete, a watchlist add.</summary>
    public async Task<ApertureResult<bool>> SendStatusAsync(HttpMethod method, string path, CancellationToken ct = default,
        HttpContent? content = null)
    {
        var sent = await SendRawAsync(method, path, content, ct).ConfigureAwait(false);
        if (!sent.Ok) return sent.As<bool>();
        using var response = sent.Value;
        if (response.IsSuccessStatusCode) return ApertureResult<bool>.Success(true);
        return ApertureResult<bool>.Fail(await FailureFromAsync(response, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Sends one request and reads the JSON answer. The response's <c>Set-Cookie</c> for the
    /// session is handed to <paramref name="cookie"/> when asked for — the one place that
    /// matters is the password sign-in, whose whole product is that cookie.
    /// </summary>
    public async Task<ApertureResult<T>> SendAsync<T>(HttpMethod method, string pathAndQuery, HttpContent? content,
        JsonTypeInfo<T> type, CancellationToken ct = default, Action<string>? cookie = null)
    {
        var sent = await SendRawAsync(method, pathAndQuery, content, ct).ConfigureAwait(false);
        if (!sent.Ok) return sent.As<T>();

        using (var response = sent.Value)
        {
            if (!response.IsSuccessStatusCode)
                return ApertureResult<T>.Fail(await FailureFromAsync(response, ct).ConfigureAwait(false));

            if (cookie is not null && SessionCookie(response) is { } session) cookie(session);

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var body = await JsonSerializer.DeserializeAsync(stream, type, ct).ConfigureAwait(false);
                return body is null
                    ? ApertureResult<T>.Fail(ApertureFailureKind.NotJellyfin, $"{Host} sent an empty answer", (int)response.StatusCode)
                    : ApertureResult<T>.Success(body);
            }
            catch (JsonException ex)
            {
                return ApertureResult<T>.Fail(ApertureFailureKind.NotJellyfin,
                    $"{Host} did not answer with the JSON Jellyseerr's API uses ({ex.Message})", (int)response.StatusCode);
            }
        }
    }

    public static StringContent JsonContent<T>(T body, JsonTypeInfo<T> type) =>
        new(JsonSerializer.Serialize(body, type), Encoding.UTF8, "application/json");

    // ── CSRF ─────────────────────────────────────────────────────────────
    //
    // With "CSRF protection" switched on in its network settings, the server sets two
    // cookies on every response — `_csrf`, a secret, and `XSRF-TOKEN`, a token derived from
    // it — and refuses any write that does not send the secret back as a cookie and the
    // token as a header. It applies to API-key calls too. A browser does that dance by
    // itself; this client keeps the two values and does it by hand.

    private string? _csrfCookie;
    private string? _xsrfToken;

    private const string CsrfCookieName = "_csrf";
    private const string XsrfCookieName = "XSRF-TOKEN";

    /// <summary>
    /// The one send everything goes through: builds the request (auth, cookies, the CSRF
    /// token on writes), sends it, and keeps whatever CSRF cookies the answer set. A write
    /// refused for a bad CSRF token when none has been seen yet is retried once, after a
    /// read that fetches one — which is how the first write of a session goes on a server
    /// with the protection on.
    /// </summary>
    private async Task<ApertureResult<HttpResponseMessage>> SendRawAsync(HttpMethod method, string pathAndQuery,
        HttpContent? content, CancellationToken ct)
    {
        // The body is buffered so a retry can send it again.
        byte[]? body = content is null ? null : await content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var contentType = content?.Headers.ContentType;
        content?.Dispose();

        bool hadToken = _xsrfToken is not null;
        var first = await SendOnceAsync(method, pathAndQuery, body, contentType, ct).ConfigureAwait(false);
        // Only a write, only the first time, and only a refusal that says it is about CSRF.
        if (!first.Ok || method == HttpMethod.Get || hadToken || first.Value.StatusCode != HttpStatusCode.Forbidden)
            return first;
        if (!await IsCsrfRefusalAsync(first.Value, ct).ConfigureAwait(false))
            return first;

        first.Value.Dispose();
        if (_xsrfToken is null)
        {
            // Any read will do: the server sets the cookies on every response.
            var primed = await SendOnceAsync(HttpMethod.Get, "status", null, null, ct).ConfigureAwait(false);
            if (primed.Ok) primed.Value.Dispose();
        }
        return await SendOnceAsync(method, pathAndQuery, body, contentType, ct).ConfigureAwait(false);
    }

    private async Task<ApertureResult<HttpResponseMessage>> SendOnceAsync(HttpMethod method, string pathAndQuery,
        byte[]? body, MediaTypeHeaderValue? contentType, CancellationToken ct)
    {
        using var request = NewRequest(method, pathAndQuery, body, contentType);
        try
        {
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            KeepCsrfCookies(response);
            return ApertureResult<HttpResponseMessage>.Success(response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ApertureResult<HttpResponseMessage>.Fail(Classify(ex));
        }
    }

    /// <summary>The csurf middleware's refusal reads "invalid csrf token"; a permission
    /// refusal is a 403 too, and must not be retried as if it were one.</summary>
    private static async Task<bool> IsCsrfRefusalAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await response.Content.LoadIntoBufferAsync(ct).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return text.Contains("csrf", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return false;
        }
    }

    private void KeepCsrfCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;
        foreach (var header in values)
        {
            int semi = header.IndexOf(';');
            var pair = (semi >= 0 ? header[..semi] : header).Trim();
            if (pair.StartsWith(CsrfCookieName + "=", StringComparison.Ordinal))
                _csrfCookie = pair;
            else if (pair.StartsWith(XsrfCookieName + "=", StringComparison.Ordinal))
                _xsrfToken = Uri.UnescapeDataString(pair[(XsrfCookieName.Length + 1)..]);
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string pathAndQuery, byte[]? body,
        MediaTypeHeaderValue? contentType)
    {
        var request = new HttpRequestMessage(method, Address + "/api/v1/" + pathAndQuery.TrimStart('/'));
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = contentType;
        }

        // One Cookie header: the session (when signed in with one) and the CSRF secret
        // (when the server has handed one out).
        var cookies = new List<string>(2);
        if (Auth is SeerrAuth.Session session) cookies.Add(session.Cookie);
        if (_csrfCookie is { } csrf) cookies.Add(csrf);
        if (cookies.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies));
        if (method != HttpMethod.Get && _xsrfToken is { } token)
            request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", token);

        Auth.Apply(request);
        return request;
    }

    /// <summary>The name the session cookie goes by. Every one of the three servers is an
    /// Express app with express-session's default name.</summary>
    public const string SessionCookieName = "connect.sid";

    /// <summary><c>connect.sid=…</c> from a response, ready to send back as a Cookie header,
    /// or null when the response set none.</summary>
    internal static string? SessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var header in values)
        {
            int semi = header.IndexOf(';');
            var pair = (semi >= 0 ? header[..semi] : header).Trim();
            if (pair.StartsWith(SessionCookieName + "=", StringComparison.Ordinal) && pair.Length > SessionCookieName.Length + 1)
                return pair;
        }
        return null;
    }

    private ApertureFailure Classify(Exception ex) => ex switch
    {
        TaskCanceledException or OperationCanceledException or TimeoutException =>
            new ApertureFailure(ApertureFailureKind.Timeout, $"{Host} did not answer within {Timeout.TotalSeconds:0} s"),
        HttpRequestException http => new ApertureFailure(ApertureFailureKind.Network, http.Message, (int?)http.StatusCode),
        _ => new ApertureFailure(ApertureFailureKind.Network, ex.Message),
    };

    /// <summary>A failed status, with the server's own sentence when it sent one — "You do
    /// not have permission to request 4K" is the message, not "403".</summary>
    private async Task<ApertureFailure> FailureFromAsync(HttpResponseMessage response, CancellationToken ct)
    {
        int status = (int)response.StatusCode;
        string message = response.ReasonPhrase is { Length: > 0 } rp ? $"{status} {rp}" : status.ToString(CultureInfo.InvariantCulture);
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (text.Length is > 0 and < 64 * 1024 && text.TrimStart().StartsWith('{'))
            {
                var error = JsonSerializer.Deserialize(text, SeerrJson.Default.SeerrErrorDto);
                if (!string.IsNullOrWhiteSpace(error?.Message)) message = error.Message!;
                else if (!string.IsNullOrWhiteSpace(error?.Error)) message = error.Error!;
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
            // A proxy's HTML error page: the status line is all there is.
        }

        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => ApertureFailureKind.Unauthorized,
            HttpStatusCode.Forbidden => ApertureFailureKind.Forbidden,
            HttpStatusCode.NotFound => ApertureFailureKind.NotFound,
            // A request the server refused on its merits — already requested, over quota,
            // a season that does not exist. The server's sentence is the whole message.
            HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity
                => ApertureFailureKind.Invalid,
            _ when status >= 500 => ApertureFailureKind.Server,
            _ when status is >= 300 and < 400 => ApertureFailureKind.NotJellyfin,
            _ => ApertureFailureKind.NotJellyfin,
        };
        return new ApertureFailure(kind, message, status);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
