using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Where HDR video lands on the scRGB canvas, and the peak its producer is told — the two
/// settings of <c>HULTUI_VIDEO_WHITE</c> on the two platforms' white scales.
/// </summary>
public class HdrVideoLuminanceTests
{
    // The Windows test machine: SDR white 240 nits (white scale 3), panel peak 455.
    private const float WindowsWhiteScale = 3f;
    // Wayland: the protocol's reference white, 203 over scRGB's 80.
    private const float WaylandWhiteScale = 2.5375f;

    [Fact]
    public void Reference_PutsTheFramesWhiteAtTheNitsItNames()
    {
        // 203 nits on a canvas whose 1.0 is 240: drawn at 203/240, which the blit's x3 turns
        // into scRGB 2.5375 — 203 nits.
        float scale = HdrVideoLuminance.FrameScale(203f, WindowsWhiteScale, matchVideoWhiteToUi: false);
        Assert.Equal(203f / 240f, scale, 5);
        Assert.Equal(2.5375f, scale * WindowsWhiteScale, 4);
    }

    [Fact]
    public void OnWayland_ReferenceAndUiAgree_BecauseTheWhitesAreTheSame()
    {
        Assert.Equal(1f, HdrVideoLuminance.FrameScale(203f, WaylandWhiteScale, false), 4);
        Assert.Equal(1f, HdrVideoLuminance.FrameScale(203f, WaylandWhiteScale, true), 4);
        Assert.Equal(455f, HdrVideoLuminance.ProducerPeakNits(455f, WaylandWhiteScale, false), 2);
        Assert.Equal(455f, HdrVideoLuminance.ProducerPeakNits(455f, WaylandWhiteScale, true), 2);
    }

    [Fact]
    public void Reference_TellsTheProducerTheDisplaysPeak()
    {
        Assert.Equal(455f, HdrVideoLuminance.ProducerPeakNits(455f, WindowsWhiteScale, matchVideoWhiteToUi: false));
    }

    [Fact]
    public void Ui_ShrinksThePeakByTheStretch_SoTheBrightestValueLandsOnThePanelsPeak()
    {
        float peak = HdrVideoLuminance.ProducerPeakNits(455f, WindowsWhiteScale, matchVideoWhiteToUi: true);
        float stretch = HdrVideoLuminance.FrameScale(203f, WindowsWhiteScale, true) * WindowsWhiteScale * 80f / 203f;

        // The producer writes its peak as peak/203; under ui that is drawn unscaled into a
        // canvas whose 1.0 is 240 nits, so it reaches the screen at peak x 240/203.
        Assert.Equal(455f * 203f / 240f, peak, 2);
        Assert.Equal(455f, peak * stretch, 2);
    }

    [Fact]
    public void AnUnknownPeak_StaysUnknown()
    {
        Assert.Equal(0f, HdrVideoLuminance.ProducerPeakNits(0f, WindowsWhiteScale, false));
        Assert.Equal(0f, HdrVideoLuminance.ProducerPeakNits(0f, WindowsWhiteScale, true));
    }
}
