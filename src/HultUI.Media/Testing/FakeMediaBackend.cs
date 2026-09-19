using HultUI.Core.Rendering;

namespace HultUI.Media.Testing;

/// <summary>
/// A scripted backend for tests: no decoder, no threads, no clock. Opening succeeds at
/// once with the configured duration, size and tracks and publishes a first frame;
/// <see cref="Advance"/> moves time by hand, publishing the frames that fall due and
/// ending playback at the duration. Every command is recorded in <see cref="Calls"/>,
/// and <see cref="Raise"/> injects any event — from any thread, to test marshalling.
/// </summary>
/// <remarks>
/// Frames are solid: byte <c>n</c> for frame <c>n</c> (mod 256), so a test that reads
/// the sink can tell which frame it holds. Events are raised synchronously on the
/// calling thread, so under a headless session a command's effects are visible as soon
/// as it returns.
/// </remarks>
public sealed class FakeMediaBackend : IMediaBackend
{
    private long _frameIndex;

    public BackendCapabilities Capabilities { get; set; } =
        BackendCapabilities.PixelFrames | BackendCapabilities.Uris | BackendCapabilities.Streams
        | BackendCapabilities.Rate | BackendCapabilities.Subtitles | BackendCapabilities.PreciseSeek;

    // ──── Script ────

    /// <summary>Length reported on open. Null plays as a live stream: no end, no clamp.</summary>
    public TimeSpan? Duration { get; set; } = TimeSpan.FromSeconds(10);
    public int VideoWidth { get; set; } = 320;
    public int VideoHeight { get; set; } = 180;
    public double SampleAspect { get; set; } = 1.0;
    public double FrameRate { get; set; } = 25;
    public List<MediaTrack> AudioTracks { get; } = [];
    public List<MediaTrack> SubtitleTracks { get; } = [];
    /// <summary>Makes the next <see cref="Open"/> fail with this message, then clears itself.</summary>
    public string? FailNextOpenWith { get; set; }

    // ──── Observed ────

