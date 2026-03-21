using HultUI.Core.Styling;
using HultUI.Text;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

public sealed class SkiaFontCollection : IFontCollection
{
    private readonly SKFontManager _fontManager = SKFontManager.Default;
    private readonly Dictionary<FontKey, SkiaResolvedFont> _fontCache = new();
    private readonly Dictionary<int, SkiaResolvedFont?> _fallbackCache = new();

    public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant)
    {
        var key = new FontKey(familyName, weight, slant);
        if (_fontCache.TryGetValue(key, out var cached))
            return cached;

        var typeface = SKTypeface.FromFamilyName(familyName, ToSkWeight(weight), SKFontStyleWidth.Normal, ToSkSlant(slant))
            ?? SKTypeface.Default;

        var resolved = new SkiaResolvedFont(typeface);
        _fontCache[key] = resolved;
        return resolved;
    }

    public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant)
    {
        if (_fallbackCache.TryGetValue(codepoint, out var cached))
            return cached;

        var typeface = _fontManager.MatchCharacter(codepoint);
        if (typeface is null)
        {
            _fallbackCache[codepoint] = null;
            return null;
        }

        var resolved = new SkiaResolvedFont(typeface);
        _fallbackCache[codepoint] = resolved;
        return resolved;
    }

    private static SKFontStyleWeight ToSkWeight(FontWeight weight) => weight switch
    {
        FontWeight.Thin => SKFontStyleWeight.Thin,
        FontWeight.Light => SKFontStyleWeight.Light,
        FontWeight.Regular => SKFontStyleWeight.Normal,
        FontWeight.Medium => SKFontStyleWeight.Medium,
        FontWeight.SemiBold => SKFontStyleWeight.SemiBold,
        FontWeight.Bold => SKFontStyleWeight.Bold,
        FontWeight.ExtraBold => SKFontStyleWeight.ExtraBold,
        FontWeight.Black => SKFontStyleWeight.Black,
        _ => SKFontStyleWeight.Normal
    };

    private static SKFontStyleSlant ToSkSlant(FontSlant slant) => slant switch
    {
        FontSlant.Italic => SKFontStyleSlant.Italic,
        FontSlant.Oblique => SKFontStyleSlant.Oblique,
        _ => SKFontStyleSlant.Upright
    };

    private readonly record struct FontKey(string Family, FontWeight Weight, FontSlant Slant);
}

internal sealed class SkiaResolvedFont : IResolvedFont
{
    private readonly SKTypeface _typeface;
    private readonly SKFont _font;
    private bool _metricsComputed;
    private float _ascent, _descent, _leading;

    public string FamilyName => _typeface.FamilyName;
    public object NativeHandle => _typeface;

    public float Ascent { get { EnsureMetrics(); return _ascent; } }
    public float Descent { get { EnsureMetrics(); return _descent; } }
    public float Leading { get { EnsureMetrics(); return _leading; } }
    public float LineHeight => Math.Abs(Ascent) + Descent + Leading;

    public SkiaResolvedFont(SKTypeface typeface)
    {
        _typeface = typeface;
        // Use a reference size for metrics — they'll be scaled by fontSize/referenceFontSize
        _font = new SKFont(typeface, 1f);
    }

    public bool HasGlyph(int codepoint)
    {
        return _typeface.GetGlyph(codepoint) != 0;
    }

    private void EnsureMetrics()
    {
        if (_metricsComputed) return;
        var metrics = _font.Metrics;
        _ascent = metrics.Ascent;    // negative in Skia (above baseline)
        _descent = metrics.Descent;  // positive (below baseline)
        _leading = metrics.Leading;  // positive
        _metricsComputed = true;
    }

    public void Dispose()
    {
        _font.Dispose();
        // Don't dispose _typeface — it may be cached/shared
    }
}
