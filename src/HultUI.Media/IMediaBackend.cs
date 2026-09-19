using HultUI.Core.Rendering;

namespace HultUI.Media;

/// <summary>What a backend can do, so the player and its controls can hide what it cannot.</summary>
[Flags]
public enum BackendCapabilities
{
    None = 0,
    /// <summary>Delivers frames as pixels through the sink's <see cref="IVideoSink.Pixels"/>.</summary>
    PixelFrames = 1 << 0,
    /// <summary>Opens <see cref="MediaSourceKind.Uri"/> sources.</summary>
    Uris = 1 << 1,
    /// <summary>Opens <see cref="MediaSourceKind.Stream"/> sources.</summary>
    Streams = 1 << 2,
    /// <summary>Honours <see cref="IMediaBackend.SetRate"/>.</summary>
    Rate = 1 << 3,
    /// <summary>Renders subtitle tracks into the frame.</summary>
    Subtitles = 1 << 4,
    /// <summary>Honours a precise (non-keyframe) seek.</summary>
    PreciseSeek = 1 << 5,
}

/// <summary>
/// Where a backend puts its frames. Created by the player, handed to the backend once
/// in <see cref="IMediaBackend.Attach"/>. Today one mode: pixels into a
/// <see cref="PixelFrameSink"/> from any thread. A GPU mode arrives with the external
/// GL seam and will sit beside it here.
/// </summary>
public interface IVideoSink
{
    /// <summary>The frame ring the backend writes decoded frames into.</summary>
    PixelFrameSink Pixels { get; }
}

/// <summary>
/// The contract a decoder package implements. Deliberately narrow: it is what the
/// player and its controls can show. Commands come on the UI thread; events may come
/// from anywhere and the backend never touches elements or reactive state.
/// </summary>
public interface IMediaBackend : IDisposable
{
    BackendCapabilities Capabilities { get; }

    /// <summary>Called once, before <see cref="Open"/>, with the sink to deliver frames to.</summary>
    void Attach(IVideoSink sink);

    /// <summary>
    /// Opens a source, replacing whatever was open. The backend reports progress
    /// through events: <see cref="BackendEvent.StateChanged"/> to
    /// <see cref="PlaybackState.Opening"/>, then duration, geometry and tracks as
    /// they become known, then <see cref="PlaybackState.Paused"/> (or
    /// <see cref="BackendEvent.Failed"/>). It does not start playing.
    /// </summary>
    void Open(MediaSource source);

    void Play();
    void Pause();

    /// <summary>Stops and rewinds to the start, keeping the media open.</summary>
    void Stop();

    /// <param name="position">Target position, clamped by the backend to the media.</param>
    /// <param name="precise">Land on the exact frame rather than the nearest keyframe;
    /// slower, and ignored by a backend without <see cref="BackendCapabilities.PreciseSeek"/>.</param>
    void Seek(TimeSpan position, bool precise);

    /// <param name="rate">Playback speed; 1.0 is normal. Pitch is preserved where the backend can.</param>
    void SetRate(double rate);

    /// <param name="volume">0 to 1, linear.</param>
    void SetVolume(float volume);
    void SetMuted(bool muted);

    /// <param name="id">A <see cref="MediaTrack.Id"/> from the last
    /// <see cref="BackendEvent.TracksChanged"/>, or null for none.</param>
    void SelectAudioTrack(int? id);
    void SelectSubtitleTrack(int? id);

    /// <summary>Raised on any thread. The player marshals.</summary>
    event Action<BackendEvent>? Event;
}
