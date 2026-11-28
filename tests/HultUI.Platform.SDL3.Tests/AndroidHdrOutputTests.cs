using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// An HDR film's metadata as the EGL surface attributes Android hands the television
/// (docs/design/TV-VIDEO.md §4): each value in units of 1/50,000, the mastering display only when
/// it is stated, the light levels only when they are. No device needed: this is the conversion.
/// </summary>
public class AndroidHdrOutputTests
{
    private static readonly HdrMetadata Hdr10 = new(
        RedX: 0.708f, RedY: 0.292f, GreenX: 0.17f, GreenY: 0.797f, BlueX: 0.131f, BlueY: 0.046f,
        WhiteX: 0.3127f, WhiteY: 0.329f, MaxLuminance: 1000f, MinLuminance: 0.005f,
        MaxContentLightLevel: 1532f, MaxFrameAverageLightLevel: 312f);

    [Fact]
    public void TheMasteringDisplayAndLightLevels_AreScaledBy50000()
    {
        var attributes = AndroidHdrOutput.EglAttributes(Hdr10).ToDictionary(a => a.Attribute, a => a.Value);

        Assert.Equal(12, attributes.Count);
        Assert.Equal(35_400, attributes[AndroidHdrOutput.EGL_SMPTE2086_DISPLAY_PRIMARY_RX_EXT]);
        Assert.Equal(39_850, attributes[AndroidHdrOutput.EGL_SMPTE2086_DISPLAY_PRIMARY_GY_EXT]);
        Assert.Equal(15_635, attributes[AndroidHdrOutput.EGL_SMPTE2086_WHITE_POINT_X_EXT]);
        Assert.Equal(50_000_000, attributes[AndroidHdrOutput.EGL_SMPTE2086_MAX_LUMINANCE_EXT]);
        Assert.Equal(250, attributes[AndroidHdrOutput.EGL_SMPTE2086_MIN_LUMINANCE_EXT]);
        Assert.Equal(76_600_000, attributes[AndroidHdrOutput.EGL_CTA861_3_MAX_CONTENT_LIGHT_LEVEL_EXT]);
        Assert.Equal(15_600_000, attributes[AndroidHdrOutput.EGL_CTA861_3_MAX_FRAME_AVERAGE_LEVEL_EXT]);
    }

    [Fact]
    public void OnlyWhatTheFilmStates_IsSet()
    {
        var levelsOnly = Hdr10 with { RedX = 0, MaxLuminance = 0 };
        Assert.Equal(
            [AndroidHdrOutput.EGL_CTA861_3_MAX_CONTENT_LIGHT_LEVEL_EXT, AndroidHdrOutput.EGL_CTA861_3_MAX_FRAME_AVERAGE_LEVEL_EXT],
            AndroidHdrOutput.EglAttributes(levelsOnly).Select(a => a.Attribute));

        var masteringOnly = Hdr10 with { MaxContentLightLevel = 0, MaxFrameAverageLightLevel = 0 };
        Assert.Equal(10, AndroidHdrOutput.EglAttributes(masteringOnly).Count);
    }

    /// <summary>A peak past what fits an EGLint at 1/50,000 is clamped, not wrapped negative.</summary>
    [Fact]
    public void AnAbsurdPeak_IsClamped_NotWrapped()
    {
        var attributes = AndroidHdrOutput.EglAttributes(Hdr10 with { MaxLuminance = 1e9f })
            .ToDictionary(a => a.Attribute, a => a.Value);
        Assert.Equal(40_000 * AndroidHdrOutput.MetadataScaling, attributes[AndroidHdrOutput.EGL_SMPTE2086_MAX_LUMINANCE_EXT]);
    }
}
