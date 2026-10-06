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
}
