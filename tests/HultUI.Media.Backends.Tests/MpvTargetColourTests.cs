using HultUI.Media.Mpv;
using Xunit;

namespace HultUI.Media.Backends.Tests;

/// <summary>
/// What mpv is told about its target, and what the frame in the target is labelled with —
/// which must be what that frame was drawn under, not what mpv has just been told. No libmpv
/// needed: this is the bookkeeping, not the player.
/// </summary>
public class MpvTargetColourTests
{
    [Fact]
    public void AFrameDrawnEncoded_StaysEncoded_UntilOneIsDrawnForTheNewTarget()
    {
        var colour = new MpvTargetColour();
        colour.Update(wantsLinearLight: false, peakNits: 0f);
        colour.FrameDrawn();
        Assert.Equal(0f, colour.ReferenceWhiteNits);

        // The window moves to an HDR monitor while the film is paused: mpv is asked at once...
        Assert.True(colour.Update(wantsLinearLight: true, peakNits: 455f));
        Assert.True(colour.Linear);
        // ...but the frame in the target is still the encoded one, and says so.
        Assert.Equal(0f, colour.ReferenceWhiteNits);

        colour.Applied(colour.Generation, linear: true);
        Assert.True(colour.AppliedSinceLastFrame);   // so the paused frame is drawn again
        colour.FrameDrawn();
        Assert.Equal(203f, colour.ReferenceWhiteNits);
        Assert.False(colour.AppliedSinceLastFrame);
    }

    [Fact]
    public void AFrameDrawnBeforeMpvConfirms_KeepsTheOldLabel()
    {
        // The options are set asynchronously. A frame mpv draws before its core has taken them
        // is drawn under the old ones, and must not be labelled as light.
        var colour = new MpvTargetColour();
        colour.Update(wantsLinearLight: true, peakNits: 455f);
        Assert.False(colour.AppliedSinceLastFrame);

        colour.FrameDrawn();
        Assert.Equal(0f, colour.ReferenceWhiteNits);

        colour.Applied(colour.Generation, linear: true);
        colour.FrameDrawn();
        Assert.Equal(203f, colour.ReferenceWhiteNits);
    }

    [Fact]
    public void ALateReplyToAnOlderChange_DoesNotTakeTheLabelBack()
    {
        var colour = new MpvTargetColour();
        colour.Update(true, 455f);
        int first = colour.Generation;
        colour.Update(false, 0f);
        colour.Applied(colour.Generation, linear: false);
        colour.Applied(first, linear: true);     // arrives last, and is older

        colour.FrameDrawn();
        Assert.Equal(0f, colour.ReferenceWhiteNits);
    }

    [Fact]
    public void ReplyTags_RoundTrip_AndOthersAreNotMistakenForThem()
    {
        ulong tag = MpvTargetColour.ReplyTag(generation: 1234567, linear: true, option: 2);
        Assert.True(MpvTargetColour.TryReadReplyTag(tag, out int generation, out bool linear));
        Assert.Equal(1234567, generation);
        Assert.True(linear);

        Assert.False(MpvTargetColour.TryReadReplyTag(0, out _, out _));
        Assert.False(MpvTargetColour.TryReadReplyTag(3, out _, out _));   // an observe id
    }

    [Fact]
    public void AChange_IsReportedOnce_SoTheCallerRedrawsOnce()
    {
        var colour = new MpvTargetColour();
        Assert.True(colour.Update(true, 455f));
        Assert.False(colour.Update(true, 455f));
        // A new peak is a change too: the frame was tone-mapped to the old one.
        Assert.True(colour.Update(true, 600f));
        Assert.True(colour.Update(false, 600f));
        Assert.False(colour.Update(false, 900f));   // the peak means nothing for an encoded target
    }

    [Theory]
    [InlineData(0f, 1000f)]        // unknown: assume a common mastering peak
    [InlineData(455f, 455f)]
    [InlineData(5f, 10f)]          // below what mpv accepts: clamped, not silently refused
    [InlineData(40000f, 10000f)]
    public void ThePeak_IsAssumedWhenUnknown_AndKeptInsideWhatMpvAccepts(float asked, float told)
    {
        var colour = new MpvTargetColour();
        colour.Update(true, asked);
        Assert.Equal(told, colour.PeakNits);
    }

    [Theory]
    [InlineData(0x8058u, 8)]    // GL_RGBA8
    [InlineData(0x881Au, 16)]   // GL_RGBA16F
    [InlineData(0x8814u, 16)]   // GL_RGBA32F: 32 is undefined behaviour in mpv's dither
    public void TheDepth_MpvIsTold_NeverExceeds16(uint internalFormat, int depth) =>
        Assert.Equal(depth, MpvBackend.DepthOf(internalFormat));
}
