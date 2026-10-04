using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace HultUI.Terminal.Ssh.Tests;

/// <summary>
/// An SSH agent holding one ECDSA key, on a pipe (Windows) or socket (elsewhere) of its own, so the
/// user's real agent is never asked. It answers the two messages signing in needs: the list of
/// keys, and a signature (draft-miller-ssh-agent).
/// </summary>
internal sealed class FakeSshAgent : IDisposable
{
    private const byte Failure = 5, RequestIdentities = 11, IdentitiesAnswer = 12, SignRequest = 13, SignResponse = 14;

    private readonly ECDsa _key;
    private readonly byte[] _blob;
    private readonly CancellationTokenSource _stop = new();
    private readonly Socket? _listener;
    private int _signatures;

    public FakeSshAgent(ECDsa key)
    {
        _key = key;
        _blob = TestKeys.Blob(key);
        if (OperatingSystem.IsWindows())
        {
            Socket = "hultui-test-agent-" + Guid.NewGuid().ToString("N");
            _ = ServePipes();
        }
        else
        {
            Socket = Path.Combine(Path.GetTempPath(), "hultui-agent-" + Guid.NewGuid().ToString("N")[..8] + ".sock");
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(Socket));
            _listener.Listen();
            _ = ServeSockets();
        }
    }

    /// <summary>What <see cref="SshAuthentication.Agent"/> takes: a pipe name or a socket path.</summary>
    public string Socket { get; }

    /// <summary>How many signatures it made: one per sign-in that used it.</summary>
    public int Signatures => Volatile.Read(ref _signatures);

    private async Task ServePipes()
    {
        while (!_stop.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(Socket, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try { await pipe.WaitForConnectionAsync(_stop.Token); }
            catch (Exception) { await pipe.DisposeAsync(); return; }
            _ = Serve(pipe);
        }
    }

    private async Task ServeSockets()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket client;
            try { client = await _listener!.AcceptAsync(_stop.Token); }
            catch (Exception) { return; }
            _ = Serve(new NetworkStream(client, ownsSocket: true));
        }
    }

    private async Task Serve(Stream stream)
    {
        await using var _ = stream;
        var header = new byte[4];
        try
        {
            while (true)
            {
                await stream.ReadExactlyAsync(header, _stop.Token);
                var message = new byte[BinaryPrimitives.ReadUInt32BigEndian(header)];
                await stream.ReadExactlyAsync(message, _stop.Token);
                var reply = Answer(message);
                var length = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(length, (uint)reply.Length);
                await stream.WriteAsync(length, _stop.Token);
                await stream.WriteAsync(reply, _stop.Token);
                await stream.FlushAsync(_stop.Token);
            }
        }
        catch (Exception) { } // the client hung up
    }

    private byte[] Answer(byte[] message)
    {
        using var reply = new MemoryStream();
        switch (message[0])
        {
            case RequestIdentities:
                reply.WriteByte(IdentitiesAnswer);
                WriteUInt32(reply, 1);
                TestKeys.WriteString(reply, _blob);
                TestKeys.WriteString(reply, "test key"u8);
                break;
            case SignRequest:
                int at = 1;
                var keyBlob = ReadString(message, ref at);
                var data = ReadString(message, ref at);
                if (!keyBlob.SequenceEqual(_blob)) { reply.WriteByte(Failure); break; }
                Interlocked.Increment(ref _signatures);
                var raw = _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                using (var inner = new MemoryStream())
                {
                    TestKeys.WriteString(inner, Mpint(raw.AsSpan(0, 32)));
                    TestKeys.WriteString(inner, Mpint(raw.AsSpan(32)));
                    using var signature = new MemoryStream();
                    TestKeys.WriteString(signature, Encoding.ASCII.GetBytes("ecdsa-sha2-nistp256"));
                    TestKeys.WriteString(signature, inner.ToArray());
                    reply.WriteByte(SignResponse);
                    TestKeys.WriteString(reply, signature.ToArray());
                }
                break;
            default:
                reply.WriteByte(Failure);
                break;
        }
        return reply.ToArray();
    }

    /// <summary>An unsigned big-endian integer as SSH's mpint: no leading zeros, and one zero byte
    /// in front when the top bit is set.</summary>
    private static byte[] Mpint(ReadOnlySpan<byte> unsigned) =>
        new BigInteger(unsigned, isUnsigned: true, isBigEndian: true).ToByteArray(isUnsigned: false, isBigEndian: true);

    private static byte[] ReadString(byte[] message, ref int at)
    {
        int length = (int)BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(at));
        var value = message.AsSpan(at + 4, length).ToArray();
        at += 4 + length;
        return value;
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Dispose();
        if (_listener is not null) try { File.Delete(Socket); } catch (IOException) { }
    }
}
