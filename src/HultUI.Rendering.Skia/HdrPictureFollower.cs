using HultUI.Core.Rendering;

namespace HultUI.Rendering.Skia;

/// <summary>
/// Whether an HDR picture is on screen, frame by frame, for a Vulkan window whose swapchain is
/// half-float scRGB only while one is (docs/design/HDR.md, Defaults) — the rule, kept apart from
/// the surface so it can be tested without a GPU.
///
/// <para><b>Why.</b> At rest on an HDR display, the half-float swapchain kept an NVIDIA (610) DXGI
/// thread waking about 96 times a second, and held 31 MB of swapchain where 8-bit holds 16, plus
/// about 26 MB more for good after its first replacement. An 8-bit swapchain shows everything but
/// HDR video the same.</para>
///
/// <para><b>The rule.</b> After each frame: an HDR picture drawn means shown; a GPU source that does
/// not know its picture yet (a video loading) leaves the answer as it was; otherwise not shown. A
/// paused video is still drawn, so it keeps its swapchain: a switch on every pause would rebuild
/// twice and change the picture. A video loading between two HDR files — Reel opens a new player
/// for each — keeps the half-float swapchain through the load. With <c>HULTUI_VIDEO_HDR=0</c> no
/// picture is drawn as HDR, so none counts.</para>
/// </summary>
internal sealed class HdrPictureFollower
{
    private PictureRange _frame;

    /// <summary>Whether an HDR picture was on screen in the last frame that decided.</summary>
    public bool Shown { get; private set; }

    /// <summary>A frame opens: it has drawn nothing yet.</summary>
    public void BeginFrame() => _frame = PictureRange.Sdr;

    /// <summary>A GPU source with this picture was drawn.</summary>
    public void Drawn(PictureRange range)
    {
        if (range > _frame) _frame = range;
    }

    /// <summary>The frame was presented. Returns whether <see cref="Shown"/> changed.</summary>
    public bool EndFrame(bool hdrVideoEnabled)
    {
        bool shown = _frame switch
        {
            PictureRange.Hdr => hdrVideoEnabled,
            PictureRange.Unknown => Shown,
            _ => false,
        };
        if (shown == Shown) return false;
        Shown = shown;
        return true;
    }
}
