using HultUI.Core.Rendering;

namespace HultUI.Media;

/// <summary>Somewhere a still can be got for a position in a file. One implementation
/// decodes with mpv; a test supplies its own.</summary>
public interface IScrubFrameSource : IDisposable
{
    /// <summary>Prepares for a file. False means no stills can be made for it, which is not
    /// an error — a player without a decoder, or a stream that cannot be seeked twice.</summary>
    Task<bool> OpenAsync(string path, CancellationToken cancellationToken);

    /// <summary>The frame at that position, encoded, or null when it could not be had.</summary>
    Task<byte[]?> GrabAsync(TimeSpan position, CancellationToken cancellationToken);
}

/// <summary>
/// Scrubber stills for a local file, generated in the background and handed to
/// <see cref="VideoPlayer.PreviewFrame"/>.
///
/// <para>A server has these already: Jellyfin renders a sprite sheet when a film is added,
/// and Aperture just fetches it. A file on disk has nothing of the sort, so they have to be
/// made here — which is why this exists at all, and why it is a cache rather than a lookup.
/// Decoding a frame costs a seek, so the stills are made once, slowly, off the UI thread,
/// and the scrubber shows whatever has arrived so far.</para>
///
/// <para><b>Nothing waits.</b> <see cref="Frame"/> is called from a build, so it never
/// blocks and never starts work: it returns the nearest still already in hand, or null, and
/// the label reads the time that still stands for rather than the pointer's, which would
/// claim a precision the picture has not got.</para>
/// </summary>
public sealed class ScrubPreviewCache : IDisposable
{
    /// <summary>At most this many stills for a file, however long it is. A three-hour film
    /// at 120 stills is one every 90 seconds, which is the resolution of "roughly where"
    /// rather than of a frame-accurate edit — and the whole set is a few megabytes.</summary>
    public const int MaxFrames = 120;

    /// <summary>No closer together than this. Below a couple of seconds the stills stop
    /// telling you anything a neighbouring one did not, and a two-minute clip does not need
    /// 120 of them.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2);

    private readonly IScrubFrameSource _source;
    private readonly Func<byte[], IImageSource?> _decode;
    private readonly Action? _onFrame;
    private readonly Dictionary<int, IImageSource> _frames = [];
    private readonly Lock _gate = new();

    private CancellationTokenSource? _work;
    private TimeSpan _interval;
    private int _count;

    /// <param name="source">Where stills come from.</param>
    /// <param name="decode">Encoded bytes to something drawable. The app's, because
    /// decoding is the renderer's business and this assembly has no opinion on it.</param>
    /// <param name="onFrame">Called on the worker when a still lands, so the window can
    /// repaint. Marshal it yourself.</param>
    public ScrubPreviewCache(IScrubFrameSource source, Func<byte[], IImageSource?> decode,
        Action? onFrame = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(decode);
        _source = source;
        _decode = decode;
        _onFrame = onFrame;
    }

    /// <summary>How far apart the stills are for the open file; zero before one is open.</summary>
    public TimeSpan Interval { get { lock (_gate) return _interval; } }

    /// <summary>How many stills the open file will have once the run finishes.</summary>
    public int Count { get { lock (_gate) return _count; } }

    /// <summary>How many have been made so far — for a test, and for anything that wants to
    /// say "still working".</summary>
    public int Ready { get { lock (_gate) return _frames.Count; } }

    /// <summary>
    /// The spacing for a file of that length: the cap spread evenly across it, but never
    /// closer than <see cref="MinInterval"/>.
    ///
    /// <para>There is deliberately no upper bound. One looks reasonable — "never further
    /// apart than twenty seconds" — and is wrong, because the cap and the ceiling then fight:
    /// 120 stills twenty seconds apart cover forty minutes, so every film longer than that
    /// would have no stills for its second half. The spacing has to grow with the film.</para>
    /// </summary>
    public static TimeSpan IntervalFor(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return MinInterval;
        var even = TimeSpan.FromSeconds(duration.TotalSeconds / MaxFrames);
        return even < MinInterval ? MinInterval : even;
    }

    /// <summary>
    /// Begins making stills for a file. Any run for a previous file is cancelled and its
    /// stills dropped — they are about a film nobody is watching any more.
    /// </summary>
    public void Start(string path, TimeSpan duration)
    {
        Stop();

        var interval = IntervalFor(duration);
        int count = duration <= TimeSpan.Zero
            ? 0
            : Math.Min(MaxFrames, (int)(duration.TotalSeconds / interval.TotalSeconds) + 1);

        lock (_gate)
        {
            _frames.Clear();
            _interval = interval;
            _count = count;
        }
        if (count == 0) return;

        var work = new CancellationTokenSource();
        _work = work;
        _ = Task.Run(() => RunAsync(path, interval, count, work.Token), work.Token);
    }

    /// <summary>Cancels the run and forgets the stills.</summary>
    public void Stop()
    {
        var work = _work;
        _work = null;
        if (work is not null)
        {
            work.Cancel();
            work.Dispose();
        }
        lock (_gate)
        {
            _frames.Clear();
            _count = 0;
        }
    }

    private async Task RunAsync(string path, TimeSpan interval, int count, CancellationToken token)
    {
        try
        {
            if (!await _source.OpenAsync(path, token).ConfigureAwait(false)) return;

            for (int i = 0; i < count && !token.IsCancellationRequested; i++)
            {
                var at = interval * i;
                var bytes = await _source.GrabAsync(at, token).ConfigureAwait(false);
                if (bytes is null) continue;

                var image = _decode(bytes);
                if (image is null) continue;

                lock (_gate) _frames[i] = image;
                _onFrame?.Invoke();
            }
        }
        catch (OperationCanceledException) { /* a new file, or the window closing */ }
    }

    /// <summary>
    /// The still to show for a position: the one made for the nearest interval, or the
    /// closest already made on either side of it, so the card is not empty while the run is
    /// still working through the film. Null when nothing has been made yet.
    /// </summary>
    public VideoPreviewFrame? Frame(TimeSpan position)
    {
        lock (_gate)
        {
            if (_count == 0 || _frames.Count == 0 || _interval <= TimeSpan.Zero) return null;

            int wanted = (int)Math.Round(position.TotalSeconds / _interval.TotalSeconds);
            wanted = Math.Clamp(wanted, 0, _count - 1);

            if (_frames.TryGetValue(wanted, out var exact))
                return new VideoPreviewFrame(exact, null, _interval * wanted);

            // Outward from what was asked for, so an early scrub shows the nearest thing
            // that exists rather than nothing at all.
            for (int step = 1; step < _count; step++)
            {
                if (wanted - step >= 0 && _frames.TryGetValue(wanted - step, out var before))
                    return new VideoPreviewFrame(before, null, _interval * (wanted - step));
                if (wanted + step < _count && _frames.TryGetValue(wanted + step, out var after))
                    return new VideoPreviewFrame(after, null, _interval * (wanted + step));
            }
            return null;
        }
    }

    public void Dispose()
    {
        Stop();
        _source.Dispose();
    }
}
