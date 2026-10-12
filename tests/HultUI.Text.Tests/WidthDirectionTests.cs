using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// <see cref="TextPipeline.MeasureWidth"/> keeps the width of text on the BiDi path as well as on
/// the plain one, and a BiDi width depends on the paragraph's direction: the space in "a ב c" sides
/// with the Latin letters in a left-to-right paragraph and with the Hebrew one in a right-to-left
/// paragraph, and is shaped in that direction's run. The shaper here makes a right-to-left space
/// narrower, so each direction has a width of its own, which the cache must not hand to another.
/// Plain text measures the same whatever the direction says, and is kept once.
/// </summary>
public class WidthDirectionTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly TextDirection[] Directions = [TextDirection.Ltr, TextDirection.Rtl, TextDirection.Auto];

    [Fact]
    public void ABidiTextsWidth_IsKeptUnderItsDirection()
    {
        var fonts = new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false);
        var pipeline = new TextPipeline(fonts, new DirectionShaper());
        var reference = new TextPipeline(fonts, new DirectionShaper());
        string text = "a " + (char)0x05D1 + " c";
        var widths = new Dictionary<TextDirection, float>();
        for (int pass = 0; pass < 3; pass++)          // measured, then found, then found again
            foreach (var direction in Directions)
            {
                var style = Base with { TextDirection = direction };
                float expected = reference.Layout(text, style, float.PositiveInfinity, maxLines: 1).Size.Width;
                float actual = pipeline.MeasureWidth(text, style);
                Assert.True(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual),
                    $"{direction}, pass {pass + 1}: laid out {expected}, measured {actual}");
                widths[direction] = actual;
            }
        // Or the test would prove nothing: the directions do measure differently here (Auto as Ltr:
        // the first strong letter is Latin). One entry a direction, each found again.
        Assert.NotEqual(widths[TextDirection.Ltr], widths[TextDirection.Rtl]);
        Assert.Equal(widths[TextDirection.Ltr], widths[TextDirection.Auto]);
        Assert.Equal(3, pipeline.WidthCacheUse.Entries);
    }

    [Fact]
    public void APlainTextsWidth_IsKeptOnceWhateverTheDirection()
    {
        var pipeline = new TextPipeline(new WidthChangeEquivalenceTests.TwoFonts(negativeDescent: false), new DirectionShaper());
        foreach (var direction in new[] { TextDirection.Ltr, TextDirection.Auto })
            pipeline.MeasureWidth("plain words", Base with { TextDirection = direction });
        Assert.Equal(1, pipeline.WidthCacheUse.Entries);
    }

    /// <summary>Every glyph 7 wide, a space 4 left to right and 2 right to left.</summary>
    private sealed class DirectionShaper : ITextShaper
    {
        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            var glyphs = new GlyphPosition[text.Length];
            float x = 0;
            for (int i = 0; i < text.Length; i++)
            {
                float advance = text[i] == ' ' ? (rtl ? 2f : 4f) : 7f;
                glyphs[rtl ? text.Length - 1 - i : i] = new GlyphPosition(text[i], 0, 0, advance);
                x += advance;
            }
            float at = 0;
            for (int g = 0; g < glyphs.Length; g++)
            {
                glyphs[g] = glyphs[g] with { X = at };
                at += glyphs[g].Advance;
            }
            return new ShapedRun(text, font, fontSize, x, glyphs);
        }
    }
}
