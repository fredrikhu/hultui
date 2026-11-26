using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using HultUI.Media.Mpv;
using Xunit;

namespace HultUI.Media.Backends.Tests;

/// <summary>
/// The loopback proxy FFmpeg fetches an https HLS playlist and its segments through when it has no
/// TLS (Android), without libmpv: what reaches the server and what comes back, against a local
/// server that records every request. Plain http:// to the server here; the proxy does not care
/// which, and the TLS is HttpClient's.
/// </summary>
public class MpvLoopbackProxyTests
{
    private const string Playlist = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=4000000\nmain.m3u8?api_key=k\n";
    private static readonly byte[] Segment = MakeData(300_007);

    private static byte[] MakeData(int length)
    {
        var data = new byte[length];
        new Random(20261127).NextBytes(data);
        return data;
    }

    private static readonly HttpClient Ffmpeg = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static (OriginServer origin, MpvLoopbackProxy proxy) Start()
    {
        var origin = new OriginServer();
        origin.Files["/videos/x/master.m3u8"] = ("application/x-mpegURL", Encoding.ASCII.GetBytes(Playlist));
        origin.Files["/videos/x/hls1/main/0.ts"] = ("video/mp2t", Segment);
        return (origin, new MpvLoopbackProxy(new HttpClient()));
    }

    [Fact]
    public async Task APlaylistThroughTheProxy_ComesFromTheServer_AsItIs()
    {
        var (origin, proxy) = Start();
        using var _ = origin;
        using var __ = proxy;
        string wrapped = proxy.Wrap(origin.Url("/videos/x/master.m3u8?api_key=k&MaxStreamingBitrate=4000000"));
        Assert.StartsWith($"http://127.0.0.1:{proxy.Port}/", wrapped);

        using var response = await Ffmpeg.GetAsync(wrapped);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-mpegURL", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Playlist, await response.Content.ReadAsStringAsync());
        Assert.Equal([("GET", "/videos/x/master.m3u8?api_key=k&MaxStreamingBitrate=4000000", null)], origin.Requests);
    }

    /// <summary>The segments a Jellyfin playlist names by relative URLs resolve, against the proxied
    /// playlist's address, to proxied addresses on the same server: what FFmpeg's HLS demuxer does.</summary>
    [Fact]
    public async Task ASegmentNamedRelativeToThePlaylist_ComesFromTheSameServer()
    {
        var (origin, proxy) = Start();
        using var _ = origin;
        using var __ = proxy;
        var playlist = new Uri(proxy.Wrap(origin.Url("/videos/x/master.m3u8?api_key=k")));

        var segment = new Uri(playlist, "hls1/main/0.ts?runtimeTicks=0&api_key=k");
        using var response = await Ffmpeg.GetAsync(segment);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Segment, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(Segment.Length, response.Content.Headers.ContentLength);
        Assert.Equal(("GET", "/videos/x/hls1/main/0.ts?runtimeTicks=0&api_key=k", (string?)null), Assert.Single(origin.Requests));
    }

    [Fact]
    public async Task ARange_GoesToTheServer_AndItsPartialAnswerComesBack()
    {
        var (origin, proxy) = Start();
        using var _ = origin;
        using var __ = proxy;
        var request = new HttpRequestMessage(HttpMethod.Get, proxy.Wrap(origin.Url("/videos/x/hls1/main/0.ts")));
        request.Headers.Range = new RangeHeaderValue(100_000, null);

        using var response = await Ffmpeg.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(100_000, response.Content.Headers.ContentRange?.From);
        Assert.Equal(Segment.Length, response.Content.Headers.ContentRange?.Length);
        Assert.Contains("bytes", response.Headers.AcceptRanges);
        Assert.Equal(Segment.AsSpan(100_000).ToArray(), await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("bytes=100000-", Assert.Single(origin.Requests).Range);
    }

    [Fact]
    public async Task AHead_HasTheServersHeaders_AndNoBody()
    {
        var (origin, proxy) = Start();
        using var _ = origin;
        using var __ = proxy;
        using var response = await Ffmpeg.SendAsync(new HttpRequestMessage(HttpMethod.Head, proxy.Wrap(origin.Url("/videos/x/hls1/main/0.ts"))));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Segment.Length, response.Content.Headers.ContentLength);
        Assert.Equal("HEAD", Assert.Single(origin.Requests).Method);
    }

