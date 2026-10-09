namespace HultUI.Core.Rendering;

// Moved from HultUI.Media when the web view became a second producer: the sink and its
// renderer contract know nothing about video.

/// <summary>How a GPU renderer's frames reach the screen, decided at the first draw.</summary>
public enum GpuSinkMode
{
    /// <summary>Nothing has been drawn yet, so the surface kind is not known.</summary>
    Undecided,
    /// <summary>The producer renders on the GPU into the sink's or the surface's framebuffer (a surface that runs external GL).</summary>
    Gpu,
    /// <summary>The producer writes pixels into the sink's <see cref="PixelFrameSink"/> (a raster or headless surface).</summary>
    Pixels,
}

/// <summary>
/// A producer that can draw its current frame into a GL framebuffer — a video decoder, a
/// browser engine — for a <see cref="GpuFrameSink"/> to composite. Every method runs
/// on the render thread, inside the frame, with the GL context current.
/// </summary>
public interface IGpuFrameRenderer
{
    /// <summary>
    /// The first draw decided the mode. In <see cref="GpuSinkMode.Gpu"/> the
    /// renderer will be asked to <see cref="Render"/>; in <see cref="GpuSinkMode.Pixels"/>
    /// it must produce frames into the sink's pixel ring on its own.
    /// </summary>
    void OnModeDecided(GpuSinkMode mode);

    /// <summary>
    /// Called once, right after <see cref="OnModeDecided"/> reported
    /// <see cref="GpuSinkMode.Gpu"/>, inside the GL frame with the context current:
    /// the place to create whatever the renderer needs before any frame exists — a
    /// decoder's GL render context has to be there before it can load a file.
    /// </summary>
    void PrepareGL(Func<string, nint> getProcAddress);

    /// <summary>The size to render at, usually the video's coded size; false before it is known.</summary>
    bool TryGetFrameSize(out int width, out int height);

    /// <summary>True when the renderer fills the framebuffer bottom row first, GL style.</summary>
    bool RendersBottomUp { get; }

    /// <summary>
    /// Draws the current frame into the framebuffer of <paramref name="target"/> (already sized
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

    /// <summary>
    /// True when a frame may be waiting to be taken. The sink asks when it cannot draw one:
    /// while <see cref="TryGetFrameSize"/> is still false, and while the window is not being
    /// drawn (minimized). It then enters the GL context to <see cref="SkipFrame"/>, which it otherwise
    /// avoids for a frame it cannot draw, because on a GL surface entering costs a flush.
    /// </summary>
    bool FrameMayBeWaiting => false;

    /// <summary>
    /// Takes a waiting frame without drawing it, on a frame where <see cref="TryGetFrameSize"/>
    /// is false. This is for a producer that holds everything until its frame is taken, and
    /// learns its size only after that. mpv holds its core for up to 200 ms after every load,
    /// and the video's size can only be read from the core. The next <see cref="Render"/> that
    /// has a target draws the frame taken here. Also every frame of a window that is not being
    /// drawn (<see cref="IGpuImageSource.ServiceUndrawn"/>), where mpv would otherwise wait out
    /// those 200 ms for each frame and drop it.
    /// </summary>
    void SkipFrame(Func<string, nint> getProcAddress) { }

    /// <summary>Frees GL resources. Render thread, context current, before the sink deletes its own.</summary>
    void ReleaseGL();
}
