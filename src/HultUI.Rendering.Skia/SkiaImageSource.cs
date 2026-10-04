using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

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
    /// The bitmap as an <see cref="SKImage"/>, created once on first draw. Sampling
    /// options — which is what stops a downscaled image being point-sampled — are only
    /// available on the SKImage draw overloads in SkiaSharp 3.
    /// </summary>
    internal SKImage? Image => _image ??= _bitmap is null ? null : SKImage.FromBitmap(_bitmap);

    public SkiaImageSource(SKBitmap bitmap, string? alternateText = null)
    {
        _bitmap = bitmap;
        AlternateText = alternateText;
    }

    public static SkiaImageSource FromFile(string path, string? alternateText = null)
    {
        var bitmap = SKBitmap.Decode(path)
            ?? throw new InvalidOperationException($"Failed to decode image: {path}");
        return new SkiaImageSource(bitmap, alternateText);
    }

    /// <summary>
    /// A copy of raw pixels, four bytes each: content made in memory rather than decoded from a
    /// file, such as a terminal's pictures. Drawn as a still image, uploaded once (an
    /// <see cref="IPixelImageSource"/> is uploaded again on every draw, for content that changes).
    /// </summary>
    /// <param name="pixels">The pixels, top row first.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="stride">Bytes from one row's start to the next's.</param>
    /// <param name="format">The byte order; the <c>x</c> formats are opaque.</param>
    /// <param name="straightAlpha">The colour is not multiplied by alpha, as Sixel and PNG
    /// decoders leave it; <see cref="PixelFormat"/>'s alpha formats are otherwise premultiplied.</param>
    /// <param name="alternateText">What the image shows, for assistive technology.</param>
    public static SkiaImageSource FromPixels(ReadOnlySpan<byte> pixels, int width, int height, int stride,
        PixelFormat format, bool straightAlpha = false, string? alternateText = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (stride < width * 4) throw new ArgumentOutOfRangeException(nameof(stride), stride, "A row holds four bytes a pixel.");
        if (pixels.Length < (long)stride * (height - 1) + width * 4)
            throw new ArgumentException($"{pixels.Length} bytes are not {width} x {height} pixels at stride {stride}.", nameof(pixels));

        var colorType = format is PixelFormat.Rgba8888 or PixelFormat.Rgbx8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
        var alphaType = format is PixelFormat.Rgbx8888 or PixelFormat.Bgrx8888 ? SKAlphaType.Opaque
            : straightAlpha ? SKAlphaType.Unpremul : SKAlphaType.Premul;
        var bitmap = new SKBitmap(new SKImageInfo(width, height, colorType, alphaType));
        var target = bitmap.GetPixelSpan();
        int row = width * 4, rowBytes = bitmap.RowBytes;
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

    public void Dispose()
    {
        _image?.Dispose();
        _image = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
