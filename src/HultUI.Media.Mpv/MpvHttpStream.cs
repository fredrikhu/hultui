using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static HultUI.Media.Mpv.Mpv;

namespace HultUI.Media.Mpv;

/// <summary>
/// An https:// source read through .NET's <see cref="HttpClient"/>, for a libmpv whose FFmpeg has
/// no TLS: the Android build (native/mpv/build-libmpv-android.sh), and a Linux one built without
/// GnuTLS or OpenSSL. Windows' FFmpeg has Schannel and is never given one of these.
///
/// <para>mpv opens it through its stream-callback API under the protocol <see cref="Protocol"/>:
/// <c>https://host/path</c> is loaded as <c>hultui-https://host/path</c>, and mpv hands the URL
/// back to <see cref="Open"/>. Reads are HTTP range requests, so mpv can seek; on Android
/// <see cref="HttpClient"/> uses the platform's TLS and its trusted certificates.</para>
///
/// <para>What it does not reach: a stream FFmpeg fetches more of by itself, such as the segments of
/// an HLS transcode, still needs TLS in FFmpeg. A direct stream, Aperture's usual source, is one
/// request.</para>
///
/// <para>Every call but <see cref="Cancel"/> comes from mpv's stream thread, one at a time;
/// <see cref="Cancel"/> comes from another, to end a read that waits on the network.</para>
/// </summary>
internal sealed unsafe class MpvHttpStream : IDisposable
{
    /// <summary>The protocol registered with mpv for https:// sources.</summary>
    internal const string Protocol = "hultui-https";

