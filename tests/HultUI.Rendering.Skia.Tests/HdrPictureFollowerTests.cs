using HultUI.Core.Rendering;
using Xunit;
using static HultUI.Core.Rendering.PictureRange;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// When an HDR picture counts as on screen, frame by frame, for the Vulkan window whose swapchain
/// is half-float only while one is. Each case counts the switches a run of frames asks the host for:
/// every one is a swapchain rebuilt.
/// </summary>
public class HdrPictureFollowerTests
{
    /// <summary>Each frame: what its GPU sources said (none for a frame that drew none).</summary>
    private static (int Switches, bool Shown) Run(bool hdrVideoEnabled, params PictureRange[][] frames)
    {
        var follower = new HdrPictureFollower();
        int switches = 0;
        foreach (var frame in frames)
        {
            follower.BeginFrame();
            foreach (var range in frame) follower.Drawn(range);
            if (follower.EndFrame(hdrVideoEnabled)) switches++;
        }
        return (switches, follower.Shown);
    }

    private static PictureRange[][] Repeat(int count, params PictureRange[] frame) =>
        Enumerable.Repeat(frame, count).ToArray();

    private static PictureRange[][] Frames(params PictureRange[][][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void AnHdrVideoPlayed_PausedAndClosed_SwitchesOnceEachWay()
    {
        var run = Run(true, Frames(
            Repeat(3),                    // the chrome
            Repeat(4, Unknown),           // the player opens; the video loads
            Repeat(30, Hdr),              // it plays
            Repeat(20, Hdr, Sdr),         // paused, with a web page beside it: still drawn
            Repeat(3)));                  // the player closes
        Assert.Equal((2, false), run);
    }

    [Fact]
    public void AnHdrVideo_StaysShownWhilePaused()
    {
        var follower = new HdrPictureFollower();
        follower.BeginFrame();
        follower.Drawn(Hdr);
        Assert.True(follower.EndFrame(true));
        for (int i = 0; i < 50; i++)
        {
            // A paused picture is drawn on every frame the window draws.
            follower.BeginFrame();
            follower.Drawn(Hdr);
            Assert.False(follower.EndFrame(true));
        }
        Assert.True(follower.Shown);
    }

    [Fact]
    public void TheNextHdrFile_KeepsTheSwapchainThroughItsLoad()
    {
        // Reel opens a new player for each file: the next one loads before it can say what it is.
        var run = Run(true, Frames(Repeat(10, Hdr), Repeat(6, Unknown), Repeat(10, Hdr)));
        Assert.Equal((1, true), run);
    }

    [Fact]
    public void AnSdrVideo_NeverSwitches()
    {
        Assert.Equal((0, false), Run(true, Frames(Repeat(3), Repeat(4, Unknown), Repeat(30, Sdr), Repeat(3))));
    }

    [Fact]
    public void AnSdrFileAfterAnHdrOne_SwitchesBack_OnceItSaysSo()
    {
        var run = Run(true, Frames(Repeat(10, Hdr), Repeat(6, Unknown), Repeat(10, Sdr)));
        Assert.Equal((2, false), run);
    }

    [Fact]
    public void WithHdrVideoOff_NoPictureCounts()
    {
        Assert.Equal((0, false), Run(false, Frames(Repeat(3), Repeat(4, Unknown), Repeat(30, Hdr), Repeat(3))));
    }

    [Fact]
    public void AVideoLoading_FirstThing_LeavesTheChromeAsItIs()
    {
        Assert.Equal((0, false), Run(true, Repeat(20, Unknown)));
    }
}
