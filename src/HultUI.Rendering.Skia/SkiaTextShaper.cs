using HultUI.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace HultUI.Rendering.Skia;

public sealed class SkiaTextShaper : ITextShaper
{
    // Cache shapers per typeface to avoid re-creating HarfBuzz objects
    private readonly Dictionary<SKTypeface, SKShaper> _shaperCache = new();

    public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
    {
        if (string.IsNullOrEmpty(text))
            return new ShapedRun(text, font, fontSize, 0, []);

        var typeface = (SKTypeface)font.NativeHandle;
        var shaper = GetOrCreateShaper(typeface);

        using var paint = new SKPaint();
        using var skFont = new SKFont(typeface, fontSize);

        SKShaper.Result result;
        if (rtl)
        {
            // Direction set explicitly, not guessed: guessing is per-buffer, and a
            // neutral-heavy RTL run (punctuation, digits) guesses LTR and comes out
            // backwards. The script is still guessed — that part is per-content.
            using var buffer = new HarfBuzzSharp.Buffer();
            buffer.AddUtf16(text);
            buffer.GuessSegmentProperties();
            buffer.Direction = HarfBuzzSharp.Direction.RightToLeft;
            result = shaper.Shape(buffer, 0, 0, skFont);
        }
        else
        {
            result = shaper.Shape(text, skFont);
        }

        // Convert SkiaSharp.HarfBuzz result to our GlyphPosition array
        var glyphs = new GlyphPosition[result.Codepoints.Length];
        for (int i = 0; i < result.Codepoints.Length; i++)
        {
            float advance = i < result.Codepoints.Length - 1
                ? result.Points[i + 1].X - result.Points[i].X
                : result.Width - result.Points[i].X;

            glyphs[i] = new GlyphPosition(
                GlyphId: result.Codepoints[i],
                X: result.Points[i].X,
                Y: result.Points[i].Y,
                Advance: advance);
        }

        return new ShapedRun(text, font, fontSize, result.Width, glyphs);
    }

    private SKShaper GetOrCreateShaper(SKTypeface typeface)
    {
        if (_shaperCache.TryGetValue(typeface, out var cached))
            return cached;

        var shaper = new SKShaper(typeface);
        _shaperCache[typeface] = shaper;
        return shaper;
    }
}
