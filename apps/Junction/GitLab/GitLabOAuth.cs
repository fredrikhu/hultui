using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Junction.GitLab;

/// <summary>Tokens as the app keeps them: when the access token dies, and what it may do.</summary>
public sealed record GitLabOAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, IReadOnlyList<string> Scopes);

/// <summary>The PKCE pair for one sign-in attempt.</summary>
public sealed record Pkce(string Verifier, string Challenge);

/// <summary>
/// OAuth 2.0 authorization-code sign-in with PKCE, the way a desktop app does it: the system
/// browser signs the user in on the instance, the instance redirects to a loopback URL this
/// process is listening on, and the code that arrives is exchanged for tokens with the
/// verifier as the proof — no client secret exists, so none can leak from the binary.
/// </summary>
/// <remarks>
/// The listener is a raw <see cref="TcpListener"/> rather than <c>HttpListener</c>: on
/// Windows the latter goes through http.sys, which refuses to bind a URL for a non-elevated
/// process without a <c>netsh urlacl</c> reservation — the exact failure that made MSAL and
/// gcloud speak HTTP by hand too. One GET, one reply, done.
/// <para>The redirect is <c>http://localhost:7171/auth/redirect</c>, the address the design's
/// "Waiting for the browser" card names; it has to match the application registered on the
/// instance character for character, so the port is fixed, not ephemeral.</para>
/// </remarks>
public static class GitLabOAuth
{
    /// <summary>The OAuth application registered on gitlab.com for Junction. Empty until the
    /// registration is done (User settings › Applications: redirect
    /// <see cref="RedirectUri"/>, <b>not</b> confidential, scopes <see cref="DefaultScopes"/>);
    /// self-managed instances register their own and put the id in the account.</summary>
    public const string GitLabComClientId = "";

    public const int DefaultPort = 7171;
    public const string RedirectPath = "/auth/redirect";
    public static readonly string[] DefaultScopes = ["api", "read_user"];

    public static string RedirectUri(int port = DefaultPort) => $"http://localhost:{port}{RedirectPath}";

