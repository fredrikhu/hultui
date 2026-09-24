using HultUI.Core.Rendering;
using HultUI.Rendering.Skia;
using SkiaSharp;

namespace Aperture;

/// <summary>
/// Artwork, fetched and decoded off the UI thread and kept in a bounded LRU. Tiles ask for
/// a poster during <c>Build()</c>; a miss starts a download and returns
/// <see cref="Status.Pending"/>, and when the bytes land and decode the supplied callback
/// marshals a rebuild so the tile swaps its picture in.
///
/// <para>Vesper's thumbnail cache is the same shape over the file system; the differences
/// are the two that matter for a server: the work is I/O-bound rather than CPU-bound (so
/// the slot count is higher), and a request can be <i>abandoned</i> — scrolling a library
/// past a hundred posters must not keep a hundred downloads alive, so the cache hands the
/// grid a generation number and drops results from an older one.</para>
///
/// <para>Everything is keyed by the full image URL, which already carries the item, the
/// image kind, the tag and the size — so new artwork on the server is a new key rather
/// than a stale picture, and the same poster at two sizes is two entries on purpose.</para>
/// </summary>
public sealed class PosterCache : IDisposable
{
    public enum Status { Pending, Ready, Failed }

    private sealed class Entry
    {
        public IImageSource? Image;
        public Status Status;
    }

    /// <summary>Artwork past this is not decoded. Jellyfin's originals can be several
    /// megabytes, and we always ask for a sized rendition — anything this large means the
    /// server ignored the size, and a grid of them would be hundreds of megabytes.</summary>
    private const int MaxBytes = 12 * 1024 * 1024;

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();           // front = oldest
    private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
    private readonly Action _onLoaded;
    private readonly HttpClient _http;
    private readonly int _cap;
    private bool _disposed;

    /// <summary>Downloads allowed at once. Higher than a decode pool would be — these are
    /// mostly waiting on a LAN — but bounded, because every one of them holds a buffer and
    /// a connection.</summary>
    private readonly SemaphoreSlim _slots = new(8);

    public PosterCache(Action onLoaded, int capacity = 400, HttpClient? http = null)
    {
        _onLoaded = onLoaded;
        _cap = capacity;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>How many downloads are running — the "loading artwork" hint in a footer.</summary>
    public int PendingCount { get { lock (_lock) return _inFlight.Count; } }

    /// <summary>
    /// The <c>Authorization</c> header to send, set once a session is open and cleared
    /// when it closes. Artwork itself needs none — Jellyfin serves images anonymously —
    /// but scrubber tiles are behind the session, and this server rejects the token as a
    /// query parameter, which is the better practice anyway: a token in a URL ends up in
    /// logs, caches and referrers.
    /// </summary>
    public string? Authorization { get; set; }

    /// <summary>The picture for a URL, or (null, Pending) while it is fetched. Null URL —
    /// an item the server has no artwork for — is <see cref="Status.Failed"/> at once, so
    /// the tile draws its placeholder on the first frame rather than shimmering forever.</summary>
    public (IImageSource? Image, Status Status) Get(Uri? url)
    {
        if (url is null) return (null, Status.Failed);
        var key = url.ToString();
        lock (_lock)
        {
            if (_disposed) return (null, Status.Failed);
            if (_map.TryGetValue(key, out var e)) { Touch(key); return (e.Image, e.Status); }
            if (!_inFlight.Add(key)) return (null, Status.Pending);
        }
        _ = FetchQueued(key, url);
        return (null, Status.Pending);
    }

    /// <summary>Waits for a slot before taking a connection. Every visible tile calls
    /// <see cref="Get"/> on the same frame, so without this a screenful of posters is a
    /// screenful of simultaneous requests at the server.</summary>
    private async Task FetchQueued(string key, Uri url)
    {
        await _slots.WaitAsync().ConfigureAwait(false);
        try
        {
            var (image, status) = await FetchAsync(url).ConfigureAwait(false);
            lock (_lock)
            {
                if (_disposed) { (image as IDisposable)?.Dispose(); return; }
                _map[key] = new Entry { Image = image, Status = status };
                _lru.AddLast(key);
                _inFlight.Remove(key);
                Trim();
            }
            _onLoaded();   // the caller marshals to the UI thread and coalesces the repaint
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<(IImageSource?, Status)> FetchAsync(Uri url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (Authorization is { Length: > 0 } auth)
                request.Headers.TryAddWithoutValidation("Authorization", auth);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            // A 404 here is ordinary: the server reported a tag for an image it no longer
            // has. The tile shows its placeholder, and nothing is logged at the user.
            if (!response.IsSuccessStatusCode) return (null, Status.Failed);
            if (response.Content.Headers.ContentLength is > MaxBytes) return (null, Status.Failed);

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > MaxBytes) return (null, Status.Failed);

            var bitmap = Decode(bytes);
            return bitmap is null ? (null, Status.Failed) : (new SkiaImageSource(bitmap), Status.Ready);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return (null, Status.Failed);
        }
    }

    /// <summary>
    /// Decodes to Bgra8888 — universally drawable, and a fixed four bytes per pixel, so the
    /// cache's memory cost is the pixel count rather than whatever colour type the server's
    /// encoder chose.
    /// </summary>
    private static SKBitmap? Decode(byte[] bytes)
    {
        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null) return null;
            var src = codec.Info;
            if (src.Width <= 0 || src.Height <= 0) return null;

            var info = new SKImageInfo(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap();
            if (!bitmap.TryAllocPixels(info)) { bitmap.Dispose(); return null; }

            var result = codec.GetPixels(info, bitmap.GetPixels());
            // A truncated response still gives the rows that arrived — the same thing a
            // browser shows for a half-loaded image.
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                bitmap.Dispose();
                return null;
            }
            return bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private void Touch(string key)
    {
        _lru.Remove(key);
        _lru.AddLast(key);
    }

    private void Trim()
    {
        while (_map.Count > _cap && _lru.First is { } node)
        {
            _lru.RemoveFirst();
            if (_map.Remove(node.Value, out var e)) (e.Image as IDisposable)?.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var e in _map.Values) (e.Image as IDisposable)?.Dispose();
            _map.Clear();
            _lru.Clear();
        }
        _http.Dispose();
        // _slots is deliberately not disposed: disposing a SemaphoreSlim with waiters
        // throws in them rather than releasing them (the same note Vesper's cache carries).
    }
}
