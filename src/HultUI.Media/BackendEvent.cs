namespace HultUI.Media;

/// <summary>
/// What a backend tells the player. Raised on whatever thread the backend runs on;
/// <see cref="MediaPlayer"/> marshals them to the UI thread. Frames are not events —
/// they go through the sink.
/// </summary>
public abstract record BackendEvent
{
    private BackendEvent() { }

    /// <summary>Playback moved to <paramref name="State"/>.</summary>
    public sealed record StateChanged(PlaybackState State) : BackendEvent;

    /// <summary>The playhead moved. Backends throttle this to a few times a second.</summary>
    public sealed record PositionChanged(TimeSpan Position) : BackendEvent;

    /// <summary>The media's length became known; null for a live stream.</summary>
    public sealed record DurationChanged(TimeSpan? Duration) : BackendEvent;

    /// <summary>
    /// The coded frame size and its sample aspect ratio; the display width is
    /// <c>Width × SampleAspect</c>. Raised on open and whenever the stream changes size.
    /// </summary>
    public sealed record VideoGeometry(int Width, int Height, double SampleAspect = 1.0) : BackendEvent;

    /// <summary>The selectable tracks, and which of each kind is active (null: none).</summary>
    public sealed record TracksChanged(
        IReadOnlyList<MediaTrack> Audio, IReadOnlyList<MediaTrack> Subtitles,
        int? SelectedAudio, int? SelectedSubtitle) : BackendEvent;

    /// <summary>
    /// The chapter marks the media carries, in order, or empty for a file with none.
    /// Raised with the tracks when a file loads.
    /// </summary>
    public sealed record ChaptersChanged(IReadOnlyList<MediaChapterMark> Chapters) : BackendEvent;

    /// <summary>Buffering progress as a fraction, or null once playback has what it needs.</summary>
    public sealed record BufferingChanged(float? Fraction) : BackendEvent;

    /// <summary>Playback reached the end of the media.</summary>
    public sealed record EndReached : BackendEvent;

    /// <summary>The source could not be opened or played. Playback is over.</summary>
    public sealed record Failed(string Message) : BackendEvent;
}