    /// <summary>A 64-character verifier from 48 random bytes and its S256 challenge.</summary>
    public static Pkce CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        return new Pkce(verifier, ChallengeFor(verifier));
    }

    public static string ChallengeFor(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string AuthorizeUrl(string webBaseUrl, string clientId, string redirectUri, string state, string challenge,
        IEnumerable<string>? scopes = null)
    {
        var scope = string.Join(' ', scopes ?? DefaultScopes);
        return $"{webBaseUrl.TrimEnd('/')}/oauth/authorize" +
               $"?client_id={Uri.EscapeDataString(clientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               "&response_type=code" +
               $"&state={Uri.EscapeDataString(state)}" +
               $"&scope={Uri.EscapeDataString(scope)}" +
               $"&code_challenge={challenge}" +
               "&code_challenge_method=S256";
    }

    /// <summary>
    /// The whole dance. Listens first, opens the browser second (so the redirect cannot beat
    /// the listener), waits for the code, exchanges it. <paramref name="openBrowser"/> is
    /// injected so a test can drive the redirect itself; it returns false when nothing could
    /// open the URL, which ends the attempt with a failure rather than a silent wait.
    /// </summary>
    public static async Task<GitLabResult<GitLabOAuthTokens>> SignInAsync(string webBaseUrl, string clientId,
        Func<string, bool> openBrowser, HttpMessageHandler? handler = null, int port = DefaultPort,
        IEnumerable<string>? scopes = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(clientId))
            return GitLabResult<GitLabOAuthTokens>.Fail(GitLabFailureKind.Invalid,
                "No OAuth application is registered for this host — paste a personal access token instead");

        var pkce = CreatePkce();
        var state = Base64Url(RandomNumberGenerator.GetBytes(24));

        using var listener = new LoopbackListener(port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            return GitLabResult<GitLabOAuthTokens>.Fail(GitLabFailureKind.Network,
                $"Port {port} is in use, so the browser could not hand the sign-in back ({ex.SocketErrorCode})");
        }

        // From the listener, not the argument: a test passes 0 and gets whatever was free.
        var redirect = RedirectUri(listener.Port);
        var url = AuthorizeUrl(webBaseUrl, clientId, redirect, state, pkce.Challenge, scopes);
        if (!openBrowser(url))
            return GitLabResult<GitLabOAuthTokens>.Fail(GitLabFailureKind.Invalid, "No browser could be opened for the sign-in");

        var callback = await listener.WaitForRedirectAsync(ct).ConfigureAwait(false);
        if (callback.Error is { } error)
            return GitLabResult<GitLabOAuthTokens>.Fail(GitLabFailureKind.Unauthorized,
                error == "access_denied" ? "Sign-in was cancelled in the browser" : $"{error}: {callback.ErrorDescription}");
        if (callback.State != state)
            return GitLabResult<GitLabOAuthTokens>.Fail(GitLabFailureKind.Invalid,
                "The sign-in that came back was not the one this app started");
        if (string.IsNullOrEmpty(callback.Code))
            return GitLabResult<GitLabOAuthTokens>.Fail(GitLabFailureKind.Invalid, "The browser came back without a code");

        return await ExchangeCodeAsync(webBaseUrl, clientId, callback.Code, pkce.Verifier, redirect, handler, ct).ConfigureAwait(false);
    }

    public static Task<GitLabResult<GitLabOAuthTokens>> ExchangeCodeAsync(string webBaseUrl, string clientId, string code,
        string verifier, string redirectUri, HttpMessageHandler? handler = null, CancellationToken ct = default) =>
        TokenRequestAsync(webBaseUrl, handler, ct,
            ("client_id", clientId),
            ("code", code),
            ("grant_type", "authorization_code"),
            ("redirect_uri", redirectUri),
            ("code_verifier", verifier));

    /// <summary>A new pair; GitLab invalidates the old refresh token in the same reply.</summary>
    public static Task<GitLabResult<GitLabOAuthTokens>> RefreshAsync(string webBaseUrl, string clientId, string refreshToken,
        HttpMessageHandler? handler = null, CancellationToken ct = default) =>
        TokenRequestAsync(webBaseUrl, handler, ct,
            ("client_id", clientId),
            ("refresh_token", refreshToken),
            ("grant_type", "refresh_token"));

    /// <summary>Best effort: a revoke that fails leaves a token that expires on its own.</summary>
    public static async Task<bool> RevokeAsync(string webBaseUrl, string clientId, string token,
        HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(10);
        try
        {
            using var content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("token", token)]);
            using var res = await http.PostAsync($"{webBaseUrl.TrimEnd('/')}/oauth/revoke", content, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task<GitLabResult<GitLabOAuthTokens>> TokenRequestAsync(string webBaseUrl, HttpMessageHandler? handler,
        CancellationToken ct, params (string Key, string Value)[] fields)
    {
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(20);
        var host = new Uri(webBaseUrl).Host;
        HttpResponseMessage res;
        try
        {
            using var content = new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));
            res = await http.PostAsync($"{webBaseUrl.TrimEnd('/')}/oauth/token", content, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return GitLabResult<GitLabOAuthTokens>.Fail(GitLabFailureKind.Network, $"Could not reach {host}: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return GitLabResult<GitLabOAuthTokens>.Fail(GitLabFailureKind.Timeout, $"{host} did not answer in time");
        }

        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                string said = $"{(int)res.StatusCode} {res.ReasonPhrase}";
                try
                {
                    var err = JsonSerializer.Deserialize(text, GitLabJson.Default.GlErrorBody);
                    said = err?.ErrorDescription ?? err?.Error ?? said;
                }
                catch (JsonException) { }
                var kind = (int)res.StatusCode is 400 or 401 ? GitLabFailureKind.Unauthorized : GitLabFailureKind.Server;
                return GitLabResult<GitLabOAuthTokens>.Fail(kind, said, (int)res.StatusCode);
            }

            GlTokenResponse? tokens;
            try { tokens = JsonSerializer.Deserialize(text, GitLabJson.Default.GlTokenResponse); }
            catch (JsonException) { tokens = null; }
            if (tokens is null || string.IsNullOrEmpty(tokens.AccessToken))
                return GitLabResult<GitLabOAuthTokens>.Fail(GitLabFailureKind.Invalid, $"{host} did not return an access token");

            var issued = tokens.CreatedAt is { } created ? DateTimeOffset.FromUnixTimeSeconds(created) : DateTimeOffset.UtcNow;
            var expires = issued.AddSeconds(tokens.ExpiresIn ?? 7200);
            var scopes = (tokens.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return GitLabResult<GitLabOAuthTokens>.Success(new GitLabOAuthTokens(tokens.AccessToken, tokens.RefreshToken, expires, scopes));
        }
    }

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>What the browser brought back on the redirect.</summary>
    public sealed record RedirectCallback(string? Code, string? State, string? Error, string? ErrorDescription);

    /// <summary>
    /// Accepts connections on <c>127.0.0.1:port</c> and <c>[::1]:port</c> until one asks for
    /// <see cref="RedirectPath"/>; anything else (a favicon probe) gets a 404 and the wait
    /// continues. Replies with a small page telling the user to close the tab.
    ///
    /// <para>Both, because the redirect names <c>localhost</c> and Windows resolves that to
    /// <c>::1</c> first. A client that tries <c>::1</c>, finds nothing there and only then
    /// falls back to <c>127.0.0.1</c> loses two seconds to Windows retrying the refused
    /// connection; .NET's HttpClient does exactly that, measured at 2 s a redirect against the
    /// IPv4-only listener this used to be. When <c>::1</c> cannot be bound (no IPv6, or the
    /// port taken there) the listener carries on with IPv4 alone, as before.</para>
    /// </summary>
    public sealed class LoopbackListener : IDisposable
    {
        private readonly TcpListener _listener;
        private TcpListener? _ipv6;

        public LoopbackListener(int port) => _listener = new TcpListener(IPAddress.Loopback, port);

        public void Start()
        {
            _listener.Start();
            if (!Socket.OSSupportsIPv6) return;
            try
            {
                // On the port IPv4 got, which for a requested 0 is only known now.
                _ipv6 = new TcpListener(IPAddress.IPv6Loopback, Port);
                _ipv6.Start();
            }
            catch (SocketException)
            {
                _ipv6 = null;
            }
        }

        /// <summary>The bound port — the requested one, or the free one chosen for 0.</summary>
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public async Task<RedirectCallback> WaitForRedirectAsync(CancellationToken ct)
        {
            // Stops whichever accept is still waiting once the redirect has come in.
            using var done = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try { return await AcceptRedirectAsync(done.Token).ConfigureAwait(false); }
            finally { done.Cancel(); }
        }

        private async Task<RedirectCallback> AcceptRedirectAsync(CancellationToken ct)
        {
            Task<TcpClient>? ipv4 = null, ipv6 = null;
            while (true)
            {
                ipv4 ??= _listener.AcceptTcpClientAsync(ct).AsTask();
                if (_ipv6 is not null)
                    ipv6 ??= _ipv6.AcceptTcpClientAsync(ct).AsTask();

                var accepted = ipv6 is null ? ipv4 : await Task.WhenAny(ipv4, ipv6).ConfigureAwait(false);
                if (accepted == ipv4) ipv4 = null; else ipv6 = null;

                using var client = await accepted.ConfigureAwait(false);
                client.ReceiveTimeout = 5000;
                using var stream = client.GetStream();
                var requestLine = await ReadRequestLineAsync(stream, ct).ConfigureAwait(false);
                var target = RequestTarget(requestLine);
                if (target is null || !target.StartsWith(RedirectPath, StringComparison.Ordinal))
                {
                    await WriteAsync(stream, 404, "Not found", ct).ConfigureAwait(false);
                    continue;
                }

                var callback = Parse(target);
                var ok = callback.Error is null && !string.IsNullOrEmpty(callback.Code);
                await WriteAsync(stream, ok ? 200 : 400, ok
                    ? "<h2>Signed in to GitLab.</h2><p>You can close this tab and go back to Junction.</p>"
                    : "<h2>Sign-in did not complete.</h2><p>Go back to Junction and try again.</p>", ct).ConfigureAwait(false);
                return callback;
            }
        }

        /// <summary>The query string of the redirect, as GitLab sends it.</summary>
        public static RedirectCallback Parse(string target)
        {
            int q = target.IndexOf('?');
            string? code = null, state = null, error = null, desc = null;
            if (q >= 0)
            {
                foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    var key = eq < 0 ? pair : pair[..eq];
                    var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                    switch (key)
                    {
                        case "code": code = value; break;
                        case "state": state = value; break;
                        case "error": error = value; break;
                        case "error_description": desc = value; break;
                    }
                }
            }
            return new RedirectCallback(code, state, error, desc);
        }

        private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
        {
            // Read up to the end of the headers (or 8 KB), keep only the request line. The
            // rest is drained so the browser's socket is not reset before it reads the reply.
            var buffer = new byte[8192];
            int total = 0;
            while (total < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
                if (n == 0) break;
                total += n;
                if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
            }
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            int eol = text.IndexOf('\n');
            return (eol < 0 ? text : text[..eol]).TrimEnd('\r');
        }

        private static string? RequestTarget(string requestLine)
        {
            var parts = requestLine.Split(' ');
            return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
        }

        private static async Task WriteAsync(NetworkStream stream, int status, string html, CancellationToken ct)
        {
            var body = Encoding.UTF8.GetBytes(
                $"<!doctype html><html><head><meta charset=\"utf-8\"><title>Junction</title></head>" +
                $"<body style=\"font-family:system-ui;background:#161826;color:#e9e9ed;padding:48px\">{html}</body></html>");
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {(status == 200 ? "OK" : status == 404 ? "Not Found" : "Bad Request")}\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(head, ct).ConfigureAwait(false);
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _listener.Stop();
            _ipv6?.Stop();
        }
    }
}
