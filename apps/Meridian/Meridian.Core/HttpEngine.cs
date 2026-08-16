using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Meridian.Core;

/// <summary>
/// Sends requests and classifies what came back. One engine per app session:
/// it owns the connection pool, the session-only cookie jar, and any spill
/// files (deleted on dispose).
///
/// <para>Phase timings come from instrumenting the handler's
/// <c>ConnectCallback</c> (DNS + TCP, and it simply not running means the pool
/// reused a connection) and <c>PlaintextStreamFilter</c> (runs after the TLS
/// handshake, so its timestamp closes the TLS phase). TTFB and download are
/// measured around <c>ResponseHeadersRead</c>.</para>
///
/// <para>An HTTP error status is a successful send. Only the four infra
/// failures — DNS, refused, TLS, timeout — return <see cref="InfraFailure"/>,
/// each with the host / endpoint / certificate subject the UI copy needs.
/// Caller cancellation propagates as <see cref="OperationCanceledException"/>;
/// it is not a failure kind.</para>
/// </summary>
public sealed class HttpEngine : IDisposable
{
    /// <summary>How much of a text body the viewer keeps in memory (the mock's
    /// "showing first 1 MB of n"). Test-sized in tests.</summary>
    private readonly int _viewerCapBytes;

    private readonly SocketsHttpHandler _handler;
    private readonly HttpClient _client;
    private readonly List<string> _spills = [];
    private readonly object _spillLock = new();

    public CookieContainer Cookies { get; } = new();

    private static readonly HttpRequestOptionsKey<ConnectionState> StateKey = new("meridian-conn-state");
    private static readonly AsyncLocal<ConnectionState?> CurrentState = new();

    private sealed class ConnectionState
    {
        public bool ConnectCalled;
        public TimeSpan Dns, Connect, Tls;
        public long ConnectDoneTs;
        public string? RemoteEndpoint;
        public string? TlsProtocol;
        public string? CertSubject;
        public string? CertThumbprint;
    }

    // Certificates the user explicitly trusted for this session/workspace, by
    // thumbprint. The GUI feeds these from the workspace's trust file; the
    // validation callback lets them through where default validation would not.
    private readonly HashSet<string> _trustedThumbprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _trustLock = new();

    public void TrustCertificate(string thumbprint)
    {
        lock (_trustLock) _trustedThumbprints.Add(thumbprint);
    }

    /// <summary>Thrown from the connect callback so the failure keeps its phase;
    /// unwrapped from the handler's HttpRequestException on the way out.</summary>
    private sealed class ConnectPhaseException(InfraKind kind, string headline, string detail) : Exception(headline)
    {
        public InfraKind Kind => kind;
        // Read back off the base rather than captured as well: handing the same string to
        // Exception(headline) and to a field stores it twice, which is what CS9107 warns about.
        public string Headline => Message;
        public string Detail => detail;
    }

