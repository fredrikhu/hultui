using HultUI.Core.Layout;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// The same string at a new width reuses what earlier widths worked out: wrapping keeps its
/// break segments and their widths, truncation (Clip and Ellipsis) finds the longest prefix
/// that fits from the string's own segments and, for one ellipsized line, skips breaking the
/// text into lines. The output must be what the reference path — everything worked out from
/// scratch at each width — produced, to the bit. These hold the two to that over a shaper
/// that kerns, ligates and has zero-width marks, and two fonts.
/// </summary>
public class WidthChangeEquivalenceTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { LetterSpacing = 1.25f },
        Base with { LineHeight = 26f },
        Base with { FontSize = 13f },
        // Every text on the BiDi path, the plain ones included.
        Base with { TextDirection = TextDirection.Rtl },
        Base with { TextDirection = TextDirection.Auto, LetterSpacing = 0.75f },
    ];

    // The first group is below U+0590, so in a left-to-right style it takes the plain path;
    // anything at or above it, and everything in a right-to-left style, the BiDi path.
    private static readonly string[] Texts =
    [
        "pretty red table chair house keyboard",
        "a longer paragraph of plain words that wraps to several lines at most of the widths tried here, office AV To",
        "AVA To Wa office fifty fine",          // kern pairs and the fi ligature, inside and across cuts
        "supercalifragilisticexpialidocious",   // one word: every cut is mid-word
        "well-known x-ray re-entry co-op",      // break opportunities inside words
        "   leading spaces then words",         // a first line of nothing but spaces
        "trailing spaces   ",
        "double  spaced  words  here",
        "\u00ADsoft hyphen first",              // a first segment of zero width
        "cafe\u0301 re\u0301sume\u0301 nai\u0308ve",   // zero-width combining marks
        "12345 67890 1029384756",               // digit fast path segments
        "tab\tseparated\tfields\there",
        "no\u00A0break\u00A0space\u00A0joined",
        "Ελληνικόκείμενοεδώ",                   // one run, in the fallback font
        "Ελληνικό κείμενο εδώ",                 // more than one run: the spaces are the primary's
        "mixed Ελλη and latin text",
        "Ελλη first and latin after",            // a line of the fallback font alone, at narrow widths
        "W",
        "Wi",
        "line one\nline two is longer",          // a newline: never the one-line shortcut
        // The BiDi path in any style.
        "שלום עולם ספר בית מחשב חלון קובץ תיקייה",
        "commit שלום merged by אבג into main and then some",
        "אבג 123 (דהו) [זחט] 4.5% done",
        "שורה ראשונה\nשורה שנייה ארוכה יותר",
        "日本語のテキストを表示",
        "emoji \U0001F600 in the \U0001F680 middle",
    ];

    private static readonly (int? MaxLines, TextOverflow Overflow)[] Modes =
    [
        (1, TextOverflow.Ellipsis),
        (2, TextOverflow.Ellipsis),
        (1, TextOverflow.Clip),
        (null, TextOverflow.Clip),
        (3, TextOverflow.Clip),
    ];

    // negativeDescent, shortFallback: see TwoFonts.
    public static TheoryData<bool, bool> FontSets => new() { { false, false }, { true, false }, { false, true } };

    [Theory]
    [MemberData(nameof(FontSets))]
    public void Reuse_MatchesTheReferencePath_AtEveryWidth(bool negativeDescent, bool shortFallback)
    {
        int compared = 0;
        foreach (var (maxLines, overflow) in Modes)
        {
            // Fresh pipelines per mode, so each string's first truncation (which builds its
            // tables) is part of what is compared, shared by every style, so a cache key that
            // left out a style field would hand one style another's result and fail here.
            var fonts = new TwoFonts(negativeDescent, shortFallback);
            var reference = new TextPipeline(fonts, new ContextShaper()) { ReuseAcrossWidths = false };
            var reuse = new TextPipeline(fonts, new ContextShaper());

            foreach (var style in Styles)
            foreach (var text in Texts)
            {
                float natural = reference.Layout(text, style, float.PositiveInfinity, null).Size.Width;
                // Narrow strings get a fine step: a one-character string hinges on half a pixel.
                float step = text.Length <= 3 ? 0.25f : 1.7f;
                var widths = new List<float> { -1f, 0f };
                for (float w = 0.5f; w < natural + 3f; w += step) widths.Add(w);

                // Down and back up: later widths reuse what the first one worked out.
                foreach (var w in widths.Concat(Enumerable.Reverse(widths)))
                {
                    var expected = reference.Layout(text, style, w, maxLines, overflow);
                    var actual = reuse.Layout(text, style, w, maxLines, overflow);
                    LayoutAssert.Same(expected, actual, $"\"{text}\" w={w} lines={maxLines} {overflow} {style}");
                    compared++;
                }
            }
        }
        Assert.True(compared > 10_000, $"compared only {compared} layouts");
    }

    [Theory]
    [MemberData(nameof(FontSets))]
    public void StyledReuse_MatchesTheReferencePath_AtEveryWidth(bool negativeDescent, bool shortFallback)
    {
        // Metric spans: the styled path for left-to-right text, the BiDi path with spans for
        // the rest. Two span lists per text through the same pipelines, so one list's work
        // handed to the other would fail here.
        int compared = 0;
        foreach (var (maxLines, overflow) in Modes)
        {
            var fonts = new TwoFonts(negativeDescent, shortFallback);
            var reference = new TextPipeline(fonts, new ContextShaper()) { ReuseAcrossWidths = false };
            var reuse = new TextPipeline(fonts, new ContextShaper());

            foreach (var style in Styles)
            foreach (var text in Texts)
            foreach (var spans in TestSpans.Variants(text))
            {
                float natural = reference.Layout(text, style, float.PositiveInfinity, null, TextOverflow.Clip, spans).Size.Width;
                var widths = new List<float> { -1f, 0f };
                for (float w = 0.5f; w < natural + 3f; w += 2.3f) widths.Add(w);

                foreach (var w in widths.Concat(Enumerable.Reverse(widths)))
                {
                    var expected = reference.Layout(text, style, w, maxLines, overflow, spans);
                    var actual = reuse.Layout(text, style, w, maxLines, overflow, spans);
                    LayoutAssert.Same(expected, actual, $"\"{text}\" spans {spans.Length} w={w} lines={maxLines} {overflow} {style}");
                    compared++;
                }
            }
        }
        Assert.True(compared > 10_000, $"compared only {compared} layouts");
    }

    [Fact]
    public void AStyledLabelThatFits_IsNotItemizedAgain_AtANewWidth()
    {
        var reuse = Counted(reuse: true, out var fonts);
        var reference = Counted(reuse: false, out var referenceFonts);
        const string label = "pretty red table chair";
        var spans = TestSpans.Variants(label).First();

        // The entry comes at the second layout; from the third on, nothing is measured or shaped.
        foreach (float w in new[] { 500f, 499f })
        {
            reuse.Layout(label, Base, w, 1, TextOverflow.Ellipsis, spans);
            reference.Layout(label, Base, w, 1, TextOverflow.Ellipsis, spans);
        }
        int before = fonts.Resolves, referenceBefore = referenceFonts.Resolves;
        for (int w = 300; w < 500; w += 7)
        {
            reuse.Layout(label, Base, w, 1, TextOverflow.Ellipsis, spans);
            reference.Layout(label, Base, w, 1, TextOverflow.Ellipsis, spans);
        }

        Assert.Equal(0, fonts.Resolves - before);
        Assert.True(referenceFonts.Resolves - referenceBefore > 29,
            $"reference path itemized {referenceFonts.Resolves - referenceBefore} times");
    }

    [Fact]
    public void AStyledRightToLeftLabelThatFits_IsNotShapedAgain_AtANewWidth()
    {
        var shaper = new CountingShaper(new ContextShaper());
        var reuse = new TextPipeline(new TwoFonts(negativeDescent: false), shaper);
        var rtl = Base with { TextDirection = TextDirection.Rtl };
        const string label = "שלום עולם ספר בית";
        var spans = TestSpans.Variants(label).First();

        foreach (float w in new[] { 500f, 499f })
            reuse.Layout(label, rtl, w, 1, TextOverflow.Ellipsis, spans);
        int before = shaper.Calls;
        for (int w = 300; w < 500; w += 7)
            reuse.Layout(label, rtl, w, 1, TextOverflow.Ellipsis, spans);

        Assert.Equal(0, shaper.Calls - before);
    }

    [Fact]
    public void OneLineEllipsis_ItemizesOnlyTheLineItShows_AtEachNewWidth()
    {
        var reuse = Counted(reuse: true, out var fonts);
        var reference = Counted(reuse: false, out var referenceFonts);
        const string text = "pretty red table chair house keyboard";

        // The first width works the string out once; every later one only shapes its line.
        reuse.Layout(text, Base, 150f, 1, TextOverflow.Ellipsis);
        reference.Layout(text, Base, 150f, 1, TextOverflow.Ellipsis);
        int before = fonts.Resolves, referenceBefore = referenceFonts.Resolves;
        for (int w = 40; w < 150; w += 5)
        {
            reuse.Layout(text, Base, w, 1, TextOverflow.Ellipsis);
            reference.Layout(text, Base, w, 1, TextOverflow.Ellipsis);
        }

        Assert.Equal(22, fonts.Resolves - before);
        // The counting is real: the reference path itemizes every probe and line segment.
        Assert.True(referenceFonts.Resolves - referenceBefore > 22 * 8,
            $"reference path itemized only {referenceFonts.Resolves - referenceBefore} times");
    }

    [Fact]
    public void OneLineEllipsis_OfAMultiFontLabel_MeasuresTheLineItReplaces_AndShapesOnlyTheOneItShows()
    {
        var reuse = Counted(reuse: true, out var fonts);
        var reference = Counted(reuse: false, out var referenceFonts);
        const string text = "mixed Ελλη and latin words here today";   // Greek is the fallback font's

        // Widest first, so every segment a narrower width reaches is measured already.
        reuse.Layout(text, Base, 240f, 1, TextOverflow.Ellipsis);
        reference.Layout(text, Base, 240f, 1, TextOverflow.Ellipsis);
        int before = fonts.Resolves, referenceBefore = referenceFonts.Resolves, widths = 0;
        for (int w = 235; w >= 90; w -= 5, widths++)
        {
            reuse.Layout(text, Base, w, 1, TextOverflow.Ellipsis);
            reference.Layout(text, Base, w, 1, TextOverflow.Ellipsis);
        }

        // Per width: the measured line's fonts, and the ellipsized line's itemization.
        Assert.Equal(2 * widths, fonts.Resolves - before);
        Assert.True(referenceFonts.Resolves - referenceBefore > 8 * widths,
            $"reference path itemized {referenceFonts.Resolves - referenceBefore} times over {widths} widths");
    }

    [Fact]
    public void OneLineClip_ItemizesOnlyTheLineItShows_AtEachNewWidth()
    {
        var reuse = Counted(reuse: true, out var fonts);
        const string text = "pretty red table chair house keyboard";

        reuse.Layout(text, Base, 150f, 1, TextOverflow.Clip);
        int before = fonts.Resolves;
        for (int w = 40; w < 150; w += 5)
            reuse.Layout(text, Base, w, 1, TextOverflow.Clip);

        Assert.Equal(22, fonts.Resolves - before);
    }

    [Fact]
    public void Rewrapping_ItemizesOnlyTheLinesItKeeps_AtEachNewWidth()
    {
        var reuse = Counted(reuse: true, out var fonts);
        var reference = Counted(reuse: false, out var referenceFonts);
        const string text = "pretty red table chair house keyboard pretty red table chair house keyboard";

        // The first width finds the segments and measures them. A later one shapes the lines it
        // keeps and measures its last line once, for the search that cuts it — at widths where
        // every word fits; a word too wide for the box is cut by measuring it.
        reuse.Layout(text, Base, 400f, null);
        reference.Layout(text, Base, 400f, null);
        int before = fonts.Resolves, referenceBefore = referenceFonts.Resolves, lines = 0, widths = 0;
        for (int w = 100; w < 400; w += 7, widths++)
        {
            lines += reuse.Layout(text, Base, w, null).Lines.Count;
            reference.Layout(text, Base, w, null);
        }

        Assert.Equal(lines + widths, fonts.Resolves - before);
        // The counting is real: the reference path itemizes every segment at every width too.
        Assert.True(referenceFonts.Resolves - referenceBefore > lines * 2,
            $"reference path itemized {referenceFonts.Resolves - referenceBefore} times for {lines} lines");
    }

    [Fact]
    public void ARightToLeftLabelThatFits_IsNotShapedAgain_AtANewWidth()
    {
        var shaper = new CountingShaper(new ContextShaper());
        var reuse = new TextPipeline(new TwoFonts(negativeDescent: false), shaper);
        var referenceShaper = new CountingShaper(new ContextShaper());
        var reference = new TextPipeline(new TwoFonts(negativeDescent: false), referenceShaper) { ReuseAcrossWidths = false };
        var rtl = Base with { TextDirection = TextDirection.Rtl };
        const string label = "שלום עולם ספר";

        // A string on this path gets its entry at its second layout; from the third on, a new
        // width that the label fits shapes nothing.
        foreach (float w in new[] { 500f, 499f })
        {
            reuse.Layout(label, rtl, w, 1, TextOverflow.Ellipsis);
            reference.Layout(label, rtl, w, 1, TextOverflow.Ellipsis);
        }
        int before = shaper.Calls, referenceBefore = referenceShaper.Calls;
        for (int w = 300; w < 500; w += 7)
        {
            reuse.Layout(label, rtl, w, 1, TextOverflow.Ellipsis);
            reference.Layout(label, rtl, w, 1, TextOverflow.Ellipsis);
        }

        Assert.Equal(0, shaper.Calls - before);
        // The counting is real: the reference path shapes the label again at every width.
        Assert.True(referenceShaper.Calls - referenceBefore > 29,
            $"reference path shaped {referenceShaper.Calls - referenceBefore} times");
    }

    private sealed class CountingShaper(ITextShaper inner) : ITextShaper
    {
        public int Calls;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            Calls++;
            return inner.Shape(text, font, fontSize, rtl);
        }
    }

    private static TextPipeline Counted(bool reuse, out TwoFonts fonts)
    {
        fonts = new TwoFonts(negativeDescent: false);
        return new TextPipeline(fonts, new ContextShaper()) { ReuseAcrossWidths = reuse };
    }

    /// <summary>
    /// A primary face with everything but Greek, CJK and emoji, and a fallback with those and
    /// other metrics, taller in every one. <c>negativeDescent</c> gives the primary a descent
    /// below zero, where an empty line and a line of glyphs come out at different heights.
    /// <c>shortFallback</c> makes the fallback shorter in every metric instead, so a line of
    /// both fonts is as tall as one of the primary alone and only a line of the fallback alone
    /// is not.
    /// </summary>
    private sealed class TwoFonts(bool negativeDescent, bool shortFallback = false) : IFontCollection
    {
        private static bool InFallback(int cp) => cp is >= 0x0370 and <= 0x03FF or >= 0x3000 and <= 0x9FFF or >= 0x1F000;

        private readonly TestFont _primary = new("Primary", -0.75f, negativeDescent ? -0.05f : 0.25f, 0f,
            cp => !InFallback(cp));
        private readonly TestFont _fallback = shortFallback
            ? new("Fallback", -0.6f, 0.2f, 0f, InFallback)
            : new("Fallback", -0.9f, 0.3f, 0.05f, InFallback);

        public int Resolves;

        public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant)
        {
            Resolves++;
            return _primary;
        }

        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant) =>
            _fallback.HasGlyph(codepoint) ? _fallback : null;
    }

    private sealed class TestFont(string family, float ascent, float descent, float leading, Func<int, bool> has)
        : IResolvedFont
    {
        public string FamilyName => family;
        public float Ascent => ascent;
        public float Descent => descent;
        public float Leading => leading;
        public float LineHeight => Math.Abs(ascent) + descent + leading;
        public object NativeHandle => this;
        public bool HasGlyph(int codepoint) => has(codepoint);
        public void Dispose() { }
    }

    /// <summary>
    /// A shaper whose output depends on context, as a real one's does: uneven fractional
    /// advances, kerning (the first glyph of a pair absorbs it, as HarfBuzz reports it), an
    /// "fi" ligature, zero-width marks, and one glyph per surrogate pair. So a prefix's width
    /// is NOT its share of the whole word's advances, and any shortcut that assumed so would
    /// show here.
    /// </summary>
    private sealed class ContextShaper : ITextShaper
    {
        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            float scale = fontSize / 16f * (font.FamilyName == "Fallback" ? 1.3f : 1f);
            var glyphs = new List<GlyphPosition>();
            float x = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                uint id = c;
                float advance;
                if (c == 'f' && i + 1 < text.Length && text[i + 1] == 'i')
                {
                    id = 0xFB01;
                    advance = (Advance('f') + Advance('i')) * 0.8f;
                    i++;
                }
                else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    id = (uint)char.ConvertToUtf32(c, text[i + 1]);
                    advance = 14.6f;
                    i++;
                }
                else
                {
                    advance = Advance(c);
                    if (i + 1 < text.Length) advance += Kern(c, text[i + 1]);
                }
                advance *= scale;
                glyphs.Add(new GlyphPosition(id, x, 0, advance));
                x += advance;
            }
            return new ShapedRun(text, font, fontSize, x, glyphs.ToArray());
        }

        private static float Advance(char c) => c switch
        {
            '\u0301' or '\u0308' or '\u00AD' => 0f,
            ' ' => 4.25f,
            '…' => 11.5f,
            _ => 5.3f + c % 7 * 0.71f,
        };

        private static float Kern(char a, char b) => (a, b) switch
        {
            ('A', 'V') or ('V', 'A') => -2.25f,
            ('T', 'o') or ('W', 'a') or ('W', 'i') => -1.75f,
            // Across a hyphen, where a line may break: "x-ray" shaped whole is wider than
            // "x-" and "ray" apart, so a last line can fit by its segments and not as a line.
            ('-', 'r') or ('-', 'e') or ('-', 'o') or ('-', 'k') => 2.5f,
            ('e', '…') or ('y', '…') => -1.5f,
            _ => 0f,
        };
    }
}
