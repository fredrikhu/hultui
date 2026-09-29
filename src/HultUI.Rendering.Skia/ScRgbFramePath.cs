namespace HultUI.Rendering.Skia;

/// <summary>
/// How a frame reaches the swapchain in <see cref="SkiaVulkanRenderSurface"/>. On scRGB the
/// UI's white has to land at the display's SDR white, which is above 1.0, and Skia's colour
/// conversion clamps at 1.0; each path is a different way around that.
/// </summary>
internal enum ScRgbFramePath
{
    /// <summary>Drawn straight onto the swapchain: it is not scRGB, or its white is 1.0.</summary>
    Direct,

    /// <summary>Straight onto the swapchain, through a destination colour space that carries
    /// the white scale (<c>HULTUI_VULKAN_SCALED_SPACE=1</c>).</summary>
    ScaledSpace,

    /// <summary>Into a linear half-float offscreen, then blitted with the white scale.</summary>
    Offscreen,

    /// <summary>Into a gamma-2.2-encoded offscreen that the blit decodes and scales, for a
    /// compositor that decodes sRGB with 2.2 (KWin). Needed even at a white scale of 1.</summary>
    EncodedOffscreen,
}

/// <summary>The choice between the <see cref="ScRgbFramePath"/>s, kept apart so it can be
/// tested.</summary>
internal static class ScRgbFramePaths
{
    public static ScRgbFramePath Choose(bool scRgb, float sdrWhiteScale, bool sdrIsGamma22, bool scaledSpaceEnabled)
    {
        if (!scRgb) return ScRgbFramePath.Direct;
        if (sdrIsGamma22) return ScRgbFramePath.EncodedOffscreen;
        if (sdrWhiteScale <= 1.0001f) return ScRgbFramePath.Direct;
        return scaledSpaceEnabled ? ScRgbFramePath.ScaledSpace : ScRgbFramePath.Offscreen;
    }
}
