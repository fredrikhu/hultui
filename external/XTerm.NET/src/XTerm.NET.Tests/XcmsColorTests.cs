using XTerm.Common;

namespace XTerm.Tests;

/// <summary>
/// The Xcms device-independent colour pipeline, checked against the byte-exact values xterm
/// produces with the same default screen data -- which is the whole reason the port exists.
/// </summary>
public class XcmsColorTests
{
    [Theory]
    [InlineData("rgbi:1/1/1", 0xFFFFFF)]
    [InlineData("rgbi:0.5/0.5/0.5", 0xC1BBBB)]      // per-channel intensity tables, not a gamma
    [InlineData("CIEXYZ:0.5/0.5/0.5", 0xDDB5A0)]
    [InlineData("CIELab:1/1/1", 0x6C6767)]
    [InlineData("TekHVC:1/1/1", 0x1A130F)]
    public void Converts_exactly_as_xterm_does(string spec, int expected)
    {
        Assert.True(ColorSpec.TryParse(spec, out var rgb));
        Assert.Equal(expected, rgb);
    }

    [Theory]
    [InlineData("CIEXYZ:1/1/1", 0xFFFFFF)]          // brighter than the screen: compresses to white
    [InlineData("CIEuvY:0.5/0.5/0.5", 0xFFA3AE)]    // out of gamut: chroma clipped, hue held
    [InlineData("CIExyY:0.5/0.5/0.5", 0xF7B30E)]
    [InlineData("CIELuv:1/1/1", 0x16140E)]
    public void Out_of_gamut_takes_the_TekHVC_chroma_clip(string spec, int expected)
    {
        Assert.True(ColorSpec.TryParse(spec, out var rgb));
        Assert.Equal(expected, rgb);
    }

    [Theory]
    [InlineData("#fff", 0xF0F0F0)]                   // hash LEFT-JUSTIFIES; rgb: scales
    [InlineData("#f00f00f00", 0xF0F0F0)]
    [InlineData("#f00ff00ff00f", 0xF0F0F0)]
    [InlineData("rgb:f/f/f", 0xFFFFFF)]
    public void Hash_forms_truncate_where_rgb_forms_scale(string spec, int expected)
    {
        Assert.True(ColorSpec.TryParse(spec, out var rgb));
        Assert.Equal(expected, rgb);
    }
}
