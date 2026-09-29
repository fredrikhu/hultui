using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// How a Vulkan frame reaches the swapchain, which decides whether the UI's white lands at the
/// display's SDR white. The machines in the comments are the ones each path was built for.
/// </summary>
public class ScRgbFramePathTests
{
    [Fact]
    public void An8BitSwapchain_IsDrawnDirectly_WhateverTheDisplaySays()
    {
        Assert.Equal(ScRgbFramePath.Direct, ScRgbFramePaths.Choose(scRgb: false, 3f, sdrIsGamma22: false, scaledSpaceEnabled: false));
        Assert.Equal(ScRgbFramePath.Direct, ScRgbFramePaths.Choose(scRgb: false, 3f, sdrIsGamma22: true, scaledSpaceEnabled: true));
    }

    [Fact]
    public void ScRgbWithWhiteAt1_IsDrawnDirectly()
    {
        // An SDR display, or an HDR one with SDR white at scRGB's 80 nits.
        Assert.Equal(ScRgbFramePath.Direct, ScRgbFramePaths.Choose(true, 1f, false, false));
        Assert.Equal(ScRgbFramePath.Direct, ScRgbFramePaths.Choose(true, 1f, false, scaledSpaceEnabled: true));
    }

    [Fact]
    public void ScRgbWithWhiteAbove1_GoesThroughTheOffscreen()
    {
        // Windows, SDR white 240 nits.
        Assert.Equal(ScRgbFramePath.Offscreen, ScRgbFramePaths.Choose(true, 3f, false, false));
    }

    [Fact]
    public void TheScaledSpace_ReplacesTheOffscreen_OnlyWhenTurnedOn()
    {
        Assert.Equal(ScRgbFramePath.ScaledSpace, ScRgbFramePaths.Choose(true, 3f, false, scaledSpaceEnabled: true));
    }

    [Fact]
    public void AGamma22Compositor_AlwaysGetsTheEncodedOffscreen()
    {
        // KWin: white is the protocol's 203 nits (scale 2.5375), and the 2.2 decode is needed
        // even at a white of 1. A transfer function cannot express a re-encode, so the scaled
        // space does not apply either.
        Assert.Equal(ScRgbFramePath.EncodedOffscreen, ScRgbFramePaths.Choose(true, 2.5375f, sdrIsGamma22: true, false));
        Assert.Equal(ScRgbFramePath.EncodedOffscreen, ScRgbFramePaths.Choose(true, 1f, sdrIsGamma22: true, false));
        Assert.Equal(ScRgbFramePath.EncodedOffscreen, ScRgbFramePaths.Choose(true, 2.5375f, sdrIsGamma22: true, scaledSpaceEnabled: true));
    }

    [Theory]
    [InlineData(1f, false, false)]      // linear light at the canvas's white: drawn as it is
    [InlineData(1f, true, true)]        // an encoded offscreen needs the 2.2 encode at any scale
    [InlineData(0.846f, false, true)]   // 203 nits on a 240-nit canvas
    [InlineData(0.9995f, false, false)] // within rounding of 1
    public void AVideoFrame_IsFilteredWhenScaledOrEncoded(float scale, bool encoded, bool filtered) =>
        Assert.Equal(filtered, HdrVideoLuminance.NeedsFrameFilter(scale, encoded));
}
