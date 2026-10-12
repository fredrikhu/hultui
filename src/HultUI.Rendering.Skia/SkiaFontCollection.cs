using HultUI.Core.Styling;
using HultUI.Text;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

public sealed class SkiaFontCollection : IFontCollection
{
    private readonly SKFontManager _fontManager = SKFontManager.Default;
    private readonly Dictionary<FontKey, SkiaResolvedFont> _fontCache = new();
    private readonly Dictionary<FallbackKey, SkiaResolvedFont?> _fallbackCache = new();
    private readonly Dictionary<FallbackKey, SkiaResolvedFont?> _emojiCache = new();
    private readonly Dictionary<SKTypeface, bool> _colourFaces = new(ReferenceEqualityComparer.Instance);

    // One wrapper per fallback typeface, not per codepoint. FontItemizer splits runs on
    // font identity, so a wrapper per character made every fallback letter its own run —
    // Hebrew shaped a letter at a time on any system whose primary font lacks it, which
    // would also break Arabic joining. Skia returns the same SKTypeface instance for the
    // same face, so reference identity is the right key.
    private readonly Dictionary<SKTypeface, SkiaResolvedFont> _fallbackFonts = new(ReferenceEqualityComparer.Instance);

    // Faces added from files or streams, found by their family name before the OS is asked.
    private readonly List<SKTypeface> _registered = [];
    private bool _pinned;

    /// <summary>
    /// A collection that knows only <paramref name="faces"/>: every family name — a generic one,
    /// a list, one that names nothing — resolves to one of them, the nearest in weight (a
    /// fixed-pitch face for <see cref="FontFamilies.Monospace"/>), and no character falls back to
    /// the operating system's fonts. What text looks like then depends on these files alone,
    /// not on what a machine has installed — the half of a golden image the fonts decide.
    /// </summary>
    public static SkiaFontCollection Pinned(params SKTypeface[] faces)
    {
        if (faces.Length == 0) throw new ArgumentException("A pinned collection needs at least one face.", nameof(faces));
        var collection = new SkiaFontCollection { _pinned = true };
        foreach (var face in faces) collection.Register(face);
        return collection;
    }

    /// <summary>Whether this collection knows only the faces registered with it.</summary>
    public bool IsPinned => _pinned;

