using Meridian.Core;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// The engine against a real (loopback) server — requests, bodies, timings,
/// and each of the four infra failures, classified. One server per class,
/// fresh engine per test (connection reuse is per-engine state).
/// </summary>
public class HttpEngineTests : IClassFixture<TestHttpServer>
{
    private readonly TestHttpServer _server;

    public HttpEngineTests(TestHttpServer server) => _server = server;

    private static RequestSpec Get(string url) => new("GET", url, []);

    // ── Responses ────────────────────────────────────────────────────────

    [Fact]
    public async Task AJsonResponseArrivesWithBodyAndTimings()
    {
        using var engine = new HttpEngine();
        var result = await engine.SendAsync(Get($"{_server.Origin}/ok"));

        var response = result.Response!;
        Assert.Null(result.Failure);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(BodyKind.Text, response.Body.Kind);
        Assert.Contains("\"ok\"", response.Body.Text);
        Assert.Contains("application/json", response.ContentType);

        var t = response.Timings;
        Assert.False(t.ConnectionReused);
        Assert.True(t.Total > TimeSpan.Zero);
        Assert.True(t.TimeToFirstByte > TimeSpan.Zero);
        Assert.True(t.TimeToFirstByte <= t.Total);
        Assert.Equal("HTTP/1.1", t.HttpVersion);
        Assert.Null(t.TlsProtocol);   // plain http
    }

    [Fact]
    public async Task AnErrorStatusIsAResponseNotAFailure()
    {
        // The design's hardest rule: 4xx/5xx render as full responses.
        using var engine = new HttpEngine();
        var result = await engine.SendAsync(Get($"{_server.Origin}/teapot"));

        Assert.Null(result.Failure);
        Assert.Equal(418, result.Response!.StatusCode);
        Assert.Equal("short and stout", result.Response.Body.Text);
    }

    [Fact]
    public async Task TheSecondRequestReusesTheConnection()
    {
        using var engine = new HttpEngine();
        var first = await engine.SendAsync(Get($"{_server.Origin}/ok"));
        var second = await engine.SendAsync(Get($"{_server.Origin}/ok"));

        Assert.False(first.Response!.Timings.ConnectionReused);
        Assert.True(second.Response!.Timings.ConnectionReused);
        Assert.Equal(TimeSpan.Zero, second.Response.Timings.Dns);
    }

    [Fact]
    public async Task PostBodyAndMethodReachTheServer()
    {
        using var engine = new HttpEngine();
        var result = await engine.SendAsync(new RequestSpec(
            "POST", $"{_server.Origin}/echo", [], "hello body", "text/plain"));

        Assert.Equal("POST|hello body", result.Response!.Body.Text);
    }

    [Fact]
    public async Task RedirectsAreFollowedToTheFinalUrl()
    {
        using var engine = new HttpEngine();
        var result = await engine.SendAsync(Get($"{_server.Origin}/redirect"));

        Assert.Equal(200, result.Response!.StatusCode);
        Assert.EndsWith("/ok", result.Response.FinalUrl);
    }

    [Fact]
    public async Task CookiesPersistAcrossRequestsInTheSession()
    {
        using var engine = new HttpEngine();
        await engine.SendAsync(Get($"{_server.Origin}/setcookie"));
        var result = await engine.SendAsync(Get($"{_server.Origin}/cookie-echo"));

        Assert.Contains("sid=s3cr3t", result.Response!.Body.Text);
    }

    // ── Body capture ─────────────────────────────────────────────────────

    [Fact]
    public async Task ABodyOverTheCapIsTruncatedAndSpilledInFull()
    {
        using var engine = new HttpEngine(viewerCapBytes: 1000);
        var result = await engine.SendAsync(Get($"{_server.Origin}/big"));

        var body = result.Response!.Body;
        Assert.True(body.Truncated);
        Assert.Equal(1000, body.Text.Length);
        Assert.Equal(5000, body.TotalBytes);
        Assert.NotNull(body.SpillPath);
        Assert.Equal(5000, new FileInfo(body.SpillPath!).Length);
        Assert.Equal(new string('a', 5000), File.ReadAllText(body.SpillPath!));
    }

