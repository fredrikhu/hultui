using HultUI.Core.Rendering;

namespace HultUI.Media;

/// <summary>What a backend can do, so the player and its controls can hide what it cannot.</summary>
[Flags]
public enum BackendCapabilities
{
    None = 0,
    /// <summary>Delivers frames as pixels through the sink's <see cref="IVideoSink.Pixels"/>.</summary>
    PixelFrames = 1 << 0,
    /// <summary>Renders on the GPU through an <see cref="IGpuFrameRenderer"/> when the surface is GL.</summary>
    GpuFrames = 1 << 6,
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

/// <summary>How a backend's frames reach the screen, decided at the first draw.</summary>
public enum VideoSinkMode
{
    /// <summary>Nothing has been drawn yet, so the surface kind is not known.</summary>
    Undecided,
    /// <summary>The backend renders on the GPU into a framebuffer the sink owns (a GL surface).</summary>
    Gpu,
    /// <summary>The backend writes pixels into <see cref="IVideoSink.Pixels"/> (a raster or headless surface).</summary>
    Pixels,
}

/// <summary>
/// Where a backend puts its frames. Created by the player, handed to the backend once
/// in <see cref="IMediaBackend.Attach"/>. Two modes: pixels into
/// <see cref="Pixels"/> from any thread, which every backend can do; and, for a
/// backend that registers an <see cref="IGpuFrameRenderer"/>, rendering into a GPU
/// framebuffer when the surface is GL. Which one applies is only known once the
/// element is first drawn — the renderer hears about it through
/// <see cref="IGpuFrameRenderer.OnModeDecided"/>.
/// </summary>
public interface IVideoSink
{
    /// <summary>The frame ring the backend writes decoded frames into.</summary>
    PixelFrameSink Pixels { get; }

    /// <summary>
    /// Registers a GPU renderer. Call from <see cref="IMediaBackend.Attach"/>; the
    /// player then draws through a <see cref="GpuFrameSink"/> that calls the renderer on
    /// a GL surface and reads <see cref="Pixels"/> elsewhere.
    /// </summary>
    void UseGpuRenderer(IGpuFrameRenderer renderer);

    /// <summary>
    /// Any thread: a GPU renderer has a new frame ready and wants to be asked for it.
    /// (A pixel producer needs nothing here; publishing to <see cref="Pixels"/> asks.)
    /// Never blocks the caller and never touches the platform on the caller's thread,
    /// so it is safe from a callback the renderer's own <see cref="IGpuFrameRenderer.Render"/>
    /// waits on, such as mpv's update callback.
    /// </summary>
    void RequestRepaint();
}

/// <summary>
/// A backend that can draw its current frame into a GL framebuffer. Every method runs
/// on the render thread, inside the frame, with the GL context current.
/// </summary>
public interface IGpuFrameRenderer
{
    /// <summary>
    /// The first draw decided the mode. In <see cref="VideoSinkMode.Gpu"/> the
    /// renderer will be asked to <see cref="Render"/>; in <see cref="VideoSinkMode.Pixels"/>
    /// it must produce frames into the sink's pixel ring on its own.
    /// </summary>
    void OnModeDecided(VideoSinkMode mode);

    /// <summary>
    /// Called once, right after <see cref="OnModeDecided"/> reported
    /// <see cref="VideoSinkMode.Gpu"/>, inside the GL frame with the context current:
    /// the place to create whatever the renderer needs before any frame exists — a
    /// decoder's GL render context has to be there before it can load a file.
    /// </summary>
    void PrepareGL(Func<string, nint> getProcAddress);

    /// <summary>The size to render at, usually the video's coded size; false before it is known.</summary>
    bool TryGetFrameSize(out int width, out int height);

    /// <summary>True when the renderer fills the framebuffer bottom row first, GL style.</summary>
    bool RendersBottomUp { get; }

    /// <summary>
    /// Draws the current frame into <paramref name="framebuffer"/> (already sized
    /// <paramref name="width"/>×<paramref name="height"/>, not bound). Returns false when
    /// there is nothing to draw, in which case the last frame stays on screen.
    /// </summary>
    /// <param name="getProcAddress">Resolver for GL entry points in the current context.</param>
    /// <param name="target">The framebuffer and what the renderer wants in it: its internal
    /// format, whether it will be composited as linear light, and the display's peak. A
    /// producer that is not told any of this stays at eight encoded bits in a target that
    /// could carry sixteen and a range above white.</param>
    /// <param name="width">The target's width in pixels.</param>
    /// <param name="height">The target's height in pixels.</param>
    bool Render(Func<string, nint> getProcAddress, in GpuFrameTarget target, int width, int height);

    /// <summary>
    /// What the value 1.0 means in the frame just rendered, in nits, or zero for an ordinary
    /// encoded picture. Read after <see cref="Render"/> and passed on in
    /// <see cref="GpuFrame.ReferenceWhiteNits"/>.
    /// </summary>
    float ReferenceWhiteNits => 0f;

    /// <summary>Frees GL resources. Render thread, context current, before the sink deletes its own.</summary>
    void ReleaseGL();
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
