using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace HultUI.Media.Mpv;

/// <summary>
/// An HLS playlist over https for a libmpv whose FFmpeg has no TLS (Android): FFmpeg fetches the
/// playlist and every segment from this process in plain http on the loopback address, and each
/// request is made to the server over https through <see cref="HttpClient"/>, which on Android
/// uses the platform's TLS and its trusted certificates.
///
/// <para><see cref="MpvHttpStream"/> carries a single resource, which mpv reads through its
/// stream callbacks. A playlist is different: FFmpeg's HLS demuxer reads the playlist and then
/// opens each segment itself, through FFmpeg's own I/O, which a stream callback never sees. So
/// FFmpeg is given an address it can open without TLS. A Jellyfin HLS transcode (Aperture with a
/// streaming limit) names its segments by relative URLs, which resolve against the proxied
/// playlist's address to more proxied addresses; seeking, the segments and the duration then work
/// as on a desktop, where FFmpeg has TLS.</para>
///
/// <para>The address is <c>http://127.0.0.1:port/secret/scheme/authority/path?query</c>. Any
/// app on the device can connect to the loopback address, so a request is forwarded only with
/// this process's random secret, and only to an origin (scheme and authority) that
/// <see cref="Wrap"/> was given: it is not a relay to anywhere. Only GET and HEAD, and only the
/// Range header, go through; each response ends its connection.</para>
/// </summary>
internal sealed class MpvLoopbackProxy : IDisposable
{
    private static readonly Lazy<MpvLoopbackProxy> s_shared = new(() => new MpvLoopbackProxy(MpvHttpStream.SharedClient));

    /// <summary>The process's proxy, listening from its first use on.</summary>
    internal static MpvLoopbackProxy Shared => s_shared.Value;

    /// <summary>Each request said on stderr, with mpv's own log (HULTUI_MPV_LOG).</summary>
    private static readonly bool Log = Environment.GetEnvironmentVariable("HULTUI_MPV_LOG") is { Length: > 0 };

    /// <summary>The most a request's line and headers may take.</summary>
    private const int MaxHead = 16 * 1024;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly HttpClient _client;
    private readonly string _secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    private readonly ConcurrentDictionary<string, byte> _origins = new(StringComparer.OrdinalIgnoreCase);

    internal MpvLoopbackProxy(HttpClient client)
    {
        _client = client;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptAsync();
    }

    internal int Port { get; }

    /// <summary>
    /// Whether <paramref name="url"/> goes through the proxy: an https:// HLS playlist. Any other
    /// https:// source is one resource, which <see cref="MpvHttpStream"/> carries.
    /// </summary>
    internal static bool Carries(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);

    /// <summary>The loopback address FFmpeg is given for <paramref name="url"/>; its origin may be
    /// forwarded to from now on.</summary>
    internal string Wrap(string url)
    {
        var uri = new Uri(url, UriKind.Absolute);
        _origins.TryAdd(Origin(uri.Scheme, uri.Authority), 0);
        return $"http://127.0.0.1:{Port}/{_secret}/{uri.Scheme}/{Uri.EscapeDataString(uri.Authority)}{uri.PathAndQuery}";
    }