    [Fact]
    public async Task TheSpillFileIsDeletedWhenTheEngineGoesAway()
    {
        string spillPath;
        using (var engine = new HttpEngine(viewerCapBytes: 1000))
        {
            var result = await engine.SendAsync(Get($"{_server.Origin}/big"));
            spillPath = result.Response!.Body.SpillPath!;
            Assert.True(File.Exists(spillPath));
        }
        Assert.False(File.Exists(spillPath));
    }

    [Fact]
    public async Task ABodyExactlyAtTheCapIsNotTruncated()
    {
        using var engine = new HttpEngine(viewerCapBytes: 5000);
        var result = await engine.SendAsync(Get($"{_server.Origin}/big"));

        Assert.False(result.Response!.Body.Truncated);
        Assert.Null(result.Response.Body.SpillPath);
    }

    [Fact]
    public async Task APngBodyIsBinaryWithAHexPreview()
    {
        using var engine = new HttpEngine();
        var result = await engine.SendAsync(Get($"{_server.Origin}/binary"));

        var body = result.Response!.Body;
        Assert.Equal(BodyKind.Binary, body.Kind);
        Assert.Equal("", body.Text);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], body.Preview[..4]);
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/problem+json", true)]
    [InlineData("text/html", true)]
    [InlineData("application/xml", true)]
    [InlineData("image/png", false)]
    [InlineData("application/octet-stream", false)]
    public void ContentTypesClassify(string mediaType, bool text)
    {
        Assert.Equal(text, HttpEngine.IsTextContent(mediaType, []));
    }

    [Fact]
    public void WithoutAContentTypeANulByteMeansBinary()
    {
        Assert.True(HttpEngine.IsTextContent(null, "plain text"u8.ToArray()));
        Assert.False(HttpEngine.IsTextContent(null, [0x89, 0x00, 0x47]));
    }

    // ── The four infra failures ──────────────────────────────────────────

    [Fact]
    public async Task AnUnknownHostIsADnsFailureNamingTheHost()
    {
        using var engine = new HttpEngine();
        var result = await engine.SendAsync(Get("http://no-such-host.invalid/x"));

        var failure = result.Failure!;
        Assert.Null(result.Response);
        Assert.Equal(InfraKind.DnsFailure, failure.Kind);
        Assert.Contains("no-such-host.invalid", failure.Detail);
    }

    [Fact]
    public async Task ADeadPortIsConnectionRefusedNamingTheEndpoint()
    {
        int deadPort = FreePort();
        using var engine = new HttpEngine();
        var result = await engine.SendAsync(Get($"http://127.0.0.1:{deadPort}/x"));

        var failure = result.Failure!;
        Assert.Equal(InfraKind.ConnectionRefused, failure.Kind);
        Assert.Contains($"127.0.0.1:{deadPort}", failure.Detail);
    }

    [Fact]
    public async Task AStalledServerIsATimeoutThatSaysItConnected()
    {
        using var engine = new HttpEngine();
        var result = await engine.SendAsync(
            Get($"{_server.Origin}/stall") with { Timeout = TimeSpan.FromMilliseconds(400) });

        var failure = result.Failure!;
        Assert.Equal(InfraKind.Timeout, failure.Kind);
        // "connected · no response bytes", never "never connected" — the socket opened.
        Assert.Contains("connected", failure.Detail);
        Assert.DoesNotContain("never", failure.Detail);
        Assert.True(failure.Elapsed >= TimeSpan.FromMilliseconds(400));
    }

    [Fact]
    public async Task AnUntrustedCertificateIsATlsFailureNamingTheCert()
    {
        using var tls = new TestTlsServer();
        using var engine = new HttpEngine();
        var result = await engine.SendAsync(Get($"https://127.0.0.1:{tls.Port}/x"));

        var failure = result.Failure!;
        Assert.Equal(InfraKind.TlsFailure, failure.Kind);
        Assert.Contains(TestTlsServer.CertCn, failure.Detail);
    }

    [Fact]
    public async Task CallerCancellationIsNotAFailureKind()
    {
        using var engine = new HttpEngine();
        using var cts = new CancellationTokenSource(120);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.SendAsync(Get($"{_server.Origin}/stall"), cts.Token));
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
