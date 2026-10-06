using HultUI.Core.Primitives;

namespace HultUI.Core.Rendering;

/// <summary>
/// Byte layout of a <see cref="PixelFrame"/>: four bytes per pixel, except
/// <see cref="Gray8"/>, which is one. <see cref="PixelFormats.BytesPerPixel"/> says which.
/// </summary>
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
    /// <summary>
    /// One byte per pixel, a grey level from black (0) to white (255); opaque. What a
    /// monochrome image is after its grey-level mapping — a black-and-white photograph, a
    /// thermal camera frame, a scanned page. A quarter of the bytes of the four-byte formats,
    /// so a quarter of the upload on every draw, which is the point for a large image
    /// whose levels change interactively.
    /// </summary>
    Gray8,
}

/// <summary>Facts about a <see cref="PixelFormat"/>.</summary>
public static class PixelFormats
{
    /// <summary>Bytes one pixel occupies: one for <see cref="PixelFormat.Gray8"/>, four otherwise.</summary>
    public static int BytesPerPixel(this PixelFormat format) => format == PixelFormat.Gray8 ? 1 : 4;

    /// <summary>Whether the format has no alpha: the <c>x</c> formats and <see cref="PixelFormat.Gray8"/>.</summary>
    public static bool IsOpaque(this PixelFormat format) =>
        format is PixelFormat.Rgbx8888 or PixelFormat.Bgrx8888 or PixelFormat.Gray8;
}

/// <summary>
/// How an image's pixels are resampled when it is drawn at a size or angle other than its
/// own. The choice matters most for content whose individual pixels carry meaning: a
/// technical image magnified to inspect single pixels wants <see cref="Nearest"/>, the
/// same image fitted to a viewport wants <see cref="Cubic"/>.
/// </summary>
public enum ImageSampling : byte
{
    /// <summary>Whatever the renderer uses for the kind of source: smooth filtering, with
    /// mipmaps for a still image and without them for a frame source.</summary>
    Default,
    /// <summary>Point sampling: each destination pixel takes the nearest source pixel, so a
    /// magnified pixel is a hard-edged square.</summary>
    Nearest,
    /// <summary>Bilinear filtering, no mipmaps.</summary>
    Linear,
    /// <summary>Bicubic filtering (Catmull-Rom): sharper than bilinear when magnifying,
    /// without the blockiness of point sampling.</summary>
    Cubic,
}

/// <summary>
/// One borrowed frame of an <see cref="IPixelImageSource"/>: a pointer into memory the
/// source owns, valid from <see cref="IPixelImageSource.TryAcquire"/> until the matching
/// <see cref="IPixelImageSource.Release"/>.
/// </summary>
/// <param name="Pixels">First byte of the top-left pixel.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Stride">Bytes from one row to the next; at least <c>Width</c> times the
/// format's <see cref="PixelFormats.BytesPerPixel"/>.</param>
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
