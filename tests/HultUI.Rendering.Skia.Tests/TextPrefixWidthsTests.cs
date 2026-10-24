using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// <see cref="TextPipeline.MeasurePrefixes"/> through HarfBuzz and the system's fonts: every
/// prefix of every string, in every style, measures what laying it out on a line of its own
/// does, to the bit — kerning, ligatures, marks, fallback faces and letter spacing included.
/// A TextArea's line-break search reads its widths from this instead of measuring each prefix,
/// and keeps its answers only because these are the same numbers.
/// </summary>
public class TextPrefixWidthsTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { LetterSpacing = 0.6f },
        Base with { FontSize = 13f, FontWeight = FontWeight.Bold },
        Base with { FontFamily = FontFamilies.Monospace },
        Base with { FontSlant = FontSlant.Italic, FontSize = 11.5f },
    ];

    private static readonly string[] Texts =
    [
        .. PlainPathEquivalenceTests.Texts.Where(t => !t.Contains('\n')),
        "AVAWAY To. Wolf office affluent fjord fi ffl",
        "cafe" + (char)0x0301 + " na" + (char)0x0308 + "ive and a" + (char)0x0323 + (char)0x0302 + " stacked",
        "  leading, trailing and  double   spaces  ",
        "1234567890 digits 3.14159 and v2.0.1",
        "東京に行きます and back to 漢字, then 한국어",
        "supercalifragilisticexpialidocious",
    ];

    [Fact]
    public void EveryPrefix_MeasuresWhatItDoesOnItsOwnLine()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var tables = new TextPipeline(fonts, shaper);
        var reference = new TextPipeline(fonts, shaper);
        int compared = 0;
        foreach (var style in Styles)
            foreach (var text in Texts)
            {
                var prefixes = tables.MeasurePrefixes(text, style);
                Assert.True(prefixes is not null, $"no table for \"{text}\"");
                for (int n = 0; n <= text.Length; n++)
                {
                    float expected = reference.Layout(text[..n], style, float.PositiveInfinity, maxLines: 1).Size.Width;
                    float actual = prefixes.Width(n);
                    Assert.True(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual),
                        $"\"{text[..n]}\" in {style.FontFamily} {style.FontSize} spaced {style.LetterSpacing}: "
                        + $"laid out {expected}, from the table {actual}");
                    compared++;
                }
            }
        Assert.True(compared > 2_000, $"only {compared} prefixes");
    }

    /// <summary>
    /// Tables built one after another in one pipeline are built in the same scratch lists: what
    /// one table leaves there must never reach the next. Random strings, long after short and short
    /// after long, in one face and in several (Han and kana fall back to faces of their own), and
    /// strings with what sends text to the BiDi path in them — a surrogate pair, a joiner, a mark,
    /// Hebrew, Arabic — which get no table. Every prefix of every table measures what its own line
    /// does, to the bit.
    /// </summary>
    [Fact]
    public void TablesBuiltOneAfterAnother_EachMeasureWhatTheirPrefixesDo()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var tables = new TextPipeline(fonts, shaper);
        var reference = new TextPipeline(fonts, shaper);
        string[] plain =
        [
            "a", "word", "office", "fi", "AVAWAY", "To.", " ", "  ", "1234", "3.14", "v2.0", "-",
            C(0x2014), C(0x2192), C(0x00AD), C(0x00E9), "東京", "に", "漢字", "한국어", "Ελληνικά", "кот",
        ];
        string[] bidi =
        [
            C(0x1F600), C(0x1F44D) + C(0x1F3FD), C(0x1F468) + C(0x200D) + C(0x1F469), C(0x200D),
            "e" + C(0x0301), C(0x05E9) + C(0x05DC) + C(0x05D5) + C(0x05DD), C(0x0645) + C(0x0631) + C(0x062D) + C(0x0628) + C(0x0627),
        ];
        var rng = new Random(17);
        int compared = 0, withTables = 0, withoutTables = 0, withFallback = 0;
        for (int i = 0; i < 600; i++)
        {
            var text = new System.Text.StringBuilder();
            int pieces = i % 3 == 0 ? 1 + rng.Next(3) : 4 + rng.Next(i % 5 == 0 ? 60 : 16);
            int bidiAt = rng.Next(6) == 0 ? rng.Next(pieces) : -1;
            for (int p = 0; p < pieces; p++)
                text.Append(p == bidiAt ? bidi[rng.Next(bidi.Length)] : plain[rng.Next(plain.Length)]);
            string s = text.ToString();
            var style = Styles[rng.Next(Styles.Length)];
            var prefixes = tables.MeasurePrefixes(s, style);
            if (s.Any(TextPipeline.MayNeedBidi))
            {
                Assert.Null(prefixes);
                withoutTables++;
                continue;
            }
            Assert.True(prefixes is not null, $"no table for \"{s}\"");
            withTables++;
            if (s.Any(c => c >= 0x3000)) withFallback++;
            for (int n = 0; n <= s.Length; n++)
            {
                float expected = reference.Layout(s[..n], style, float.PositiveInfinity, maxLines: 1).Size.Width;
                float actual = prefixes.Width(n);
                Assert.True(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual),
                    $"table {i}, \"{s[..n]}\" of \"{s}\" in {style.FontFamily} {style.FontSize} spaced "
                    + $"{style.LetterSpacing}: laid out {expected}, from the table {actual}");
                compared++;
            }
        }
        Assert.True(withTables > 400 && withoutTables > 60 && withFallback > 200,
            $"{withTables} tables, {withoutTables} BiDi strings, {withFallback} with fallback faces");
        Assert.True(compared > 10_000, $"only {compared} prefixes");
    }

    /// <summary>
    /// A TextArea's line-break search asks for the prefix widths of a range of its document, one
    /// range after another, each a view in the pipeline's scratch: every prefix of every range
    /// measures what laying out its own string does, to the bit. Random documents of the plain
    /// pieces, fallback faces and emoji that ask for the emoji face among them, ranges anywhere —
    /// in the middle of a word, just before a variation selector or a kana the range does not
    /// reach (the substring's itemizing and Han face are what count, not the document's).
    /// </summary>
    [Fact]
    public void RangesOfADocument_EachMeasureWhatTheirSubstringsDo()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var ranges = new TextPipeline(fonts, shaper);
        var reference = new TextPipeline(fonts, shaper);
        string[] pieces =
        [
            "a", "word", "office", "fi", "AVAWAY", "To.", " ", "  ", "1234", "3.14", "-", C(0x2014), C(0x2192),
            C(0x00E9), "東京", "に", "漢字", "한국어", "Ελληνικά", "кот", C(0x26A1), C(0x2705), C(0x2B50),
            "#" + C(0xFE0F) + C(0x20E3), C(0x2764) + C(0xFE0F), "\n",
        ];
        var rng = new Random(23);
        int compared = 0, views = 0, beforeSelector = 0, withFallback = 0;
        for (int d = 0; d < 60; d++)
        {
            var document = new System.Text.StringBuilder();
            for (int p = 0, count = 20 + rng.Next(80); p < count; p++)
                document.Append(pieces[rng.Next(pieces.Length)]);
            string text = document.ToString();
            var style = Styles[rng.Next(Styles.Length)];
            int selector = text.IndexOf((char)0xFE0F);
            for (int r = 0; r < 21; r++)
            {
                int start = rng.Next(text.Length);
                int length = Math.Min(text.Length - start, rng.Next(60));
                if (r == 20)
                {
                    if (selector < 0) break;
                    // Up to the selector, which a substring has no more of.
                    start = text.LastIndexOf('\n', selector) + 1;
                    start = Math.Max(start, selector - 1 - rng.Next(20));
                    length = selector - start;
                }
                string sub = text.Substring(start, length);
                var prefixes = ranges.MeasurePrefixes(text, start, length, style);
                if (sub.Length == 0 || sub.Contains('\n') || sub.Any(TextPipeline.MayNeedBidi))
                {
                    Assert.Null(prefixes);
                    continue;
                }
                Assert.True(prefixes is not null, $"no table for \"{sub}\"");
                Assert.Equal(length, prefixes.Length);
                views++;
                if (start + length < text.Length && text[start + length] == (char)0xFE0F) beforeSelector++;
                if (sub.Any(c => c >= 0x3000)) withFallback++;
                for (int n = 0; n <= length; n++)
                {
                    float expected = reference.Layout(sub[..n], style, float.PositiveInfinity, maxLines: 1).Size.Width;
                    float actual = prefixes.Width(n);
                    Assert.True(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual),
                        $"document {d}, range {start}+{length}, \"{sub[..n]}\" in {style.FontFamily} {style.FontSize} "
                        + $"spaced {style.LetterSpacing}: laid out {expected}, from the range {actual}");
                    compared++;
                }
            }
        }
        Assert.True(views > 150 && beforeSelector > 0 && withFallback > 50,
            $"{views} ranges with a table, {beforeSelector} just before a variation selector, {withFallback} with fallback faces");
        Assert.True(compared > 2_000, $"only {compared} prefixes");
    }

    private static string C(int codePoint) => char.ConvertFromUtf32(codePoint);
}
