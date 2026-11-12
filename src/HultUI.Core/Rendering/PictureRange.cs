namespace HultUI.Core.Rendering;

/// <summary>
/// What a GPU source's picture is, for a Vulkan window whose swapchain follows the pictures on
/// screen: the half-float scRGB swapchain only while an HDR video is shown on an HDR display, and
/// 8-bit otherwise (docs/design/HDR.md, Defaults). Ordered: a frame's answer is the most of what
/// its sources said, and a frame that drew none says <see cref="Sdr"/>.
/// </summary>
internal enum PictureRange : byte
{
    /// <summary>An ordinary picture, or none: every source but a video, and a video whose own
    /// transfer is SDR.</summary>
    Sdr = 0,

    /// <summary>Not known yet: a video still loading. The swapchain stays as it is, so the next
    /// HDR episode after an HDR one keeps the half-float swapchain through the load.</summary>
    Unknown = 1,

    /// <summary>The video's own transfer is PQ or HLG.</summary>
    Hdr = 2,
}

/// <summary>
/// A GPU frame renderer that knows its picture's range. mpv reads it from the video, never from
/// the target it draws into, so an 8-bit swapchain, whose target asks for an encoded picture, can
/// still learn that an HDR picture is on screen.
/// </summary>
internal interface IPictureRangeSource
{
    /// <summary>What the picture is now. Any thread.</summary>
    PictureRange PictureRange { get; }
}

/// <summary>
/// A surface whose swapchain follows the pictures drawn on it. <see cref="GpuFrameSink"/> tells it,
/// on every draw of its source, what that source's picture is.
/// </summary>
internal interface IPictureRangeSurface
{
    /// <summary>A GPU source with this picture was drawn in the frame now open. Render thread.</summary>
    void PictureDrawn(PictureRange range);

    /// <summary>
    /// True once after a frame whose pictures changed what the swapchain should be: the swapchain is
    /// rebuilt as the next frame begins, and that frame is owed though nothing in the tree changed.
    /// The window asks after every present.
    /// </summary>
    bool TakeOwedFrame();
}

/// <summary>
/// The half of a Vulkan host whose swapchain format follows the pictures on screen; the surface
/// tells it (<see cref="IPictureRangeSurface"/>).
/// </summary>
internal interface IVulkanPictureHost
{
    /// <summary>
    /// Whether an HDR picture is on screen now. Returns whether the swapchain is rebuilt for it,
    /// which happens at the next acquire. Render thread, between frames.
    /// </summary>
    bool SetHdrPictureShown(bool shown);
}
