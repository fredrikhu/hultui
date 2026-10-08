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
///
/// <para>Sizes follow the window's display scale, so a move to another monitor asks for
/// everything again at new sizes. <see cref="Retire()"/> marks what is held as fetched for
/// the old scale: a tile keeps drawing its old picture (soft, briefly) while the new one
/// downloads, and once every download has landed whatever is left from the old scale is
/// dropped, instead of sitting in the cache beside its replacement. A hero, whose size also
/// follows the window's, retires its old size the same way one picture at a time
/// (<see cref="Retire(Uri)"/>).</para>
/// </summary>
public sealed class PosterCache : IDisposable
{
    public enum Status { Pending, Ready, Failed }

    private sealed class Entry
    {
        public IImageSource? Image;
        public Status Status;
        /// <summary>Fetched for a display scale the window has since left; see
        /// <see cref="Retire()"/>.</summary>
        public bool Retired;
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

    /// <summary>Bumped by <see cref="Retire()"/>. A download started before the bump lands
    /// retired: it was asked for at the old scale.</summary>
    private int _scaleGeneration;
    private int _retiredCount;
    /// <summary>Set when the last download lands while retired entries remain; the next
    /// <see cref="Get"/> drops them. Not dropped on the download's own thread, because the
    /// UI thread may be drawing one of them.</summary>
    private bool _sweepRetired;
    /// <summary>Downloads retired one by one (<see cref="Retire(Uri)"/>) before they landed:
    /// they land retired.</summary>
    private readonly HashSet<string> _retireOnLanding = new(StringComparer.Ordinal);

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
    public string? Authorization { get; private set; }

    /// <summary>The one origin (<c>scheme://host:port</c>) <see cref="Authorization"/> is
    /// sent to. The cache also fetches from hosts that are not the media server — the
    /// discovery pages draw posters straight from TMDB — and the session token must never
    /// ride along to those.</summary>
    public string? AuthorizedOrigin { get; private set; }

