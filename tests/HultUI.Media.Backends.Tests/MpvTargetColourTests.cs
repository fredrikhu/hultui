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
    public void WideGamut_IsAChangeForALinearTarget_AndMeaninglessForAnEncodedOne()
    {
        var colour = new MpvTargetColour();
        Assert.True(colour.Update(true, 455f, wideGamut: true));
        Assert.True(colour.WideGamut);
        Assert.False(colour.Update(true, 455f, wideGamut: true));
        Assert.True(colour.Update(true, 455f, wideGamut: false));   // back to mapping inside BT.709

        Assert.True(colour.Update(false, 0f, wideGamut: true));
        Assert.False(colour.WideGamut);                               // no negatives in an encoded target
        Assert.False(colour.Update(false, 0f, wideGamut: false));
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
    [InlineData(0x8059u, 10)]   // GL_RGB10_A2: an Android TV's PQ target
    public void TheDepth_MpvIsTold_NeverExceeds16(uint internalFormat, int depth) =>
        Assert.Equal(depth, MpvBackend.DepthOf(internalFormat));

    /// <summary>
    /// An Android TV's surface asks for PQ while an HDR video is on screen, and for the ordinary
    /// encoded picture after (docs/design/TV-VIDEO.md §4): one change each way, and a PQ frame is
    /// encoded, so it carries no reference white for the renderer to place.
    /// </summary>
    [Fact]
    public void APqTarget_IsAskedForOnce_EachWay_AndItsFramesStayEncoded()
    {
        var colour = new MpvTargetColour();
        Assert.False(colour.Update(wantsLinearLight: false, peakNits: 0f));

        Assert.True(colour.Update(wantsLinearLight: false, peakNits: 0f, pq: true));
        Assert.True(colour.Pq);
        Assert.False(colour.Linear);
        Assert.False(colour.Update(wantsLinearLight: false, peakNits: 0f, pq: true));
        colour.Applied(colour.Generation, linear: false);
        colour.FrameDrawn();
        Assert.Equal(0f, colour.ReferenceWhiteNits);

        Assert.True(colour.Update(wantsLinearLight: false, peakNits: 0f));
        Assert.False(colour.Pq);
    }

    /// <summary>PQ and linear light are two different targets; a surface asking for both gets
    /// linear light, which the Vulkan path means.</summary>
    [Fact]
    public void LinearLight_WinsOverPq()
    {
        var colour = new MpvTargetColour();
        Assert.True(colour.Update(wantsLinearLight: true, peakNits: 455f, pq: true));
        Assert.True(colour.Linear);
        Assert.False(colour.Pq);
    }

    /// <summary>
    /// The film's metadata as mpv reports it in <c>video-params</c>: the mastering display (BT.2020
    /// primaries, D65, 1000 nits down to 0.005) and the light levels. A stream that states neither
    /// has none, and the television assumes its own.
    /// </summary>
    [Fact]
    public void TheMetadata_IsReadFromVideoParams()
    {
        var hdr10 = new Dictionary<string, double>
        {
            ["prim-red-x"] = 0.708, ["prim-red-y"] = 0.292, ["prim-green-x"] = 0.17, ["prim-green-y"] = 0.797,
            ["prim-blue-x"] = 0.131, ["prim-blue-y"] = 0.046, ["prim-white-x"] = 0.3127, ["prim-white-y"] = 0.329,
            ["max-luma"] = 1000, ["min-luma"] = 0.005, ["max-cll"] = 1532, ["max-fall"] = 312,
        };
        var metadata = MpvBackend.MetadataOf(name => hdr10.TryGetValue(name, out var v) ? v : null);

        Assert.NotNull(metadata);
        Assert.True(metadata.Value.HasMasteringDisplay);
        Assert.True(metadata.Value.HasLightLevels);
        Assert.Equal(0.708f, metadata.Value.RedX);
        Assert.Equal(0.3127f, metadata.Value.WhiteX);
        Assert.Equal(1000f, metadata.Value.MaxLuminance);
        Assert.Equal(0.005f, metadata.Value.MinLuminance);
        Assert.Equal(1532f, metadata.Value.MaxContentLightLevel);
        Assert.Equal(312f, metadata.Value.MaxFrameAverageLightLevel);

        var levelsOnly = MpvBackend.MetadataOf(name => name is "max-cll" ? 800 : null);
        Assert.False(levelsOnly!.Value.HasMasteringDisplay);
        Assert.True(levelsOnly.Value.HasLightLevels);

        Assert.Null(MpvBackend.MetadataOf(_ => null));
        Assert.Null(MpvBackend.MetadataOf(_ => double.NaN));
    }

    /// <summary>mpv gives the mastering primaries' coordinates only where they differ from the
    /// video's named primaries: without them, a stated peak takes the named ones, D65 white.</summary>
    [Fact]
    public void WithoutPrimaryCoordinates_TheNamedPrimariesAreTheMasteringDisplays()
    {
        var metadata = MpvBackend.MetadataOf(name => name is "max-luma" ? 4000 : null, "bt.2020");

        Assert.True(metadata!.Value.HasMasteringDisplay);
        Assert.Equal(0.708f, metadata.Value.RedX);
        Assert.Equal(0.046f, metadata.Value.BlueY);
        Assert.Equal(0.3127f, metadata.Value.WhiteX);
        Assert.Equal(4000f, metadata.Value.MaxLuminance);

        // A peak alone, with primaries this cannot place, is no mastering display.
        Assert.Null(MpvBackend.MetadataOf(name => name is "max-luma" ? 4000 : null, "unknown"));
    }

    /// <summary>
    /// libplacebo puts PQ's ceiling, 10,000 nits, in <c>max-luma</c> when a video states no peak (a
    /// 4K HDR10 film through MediaCodec on a Shield). That is no metadata, and a television told it
    /// would tone-map for a 10,000-nit master.
    /// </summary>
    [Fact]
    public void PqsCeiling_IsNoStatedPeak()
    {
        Assert.Null(MpvBackend.MetadataOf(name => name switch { "max-luma" => 10000, "min-luma" => 0.005, _ => null }, "bt.2020"));

        var levels = MpvBackend.MetadataOf(name => name switch { "max-luma" => 10000, "max-cll" => 900, _ => null }, "bt.2020");
        Assert.False(levels!.Value.HasMasteringDisplay);
        Assert.Equal(900f, levels.Value.MaxContentLightLevel);
    }
}
