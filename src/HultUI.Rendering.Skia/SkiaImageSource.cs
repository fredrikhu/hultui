using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

public sealed class SkiaImageSource : IImageSource, IDisposable
{
    private SKBitmap? _bitmap;

    public Size IntrinsicSize => _bitmap is not null
        ? new Size(_bitmap.Width, _bitmap.Height)
        : Size.Zero;

    public bool IsLoaded => _bitmap is not null;
    public string? AlternateText { get; set; }

    internal SKBitmap? Bitmap => _bitmap;

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

    public static SkiaImageSource FromBytes(ReadOnlySpan<byte> data, string? alternateText = null)
    {
        var bitmap = SKBitmap.Decode(data)
            ?? throw new InvalidOperationException("Failed to decode image from bytes.");
        return new SkiaImageSource(bitmap, alternateText);
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
