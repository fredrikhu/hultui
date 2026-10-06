using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

/// <summary>
/// A still image for the Skia renderer: a decoded or drawn <see cref="SKBitmap"/>.
///
/// <para><b>The source takes the bitmap over.</b> The constructor marks it immutable
/// (<see cref="SKBitmap.SetImmutable"/>), the image it draws from shares its pixels, and
/// <see cref="Dispose"/> disposes it. So finish drawing into the bitmap before you hand it
/// over, and afterwards neither change its pixels nor dispose it yourself. Reading it (its
/// size, its pixels, encoding it) stays fine. A change made after the hand-over is not
/// supported: a CPU surface may draw it, torn if it lands mid-frame, while a GPU window
/// keeps the pixels it uploaded first. To keep drawing into a bitmap, hand over a copy
/// (<see cref="SKBitmap.Copy()"/>). Pixels you installed yourself (<c>InstallPixels</c>)
/// must stay valid until the source is disposed.</para>
///
/// <para>Why: <see cref="SKImage.FromBitmap"/> copies a mutable bitmap's pixels, and
/// <c>SKBitmap.Decode</c> and <c>new SKBitmap(...)</c> both make mutable ones. Left
/// mutable, every image would be held twice once drawn: 35 MB more for a 3840×2400 photo,
/// and twice the pixels of every poster in Aperture's cache.</para>
/// </summary>
public sealed class SkiaImageSource : IImageSource, IDisposable
{
    private SKBitmap? _bitmap;
    private SKImage? _image;

    public Size IntrinsicSize => _bitmap is not null
        ? new Size(_bitmap.Width, _bitmap.Height)
        : Size.Zero;

    public bool IsLoaded => _bitmap is not null;
    public string? AlternateText { get; set; }

    internal SKBitmap? Bitmap => _bitmap;

    /// <summary>
    /// The bitmap as an <see cref="SKImage"/>, created once on first draw. It shares the
    /// bitmap's pixels rather than copying them, because the bitmap is immutable. Sampling
    /// options — which is what stops a downscaled image being point-sampled — are only
    /// available on the SKImage draw overloads in SkiaSharp 3.
    /// </summary>
    internal SKImage? Image => _image ??= _bitmap is null ? null : SKImage.FromBitmap(_bitmap);

    /// <summary>
    /// Takes <paramref name="bitmap"/> over: marks it immutable and disposes it with the
    /// source. Do not draw into it, change its pixels or dispose it afterwards; see the
    /// type's remarks.
    /// </summary>
    public SkiaImageSource(SKBitmap bitmap, string? alternateText = null)
    {
        _bitmap = bitmap;
        // Without this, SKImage.FromBitmap copies the pixels on first draw and the image is
        // held twice for as long as the source lives.
        _bitmap?.SetImmutable();
        AlternateText = alternateText;
    }

    public static SkiaImageSource FromFile(string path, string? alternateText = null)
    {
        var bitmap = SKBitmap.Decode(path)
            ?? throw new InvalidOperationException($"Failed to decode image: {path}");
        return new SkiaImageSource(bitmap, alternateText);
    }

    /// <summary>
    /// A copy of raw pixels — four bytes each, or one for <see cref="PixelFormat.Gray8"/>: content
    /// made in memory rather than decoded from a file, such as a terminal's pictures or a
    /// thumbnail of a monochrome image. Drawn as a still image, uploaded once (an
    /// <see cref="IPixelImageSource"/> is uploaded again on every draw, for content that changes).
    /// </summary>
    /// <param name="pixels">The pixels, top row first.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="stride">Bytes from one row's start to the next's.</param>
    /// <param name="format">The byte order; the <c>x</c> formats and Gray8 are opaque.</param>
    /// <param name="straightAlpha">The colour is not multiplied by alpha, as Sixel and PNG
    /// decoders leave it; <see cref="PixelFormat"/>'s alpha formats are otherwise premultiplied.</param>
    /// <param name="alternateText">What the image shows, for assistive technology.</param>
    public static SkiaImageSource FromPixels(ReadOnlySpan<byte> pixels, int width, int height, int stride,
        PixelFormat format, bool straightAlpha = false, string? alternateText = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int row = width * format.BytesPerPixel();
        if (stride < row) throw new ArgumentOutOfRangeException(nameof(stride), stride, $"A row holds {row} bytes.");
        if (pixels.Length < (long)stride * (height - 1) + row)
            throw new ArgumentException($"{pixels.Length} bytes are not {width} x {height} pixels at stride {stride}.", nameof(pixels));

        var colorType = format switch
        {
            PixelFormat.Rgba8888 or PixelFormat.Rgbx8888 => SKColorType.Rgba8888,
            PixelFormat.Gray8 => SKColorType.Gray8,
            _ => SKColorType.Bgra8888,
        };
        var alphaType = format.IsOpaque() ? SKAlphaType.Opaque
            : straightAlpha ? SKAlphaType.Unpremul : SKAlphaType.Premul;
        var bitmap = new SKBitmap(new SKImageInfo(width, height, colorType, alphaType));
        var target = bitmap.GetPixelSpan();
        int rowBytes = bitmap.RowBytes;
        for (int y = 0; y < height; y++)
            pixels.Slice(y * stride, row).CopyTo(target.Slice(y * rowBytes, row));
        return new SkiaImageSource(bitmap, alternateText);
    }

    public static SkiaImageSource FromBytes(ReadOnlySpan<byte> data, string? alternateText = null)
    {
        var bitmap = SKBitmap.Decode(data)
            ?? throw new InvalidOperationException("Failed to decode image from bytes.");
        return new SkiaImageSource(bitmap, alternateText);
    }

    /// <summary>
    /// How many sources have been disposed after being drawn, in this process. The Vulkan
    /// surface reads it after each frame: a GPU context keeps the texture it uploaded for an
    /// image after the image is gone, as a spare for a later texture of exactly that size, and
    /// the surface frees those spares when this has moved (see
    /// <see cref="SkiaVulkanRenderSurface"/>). A source never drawn uploaded nothing.
    /// </summary>
    internal static int DrawnDisposals => Volatile.Read(ref s_drawnDisposals);
    private static int s_drawnDisposals;

    public void Dispose()
    {
        // The image is made on first draw, so it says whether anything was uploaded. Any
        // thread may dispose a source, hence the interlocked count.
        if (_image is not null) Interlocked.Increment(ref s_drawnDisposals);
        _image?.Dispose();
        _image = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