    /// <summary>Every command, in order: <c>Open(file:x.mp4)</c>, <c>Play</c>, <c>Seek(00:00:05, precise)</c>…</summary>
    public List<string> Calls { get; } = [];
    public IVideoSink? Sink { get; private set; }
    public MediaSource? Source { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.NoMedia;
    public TimeSpan Position { get; private set; }
    public double Rate { get; private set; } = 1.0;
    public float Volume { get; private set; } = 1f;
    public bool Muted { get; private set; }
    public int? SelectedAudio { get; private set; }
    public int? SelectedSubtitle { get; private set; }
    public long FramesPublished { get; private set; }
    public bool Disposed { get; private set; }

    public event Action<BackendEvent>? Event;

    /// <summary>Raises an event as the backend would, on the calling thread.</summary>
    public void Raise(BackendEvent e) => Event?.Invoke(e);

    public void Attach(IVideoSink sink)
    {
        Calls.Add("Attach");
        Sink = sink;
    }

    public void Open(MediaSource source)
    {
        Calls.Add($"Open({source})");
        Source = source;
        Position = TimeSpan.Zero;
        _frameIndex = 0;

        if (FailNextOpenWith is { } message)
        {
            FailNextOpenWith = null;
            SetState(PlaybackState.Failed);
            Raise(new BackendEvent.Failed(message));
            return;
        }

        SetState(PlaybackState.Opening);
        Raise(new BackendEvent.DurationChanged(Duration));
        Raise(new BackendEvent.VideoGeometry(VideoWidth, VideoHeight, SampleAspect));
        SelectedAudio = AudioTracks.Count > 0 ? AudioTracks[0].Id : null;
        SelectedSubtitle = null;
        RaiseTracks();
        SetState(PlaybackState.Paused);
        PublishFrame();
    }

    public void Play()
    {
        Calls.Add("Play");
        if (State is PlaybackState.NoMedia or PlaybackState.Opening or PlaybackState.Failed) return;
        if (State == PlaybackState.Ended)
        {
            Position = TimeSpan.Zero;
            Raise(new BackendEvent.PositionChanged(Position));
        }
        SetState(PlaybackState.Playing);
    }

    public void Pause()
    {
        Calls.Add("Pause");
        if (State == PlaybackState.Playing) SetState(PlaybackState.Paused);
    }

    public void Stop()
    {
        Calls.Add("Stop");
        if (State is PlaybackState.NoMedia or PlaybackState.Failed) return;
        Position = TimeSpan.Zero;
        Raise(new BackendEvent.PositionChanged(Position));
        SetState(PlaybackState.Paused);
    }

    public void Seek(TimeSpan position, bool precise)
    {
        Calls.Add($"Seek({position:hh\\:mm\\:ss\\.fff}, {(precise ? "precise" : "keyframe")})");
        if (State is PlaybackState.NoMedia or PlaybackState.Failed) return;
        Position = Clamp(position);
        _frameIndex = (long)Math.Floor(Position.TotalSeconds * FrameRate);
        Raise(new BackendEvent.PositionChanged(Position));
        if (State == PlaybackState.Ended && Position < Duration) SetState(PlaybackState.Paused);
        PublishFrame();
    }

    public void SetRate(double rate) { Calls.Add($"SetRate({rate})"); Rate = rate; }
    public void SetVolume(float volume) { Calls.Add($"SetVolume({volume})"); Volume = volume; }
    public void SetMuted(bool muted) { Calls.Add($"SetMuted({muted})"); Muted = muted; }

    public void SelectAudioTrack(int? id)
    {
        Calls.Add($"SelectAudioTrack({id?.ToString() ?? "none"})");
        SelectedAudio = id;
        RaiseTracks();
    }

    public void SelectSubtitleTrack(int? id)
    {
        Calls.Add($"SelectSubtitleTrack({id?.ToString() ?? "none"})");
        SelectedSubtitle = id;
        RaiseTracks();
    }

    /// <summary>
    /// Moves playback forward by <paramref name="wallClock"/> at the current rate:
    /// publishes every frame that falls due, reports the new position, and ends
    /// playback at the duration. Nothing happens unless playing.
    /// </summary>
    public void Advance(TimeSpan wallClock)
    {
        if (State != PlaybackState.Playing) return;

        var target = Position + wallClock * Rate;
        bool ended = Duration is { } duration && target >= duration;
        if (ended) target = Duration!.Value;

        long due = (long)Math.Floor(target.TotalSeconds * FrameRate);
        while (_frameIndex < due)
        {
            _frameIndex++;
            PublishFrame();
        }

        Position = target;
        Raise(new BackendEvent.PositionChanged(Position));
        if (ended)
        {
            State = PlaybackState.Ended;
            Raise(new BackendEvent.EndReached());
        }
    }

    /// <summary>Publishes one solid frame (byte = frame index mod 256) at the configured size.</summary>
    public void PublishFrame()
    {
        if (Sink is null) return;
        using var writer = Sink.Pixels.BeginWrite(VideoWidth, VideoHeight, PixelFormat.Rgba8888);
        writer.Pixels.Fill((byte)(_frameIndex % 256));
        writer.Publish();
        FramesPublished++;
    }

    public void ReportBuffering(float? fraction) => Raise(new BackendEvent.BufferingChanged(fraction));

    public void Dispose() => Disposed = true;

    private void SetState(PlaybackState state)
    {
        State = state;
        Raise(new BackendEvent.StateChanged(state));
    }

    private void RaiseTracks() =>
        Raise(new BackendEvent.TracksChanged(AudioTracks.ToArray(), SubtitleTracks.ToArray(), SelectedAudio, SelectedSubtitle));

    private TimeSpan Clamp(TimeSpan position)
    {
        if (position < TimeSpan.Zero) return TimeSpan.Zero;
        if (Duration is { } d && position > d) return d;
        return position;
    }
}
