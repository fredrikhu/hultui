using SkiaSharp;

namespace VesperCommander;

/// <summary>
/// Decoding for the preview panel and the thumbnail grid, bounded by <i>pixels</i> rather
/// than by file size.
///
/// <para><b>Why.</b> Both callers used to gate on bytes on disk and then hand the whole file
/// to <c>SKBitmap.Decode</c>. Compressed image formats have no fixed ratio between the two: a
/// 30000×30000 PNG of one flat colour is 854 KB on disk and 900 MB decoded, and a 20000×20000
/// RGBA one is 1.5 MB and 1.6 GB. Neither comes near the 24 MB byte cap. The preview decodes
/// as soon as the cursor lands on the row and the grid decodes every visible tile at once, so
/// a folder of those files was several gigabytes of allocation for no user action beyond
/// arrowing through a directory.</para>
///
/// <para><b>How.</b> Open the codec first — that reads the header only — and ask it for the
/// dimensions. Where the format supports sampled decoding (JPEG, WebP) the image is decoded
/// straight to a size near what the caller will draw, so a 100-megapixel photo costs a couple
/// of megabytes and still gets a thumbnail. Where it doesn't (PNG, BMP, GIF) the full frame is
/// the only option, so anything past <see cref="MaxPixels"/> is refused and the caller falls
/// back to the file-kind icon, which both call sites already render for an undecodable
/// file.</para>
/// </summary>
public static class ImageDecode
{
    /// <summary>The most pixels we will materialize for one image: 64 megapixels, or 256 MB
    /// at the 4-bytes-per-pixel format everything is decoded into. Comfortably past any
    /// camera image the grid is likely to meet (a 45 MP full-frame frame is 8256×5504), and
    /// the ceiling applies to the <i>decoded</i> size — so a larger photo in a sampleable
    /// format is scaled down rather than refused.</summary>
    public const long MaxPixels = 64_000_000;

    /// <summary>Longest edge the preview panel decodes to. The panel is a few hundred points
    /// wide, so this is invisible even at high DPI, and it keeps a full-resolution bitmap of
    /// whatever the cursor happens to be sitting on out of memory.</summary>
    public const int PreviewTargetPx = 2048;

    /// <summary>Decode <paramref name="path"/> to roughly <paramref name="targetPx"/> on its
    /// longest edge, or null when it can't be decoded within the pixel budget.</summary>
    public static SKBitmap? Bounded(string path, int targetPx)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            return FromCodec(codec, targetPx);
        }
        catch { return null; }
    }

    /// <summary>The same, for bytes already in memory (an archive member).</summary>
    public static SKBitmap? Bounded(byte[] bytes, int targetPx)
    {
        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            return FromCodec(codec, targetPx);
        }
        catch { return null; }
    }

    private static SKBitmap? FromCodec(SKCodec? codec, int targetPx)
    {
        if (codec is null) return null;
        var size = DecodeSize(codec, targetPx);
        if (size.Width <= 0 || size.Height <= 0) return null;
        if ((long)size.Width * size.Height > MaxPixels) return null;   // a bomb, or simply too big

        // Bgra8888 whatever the source was: it is universally drawable, and it makes the
        // budget above mean a fixed number of bytes instead of one that depends on the
        // attacker's choice of colour type.
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap();
        if (!bitmap.TryAllocPixels(info)) { bitmap.Dispose(); return null; }

        var result = codec.GetPixels(info, bitmap.GetPixels());
        // A truncated file still gives us the rows that arrived, which is what the shell shows
        // for a partial download too.
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            return null;
        }
        return bitmap;
    }

    /// <summary>The size this image will actually be decoded at: the codec's own nearest
    /// supported sampling of the requested scale, or the full frame when the format can't
    /// sample (which is then subject to the budget).</summary>
    private static SKSizeI DecodeSize(SKCodec codec, int targetPx)
    {
        var src = codec.Info;
        if (src.Width <= 0 || src.Height <= 0) return SKSizeI.Empty;

        int longest = Math.Max(src.Width, src.Height);
        if (targetPx <= 0 || longest <= targetPx) return new SKSizeI(src.Width, src.Height);

        var scaled = codec.GetScaledDimensions(targetPx / (float)longest);
        return scaled.Width > 0 && scaled.Height > 0 ? scaled : new SKSizeI(src.Width, src.Height);
    }
}
