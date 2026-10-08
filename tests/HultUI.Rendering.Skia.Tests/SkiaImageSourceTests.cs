using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A <see cref="SkiaImageSource"/> holds its picture once. <c>SKImage.FromBitmap</c> copies
/// a mutable bitmap's pixels, and every way an app makes a bitmap — decoding, a canvas,
/// raw pixels — makes a mutable one, so before the source marked the bitmap immutable each
/// image was held twice once drawn (35 MB more for a 3840×2400 photo).
/// </summary>
public class SkiaImageSourceTests
{
    /// <summary>The ways the apps make a source: FromBytes/FromFile (Junction's diff,
    /// SKBitmap.Decode), a bitmap drawn on a canvas (the icon sets), FromPixels (the
    /// terminal).</summary>
    private static SkiaImageSource Make(string how) => how switch
    {
        "decoded" => SkiaImageSource.FromBytes(Png(Quadrants())),
        "canvas" => new SkiaImageSource(Quadrants()),
        "pixels" => FromPixels(),
        _ => throw new ArgumentOutOfRangeException(nameof(how)),
    };

    [Theory]
    [InlineData("decoded")]
    [InlineData("canvas")]
    [InlineData("pixels")]
    public void TheImageDrawsFromTheBitmapsOwnPixels_NotACopy(string how)
    {
        using var source = Make(how);
        var bitmap = source.Bitmap!;
        var image = source.Image!;

        using var pixmap = image.PeekPixels();
        Assert.NotNull(pixmap);
        Assert.Equal(bitmap.GetPixels(), pixmap.GetPixels());
        Assert.True(bitmap.IsImmutable);
    }

    [Theory]
    [InlineData("decoded")]
    [InlineData("canvas")]
    [InlineData("pixels")]
    public void TheSharedPixelsStillDraw(string how)
    {
        using var source = Make(how);
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(100, 100, 1f);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        ctx.DrawImage(source, new Rect(0, 0, 64, 64), ImageFit.Fill);
        ctx.EndFrame();
        surface.Present();

        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        AssertPixel(pixels, rowBytes, 16, 16, 255, 0, 0);
        AssertPixel(pixels, rowBytes, 48, 16, 0, 255, 0);
        AssertPixel(pixels, rowBytes, 16, 48, 0, 0, 255);
        AssertPixel(pixels, rowBytes, 48, 48, 255, 255, 0);
        AssertPixel(pixels, rowBytes, 80, 80, 255, 255, 255);
    }

    [Fact]
    public void TheBitmapStaysReadableUntilTheSourceIsDisposed()
    {
        var bitmap = Quadrants();
        var source = new SkiaImageSource(bitmap);
        Assert.NotNull(source.Image);

        // The caller's reference is still a live bitmap: reading it is part of the contract.
        Assert.Equal(64, bitmap.Width);
        Assert.Equal(new SKColor(0, 255, 0), bitmap.GetPixel(48, 16));

        source.Dispose();
        Assert.Equal(IntPtr.Zero, bitmap.Handle);
        Assert.False(source.IsLoaded);
    }

    /// <summary>Red, green / blue, yellow quadrants, 64 pixels square.</summary>
    private static SKBitmap Quadrants()
    {
        var bitmap = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint();
        paint.Color = new SKColor(255, 0, 0); canvas.DrawRect(0, 0, 32, 32, paint);
        paint.Color = new SKColor(0, 255, 0); canvas.DrawRect(32, 0, 32, 32, paint);
        paint.Color = new SKColor(0, 0, 255); canvas.DrawRect(0, 32, 32, 32, paint);
        paint.Color = new SKColor(255, 255, 0); canvas.DrawRect(32, 32, 32, 32, paint);
        return bitmap;
    }

    private static byte[] Png(SKBitmap bitmap)
    {
        using (bitmap)
        using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
            return data.ToArray();
    }

    private static SkiaImageSource FromPixels()
    {
        using var bitmap = Quadrants();
        return SkiaImageSource.FromPixels(bitmap.GetPixelSpan(), 64, 64, bitmap.RowBytes, PixelFormat.Rgba8888);
    }

    private static void AssertPixel(byte[] pixels, int rowBytes, int x, int y, byte r, byte g, byte b)
    {
        int i = y * rowBytes + x * 4;
        Assert.True(pixels[i] == r && pixels[i + 1] == g && pixels[i + 2] == b,
            $"pixel ({x},{y}) was ({pixels[i]},{pixels[i + 1]},{pixels[i + 2]}), expected ({r},{g},{b})");
    }
}