    // Shared, as HttpClient is meant to be: one connection pool for every stream of the process.
    private static readonly Lazy<HttpClient> s_client = new(() => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

    /// <summary>A read that fails (a dropped connection, a reset) is tried again from where it
    /// stopped this many times before mpv is told the stream ended in an error.</summary>
    internal const int Retries = 3;

    /// <summary>Each request said on stderr, with mpv's own log (HULTUI_MPV_LOG).</summary>
    private static readonly bool Log = Environment.GetEnvironmentVariable("HULTUI_MPV_LOG") is { Length: > 0 };

    private readonly Uri _url;
    private readonly HttpClient _client;
    private readonly CancellationTokenSource _cancel = new();
    private HttpResponseMessage? _response;
    private Stream? _body;
    private long _position;
    private long? _size;

    internal MpvHttpStream(Uri url, HttpClient? client = null)
    {
        _url = url;
        _client = client ?? s_client.Value;
    }

    /// <summary>The https:// URL mpv was given <paramref name="mpvUrl"/> for, or null if it is not one
    /// of this protocol's.</summary>
    internal static Uri? Unwrap(string mpvUrl) =>
        mpvUrl.StartsWith(Protocol + "://", StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate("https" + mpvUrl[Protocol.Length..], UriKind.Absolute, out var url) ? url : null;

    /// <summary>What mpv is given to load for <paramref name="httpsUrl"/>.</summary>
    internal static string Wrap(string httpsUrl) => Protocol + httpsUrl["https".Length..];

    /// <summary>The resource's length in bytes, once a response has said it.</summary>
    internal long? Size => _size;

    internal long Position => _position;

    /// <summary>The first request, made while mpv opens the stream, so that a refused or missing
    /// source fails the open rather than the first read. Throws on failure.</summary>
    internal void Open() => Request(_position);

    /// <summary>Up to <paramref name="buffer"/>'s length of bytes from the current position: the
    /// count read, 0 at the end, or -1 on an error that retrying did not get past, or after
    /// <see cref="Cancel"/>.</summary>
    internal long Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty) return 0;
        for (int attempt = 0; ; attempt++)
        {
            if (_cancel.IsCancellationRequested) return -1;
            if (_size is { } size && _position >= size) return 0;
            try
            {
                if (_body is null) Request(_position);
                int got = _body!.Read(buffer);
                if (got > 0)
                {
                    _position += got;
                    return got;
                }
                // The body ended. With no length to go by, that is the end; short of a known
                // length, the connection was cut, and the rest is asked for again.
                CloseResponse();
                if (_size is null || _position >= _size) return 0;
            }
            catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException
                                      or ObjectDisposedException)
            {
                CloseResponse();
                if (_cancel.IsCancellationRequested) return -1;
            }
            if (attempt >= Retries) return -1;
        }
    }

    /// <summary>Moves to <paramref name="offset"/>; the next read asks for the bytes from there.
    /// The new position, or -1 outside the resource.</summary>
    internal long Seek(long offset)
    {
        if (offset < 0 || (_size is { } size && offset > size)) return -1;
        if (offset == _position) return offset;
        CloseResponse();
        _position = offset;
        return offset;
    }

    /// <summary>Ends a read waiting on the network, and every read after it. Any thread.</summary>
    internal void Cancel()
    {
        try { _cancel.Cancel(); }
        catch (ObjectDisposedException) { }
        // A synchronous read of the body does not watch the token; disposing the response aborts
        // its connection, and the read ends with an exception that Read turns into -1.
        try { Volatile.Read(ref _response)?.Dispose(); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
    }

    public void Dispose()
    {
        CloseResponse();
        _cancel.Dispose();
    }

    private void Request(long from)
    {
        CloseResponse();
        var request = new HttpRequestMessage(HttpMethod.Get, _url);
        request.Headers.Range = new RangeHeaderValue(from, null);
        // On mpv's stream thread, which no synchronization context waits on.
        var response = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancel.Token)
            .GetAwaiter().GetResult();
        try
        {
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && _size is { } known && from >= known)
            {
                response.Dispose();
                _body = Stream.Null;
                return;
            }
            response.EnsureSuccessStatusCode();
            var body = response.Content.ReadAsStreamAsync(_cancel.Token).GetAwaiter().GetResult();
            long start = 0;
            if (response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange is { } range)
            {
                start = range.From ?? 0;
                if (range.Length is { } total) _size = total;
            }
            else if (response.Content.Headers.ContentLength is { } length)
            {
                // The whole resource, from its first byte: a server that ignores ranges.
                _size = length;
            }
            _response = response;
            _body = body;
            if (Log)
                Console.Error.WriteLine($"[mpv] https: from {from}: {(int)response.StatusCode}, " +
                    $"range {response.Content.Headers.ContentRange?.ToString() ?? "none"}, length {_size?.ToString() ?? "unknown"}");
            // A server that ignored the range sent from the start; the bytes before the position are
            // read and dropped, which is slow but right.
            if (start < from)
            {
                if (Log) Console.Error.WriteLine($"[mpv] https: the server ignored the range; skipping {from - start} bytes");
                Skip(from - start);
            }
        }
        catch
        {
            response.Dispose();
            _response = null;
            _body = null;
            throw;
        }
    }

    private void Skip(long count)
    {
        var scratch = new byte[64 * 1024];
        while (count > 0)
        {
            int got = _body!.Read(scratch, 0, (int)Math.Min(scratch.Length, count));
            if (got == 0) throw new IOException("The response ended before the position asked for.");
            count -= got;
        }
    }

    private void CloseResponse()
    {
        var body = _body;
        var response = _response;
        _body = null;
        _response = null;
        try { body?.Dispose(); } catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        response?.Dispose();
    }

    // ──── mpv's callbacks ────

    /// <summary>Registers <see cref="Protocol"/> with <paramref name="mpv"/>, an initialized handle.
    /// <paramref name="withCancel"/> only for a client API of 1.106 or later, whose info struct has
    /// the cancel callback; an older one's has no room for it.</summary>
    internal static bool Register(nint mpv, bool withCancel)
    {
        s_withCancel = withCancel;
        return mpv_stream_cb_add_ro(mpv, Protocol, null, &OpenThunk) >= 0;
    }

    private static bool s_withCancel;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OpenThunk(void* userData, byte* uri, mpv_stream_cb_info* info)
    {
        MpvHttpStream? stream = null;
        try
        {
            string? text = Marshal.PtrToStringUTF8((nint)uri);
            if (text is null || Unwrap(text) is not { } url) return MPV_ERROR_LOADING_FAILED;
            stream = new MpvHttpStream(url);
            stream.Open();
            info->cookie = (void*)GCHandle.ToIntPtr(GCHandle.Alloc(stream));
            info->read_fn = &ReadThunk;
            info->seek_fn = &SeekThunk;
            info->size_fn = &SizeThunk;
            info->close_fn = &CloseThunk;
            if (s_withCancel) info->cancel_fn = &CancelThunk;
            return 0;
        }
        catch (Exception e)
        {
            stream?.Dispose();
            if (Environment.GetEnvironmentVariable("HULTUI_MPV_LOG") is { Length: > 0 })
                Console.Error.WriteLine($"[mpv] https stream did not open: {e.GetType().Name}: {e.Message}");
            return MPV_ERROR_LOADING_FAILED;
        }
    }

    private static MpvHttpStream Of(void* cookie) => (MpvHttpStream)GCHandle.FromIntPtr((nint)cookie).Target!;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long ReadThunk(void* cookie, byte* buffer, ulong count)
    {
        try { return Of(cookie).Read(new Span<byte>(buffer, (int)Math.Min(count, int.MaxValue))); }
        catch { return -1; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long SeekThunk(void* cookie, long offset)
    {
        try { return Of(cookie).Seek(offset) is var at and >= 0 ? at : MPV_ERROR_GENERIC; }
        catch { return MPV_ERROR_GENERIC; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long SizeThunk(void* cookie)
    {
        try { return Of(cookie).Size ?? MPV_ERROR_UNSUPPORTED; }
        catch { return MPV_ERROR_UNSUPPORTED; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CloseThunk(void* cookie)
    {
        try
        {
            var handle = GCHandle.FromIntPtr((nint)cookie);
            (handle.Target as MpvHttpStream)?.Dispose();
            handle.Free();
        }
        catch { }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CancelThunk(void* cookie)
    {
        try { Of(cookie).Cancel(); }
        catch { }
    }
}
