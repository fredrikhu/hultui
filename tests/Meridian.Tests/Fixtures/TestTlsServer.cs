using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Meridian.Tests.Fixtures;

/// <summary>
/// A loopback TLS endpoint presenting a freshly generated self-signed
/// certificate. No client should trust it — that's the point: the engine must
/// fail the handshake and still name the certificate it rejected.
/// </summary>
public sealed class TestTlsServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly X509Certificate2 _cert;

    public int Port { get; }
    public const string CertCn = "meridian-selftest";

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
                        // The client rejects the cert, so this throws — expected.
                        await ssl.AuthenticateAsServerAsync(_cert);
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
