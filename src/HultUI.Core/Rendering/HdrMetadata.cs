namespace HultUI.Core.Rendering;

/// <summary>
/// An HDR video's static metadata, as its stream or container states it: the display it was
/// mastered on (SMPTE ST 2086: the primaries and white point as CIE 1931 xy, the luminance range in
/// nits) and its light levels (CTA-861.3: the brightest pixel, MaxCLL, and the brightest frame's
/// average, MaxFALL, in nits). A television tone-maps with it; without it, it assumes its own
/// defaults (docs/design/TV-VIDEO.md §4). Zero for a value the video does not state.
/// </summary>
internal readonly record struct HdrMetadata(
    float RedX, float RedY, float GreenX, float GreenY, float BlueX, float BlueY,
    float WhiteX, float WhiteY, float MaxLuminance, float MinLuminance,
    float MaxContentLightLevel, float MaxFrameAverageLightLevel)
{
    /// <summary>Whether it states the mastering display: its primaries, white point and peak.</summary>
    public bool HasMasteringDisplay => MaxLuminance > 0f && RedX > 0f && GreenX > 0f && BlueX > 0f && WhiteX > 0f;

    /// <summary>Whether it states the content's light levels.</summary>
    public bool HasLightLevels => MaxContentLightLevel > 0f || MaxFrameAverageLightLevel > 0f;
}

/// <summary>A GPU frame renderer that knows its HDR video's metadata.</summary>
internal interface IHdrMetadataSource
{
    /// <summary>The metadata of the picture now; null for none (an SDR picture, or nothing stated).
    /// Any thread.</summary>
    HdrMetadata? HdrMetadata { get; }
}
