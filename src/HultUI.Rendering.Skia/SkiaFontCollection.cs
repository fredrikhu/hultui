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

        var resolved = new SkiaResolvedFont(Match(familyName, ToSkWeight(weight), ToSkSlant(slant)));
        _fontCache[key] = resolved;
        return resolved;
    }

    /// <summary>
    /// The face for a family string.
    /// <list type="bullet">
    /// <item>A single name is asked for as it always was, so one that is not installed becomes
    /// the platform's default face.</item>
    /// <item>A list takes the first family installed, as CSS's <c>font-family</c> does, and the
    /// first name's answer when none is.</item>
    /// <item>The generic <see cref="FontFamilies.Monospace"/> names the platform's monospace
    /// face. DirectWrite does not know the name, and answered Segoe UI, a proportional face.</item>
    /// </list>
    /// "Installed" is the font manager's family match, which knows every localized name of a
    /// family and, unlike the legacy lookup, says when there is none.
    /// </summary>
    private SKTypeface Match(string familyName, SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        if (!familyName.Contains(',') && !IsMonospace(familyName))
            return Legacy(familyName, weight, slant);

        using var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
        string? first = null;
        foreach (var part in familyName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Trim('"', '\'');
            first ??= name;
            var typeface = IsMonospace(name) ? MatchMonospace(style, weight, slant) : _fontManager.MatchFamily(name, style);
            if (typeface is not null) return typeface;
        }
        return Legacy(first ?? "", weight, slant);
    }

    private static SKTypeface Legacy(string familyName, SKFontStyleWeight weight, SKFontStyleSlant slant) =>
        SKTypeface.FromFamilyName(familyName, weight, SKFontStyleWidth.Normal, slant) ?? SKTypeface.Default;

    private static bool IsMonospace(string name) =>
        string.Equals(name, FontFamilies.Monospace, StringComparison.OrdinalIgnoreCase);

    /// <summary>The faces <see cref="FontFamilies.Monospace"/> tries, in order.</summary>
    private static readonly string[] MonospaceFaces =
        OperatingSystem.IsWindows() ? ["Cascadia Mono", "Consolas", "Courier New"]
        : OperatingSystem.IsMacOS() ? ["Menlo", "Monaco", "Courier New"]
        : ["DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono"];

    private SKTypeface? MatchMonospace(SKFontStyle style, SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        // Fontconfig knows "monospace" itself, and a user may have configured what it means.
        // Its answer is taken when it is a fixed-pitch face; otherwise the alias is missing.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()
            && SKTypeface.FromFamilyName(FontFamilies.Monospace, weight, SKFontStyleWidth.Normal, slant) is { IsFixedPitch: true } alias)
            return alias;

        foreach (var name in MonospaceFaces)
            if (_fontManager.MatchFamily(name, style) is { } typeface)
                return typeface;
        return null;
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
    private (float, float)? _underline, _strikeout;

    public string FamilyName => _typeface.FamilyName;
    public object NativeHandle => _typeface;

    public float Ascent { get { EnsureMetrics(); return _ascent; } }
    public float Descent { get { EnsureMetrics(); return _descent; } }
    public float Leading { get { EnsureMetrics(); return _leading; } }
    public float LineHeight => Math.Abs(Ascent) + Descent + Leading;

    public bool IsFixedPitch => _typeface.IsFixedPitch;
    public (float Position, float Thickness)? Underline { get { EnsureMetrics(); return _underline; } }
    public (float Position, float Thickness)? Strikeout { get { EnsureMetrics(); return _strikeout; } }

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
        // IResolvedFont measures both from the baseline, positive down, to the top of the stroke.
        // Skia's underline position is that; its strikeout position is to the BOTTOM of the
        // stroke (SkFontMetrics.h), so the thickness comes off it. Null where the font leaves
        // them out.
        if (metrics.UnderlinePosition is { } up && metrics.UnderlineThickness is { } ut)
            _underline = (up, ut);
        if (metrics.StrikeoutPosition is { } sp && metrics.StrikeoutThickness is { } st)
            _strikeout = (sp - st, st);
        _metricsComputed = true;
    }

    public void Dispose()
    {
        _font.Dispose();
        // Don't dispose _typeface — it may be cached/shared
    }
}
