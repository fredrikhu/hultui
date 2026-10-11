namespace HultUI.Rendering.Skia;

/// <summary>
/// When a GPU surface frees the textures of images disposed since it last looked, kept apart
/// from <see cref="SkiaVulkanRenderSurface"/> so it can be tested.
///
/// <para><b>What is freed.</b> A Skia GPU context keeps the texture it uploaded for an image after
/// the image is gone: the upload loses its key and stays as a spare for a later texture of
/// exactly its size, until the context's 256 MB budget pushes it out. Eight 3840×2400 photos
/// drawn and then disposed left 176 MB of such spares; DXGI read 391.5 MB, and 213.2 once they
/// were purged. Only spares go (Skia's scratch purge): the texture of an image that is still alive
/// keeps its key, so a poster scrolled out of view and back in is not uploaded again.</para>
///
/// <para><b>At most once a second.</b> A poster grid that evicts as it scrolls disposes images
/// on most frames, and each purge also drops the context's other spares (layer textures,
/// vertex buffers), which the next frames make again. A disposal inside the interval waits for
/// the first frame after it; one in a window that draws nothing more waits for its next
/// frame.</para>
/// </summary>
internal sealed class DisposedImagePurge
{
    /// <summary>The shortest time between two purges.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    private int _seen;
    private long _lastPurge;
    private bool _purgedOnce;

    /// <param name="disposals">The disposal count as of construction: what was disposed before
    /// this surface existed has nothing in its context.</param>
    public DisposedImagePurge(int disposals) => _seen = disposals;

    /// <summary>Whether images have been disposed since the last purge: the per-frame question,
    /// asked before the clock is read.</summary>
    /// <param name="disposals">The process's count of drawn images disposed so far
    /// (<see cref="SkiaImageSource.DrawnDisposals"/>).</param>
    public bool HasNew(int disposals) => disposals != _seen;

    /// <summary>Whether a purge is due after this frame.</summary>
    /// <param name="disposals">As for <see cref="HasNew"/>.</param>
    /// <param name="now">A <see cref="System.Diagnostics.Stopwatch"/> timestamp.</param>
    public bool IsDue(int disposals, long now) =>
        HasNew(disposals)
        && (!_purgedOnce || now - _lastPurge >= MinInterval.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);

    /// <summary>Records a purge that freed everything disposed up to <paramref name="disposals"/>.</summary>
    public void Purged(int disposals, long now)
    {
        _seen = disposals;
        _lastPurge = now;
        _purgedOnce = true;
    }
}
