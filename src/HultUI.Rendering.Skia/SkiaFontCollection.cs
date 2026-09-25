using HultUI.Core.Styling;
using HultUI.Text;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

public sealed class SkiaFontCollection : IFontCollection
{
    private readonly SKFontManager _fontManager = SKFontManager.Default;
    private readonly Dictionary<FontKey, SkiaResolvedFont> _fontCache = new();
    private readonly Dictionary<FallbackKey, SkiaResolvedFont?> _fallbackCache = new();

    // One wrapper per fallback typeface, not per codepoint. FontItemizer splits runs on
    // font identity, so a wrapper per character made every fallback letter its own run —
    // Hebrew shaped a letter at a time on any system whose primary font lacks it, which
    // would also break Arabic joining. Skia returns the same SKTypeface instance for the
    // same face, so reference identity is the right key.
    private readonly Dictionary<SKTypeface, SkiaResolvedFont> _fallbackFonts = new(ReferenceEqualityComparer.Instance);

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
        => MatchCharacter(codepoint, weight, slant, hanContext: null);

    public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant, string? hanContext)
    {
        // The language is part of the key because it can change under a codepoint: Han
        // follows the paragraph (kana beside it) or else the UI culture, and the same
        // ideograph must not keep the first answer's glyphs.
        string? language = ScriptLanguage.ForCodepoint(codepoint, hanContext);
        var key = new FallbackKey(codepoint, weight, slant, language);
        if (_fallbackCache.TryGetValue(key, out var cached))
            return cached;

        // With the style, so bold text falls back to a bold face rather than a regular one,
        // and with the script's language, so it falls back to a face made for the script
        // rather than the first one that covers the codepoint (see ScriptLanguage).
        using var style = new SKFontStyle(ToSkWeight(weight), SKFontStyleWidth.Normal, ToSkSlant(slant));
        var typeface = _fontManager.MatchCharacter(null, style, language is null ? null : [language], codepoint);

        SkiaResolvedFont? resolved = null;
        if (typeface is not null && !_fallbackFonts.TryGetValue(typeface, out resolved))
        {
            resolved = new SkiaResolvedFont(typeface);
            _fallbackFonts[typeface] = resolved;
        }

        _fallbackCache[key] = resolved;
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
    private readonly record struct FallbackKey(int Codepoint, FontWeight Weight, FontSlant Slant, string? Language);
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
