using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Events;
using Microsoft.DevTunnels.Ssh.IO;
using Microsoft.DevTunnels.Ssh.Messages;
using DevTunnelsECDsa = Microsoft.DevTunnels.Ssh.Algorithms.ECDsa;

namespace HultUI.Terminal.Ssh.Tests;

/// <summary>
/// An SSH server inside the test process, on a loopback port: Microsoft's Dev Tunnels SSH library
/// speaking the protocol, so SSH.NET is tested against a real peer with nothing installed or
/// configured on the machine. Loopback only, so no firewall asks.
/// </summary>
/// <remarks>
/// Its shell reads lines (ended by CR, as a terminal sends Enter) and answers:
/// <list type="bullet">
/// <item><c>exit N</c> ends with status N; <c>kill SIG</c> ends by that signal.</item>
/// <item><c>size</c> prints the grid it was last told, <c>COLSxROWS</c>.</item>
/// <item><c>flood N</c> writes N bytes as fast as the connection takes them, then <c>done</c>.</item>
/// <item><c>drop</c> resets the TCP connection, as a network failure would.</item>
/// <item>Anything else comes back as <c>[line]</c>.</item>
/// </list>
/// </remarks>
internal sealed class TestSshServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly DevTunnelsECDsa.KeyPair _hostKeyPair;
    private readonly StringBuilder _received = new();
    private int _passwordAttempts;
    private long _floodSent;

    public TestSshServer(ECDsa? hostKey = null)
    {
        HostKey = hostKey ?? ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _hostKeyPair = new DevTunnelsECDsa.KeyPair(HostKey);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = AcceptLoop();
    }

    public ECDsa HostKey { get; }
    public byte[] HostKeyBlob => TestKeys.Blob(HostKey);
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public string User { get; set; } = "tester";
    public string? Password { get; set; } = "secret";
    public byte[]? AuthorizedKey { get; set; }
    public string? InteractiveCode { get; set; }

    /// <summary>The sign-in methods the server offers; null for the library's own list.</summary>
    public string[]? Methods { get; set; }

    public int PasswordAttempts => Volatile.Read(ref _passwordAttempts);
    public PtyRequestMessage? Pty { get; private set; }
    public (uint Columns, uint Rows) Size { get; private set; }
    public ConcurrentQueue<(uint Columns, uint Rows)> WindowChanges { get; } = new();
    public long FloodSent => Interlocked.Read(ref _floodSent);

    /// <summary>What went wrong on the server's side, for a failing test to show.</summary>
    public ConcurrentQueue<string> Errors { get; } = new();
    public string Received { get { lock (_received) return _received.ToString(); } }

    /// <summary>Set once a shell's channel has closed on the server's side, for whatever reason.</summary>
    public TaskCompletionSource ShellClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) { return; }
            _ = Serve(tcp);
        }
    }

    private async Task Serve(TcpClient tcp)
    {
        var config = new SshSessionConfiguration();
        if (Methods is { } methods)
        {
            config.AuthenticationMethods.Clear();
            foreach (var method in methods) config.AuthenticationMethods.Add(method);
        }
        var session = new SshServerSession(config, new TraceSource("test-ssh-server"))
        {
            Credentials = new SshServerCredentials([_hostKeyPair]),
        };
        session.Authenticating += OnAuthenticating;
        session.ChannelOpening += (_, e) =>
        {
            if (e.IsRemoteRequest) Watch(e.Channel, tcp);
        };
        session.Closed += (_, e) =>
        {
            if (e.Exception is { } ex) Errors.Enqueue($"session closed: {e.Reason} {e.Message} {ex}");
        };
        try { await session.ConnectAsync(tcp.GetStream(), _stop.Token); }
        catch (Exception ex) { Errors.Enqueue("connect: " + ex); tcp.Dispose(); }
    }

    private void OnAuthenticating(object? sender, SshAuthenticatingEventArgs e)
    {
        bool ok = false;
        switch (e.AuthenticationType)
        {
            case SshAuthenticationType.ClientPassword:
                Interlocked.Increment(ref _passwordAttempts);
                ok = e.Username == User && Password is not null && e.Password == Password;
                break;
            case SshAuthenticationType.ClientPublicKeyQuery:
            case SshAuthenticationType.ClientPublicKey:
                // The key's own type names it in the blob: ssh-rsa, whichever rsa-sha2 signs.
                try
                {
                    ok = e.Username == User && AuthorizedKey is not null && e.PublicKey is not null
                        && e.PublicKey.GetPublicKeyBytes(KnownHostsFile.KeyType(AuthorizedKey)).ToArray().AsSpan().SequenceEqual(AuthorizedKey);
                }
                catch (Exception ex) { Errors.Enqueue($"{e.AuthenticationType} {e.PublicKey?.KeyAlgorithmName}: {ex}"); }
                break;
            case SshAuthenticationType.ClientInteractive when InteractiveCode is not null:
                if (e.InfoResponse is null)
                {
                    e.InfoRequest = new AuthenticationInfoRequestMessage("test");
                    e.InfoRequest.Instruction = "Enter the code from your device.";
                    e.InfoRequest.AddPrompt("Code: ", echo: false);
                }
                else ok = e.InfoResponse.Responses.Count == 1 && e.InfoResponse.Responses[0] == InteractiveCode;
                break;
        }
        e.AuthenticationTask = Task.FromResult<ClaimsPrincipal?>(ok ? new ClaimsPrincipal(new ClaimsIdentity("test")) : null)!;
    }

    private void Watch(SshChannel channel, TcpClient tcp)
    {
        channel.Closed += (_, _) => ShellClosed.TrySetResult();
        channel.Request += (_, e) =>
        {
            switch (e.RequestType)
            {
                case ChannelRequestTypes.Terminal:
                    Pty = e.Request.ConvertTo<PtyRequestMessage>();
                    Size = (Pty.Columns, Pty.Rows);
                    e.IsAuthorized = true;
                    break;
                case ChannelRequestTypes.Shell:
                    e.IsAuthorized = true;
                    _ = Task.Run(() => RunShell(channel, tcp));
                    break;
                case "window-change":
                    var change = e.Request.ConvertTo<WindowChangeMessage>();
                    Size = (change.Columns, change.Rows);
                    WindowChanges.Enqueue(Size);
                    e.IsAuthorized = true;
                    break;
            }
        };
    }

    private async Task RunShell(SshChannel channel, TcpClient tcp)
    {
        try
        {
            using var stream = new SshStream(channel);
            await Write(stream, "ready\r\n");
            var line = new StringBuilder();
            var buffer = new byte[4096];
            while (true)
            {
                int n = await stream.ReadAsync(buffer, 0, buffer.Length, _stop.Token);
                if (n == 0) return;
                var text = Encoding.UTF8.GetString(buffer, 0, n);
                lock (_received) _received.Append(text);
                foreach (var ch in text)
                {
                    if (ch != '\r') { line.Append(ch); continue; }
                    var command = line.ToString();
                    line.Clear();
                    if (!await Run(command, stream, channel, tcp)) return;
                }
            }
        }
        catch (Exception) { }
    }

    /// <summary>Runs one line; false when the shell has ended.</summary>
    private async Task<bool> Run(string command, SshStream stream, SshChannel channel, TcpClient tcp)
    {
        var (verb, argument) = command.IndexOf(' ') is int space and >= 0
            ? (command[..space], command[(space + 1)..])
            : (command, "");
        switch (verb)
        {
            case "exit":
                await channel.CloseAsync(uint.Parse(argument), _stop.Token);
                return false;
            case "kill":
                await channel.CloseAsync(argument, "killed", _stop.Token);
                return false;
            case "size":
                await Write(stream, $"{Size.Columns}x{Size.Rows}\r\n");
                return true;
            case "flood":
                var chunk = Encoding.ASCII.GetBytes(new string('x', 32 * 1024));
                for (long left = long.Parse(argument); left > 0; left -= chunk.Length)
                {
                    int count = (int)Math.Min(left, chunk.Length);
                    await stream.WriteAsync(chunk, 0, count, _stop.Token);
                    Interlocked.Add(ref _floodSent, count);
                }
                await Write(stream, "\r\ndone\r\n");
                return true;
            case "drop":
                tcp.Client.LingerState = new LingerOption(true, 0); // a reset, not a goodbye
                tcp.Client.Close();
                return false;
            default:
                await Write(stream, $"[{command}]\r\n");
                return true;
        }
    }

    private Task Write(SshStream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return stream.WriteAsync(bytes, 0, bytes.Length, _stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    /// <summary>RFC 4254's pty-req, read here rather than by the library's own message, whose
    /// terminal modes came back empty.</summary>
    public sealed class PtyRequestMessage : ChannelRequestMessage
    {
        public string Term { get; private set; } = "";
        public uint Columns { get; private set; }
        public uint Rows { get; private set; }

        /// <summary>The encoded modes: opcode and value pairs, opcode 0 ends them.</summary>
        public Dictionary<byte, uint> Modes { get; } = [];

        protected override void OnRead(ref SshDataReader reader)
        {
            base.OnRead(ref reader);
            Term = reader.ReadString(Encoding.ASCII);
            Columns = reader.ReadUInt32();
            Rows = reader.ReadUInt32();
            reader.ReadUInt32(); // pixels
            reader.ReadUInt32();
            var modes = reader.ReadBinary().ToArray();
            for (int at = 0; at + 5 <= modes.Length && modes[at] is not 0 and < 160; at += 5)
                Modes[modes[at]] = BinaryPrimitives.ReadUInt32BigEndian(modes.AsSpan(at + 1));
        }
    }

    /// <summary>RFC 4254's window-change: the grid's columns and rows, then its pixels.</summary>
    private sealed class WindowChangeMessage : ChannelRequestMessage
    {
        public uint Columns { get; private set; }
        public uint Rows { get; private set; }

        protected override void OnRead(ref SshDataReader reader)
        {
            base.OnRead(ref reader);
            Columns = reader.ReadUInt32();
            Rows = reader.ReadUInt32();
        }
    }
}

/// <summary>ECDSA P-256 keys in the forms each side wants.</summary>
internal static class TestKeys
{
    public static ECDsa New() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The public key in SSH wire format (RFC 5656).</summary>
    public static byte[] Blob(ECDsa key)
    {
        var p = key.ExportParameters(includePrivateParameters: false);
        var point = new byte[1 + p.Q.X!.Length + p.Q.Y!.Length];
        point[0] = 4;
        p.Q.X.CopyTo(point, 1);
        p.Q.Y.CopyTo(point, 1 + p.Q.X.Length);
        using var stream = new MemoryStream();
        WriteString(stream, Encoding.ASCII.GetBytes("ecdsa-sha2-nistp256"));
        WriteString(stream, Encoding.ASCII.GetBytes("nistp256"));
        WriteString(stream, point);
        return stream.ToArray();
    }

    public static void WriteString(Stream stream, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    /// <summary>An RSA public key in SSH wire format (RFC 4253).</summary>
    public static byte[] Blob(RSA key)
    {
        var p = key.ExportParameters(includePrivateParameters: false);
        using var stream = new MemoryStream();
        WriteString(stream, "ssh-rsa"u8);
        WriteString(stream, Mpint(p.Exponent!));
        WriteString(stream, Mpint(p.Modulus!));
        return stream.ToArray();

        static byte[] Mpint(byte[] unsigned) => (unsigned[0] & 0x80) != 0 ? [0, .. unsigned] : unsigned;
    }

    /// <summary>A private key file: SEC1 PEM (<c>EC PRIVATE KEY</c>), PKCS#8 when asked, and
    /// encrypted PKCS#8 when given a passphrase.</summary>
    public static string File(AsymmetricAlgorithm key, string? passphrase = null, bool pkcs8 = false) =>
        Write(passphrase is not null
            ? key.ExportEncryptedPkcs8PrivateKeyPem(passphrase,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 10_000))
            : pkcs8 ? key.ExportPkcs8PrivateKeyPem()
            : ((ECDsa)key).ExportECPrivateKeyPem());

    public static string Write(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), "hultui-ssh-test-" + Guid.NewGuid().ToString("N") + ".pem");
        System.IO.File.WriteAllText(path, text);
        return path;
    }
}