    /// <summary>Sets the header and the server it belongs to together, so there is no
    /// moment where one is set without the other. Null for either clears both.</summary>
    public void Authorize(string? serverAddress, string? header)
    {
        var origin = serverAddress is not null && Uri.TryCreate(serverAddress, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
        bool on = origin is not null && header is { Length: > 0 };
        AuthorizedOrigin = on ? origin : null;
        Authorization = on ? header : null;
    }

    /// <summary>Whether a request to this URL may carry the session header.</summary>
    internal bool IsAuthorizedFor(Uri url) =>
        Authorization is { Length: > 0 } && AuthorizedOrigin is { } origin
        && string.Equals(url.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase);

    /// <summary>The picture for a URL, or (null, Pending) while it is fetched. Null URL —
    /// an item the server has no artwork for — is <see cref="Status.Failed"/> at once, so
    /// the tile draws its placeholder on the first frame rather than shimmering forever.
    ///
    /// <para><paramref name="standIn"/> is the same artwork at the size asked for before the
    /// window last changed scale. While <paramref name="url"/> downloads, a retired picture
    /// there is returned as <see cref="Status.Ready"/>, so the tile stays drawn; once
    /// <paramref name="url"/> has landed, asking again drops the stand-in.</para>
    ///
    /// <para>Call it on the UI thread: it is where retired pictures are disposed, and that
    /// thread is the one that may be drawing them.</para></summary>
    public (IImageSource? Image, Status Status) Get(Uri? url, Uri? standIn = null)
    {
        if (url is null) return (null, Status.Failed);
        var key = url.ToString();
        var standInKey = standIn?.ToString();
        int generation;
        Entry? stand = null;
        lock (_lock)
        {
            if (_disposed) return (null, Status.Failed);
            // The stand-in is spared: it may have been retired since the last download
            // landed, and it is what this caller draws until its own picture is in.
            if (_sweepRetired) SweepRetired(standInKey);
            if (_map.TryGetValue(key, out var e))
            {
                Touch(key);
                // Back at the scale it was fetched for: it is the right size again.
                if (e.Retired) { e.Retired = false; _retiredCount--; }
                if (standInKey is not null) DropRetired(standInKey);
                return (e.Image, e.Status);
            }
            if (standInKey is not null && _map.TryGetValue(standInKey, out var s)
                && s.Retired && s.Status == Status.Ready)
            {
                stand = s;
                Touch(standInKey);   // drawn, so not the next thing the LRU drops
            }
            // Asked for again before it landed: it is wanted after all.
            _retireOnLanding.Remove(key);
            if (!_inFlight.Add(key)) return stand is null ? (null, Status.Pending) : (stand.Image, Status.Ready);
            generation = _scaleGeneration;
        }
        _ = FetchQueued(key, url, generation);
        return stand is null ? (null, Status.Pending) : (stand.Image, Status.Ready);
    }

    /// <summary>Whether anything fetched for an earlier display scale is still held — the
    /// only time a stand-in is worth looking up.</summary>
    public bool HasRetired { get { lock (_lock) return _retiredCount > 0; } }

    /// <summary>
    /// The window changed display scale, so every size asked for from now on is new: marks
    /// everything held, and everything still downloading, as fetched for the old scale.
    /// Retired pictures still serve as stand-ins (see <see cref="Get"/>) and are dropped
    /// once the downloads that follow have all landed. One asked for again by its own URL
    /// — the window went back — is simply current again.
    /// </summary>
    public void Retire()
    {
        lock (_lock)
        {
            _scaleGeneration++;
            _sweepRetired = false;   // the stand-ins this makes are about to be needed
            _retireOnLanding.Clear();   // every download now lands retired anyway
            foreach (var e in _map.Values)
                if (!e.Retired) { e.Retired = true; _retiredCount++; }
        }
    }

    /// <summary>
    /// One picture is no longer wanted at its size: a hero asked for the same artwork at
    /// another size, because the window was resized past a step. Retired as by
    /// <see cref="Retire()"/> — still a stand-in, dropped once the downloads that follow have
    /// landed, current again if asked for by its own URL — and if it is still downloading,
    /// it lands retired.
    /// </summary>
    public void Retire(Uri url)
    {
        var key = url.ToString();
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var e))
            {
                if (!e.Retired) { e.Retired = true; _retiredCount++; }
            }
            else if (_inFlight.Contains(key))
            {
                _retireOnLanding.Add(key);
            }
        }
    }

    /// <summary>Whether this URL's own picture has landed — not a stand-in for it, and not a
    /// download that failed.</summary>
    public bool IsReady(Uri url)
    {
        lock (_lock) return _map.TryGetValue(url.ToString(), out var e) && e.Status == Status.Ready;
    }

    /// <summary>Waits for a slot before taking a connection. Every visible tile calls
    /// <see cref="Get"/> on the same frame, so without this a screenful of posters is a
    /// screenful of simultaneous requests at the server.</summary>
    private async Task FetchQueued(string key, Uri url, int generation)
    {
        await _slots.WaitAsync().ConfigureAwait(false);
        try
        {
            var (image, status) = await FetchAsync(url).ConfigureAwait(false);
            lock (_lock)
            {
                if (_disposed) { (image as IDisposable)?.Dispose(); return; }
                // Not ||: the key leaves the set either way.
                bool retired = _retireOnLanding.Remove(key) | (generation != _scaleGeneration);
                _map[key] = new Entry { Image = image, Status = status, Retired = retired };
                if (retired) _retiredCount++;
                _lru.AddLast(key);
                _inFlight.Remove(key);
                // The last download has landed: every tile that asked has its picture at the
                // new size, so what is left from the old scale is drawn nowhere.
                if (_inFlight.Count == 0 && _retiredCount > 0) _sweepRetired = true;
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
            if (IsAuthorizedFor(url))
                request.Headers.TryAddWithoutValidation("Authorization", Authorization);
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
            if (_map.Remove(node.Value, out var e))
            {
                if (e.Retired) _retiredCount--;
                (e.Image as IDisposable)?.Dispose();
            }
        }
    }

    /// <summary>Drops one entry if it is retired. Under the lock.</summary>
    private void DropRetired(string key)
    {
        if (!_map.TryGetValue(key, out var e) || !e.Retired) return;
        _map.Remove(key);
        _lru.Remove(key);
        _retiredCount--;
        (e.Image as IDisposable)?.Dispose();
    }

    /// <summary>Drops every retired entry but <paramref name="keep"/> (a stand-in about to be
    /// drawn). Under the lock, on the UI thread; once per change of scale or hero size, so
    /// the list it makes does not matter.</summary>
    private void SweepRetired(string? keep)
    {
        _sweepRetired = false;
        if (_retiredCount == 0) return;
        var keys = new List<string>(_retiredCount);
        foreach (var (key, e) in _map)
            if (e.Retired && !string.Equals(key, keep, StringComparison.Ordinal)) keys.Add(key);
        foreach (var key in keys) DropRetired(key);
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
