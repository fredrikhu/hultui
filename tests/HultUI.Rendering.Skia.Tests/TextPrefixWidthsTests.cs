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

    private static string C(int codePoint) => char.ConvertFromUtf32(codePoint);
}
