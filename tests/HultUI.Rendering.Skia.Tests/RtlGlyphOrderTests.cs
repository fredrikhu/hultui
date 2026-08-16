using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Pins the shaper contract the BiDi caret math is built on: an RTL run's glyphs
/// come back in VISUAL order — glyph 0 is the leftmost on screen and corresponds
/// to the LAST logical character. Everything in TextLine's offset↔x mapping
/// follows from that, so it is asserted directly rather than assumed.
/// </summary>
public class RtlGlyphOrderTests
{
    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    [Fact]
    public void AnRtlRun_ReturnsGlyphsLeftToRight_WithTheLastLogicalCharFirst()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var font = fonts.Resolve(Style.FontFamily, Style.FontWeight, Style.FontSlant);

        // Hebrew: no joining, no ligatures, one glyph per character on every
        // platform's fallback font — so glyph index maps 1:1 to a character.
        var rtl = shaper.Shape("אבג", font, Style.FontSize, rtl: true);
        Assert.Equal(3, rtl.Glyphs.Length);

        // Visual order: x strictly increases with glyph index.
        for (int i = 1; i < rtl.Glyphs.Length; i++)
            Assert.True(rtl.Glyphs[i].X > rtl.Glyphs[i - 1].X,
                $"glyph {i} at {rtl.Glyphs[i].X} should sit right of glyph {i - 1} at {rtl.Glyphs[i - 1].X}");
        Assert.Equal(0f, rtl.Glyphs[0].X, 2);

        // Glyph 0 is the LAST logical character: shaping that character alone
        // yields the same glyph id.
        var lastChar = shaper.Shape("ג", font, Style.FontSize, rtl: true);
        Assert.Equal(lastChar.Glyphs[0].GlyphId, rtl.Glyphs[0].GlyphId);

        var firstChar = shaper.Shape("א", font, Style.FontSize, rtl: true);
        Assert.Equal(firstChar.Glyphs[0].GlyphId, rtl.Glyphs[^1].GlyphId);
    }
}
