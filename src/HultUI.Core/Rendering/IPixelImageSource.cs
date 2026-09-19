using HultUI.Core.Primitives;

namespace HultUI.Core.Rendering;

/// <summary>Byte layout of a <see cref="PixelFrame"/>, four bytes per pixel.</summary>
public enum PixelFormat : byte
{
    /// <summary>R, G, B, A in memory order; alpha premultiplied.</summary>
    Rgba8888,
    /// <summary>B, G, R, A in memory order; alpha premultiplied.</summary>
    Bgra8888,
    /// <summary>R, G, B, then a byte the renderer ignores — the frame is opaque. What
    /// a decoder emitting <c>rgb0</c> produces, where that byte is undefined.</summary>
    Rgbx8888,
    /// <summary>B, G, R, then an ignored byte; opaque. A decoder's <c>bgr0</c>.</summary>
    Bgrx8888,
}

/// <summary>
/// One borrowed frame of an <see cref="IPixelImageSource"/>: a pointer into memory the
/// source owns, valid from <see cref="IPixelImageSource.TryAcquire"/> until the matching
/// <see cref="IPixelImageSource.Release"/>.
/// </summary>
/// <param name="Pixels">First byte of the top-left pixel.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Stride">Bytes from one row to the next; at least <c>Width * 4</c>.</param>
/// <param name="Format">Byte layout.</param>
/// <param name="Slot">Opaque to the renderer: whatever the source needs to know which
/// buffer is being handed back, typically an index into its ring.</param>
public readonly record struct PixelFrame(
    nint Pixels, int Width, int Height, int Stride, PixelFormat Format, int Slot);

/// <summary>
/// An image whose pixels change over time — a video frame, a live camera, a
/// software-rendered view — produced on any thread and drawn without copying.
/// </summary>
/// <remarks>
/// <para><b>The borrow.</b> The renderer calls <see cref="TryAcquire"/> when it draws
/// the image and <see cref="Release"/> only at the end of the frame, after its
/// drawing has been flushed. That is later than the draw call on purpose: a GPU
/// backend uploads the bytes at flush, not at the call, so the source must keep the
/// borrowed buffer untouched until the release or the frame on screen would be torn.
/// A producer that wants to write while a frame is borrowed keeps more than one
/// buffer; three is enough (one borrowed, one being written, one just published).</para>
///
/// <para><b>Threads.</b> <see cref="TryAcquire"/> and <see cref="Release"/> are
/// called on the UI thread. The producer publishes from wherever it decodes, then
/// marks the drawing element paint-dirty on the UI thread (and layout-dirty when the
/// size changed, since <see cref="IImageSource.IntrinsicSize"/> is the frame size).</para>
///
/// <para><b>Lifetime.</b> A source may be disposed while a frame is borrowed — the
/// element is removed while a frame is on screen — and its <see cref="Release"/> must
/// then still be safe to call; it must not free the borrowed buffer before that call.</para>
///
/// <para><b>Cost.</b> The renderer wraps the borrowed bytes as an image for the one
/// frame and lets the wrapper go at release, so every draw uploads the frame again.
/// That is what a playing video needs anyway; a paused one repainting behind a
/// tooltip pays about a millisecond per 1080p frame. Content that changes rarely
/// belongs in a static image source.</para>
/// </remarks>
public interface IPixelImageSource : IImageSource
{
    /// <summary>
    /// Incremented by the producer on every new frame, never reset. The renderer does
    /// not need it (it re-reads on every draw); it is recorded in the headless render
    /// log so a test can assert that a given frame was painted.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// Borrows the current frame. Returns false when there is none yet — the
    /// renderer then draws nothing. A true result must be balanced by exactly one
    /// <see cref="Release"/> of the same frame.
    /// </summary>
    bool TryAcquire(out PixelFrame frame);

    /// <summary>Hands a borrowed frame back; its buffer may be reused after this returns.</summary>
    void Release(in PixelFrame frame);
}
