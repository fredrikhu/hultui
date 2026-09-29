namespace HultUI.Rendering.Skia;

/// <summary>
/// Where a frame of linear-light video lands on an scRGB canvas, and what peak its producer
/// should tone-map to so that it lands inside the display — the arithmetic behind
/// <see cref="SkiaVulkanRenderSurface"/>'s HDR video, kept apart so it can be tested.
///
/// <para>Three luminance scales meet here. scRGB fixes 1.0 at 80 nits. The canvas's 1.0 is
/// the display's SDR white, <c>sdrWhiteScale</c> × 80 (240 nits on the Windows test machine,
/// the protocol's 203 on Wayland). The frame's 1.0 is whatever its producer says — 203 for
/// libplacebo, the BT.2408 reference white.</para>
/// </summary>
internal static class HdrVideoLuminance
{
    /// <summary>scRGB's 1.0, in nits.</summary>
    public const float ScRgbReferenceNits = 80f;

    /// <summary>BT.2408 reference white: what a producer's 1.0 is taken to be when it has not
    /// yet said. libplacebo normalises to exactly this.</summary>
    public const float Bt2408ReferenceWhiteNits = 203f;

    /// <summary>
    /// What a frame's linear values are multiplied by on the way into the canvas.
    ///
    /// <para>Under the default (<c>reference</c>) policy the frame's white is put at the
    /// luminance it names, so a 1000-nit highlight is 1000 nits and the UI's white may sit
    /// beside it at a different level. Under <c>ui</c> the frame's white is stretched to the
    /// canvas's, so the two agree and every absolute luminance in the film moves by the same
    /// factor.</para>
    /// </summary>
    public static float FrameScale(float frameReferenceWhiteNits, float sdrWhiteScale, bool matchVideoWhiteToUi)
    {
        float canvasWhiteNits = sdrWhiteScale * ScRgbReferenceNits;
        return matchVideoWhiteToUi || canvasWhiteNits <= 0f
            ? 1f
            : frameReferenceWhiteNits / canvasWhiteNits;
    }

    /// <summary>Whether a frame of linear light needs a colour filter at all: to scale it, or to
    /// encode it for a gamma-2.2 offscreen, which it needs whatever the scale.</summary>
    public static bool NeedsFrameFilter(float frameScale, bool encodedOffscreen) =>
        encodedOffscreen || Math.Abs(frameScale - 1f) >= 0.001f;

    /// <summary>
    /// The peak to hand the producer, in the producer's own nits, or zero when the display's
    /// is unknown.
    ///
    /// <para>Under <c>reference</c> the producer's nits are the display's, so it is the
    /// display's peak unchanged. Under <c>ui</c> every value the producer writes is shown
    /// brighter by <c>canvas white / 203</c>, so its peak has to be smaller by the same factor
    /// — otherwise highlights between the two are sent past what the panel can show and clip
    /// there, instead of being tone-mapped by the producer. On the 240-nit, 455-nit Windows
    /// machine that band was 385 to 455 nits.</para>
    /// </summary>
    public static float ProducerPeakNits(float displayPeakNits, float sdrWhiteScale, bool matchVideoWhiteToUi)
    {
        if (displayPeakNits <= 0f) return 0f;
        float canvasWhiteNits = sdrWhiteScale * ScRgbReferenceNits;
        if (!matchVideoWhiteToUi || canvasWhiteNits <= 0f) return displayPeakNits;
        return displayPeakNits * Bt2408ReferenceWhiteNits / canvasWhiteNits;
    }
}