    /// <summary>
    /// Adds a face the collection finds by its own family name ahead of the installed ones — a
    /// font an app ships rather than one the machine has.
    /// </summary>
    public void Register(SKTypeface typeface)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        _registered.Add(typeface);
        _fontCache.Clear();
        _fallbackCache.Clear();
        _emojiCache.Clear();
    }

    /// <summary>Adds a face read from <paramref name="stream"/> (a .ttf or .otf).</summary>
    public SKTypeface Register(Stream stream)
    {
        var typeface = SKTypeface.FromStream(stream)
            ?? throw new ArgumentException("The stream is not a font Skia can read.", nameof(stream));
        Register(typeface);
        return typeface;
    }

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
        if (_registered.Count > 0 && MatchRegistered(familyName, weight, slant) is { } registered)
            return registered;

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

    /// <summary>
    /// A registered face for a family string: the first listed name some registered face
    /// carries, slanted or upright as asked where it can be, then nearest in weight. Pinned,
    /// every name finds one — a fixed-pitch face for the monospace generic or a name that is
    /// one, otherwise the proportional faces first.
    /// </summary>
    private SKTypeface? MatchRegistered(string familyName, SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        bool wantsFixed = false;
        foreach (var part in familyName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Trim('"', '\'');
            if (IsMonospace(name)) { wantsFixed = true; continue; }
            if (Nearest(_registered.Where(f => string.Equals(f.FamilyName, name, StringComparison.OrdinalIgnoreCase)), weight, slant) is { } named)
                return named;
        }
        if (!_pinned) return null;
        return Nearest(_registered.Where(f => f.IsFixedPitch == wantsFixed), weight, slant)
            ?? Nearest(_registered, weight, slant);
    }

    // The slant decides before the weight does: an italic asked for in bold that only has a
    // regular italic and an upright bold gets the italic, which is the difference a reader sees.
    // Italic and oblique count as the same.
    private static SKTypeface? Nearest(IEnumerable<SKTypeface> faces, SKFontStyleWeight weight, SKFontStyleSlant slant)
    {
        bool slanted = slant != SKFontStyleSlant.Upright;
        SKTypeface? best = null;
        int bestDistance = int.MaxValue;
        foreach (var face in faces)
        {
            int distance = Math.Abs(face.FontWeight - (int)weight)
                + ((face.FontSlant != SKFontStyleSlant.Upright) == slanted ? 0 : 10_000);
            if (distance < bestDistance) { best = face; bestDistance = distance; }
        }
        return best;
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
        // Pinned, nothing comes from the OS: a character the faces lack draws as their
        // missing-glyph box, the same on every machine.
        if (_pinned) return null;

        string? language = ScriptLanguage.ForCodepoint(codepoint, hanContext);
        var key = new FallbackKey(codepoint, weight, slant, language);
        if (_fallbackCache.TryGetValue(key, out var cached))
            return cached;

        // With the style, so bold text falls back to a bold face rather than a regular one,
        // and with the script's language, so it falls back to a face made for the script
        // rather than the first one that covers the codepoint (see ScriptLanguage).
        using var style = new SKFontStyle(ToSkWeight(weight), SKFontStyleWidth.Normal, ToSkSlant(slant));
        var typeface = _fontManager.MatchCharacter(null, style, language is null ? null : [language], codepoint);

        var resolved = typeface is null ? null : FallbackFont(typeface);
        _fallbackCache[key] = resolved;
        return resolved;
    }

    private SkiaResolvedFont FallbackFont(SKTypeface typeface)
    {
        if (!_fallbackFonts.TryGetValue(typeface, out var resolved))
        {
            resolved = new SkiaResolvedFont(typeface);
            _fallbackFonts[typeface] = resolved;
        }
        return resolved;
    }

    /// <summary>
    /// The colour emoji face for a character that asks to be drawn as an emoji. Fontconfig is asked
    /// for the character in the emoji "language", <c>und-Zsye</c>, which its emoji faces cover, and
    /// answers Noto Color Emoji where a bare question got DejaVu Sans for 😀 and ❤. DirectWrite does
    /// not know the tag and answers Segoe UI for # © ™, so then the platform's emoji family is
    /// asked by name. Only a colour face (COLR, CBDT, sbix, SVG) is taken: on a machine without one,
    /// the character falls back as any other does. Pinned, there is none.
    /// </summary>
    public IResolvedFont? MatchEmoji(int codepoint, FontWeight weight, FontSlant slant)
    {
        if (_pinned) return null;

        var key = new FallbackKey(codepoint, weight, slant, Language: null);
        if (_emojiCache.TryGetValue(key, out var cached))
            return cached;

        using var style = new SKFontStyle(ToSkWeight(weight), SKFontStyleWidth.Normal, ToSkSlant(slant));
        var typeface = _fontManager.MatchCharacter(null, style, EmojiLanguage, codepoint);
        if (typeface is null || !IsColour(typeface))
        {
            typeface = null;
            foreach (var name in EmojiFamilies)
            {
                if (_fontManager.MatchFamily(name, style) is { } face && IsColour(face) && face.GetGlyph(codepoint) != 0)
                {
                    typeface = face;
                    break;
                }
            }
        }

        var resolved = typeface is null ? null : FallbackFont(typeface);
        _emojiCache[key] = resolved;
        return resolved;
    }

    private static readonly string[] EmojiLanguage = ["und-Zsye"];

    /// <summary>The colour emoji faces each platform ships, asked for by name.</summary>
    private static readonly string[] EmojiFamilies =
        OperatingSystem.IsWindows() ? ["Segoe UI Emoji"]
        : OperatingSystem.IsMacOS() ? ["Apple Color Emoji"]
        : ["Noto Color Emoji", "Twemoji", "JoyPixels"];

    /// <summary>Whether a face draws its glyphs in colour: it has a COLR, CBDT, sbix or SVG table.</summary>
    private bool IsColour(SKTypeface typeface)
    {
        if (_colourFaces.TryGetValue(typeface, out bool colour)) return colour;
        colour = typeface.GetTableSize(Tag('C', 'O', 'L', 'R')) > 0 || typeface.GetTableSize(Tag('C', 'B', 'D', 'T')) > 0
            || typeface.GetTableSize(Tag('s', 'b', 'i', 'x')) > 0 || typeface.GetTableSize(Tag('S', 'V', 'G', ' ')) > 0;
        _colourFaces[typeface] = colour;
        return colour;

        static uint Tag(char a, char b, char c, char d) => (uint)(a << 24 | b << 16 | c << 8 | d);
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

    // Which codepoints of the Basic Multilingual Plane have been asked about (the first 1,024
    // words) and which of those the face has (the next 1,024): two bits a codepoint, 16 KB a
    // face, made on the first question. Itemizing asks for every character of every run it
    // shapes, and each answer was a call into Skia; text is mostly a few hundred distinct
    // characters asked about again and again. One array, so a question and its answer are
    // always read from the same one, even if two threads race to make it.
    private long[]? _glyphBits;

    public bool HasGlyph(int codepoint)
    {
        if ((uint)codepoint > 0xFFFF) return _typeface.GetGlyph(codepoint) != 0;
        var bits = _glyphBits ??= new long[2048];
        int word = codepoint >> 6;
        long bit = 1L << (codepoint & 63);
        // Asked before: the answer was written before the question was marked, and the read
        // of the mark orders the read of the answer after it — layout can run off the UI thread.
        if ((Volatile.Read(ref bits[word]) & bit) != 0) return (bits[1024 + word] & bit) != 0;
        bool has = _typeface.GetGlyph(codepoint) != 0;
        if (has) Interlocked.Or(ref bits[1024 + word], bit);
        Interlocked.Or(ref bits[word], bit);
        return has;
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