    public HttpEngine(int viewerCapBytes = 1024 * 1024)
    {
        _viewerCapBytes = viewerCapBytes;
        _handler = new SocketsHttpHandler
        {
            CookieContainer = Cookies,
            AllowAutoRedirect = true,
            ConnectCallback = ConnectAsync,
            PlaintextStreamFilter = (context, _) =>
            {
                if (Correlate(context.InitialRequestMessage) is { } state)
                {
                    if (context.PlaintextStream is SslStream ssl)
                    {
                        state.TlsProtocol = ssl.SslProtocol.ToString();
                        state.Tls = Elapsed(state.ConnectDoneTs);
                    }
                }
                return ValueTask.FromResult(context.PlaintextStream);
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                // Standard validation, but remember the certificate first — a
                // rejected handshake must still name what it rejected.
                RemoteCertificateValidationCallback = (_, cert, _, errors) =>
                {
                    string? thumbprint = (cert as System.Security.Cryptography.X509Certificates.X509Certificate2)?.Thumbprint
                        ?? cert?.GetCertHashString();
                    if (CurrentState.Value is { } state && cert != null)
                    {
                        state.CertSubject = cert.Subject;
                        state.CertThumbprint = thumbprint;
                    }
                    if (errors == SslPolicyErrors.None) return true;
                    lock (_trustLock)
                        return thumbprint != null && _trustedThumbprints.Contains(thumbprint);
                },
            },
        };
        _client = new HttpClient(_handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public async Task<SendResult> SendAsync(RequestSpec spec, CancellationToken ct = default)
    {
        var state = new ConnectionState();
        CurrentState.Value = state;
        var total = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(spec.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var uri = new Uri(spec.Url, UriKind.Absolute);
        bool gotHeaders = false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Parse(spec.Method), uri);
            request.Options.Set(StateKey, state);
            if (spec.BodyText != null)
            {
                request.Content = new StringContent(spec.BodyText);
                request.Content.Headers.ContentType = spec.BodyContentType != null
                    ? System.Net.Http.Headers.MediaTypeHeaderValue.Parse(spec.BodyContentType)
                    : null;
            }
            foreach (var (name, value) in spec.Headers)
            {
                if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    (request.Content ??= new ByteArrayContent([])).Headers.TryAddWithoutValidation(name, value);
                else
                    request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            gotHeaders = true;
            var ttfb = total.Elapsed;

            var body = await CaptureBodyAsync(response, linked.Token);
            total.Stop();

            var headers = response.Headers
                .Concat(response.Content.Headers)
                .SelectMany(h => h.Value.Select(v => (h.Key, v)))
                .ToList();

            var timings = new PhaseTimings(
                state.Dns, state.Connect, state.Tls,
                TimeToFirstByte: ttfb,
                Download: total.Elapsed - ttfb,
                Total: total.Elapsed,
                ConnectionReused: !state.ConnectCalled,
                HttpVersion: $"HTTP/{response.Version}",
                TlsProtocol: state.TlsProtocol);

            return SendResult.Ok(new HttpResponseData(
                (int)response.StatusCode,
                response.ReasonPhrase ?? "",
                headers,
                response.Content.Headers.ContentType?.ToString(),
                body,
                timings,
                response.RequestMessage?.RequestUri?.ToString() ?? spec.Url));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            total.Stop();
            string detail = !state.ConnectCalled ? "never connected"
                : gotHeaders ? "connected · headers received · body stalled"
                : "connected · no response bytes";
            return SendResult.Infra(new InfraFailure(
                InfraKind.Timeout,
                $"timed out after {Round(spec.Timeout)}",
                detail,
                total.Elapsed));
        }
        catch (Exception e) when (Unwrap<ConnectPhaseException>(e) is { } phase)
        {
            total.Stop();
            return SendResult.Infra(new InfraFailure(phase.Kind, phase.Headline, phase.Detail, total.Elapsed));
        }
        catch (HttpRequestException e) when (Unwrap<AuthenticationException>(e) != null)
        {
            total.Stop();
            string detail = state.CertSubject is { } subject
                ? $"certificate not trusted · {subject}"
                : Unwrap<AuthenticationException>(e)!.Message;
            return SendResult.Infra(new InfraFailure(
                InfraKind.TlsFailure, "TLS handshake failed", detail, total.Elapsed)
            { CertThumbprint = state.CertThumbprint });
        }
        catch (HttpRequestException e) when (Unwrap<SocketException>(e) is { } socket)
        {
            // Fallback for socket errors that surface outside the connect callback.
            total.Stop();
            return SendResult.Infra(Classify(socket, uri, state, total.Elapsed));
        }
        finally
        {
            CurrentState.Value = null;
        }
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var state = Correlate(context.InitialRequestMessage);
        if (state != null) state.ConnectCalled = true;

        string host = context.DnsEndPoint.Host;
        int port = context.DnsEndPoint.Port;
        var sw = Stopwatch.StartNew();

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
            addresses = [literal];
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, ct);
                if (addresses.Length == 0) throw new SocketException((int)SocketError.HostNotFound);
            }
            catch (SocketException)
            {
                throw new ConnectPhaseException(InfraKind.DnsFailure,
                    "could not resolve host", $"{host} — NXDOMAIN");
            }
        }
        if (state != null) state.Dns = sw.Elapsed;

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        var connectStart = sw.Elapsed;
        try
        {
            await socket.ConnectAsync(addresses, port, ct);
        }
        catch (SocketException se)
        {
            socket.Dispose();
            string endpoint = $"{addresses[0]}:{port}";
            if (se.SocketErrorCode == SocketError.ConnectionRefused)
                throw new ConnectPhaseException(InfraKind.ConnectionRefused,
                    "connection refused", $"{endpoint} — ECONNREFUSED");
            throw new ConnectPhaseException(InfraKind.ConnectionRefused,
                "connection failed", $"{endpoint} — {se.SocketErrorCode}");
        }
        if (state != null)
        {
            state.Connect = sw.Elapsed - connectStart;
            state.ConnectDoneTs = Stopwatch.GetTimestamp();
            state.RemoteEndpoint = $"{socket.RemoteEndPoint}";
        }
        return new NetworkStream(socket, ownsSocket: true);
    }

    private async Task<ResponseBody> CaptureBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var head = new MemoryStream();
        var buffer = new byte[64 * 1024];
        long totalBytes = 0;
        string? spillPath = null;
        FileStream? spill = null;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                totalBytes += read;
                if (head.Length < _viewerCapBytes)
                    head.Write(buffer, 0, (int)Math.Min(read, _viewerCapBytes - head.Length));
                if (totalBytes > _viewerCapBytes && spill == null)
                {
                    // Over the cap: spill the whole body to disk — head first,
                    // then keep streaming the rest straight to the file.
                    spillPath = Path.Combine(SpillDir(), $"body-{Guid.NewGuid():N}.bin");
                    spill = File.Create(spillPath);
                    head.Position = 0;
                    await head.CopyToAsync(spill, ct);
                    head.Position = head.Length;
                    // The bytes of this read that the head didn't take.
                    int taken = (int)(head.Length - (totalBytes - read));
                    if (taken < 0) taken = 0;
                    if (taken < read) await spill.WriteAsync(buffer.AsMemory(taken, read - taken), ct);
                    continue;
                }
                if (spill != null)
                    await spill.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        finally
        {
            if (spill != null)
            {
                await spill.DisposeAsync();
                lock (_spillLock) _spills.Add(spillPath!);
            }
        }

        if (totalBytes == 0) return ResponseBody.None;

        var headBytes = head.ToArray();
        bool truncated = totalBytes > _viewerCapBytes;
        return IsTextContent(response.Content.Headers.ContentType?.MediaType, headBytes)
            ? new ResponseBody(BodyKind.Text, System.Text.Encoding.UTF8.GetString(headBytes), [], totalBytes, truncated, spillPath)
            : new ResponseBody(BodyKind.Binary, "", headBytes, totalBytes, truncated, spillPath);
    }

    /// <summary>Known text media types; without a content type, a NUL byte in
    /// the head means binary.</summary>
    internal static bool IsTextContent(string? mediaType, byte[] head)
    {
        if (mediaType != null)
        {
            if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return true;
            if (mediaType.EndsWith("/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)) return true;
            if (mediaType.EndsWith("/xml", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)) return true;
            if (mediaType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
                || mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        return !head.AsSpan(0, Math.Min(head.Length, 8192)).Contains((byte)0);
    }

    private static InfraFailure Classify(SocketException socket, Uri uri, ConnectionState state, TimeSpan elapsed)
    {
        string endpoint = state.RemoteEndpoint ?? $"{uri.Host}:{uri.Port}";
        return socket.SocketErrorCode switch
        {
            SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData =>
                new InfraFailure(InfraKind.DnsFailure, "could not resolve host", $"{uri.Host} — NXDOMAIN", elapsed),
            SocketError.ConnectionRefused =>
                new InfraFailure(InfraKind.ConnectionRefused, "connection refused", $"{endpoint} — ECONNREFUSED", elapsed),
            _ =>
                new InfraFailure(InfraKind.ConnectionRefused, "connection failed", $"{endpoint} — {socket.SocketErrorCode}", elapsed),
        };
    }

    private static ConnectionState? Correlate(HttpRequestMessage? request) =>
        request != null && request.Options.TryGetValue(StateKey, out var state) ? state : CurrentState.Value;

    private static T? Unwrap<T>(Exception e) where T : Exception
    {
        for (Exception? cursor = e; cursor != null; cursor = cursor.InnerException)
            if (cursor is T match)
                return match;
        return null;
    }

    private static TimeSpan Elapsed(long fromTimestamp) =>
        fromTimestamp == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(fromTimestamp);

    private static string Round(TimeSpan t) =>
        t.TotalSeconds >= 1 ? $"{t.TotalSeconds:0.#} s" : $"{t.TotalMilliseconds:0} ms";

    private static string SpillDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "meridian-bodies");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        _client.Dispose();
        lock (_spillLock)
        {
            foreach (var path in _spills)
            {
                try { File.Delete(path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            _spills.Clear();
        }
    }
}