    [Fact]
    public async Task TheServersRefusal_ComesBack()
    {
        var (origin, proxy) = Start();
        using var _ = origin;
        using var __ = proxy;
        using var response = await Ffmpeg.GetAsync(proxy.Wrap(origin.Url("/videos/missing/master.m3u8")));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AServerThatCannotBeReached_IsABadGateway()
    {
        using var proxy = new MpvLoopbackProxy(new HttpClient());
        var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        int port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();

        using var response = await Ffmpeg.GetAsync(proxy.Wrap($"http://127.0.0.1:{port}/videos/x/master.m3u8"));
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    /// <summary>Any app on the device can reach the loopback address. Without the process's secret,
    /// for an origin it was never given, or with another method, nothing reaches a server.</summary>
    [Theory]
    [InlineData("secret", HttpStatusCode.NotFound)]
    [InlineData("origin", HttpStatusCode.NotFound)]
    [InlineData("method", HttpStatusCode.MethodNotAllowed)]
    public async Task ARequestTheProxyDidNotMake_ReachesNoServer(string wrong, HttpStatusCode expected)
    {
        var (origin, proxy) = Start();
        using var _ = origin;
        using var __ = proxy;
        using var other = new OriginServer();
        other.Files["/videos/x/master.m3u8"] = ("application/x-mpegURL", Encoding.ASCII.GetBytes(Playlist));
        string wrapped = proxy.Wrap(origin.Url("/videos/x/master.m3u8"));
        var parts = new Uri(wrapped).AbsolutePath.Split('/');   // "", secret, scheme, authority, ...
        string secret = parts[1], authority = parts[3];

        var request = wrong switch
        {
            "secret" => new HttpRequestMessage(HttpMethod.Get, wrapped.Replace(secret, new string('0', secret.Length))),
            "origin" => new HttpRequestMessage(HttpMethod.Get,
                wrapped.Replace(authority, Uri.EscapeDataString(new Uri(other.Url("/")).Authority))),
            _ => new HttpRequestMessage(HttpMethod.Post, wrapped),
        };
        using var response = await Ffmpeg.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(origin.Requests);
        Assert.Empty(other.Requests);
    }

    /// <summary>FFmpeg closing its connection midway (a seek, a stop) ends the request to the server,
    /// rather than leaving it to run to the end of a segment nobody reads.</summary>
    [Fact]
    public async Task FfmpegLettingGoMidway_EndsTheRequestToTheServer()
    {
        var (origin, proxy) = Start();
        using var _ = origin;
        using var __ = proxy;
        origin.Files["/videos/x/hls1/main/big.ts"] = ("video/mp2t", MakeData(64 * 1024 * 1024));

        using (var response = await Ffmpeg.GetAsync(proxy.Wrap(origin.Url("/videos/x/hls1/main/big.ts")),
                   HttpCompletionOption.ResponseHeadersRead))
        {
            await using var body = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[64 * 1024];
            await body.ReadExactlyAsync(buffer);
        }
        Assert.True(origin.Aborted.Wait(TimeSpan.FromSeconds(10)), "the server went on sending");
    }

    [Theory]
    [InlineData("https://jellyfin.example/videos/x/master.m3u8?api_key=k", true)]
    [InlineData("https://jellyfin.example/videos/x/stream?static=true", false)]
    [InlineData("http://jellyfin.example/videos/x/master.m3u8", false)]
    [InlineData("file:///sdcard/clip.m3u8", false)]
    public void AnHttpsHlsPlaylist_IsCarried_AndNothingElse(string url, bool carried) =>
        Assert.Equal(carried, MpvLoopbackProxy.Carries(url));

    [Fact]
    public void AnHttpsSourceThatIsNotAPlaylist_IsTheStreamsToCarry()
    {
        const string file = "https://jellyfin.example/videos/x/stream?static=true";
        const string playlist = "https://jellyfin.example/videos/x/master.m3u8";
        Assert.True(MpvHttpStream.Carries(file) && !MpvLoopbackProxy.Carries(file));
        Assert.True(!MpvHttpStream.Carries(playlist) && MpvLoopbackProxy.Carries(playlist));
    }

    /// <summary>One request per connection, answered from <see cref="Files"/> on a raw socket, and
    /// every request line recorded.</summary>
    private sealed class OriginServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<(string Method, string Target, string? Range)> _requests = [];

        public Dictionary<string, (string Type, byte[] Body)> Files { get; } = [];

        /// <summary>Set when the far end closed a connection before its body was all sent.</summary>
        public ManualResetEventSlim Aborted { get; } = new();

        public OriginServer()
        {
            _listener.Start();
            _ = AcceptAsync();
        }

        public string Url(string pathAndQuery) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{pathAndQuery}";

        public List<(string Method, string Target, string? Range)> Requests { get { lock (_requests) return [.. _requests]; } }

        private async Task AcceptAsync()
        {
            while (true)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch (Exception e) when (e is SocketException or ObjectDisposedException) { return; }
                _ = Task.Run(() => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var net = client.GetStream();
                    var reader = new StreamReader(net, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var line = reader.ReadLine()!.Split(' ');
                    string method = line[0], target = line[1];
                    string? range = null;
                    for (string? header = reader.ReadLine(); !string.IsNullOrEmpty(header); header = reader.ReadLine())
                        if (header.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                            range = header["Range:".Length..].Trim();
                    lock (_requests) _requests.Add((method, target, range));

                    string path = target.Split('?')[0];
                    if (!Files.TryGetValue(path, out var file))
                    {
                        Write(net, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                        return;
                    }
                    long from = 0;
                    string head;
                    if (range is not null && range.StartsWith("bytes=", StringComparison.Ordinal))
                    {
                        from = long.Parse(range["bytes=".Length..].TrimEnd('-'));
                        head = $"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes {from}-{file.Body.Length - 1}/{file.Body.Length}\r\n";
                    }
                    else head = "HTTP/1.1 200 OK\r\n";
                    Write(net, head + $"Content-Type: {file.Type}\r\nContent-Length: {file.Body.Length - from}\r\n" +
                               "Accept-Ranges: bytes\r\nConnection: close\r\n\r\n");
                    if (method == "HEAD") return;
                    for (int at = (int)from; at < file.Body.Length; at += 16 * 1024)
                        net.Write(file.Body, at, Math.Min(16 * 1024, file.Body.Length - at));
                    net.Flush();
                }
                catch (IOException) { Aborted.Set(); }
            }
        }

        private static void Write(Stream net, string text)
        {
            net.Write(Encoding.ASCII.GetBytes(text));
            net.Flush();
        }

        public void Dispose() => _listener.Stop();
    }
}
