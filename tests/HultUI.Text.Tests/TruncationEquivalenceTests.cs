using HultUI.Core.Layout;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// Truncation (Clip and Ellipsis) finds the longest prefix that fits from a string's own
/// segments and, for one ellipsized line, skips breaking the text into lines. Its output must
/// be what the reference path — every candidate prefix measured from scratch, one line
/// through the line breaker — produced, to the bit. These hold the two to that over a shaper
/// that kerns, ligates and has zero-width marks, and two fonts.
/// </summary>
public class TruncationEquivalenceTests
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
    ];

    // Everything here but the last group is below U+0590, so it takes the plain path the
    // shortcut lives on; anything at or above it is laid out by the BiDi path.
    private static readonly string[] Texts =
    [
        "pretty red table chair house keyboard",
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
        "W",
        "Wi",
        "line one\nline two is longer",          // a newline: never the one-line shortcut
        // The BiDi path, untouched: here only to show it stays so.
        "日本語のテキストを表示",
        "emoji \U0001F600 in the \U0001F680 middle",
    ];

    private static readonly (int MaxLines, TextOverflow Overflow)[] Modes =
    [
        (1, TextOverflow.Ellipsis),
        (2, TextOverflow.Ellipsis),
        (1, TextOverflow.Clip),
    ];

    public static TheoryData<bool> FontSets => new() { false, true };

    [Theory]
    [MemberData(nameof(FontSets))]
    public void Shortcut_MatchesTheReferencePath_AtEveryWidth(bool negativeDescent)
    {
        int compared = 0;
        foreach (var style in Styles)
        foreach (var (maxLines, overflow) in Modes)
        {
            // Fresh pipelines per style and mode, so each string's first truncation (which
            // builds its tables) is part of what is compared.
            var fonts = new TwoFonts(negativeDescent);
            var reference = new TextPipeline(fonts, new ContextShaper()) { ShortcutTruncation = false };
            var shortcut = new TextPipeline(fonts, new ContextShaper());

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
                    var actual = shortcut.Layout(text, style, w, maxLines, overflow);
                    LayoutAssert.Same(expected, actual, $"\"{text}\" w={w} lines={maxLines} {overflow} {style}");
                    compared++;
                }
            }
        }
        Assert.True(compared > 10_000, $"compared only {compared} layouts");
    }

    [Fact]
    public void OneLineEllipsis_ItemizesOnlyTheLineItShows_AtEachNewWidth()
    {
        var shortcut = Counted(shortcutOn: true, out var fonts);
        var reference = Counted(shortcutOn: false, out var referenceFonts);
        const string text = "pretty red table chair house keyboard";

        // The first width works the string out once; every later one only shapes its line.
        shortcut.Layout(text, Base, 150f, 1, TextOverflow.Ellipsis);
        reference.Layout(text, Base, 150f, 1, TextOverflow.Ellipsis);
        int before = fonts.Resolves, referenceBefore = referenceFonts.Resolves;
        for (int w = 40; w < 150; w += 5)
        {
            shortcut.Layout(text, Base, w, 1, TextOverflow.Ellipsis);
            reference.Layout(text, Base, w, 1, TextOverflow.Ellipsis);
        }

        Assert.Equal(22, fonts.Resolves - before);
        // The counting is real: the reference path itemizes every probe and line segment.
        Assert.True(referenceFonts.Resolves - referenceBefore > 22 * 8,
            $"reference path itemized only {referenceFonts.Resolves - referenceBefore} times");
    }

    [Fact]
    public void OneLineClip_ItemizesOnlyTheLineItShows_AtEachNewWidth()
    {
        var shortcut = Counted(shortcutOn: true, out var fonts);
        const string text = "pretty red table chair house keyboard";

        shortcut.Layout(text, Base, 150f, 1, TextOverflow.Clip);
        int before = fonts.Resolves;
        for (int w = 40; w < 150; w += 5)
            shortcut.Layout(text, Base, w, 1, TextOverflow.Clip);

        Assert.Equal(22, fonts.Resolves - before);
    }

    private static TextPipeline Counted(bool shortcutOn, out TwoFonts fonts)
    {
        fonts = new TwoFonts(negativeDescent: false);
        return new TextPipeline(fonts, new ContextShaper()) { ShortcutTruncation = shortcutOn };
    }

    /// <summary>
    /// A primary face with everything but Greek, CJK and emoji, and a fallback with those and
    /// other metrics. <c>negativeDescent</c> gives the primary a descent below zero, where an
    /// empty line and a line of glyphs come out at different heights.
    /// </summary>
    private sealed class TwoFonts(bool negativeDescent) : IFontCollection
    {
        private static bool InFallback(int cp) => cp is >= 0x0370 and <= 0x03FF or >= 0x3000 and <= 0x9FFF or >= 0x1F000;

        private readonly TestFont _primary = new("Primary", -0.75f, negativeDescent ? -0.05f : 0.25f, 0f,
            cp => !InFallback(cp));
        private readonly TestFont _fallback = new("Fallback", -0.9f, 0.3f, 0.05f, InFallback);

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
            ('e', '…') or ('y', '…') => -1.5f,
            _ => 0f,
        };
    }
}
