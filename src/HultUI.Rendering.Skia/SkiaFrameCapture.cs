using SkiaSharp;

namespace HultUI.Rendering.Skia;

/// <summary>
/// A surface that can hand over the frame it presents: what <c>Window.SaveFrame</c> writes.
///
/// <para>Read from the surface HultUI drew into rather than from the screen, so what comes back
/// is the whole frame at device pixels whatever covers the window, and never half of one: a
/// screen grab or <c>PrintWindow</c> right after a maximize on a 150 % monitor came back half
/// drawn or at half scale. Diagnostic: every capture waits for the GPU and copies the frame.</para>
/// </summary>
public interface ISkiaFrameCapture
{
    /// <summary>
    /// Presents the frame, as <see cref="Core.Rendering.IRenderSurface.Present"/> does, and returns
    /// it as a raster image, <c>Rgba8888</c> premultiplied sRGB at device pixels, that any thread
    /// may read. The caller owns the image. The frame is presented whether or not it can be read;
    /// when it cannot, this throws after presenting.
    /// </summary>
    SKImage PresentCapturing();
}

internal static class SkiaFrameCapture
{
    /// <summary>
    /// The top-left <paramref name="width"/> × <paramref name="height"/> of
    /// <paramref name="surface"/> as a raster image of its own. Converted to sRGB on the way, so a
    /// linear offscreen comes out as it is shown.
    /// </summary>
    public static SKImage Copy(SKSurface surface, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap(info);
        try
        {
            if (!surface.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
                throw new InvalidOperationException($"The frame ({width}x{height}) could not be read back.");
            bitmap.SetImmutable();
            // Shares the immutable pixels, which outlive the managed bitmap.
            return SKImage.FromBitmap(bitmap);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>Opaque 8-bit sRGB RGBA, top row first, as a raster image.</summary>
    public static SKImage FromRgba(byte[] rgba, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque, SKColorSpace.CreateSrgb());
        using var data = SKData.CreateCopy(rgba);
        return SKImage.FromPixels(info, data, width * 4)
            ?? throw new InvalidOperationException("The frame could not be made an image.");
    }
}
