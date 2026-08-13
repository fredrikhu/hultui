using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Meridian.Tests.Fixtures;

/// <summary>
/// A tiny hand-rolled HTTP/1.1 server on a loopback TCP socket. Not HttpListener:
/// this needs no http.sys reservation, runs identically on Linux CI, and — the
/// real reason — can misbehave on command (stall forever, hold keep-alive
/// connections) in ways a well-behaved server API won't allow.
///
/// Routes: /ok (JSON, keep-alive) · /echo (mirrors method+body) · /big (5000
/// bytes) · /binary (PNG-ish bytes) · /redirect (302 → /ok) · /setcookie ·
/// /cookie-echo · /stall (reads the request, never answers).
/// </summary>
public sealed class TestHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<TcpClient> _held = [];

    public int Port { get; }
    public string Origin => $"http://127.0.0.1:{Port}";

    public TestHttpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => ServeAsync(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            // Keep-alive: serve requests on this connection until close/stall.
            while (true)
            {
                var (method, path, contentLength, authHeader) = await ReadRequestAsync(stream);
                if (method == null) return;

                byte[] body = new byte[contentLength];
                int got = 0;
                while (got < contentLength)
                {
                    int n = await stream.ReadAsync(body.AsMemory(got));
                    if (n == 0) return;
                    got += n;
                }

                if (path == "/stall")
                {
                    lock (_held) _held.Add(client);
                    // Hold the connection open, never respond; dispose releases it.
                    await Task.Delay(System.Threading.Timeout.Infinite, _cts.Token);
                    return;
                }

                await (path switch
                {
                    "/ok" => Respond(stream, 200, "OK", "application/json", """{"ok":true}"""u8.ToArray()),
                    "/two" => Respond(stream, 200, "OK", "application/json", """{"alpha":"xray","beta":"yankee"}"""u8.ToArray()),
                    "/echo" => Respond(stream, 200, "OK", "text/plain",
                        Encoding.UTF8.GetBytes($"{method}|{Encoding.UTF8.GetString(body)}")),
                    "/big" => Respond(stream, 200, "OK", "text/plain", Encoding.UTF8.GetBytes(new string('a', 5000))),
                    "/binary" => Respond(stream, 200, "OK", "image/png",
                        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D]),
                    "/redirect" => Respond(stream, 302, "Found", "text/plain", [], "Location: /ok\r\n"),
                    "/setcookie" => Respond(stream, 200, "OK", "text/plain", "set"u8.ToArray(),
                        "Set-Cookie: sid=s3cr3t; Path=/\r\n"),
                    "/teapot" => Respond(stream, 418, "I'm a teapot", "text/plain", "short and stout"u8.ToArray()),
                    // An OAuth2 token endpoint: echoes back a fixed grant so the
                    // client-credentials flow can be exercised end to end.
                    "/token" => Respond(stream, 200, "OK", "application/json",
                        Encoding.UTF8.GetBytes($$"""{"access_token":"tok-abc123","token_type":"Bearer","expires_in":3600,"echo":"{{Encoding.UTF8.GetString(body).Replace("\"", "")}}"}""")),
                    // Mirrors the Authorization header, so a test can prove what
                    // auth actually put on the wire.
                    "/whoami" => Respond(stream, 200, "OK", "text/plain",
                        Encoding.UTF8.GetBytes(authHeader ?? "none")),
                    _ when path == "/cookie-echo" => Respond(stream, 200, "OK", "text/plain",
                        Encoding.UTF8.GetBytes(_lastCookieHeader ?? "none")),
                    _ => Respond(stream, 404, "Not Found", "text/plain", []),
                });
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or SocketException) { }
        finally
        {
            lock (_held) { if (!_held.Contains(client)) client.Dispose(); }
        }
    }

    private string? _lastCookieHeader;
    /// <summary>The full request line of the last request, so a test can see
    /// query parameters auth appended.</summary>
    public string? LastRequestLine { get; private set; }

    /// <summary>The Authorization header travels with its own request rather
    /// than through a field: connections are served concurrently, and a shared
    /// "last header seen" reports the wrong request's credentials.</summary>
    private async Task<(string? Method, string Path, int ContentLength, string? Auth)> ReadRequestAsync(NetworkStream stream)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (!sb.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            int n = await stream.ReadAsync(one.AsMemory(), _cts.Token);
            if (n == 0) return (null, "", 0, null);
            sb.Append((char)one[0]);
        }
        var lines = sb.ToString().Split("\r\n");
        LastRequestLine = lines[0];
        var parts = lines[0].Split(' ');
        int contentLength = 0;
        string? auth = null;
        foreach (var line in lines[1..])
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(line.Split(':')[1].Trim());
            if (line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
                _lastCookieHeader = line["Cookie:".Length..].Trim();
            if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                auth = line["Authorization:".Length..].Trim();
        }
        return (parts[0], parts.Length > 1 ? parts[1] : "", contentLength, auth);
    }

    private static async Task Respond(NetworkStream stream, int status, string reason,
        string contentType, byte[] body, string extraHeaders = "")
    {
        var head = $"HTTP/1.1 {status} {reason}\r\n" +
                   $"Content-Type: {contentType}\r\n" +
                   $"Content-Length: {body.Length}\r\n" +
                   extraHeaders +
                   "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        lock (_held)
        {
            foreach (var held in _held) held.Dispose();
            _held.Clear();
        }
        _cts.Dispose();
    }
}
