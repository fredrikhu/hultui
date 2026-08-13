using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Meridian.Tests.Fixtures;

/// <summary>
/// A loopback TLS endpoint presenting a freshly generated self-signed
/// certificate. A default client rejects the handshake — the engine must fail
/// and still name what it rejected. A client that explicitly trusts the
/// thumbprint completes the handshake and gets a real 200 back, which is what
/// proves the trust-for-this-workspace round trip.
/// </summary>
public sealed class TestTlsServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly X509Certificate2 _cert;

    public int Port { get; }
    public const string CertCn = "meridian-selftest";
    public string Thumbprint => _cert.Thumbprint;

    public TestTlsServer()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={CertCn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Windows SChannel needs the key re-imported as exportable/persisted to
        // use it for a handshake.
        _cert = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null);

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
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await using var ssl = new SslStream(client.GetStream());
                        // An untrusting client makes this throw — expected. A
                        // trusting one completes, sends a request, and gets 200.
                        await ssl.AuthenticateAsServerAsync(_cert);
                        var buffer = new byte[4096];
                        int n = await ssl.ReadAsync(buffer);
                        if (n > 0)
                        {
                            var body = "trusted"u8.ToArray();
                            var head = System.Text.Encoding.ASCII.GetBytes(
                                $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                            await ssl.WriteAsync(head);
                            await ssl.WriteAsync(body);
                            await ssl.FlushAsync();
                        }
                    }
                    catch (Exception e) when (e is IOException or System.Security.Authentication.AuthenticationException or ObjectDisposedException) { }
                    finally { client.Dispose(); }
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cert.Dispose();
        _cts.Dispose();
    }
}
