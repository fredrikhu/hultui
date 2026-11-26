using System.Net;
using System.Net.Sockets;
using System.Text;
using HultUI.Media.Mpv;
using Xunit;

namespace HultUI.Media.Backends.Tests;

/// <summary>
/// The https:// stream mpv reads through HttpClient when its FFmpeg has no TLS (Android), without
/// libmpv: its reads, seeks and retries against a local server that does ranges, ignores them,
/// cuts a response short, has nothing at the address, or stalls. Plain http:// here; the stream
/// does not care which, and the TLS is HttpClient's.
/// </summary>
public class MpvHttpStreamTests
{
    private static readonly byte[] Data = MakeData(1_000_003);

    private static byte[] MakeData(int length)
    {
        var data = new byte[length];
        new Random(20261126).NextBytes(data);
        return data;
    }

    private static byte[] ReadToEnd(MpvHttpStream stream)
    {
        var all = new MemoryStream();
        var buffer = new byte[48 * 1024];
        while (true)
        {
            long got = stream.Read(buffer);
            Assert.True(got >= 0, "a read failed");
            if (got == 0) return all.ToArray();
            all.Write(buffer, 0, (int)got);
        }
    }

    [Fact]
    public void ItReadsTheWholeResource_InOneRequest_AndKnowsItsSize()
    {
        using var server = new RangeServer(Data);
        using var stream = new MpvHttpStream(server.Url);
        stream.Open();

        Assert.Equal(Data.Length, stream.Size);
        Assert.Equal(Data, ReadToEnd(stream));
        Assert.Equal(["bytes=0-"], server.Ranges);
    }

    [Fact]
    public void ASeek_AsksForTheBytesFromThere()
    {
        using var server = new RangeServer(Data);
        using var stream = new MpvHttpStream(server.Url);
        stream.Open();

        Assert.Equal(700_000, stream.Seek(700_000));
        Assert.Equal(Data[700_000..], ReadToEnd(stream));
        Assert.Equal(["bytes=0-", "bytes=700000-"], server.Ranges);
    }

    [Fact]
    public void ASeekToWhereItIs_AsksForNothing_AndOnePastTheEnd_IsRefused()
    {
        using var server = new RangeServer(Data);
        using var stream = new MpvHttpStream(server.Url);
        stream.Open();

        Assert.Equal(0, stream.Seek(0));   // mpv's own test of seekability, as it opens
        Assert.Equal(-1, stream.Seek(Data.Length + 1));
        Assert.Equal(-1, stream.Seek(-1));
        Assert.Single(server.Ranges);
    }

    [Fact]
    public void AServerThatIgnoresRanges_StillGivesTheBytesAfterASeek()
    {
        using var server = new RangeServer(Data) { IgnoreRanges = true };
        using var stream = new MpvHttpStream(server.Url);
        stream.Open();
        Assert.Equal(Data.Length, stream.Size);

        stream.Seek(123_457);
        Assert.Equal(Data[123_457..], ReadToEnd(stream));
    }

    [Fact]
    public void AResponseCutShort_IsAskedForAgainFromWhereItStopped()
    {
        using var server = new RangeServer(Data) { CutFirstAfter = 300_000 };
        using var stream = new MpvHttpStream(server.Url);
        stream.Open();

        Assert.Equal(Data, ReadToEnd(stream));
        Assert.Equal(2, server.Ranges.Count);
        Assert.Equal("bytes=300000-", server.Ranges[1]);
    }

    [Fact]
    public void NothingAtTheAddress_FailsTheOpen()
    {
        using var server = new RangeServer(Data) { NotFound = true };
        using var stream = new MpvHttpStream(server.Url);

        Assert.Throws<HttpRequestException>(stream.Open);
    }

    [Fact]
    public void Cancel_EndsAReadThatWaitsOnTheNetwork()
    {
        using var server = new RangeServer(Data) { Stall = true };
        using var stream = new MpvHttpStream(server.Url);
        stream.Open();

        var read = Task.Run(() => stream.Read(new byte[4096]));
        Assert.False(read.Wait(300));   // it waits for bytes that do not come
        stream.Cancel();

        Assert.True(read.Wait(TimeSpan.FromSeconds(5)), "the read did not end");
        Assert.Equal(-1, read.Result);
        Assert.Equal(-1, stream.Read(new byte[16]));
    }

    [Fact]
    public void AnHttpsUrl_IsWrappedForMpv_AndUnwrappedBack()
    {
        const string url = "https://media.example/Videos/abc/stream?static=true&x=1";
        string wrapped = MpvHttpStream.Wrap(url);

        Assert.Equal("hultui-https://media.example/Videos/abc/stream?static=true&x=1", wrapped);
        Assert.Equal(new Uri(url), MpvHttpStream.Unwrap(wrapped));
        Assert.Null(MpvHttpStream.Unwrap("https://media.example/x"));
    }

    /// <summary>One request per connection, answered as asked, on a raw socket so a test decides
    /// exactly what goes on the wire.</summary>
    private sealed class RangeServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly byte[] _data;
        private readonly ManualResetEventSlim _stop = new();
        private readonly List<string?> _ranges = [];
        private int _served;

        public bool IgnoreRanges { get; init; }
        public bool NotFound { get; init; }
        public bool Stall { get; init; }
        public int CutFirstAfter { get; init; } = -1;

        public RangeServer(byte[] data)
        {
            _data = data;
            _listener.Start();
            _ = AcceptAsync();
        }

        public Uri Url => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/file");

        public List<string?> Ranges { get { lock (_ranges) return [.. _ranges]; } }

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
                    string? range = null;
                    for (string? line = reader.ReadLine(); !string.IsNullOrEmpty(line); line = reader.ReadLine())
                        if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                            range = line["Range:".Length..].Trim();
                    lock (_ranges) _ranges.Add(range);
                    bool first = Interlocked.Increment(ref _served) == 1;

                    if (NotFound)
                    {
                        Write(net, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                        return;
                    }
                    long from = 0;
                    string head;
                    if (!IgnoreRanges && range is not null && range.StartsWith("bytes=", StringComparison.Ordinal))
                    {
                        from = long.Parse(range["bytes=".Length..].TrimEnd('-'));
                        head = $"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes {from}-{_data.Length - 1}/{_data.Length}\r\n" +
                               $"Content-Length: {_data.Length - from}\r\n";
                    }
                    else
                        head = $"HTTP/1.1 200 OK\r\nContent-Length: {_data.Length}\r\n";
                    Write(net, head + "Connection: close\r\n\r\n");

                    if (Stall)
                    {
                        _stop.Wait();
                        return;
                    }
                    int count = (int)(_data.Length - from);
                    if (first && CutFirstAfter >= 0) count = Math.Min(count, CutFirstAfter);
                    net.Write(_data, (int)from, count);
                    net.Flush();
                }
                catch (IOException) { }
            }
        }

        private static void Write(Stream net, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            net.Write(bytes);
            net.Flush();
        }

        public void Dispose()
        {
            _stop.Set();
            _listener.Stop();
        }
    }
}