    /// <summary>The server address a request for <paramref name="target"/> (its path and query, as
    /// the request line has them) is forwarded to, or null: not this process's secret, not an
    /// address <see cref="Wrap"/> made, or an origin it was never given.</summary>
    internal Uri? Unwrap(string target)
    {
        // /secret/scheme/authority/rest
        var parts = target.Split('/', 5);
        if (parts.Length < 4 || parts[0].Length != 0) return null;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(parts[1]), Encoding.ASCII.GetBytes(_secret)))
            return null;
        string scheme = parts[2], authority = Uri.UnescapeDataString(parts[3]);
        if (!_origins.ContainsKey(Origin(scheme, authority))) return null;
        string rest = parts.Length == 5 ? parts[4] : "";
        return Uri.TryCreate($"{scheme}://{authority}/{rest}", UriKind.Absolute, out var url) ? url : null;
    }

    private static string Origin(string scheme, string authority) => scheme + "://" + authority;

    public void Dispose() => _listener.Stop();

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException) { return; }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient tcp)
    {
        using var _ = tcp;
        using var cancel = new CancellationTokenSource();
        try
        {
            tcp.NoDelay = true;
            var net = tcp.GetStream();
            if (await ReadHeadAsync(net, cancel.Token).ConfigureAwait(false) is not { } head)
                return;
            var (method, target, range) = head;

            if (method is not ("GET" or "HEAD"))
            {
                await WriteHeadAsync(net, "405 Method Not Allowed", null, cancel.Token).ConfigureAwait(false);
                return;
            }
            if (Unwrap(target) is not { } url)
            {
                await WriteHeadAsync(net, "404 Not Found", null, cancel.Token).ConfigureAwait(false);
                return;
            }

            using var request = new HttpRequestMessage(method == "HEAD" ? HttpMethod.Head : HttpMethod.Get, url);
            if (range is not null) request.Headers.TryAddWithoutValidation("Range", range);
            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
            {
                if (Log) Console.Error.WriteLine($"[mpv] proxy: {url.AbsolutePath}: {e.GetType().Name}: {e.Message}");
                await WriteHeadAsync(net, "502 Bad Gateway", null, cancel.Token).ConfigureAwait(false);
                return;
            }
            using (response)
            {
                if (Log)
                    Console.Error.WriteLine($"[mpv] proxy: {method} {url.AbsolutePath} range {range ?? "none"}: {(int)response.StatusCode}");
                await WriteHeadAsync(net, $"{(int)response.StatusCode} {response.ReasonPhrase}", response,
                    cancel.Token).ConfigureAwait(false);
                if (method == "HEAD") return;
                // FFmpeg closing the connection (a seek, a stop) ends the copy with an IOException,
                // and the request to the server with it.
                await using var body = await response.Content.ReadAsStreamAsync(cancel.Token).ConfigureAwait(false);
                await body.CopyToAsync(net, 64 * 1024, cancel.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            cancel.Cancel();
        }
    }

    /// <summary>The request's method, target and Range header; null if it is not a request line
    /// and headers within <see cref="MaxHead"/>.</summary>
    private static async Task<(string Method, string Target, string? Range)?> ReadHeadAsync(Stream net, CancellationToken ct)
    {
        var buffer = new byte[MaxHead];
        int length = 0, end = -1;
        while (end < 0)
        {
            if (length == buffer.Length) return null;
            int got = await net.ReadAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
            if (got == 0) return null;
            int from = Math.Max(0, length - 3);
            length += got;
            end = buffer.AsSpan(from, length - from).IndexOf("\r\n\r\n"u8) is var i and >= 0 ? from + i : -1;
        }
        var lines = Encoding.ASCII.GetString(buffer, 0, end).Split("\r\n");
        var request = lines[0].Split(' ');
        if (request.Length != 3 || !request[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return null;
        string? range = null;
        foreach (var line in lines.AsSpan(1))
            if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                range = line["Range:".Length..].Trim();
        return (request[0], request[1], range);
    }

    /// <summary>The status line and the headers FFmpeg reads: the type, the length and the range of
    /// what follows, whether the server takes ranges, and that the connection ends with it. With
    /// no <paramref name="response"/>, an empty body.</summary>
    private static Task WriteHeadAsync(Stream net, string status, HttpResponseMessage? response, CancellationToken ct)
    {
        var head = new StringBuilder($"HTTP/1.1 {status}\r\n");
        if (response is null) head.Append("Content-Length: 0\r\n");
        else
        {
            var content = response.Content.Headers;
            if (content.ContentType is { } type) head.Append($"Content-Type: {type}\r\n");
            if (content.ContentLength is { } length) head.Append($"Content-Length: {length}\r\n");
            if (content.ContentRange is { } range) head.Append($"Content-Range: {range}\r\n");
            if (response.Headers.AcceptRanges.Count > 0)
                head.Append($"Accept-Ranges: {string.Join(", ", response.Headers.AcceptRanges)}\r\n");
        }
        head.Append("Connection: close\r\n\r\n");
        return net.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), ct).AsTask();
    }
}
