using HultUI.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace HultUI.Rendering.Skia;

/// <summary>
/// Shapes text with HarfBuzz, keeping one HarfBuzz font per typeface it has met.
///
/// <para>Each of those holds native memory and a stream over the font file that only
/// <see cref="Dispose"/> gives back: a finalizer never runs for it, because the stream is
/// reachable from a handle HarfBuzz holds until the font is destroyed. A shaper that is
/// dropped without being disposed leaks them.</para>
/// </summary>
public sealed class SkiaTextShaper : ITextShaper, IDisposable
{
    // Cache shapers per typeface to avoid re-creating HarfBuzz objects
    private readonly Dictionary<SKTypeface, SKShaper> _shaperCache = new();
    private bool _disposed;

    /// <summary>How many HarfBuzz fonts the shaper holds: one per typeface it has shaped.</summary>
    internal int CachedShaperCount => _shaperCache.Count;

    public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
    {
        if (string.IsNullOrEmpty(text))
            return new ShapedRun(text, font, fontSize, 0, []);

        var typeface = (SKTypeface)font.NativeHandle;
        if (_disposed)
        {
            // Still shapes, keeping nothing. A disposed shaper can be reached afterwards —
            // TextMeasure's ambient context outlives the application that installed it — and
            // shaping there must neither fail nor start leaking again.
            using var transient = new SKShaper(typeface);
            return Shape(transient, typeface, text, font, fontSize, rtl);
        }
        return Shape(GetOrCreateShaper(typeface), typeface, text, font, fontSize, rtl);
    }

    private static ShapedRun Shape(SKShaper shaper, SKTypeface typeface, string text, IResolvedFont font,
        float fontSize, bool rtl)
    {
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

    /// <summary>
    /// Destroys the HarfBuzz fonts the shaper holds. It can still shape afterwards, at the
    /// cost of building a HarfBuzz font for each call, and keeps nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var shaper in _shaperCache.Values)
            shaper.Dispose();
        _shaperCache.Clear();
    }
}
