using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Rendering;

/// <summary>What a measure context that cannot read a font answers for <see cref="IMeasureContext.MeasureFont"/>
/// and <see cref="IMeasureContext.HasGlyph"/>: estimates from a measured "0".</summary>
public class FontMetricsTests
{
    private static readonly ResolvedInheritedStyle Style = StyleResolver.DefaultStyle with
    {
        FontFamily = FontFamilies.Monospace,
        FontSize = 10f,
    };

    [Fact]
    public void TheDefault_EstimatesFromAMeasuredZero()
    {
        IMeasureContext ctx = new StubMeasureContext(charWidth: 7f, lineHeight: 14f);
        var m = ctx.MeasureFont(Style);

        Assert.Equal(7f, m.CharacterWidth);
        Assert.Equal(14f, m.LineHeight, 3);
        Assert.Equal(FontFamilies.Monospace, m.FamilyName);
        Assert.False(m.IsFixedPitch); // it cannot know
        Assert.True(m.Ascent > m.Descent && m.Descent > 0);
        Assert.True(m.UnderlinePosition > 0 && m.UnderlineThickness >= 1f);
        Assert.True(m.StrikeoutPosition < 0);
    }

    [Fact]
    public void TheDefault_SaysEveryGlyphIsThere() =>
        Assert.True(((IMeasureContext)new StubMeasureContext()).HasGlyph(Style, 0x10FFFD));

    [Fact]
    public void AnEstimate_WithNothingMeasured_UsesTheFontSize()
    {
        var m = FontMetrics.Estimate(Style, Size.Zero);
        Assert.Equal(6f, m.CharacterWidth, 3);
        Assert.Equal(12f, m.LineHeight, 3);
    }
}
