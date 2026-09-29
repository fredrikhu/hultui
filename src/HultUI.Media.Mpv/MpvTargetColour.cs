namespace HultUI.Media.Mpv;

/// <summary>
/// What mpv has been asked to draw for, what it has confirmed, and — separately — what the frame
/// already sitting in the target was drawn with.
///
/// <para>The three differ for a while after every change. A window moved from an SDR monitor to an
/// HDR one asks for linear light at once, but mpv applies the options only when its core gets to
/// them, and a paused film keeps the frame it rendered as an encoded picture until something draws
/// again. Labelling that frame with the new setting drew encoded values as light — dark and crushed
/// — for as long as the film stayed paused. So the label follows the frame, the frame follows what
/// mpv has confirmed, and a confirmation asks for the frame to be drawn again.</para>
///
/// <para><b>Why confirmation, and not simply "set".</b> The options are set asynchronously. Set
/// synchronously from the render thread, each <c>mpv_set_property</c> waited for mpv's core while
/// the core waited for its video output and the output waited for the render that thread was
/// holding up: 360 ms on the first HDR frame, broken only by mpv's own timeout, which logged
/// <c>render() not being called or stuck</c> once per property.</para>
///
/// <para>Threads: <see cref="Update"/>, <see cref="AppliedSinceLastFrame"/> and
/// <see cref="FrameDrawn"/> on the render thread; <see cref="Applied"/> on mpv's event thread.</para>
/// </summary>
internal sealed class MpvTargetColour
{
    /// <summary>
    /// libplacebo's linear output puts 1.0 at the BT.2408 reference white of 203 nits.
    ///
    /// <para>Measured rather than assumed: <c>spikes/HdrInterop --range</c> reads back a peak
    /// of exactly 1.0000 at <c>target-peak=203</c>, 2.0000 at 406 and 5.0000 at 1015. It is
    /// also the number the Wayland colour-management protocol names as the reference white of
    /// Windows-scRGB, which is why the two ends of this pipeline need no fudge between
    /// them.</para>
    /// </summary>
    public const float LibplaceboReferenceWhiteNits = 203f;

    /// <summary>What <c>target-peak</c> is told when the renderer does not know the display's
    /// peak: a common mastering peak, and wrong on any particular monitor.</summary>
    public const float AssumedPeakNits = 1000f;

    /// <summary>The range mpv accepts for <c>target-peak</c>. A value outside it makes the set
    /// fail — silently, unless its return code is read — and leaves the old peak in force.</summary>
    public const float MinPeakNits = 10f;
    public const float MaxPeakNits = 10000f;

    /// <summary>How many options one change sets: prim, trc, peak.</summary>
    public const int OptionsPerChange = 3;

    /// <summary>Whether mpv has been asked to write linear light.</summary>
    public bool Linear { get; private set; }

    /// <summary>The <c>target-peak</c> mpv has been asked for, when <see cref="Linear"/>.</summary>
    public float PeakNits { get; private set; }

    /// <summary>Counts changes asked for; each one's replies carry it.</summary>
    public int Generation { get; private set; }

    // (generation << 1) | linear, of the newest change mpv has confirmed. Generation 0 is the
    // state mpv starts in — its own "auto", an encoded picture — and needs no confirming.
    private long _applied;

    private int _drawnGeneration;
    private bool _frameLinear;

    /// <summary>
    /// Records what the target wants now. True when that differs from what mpv was last asked
    /// for: the caller must set the options, tagged with the new <see cref="Generation"/>.
    /// </summary>
    public bool Update(bool wantsLinearLight, float peakNits)
    {
        float peak = Math.Clamp(peakNits > 0f ? peakNits : AssumedPeakNits, MinPeakNits, MaxPeakNits);
        if (wantsLinearLight == Linear && (!Linear || peak == PeakNits)) return false;

        Linear = wantsLinearLight;
        PeakNits = wantsLinearLight ? peak : 0f;
        Generation++;
        return true;
    }

    /// <summary>
    /// mpv has confirmed every option of that change. Only ever moves forward: a slow reply to an
    /// older change arriving after a newer one's does not take the label back.
    /// </summary>
    public void Applied(int generation, bool linear)
    {
        long value = ((long)generation << 1) | (linear ? 1L : 0L);
        long seen;
        do
        {
            seen = Volatile.Read(ref _applied);
            if ((seen >> 1) >= generation) return;
        }
        while (Interlocked.CompareExchange(ref _applied, value, seen) != seen);
    }

    /// <summary>Whether mpv has confirmed a change the frame in the target was not drawn with,
    /// so it has to be drawn again even if mpv has nothing new.</summary>
    public bool AppliedSinceLastFrame => (int)(Volatile.Read(ref _applied) >> 1) != _drawnGeneration;

    /// <summary>A frame was drawn into the target, under whatever mpv had confirmed by then.</summary>
    public void FrameDrawn()
    {
        long applied = Volatile.Read(ref _applied);
        _drawnGeneration = (int)(applied >> 1);
        _frameLinear = (applied & 1) != 0;
    }

    /// <summary>What 1.0 means in the frame in the target — see
    /// <see cref="HultUI.Core.Rendering.GpuFrame.ReferenceWhiteNits"/>.</summary>
    public float ReferenceWhiteNits => _frameLinear ? LibplaceboReferenceWhiteNits : 0f;

    /// <summary>The reply tag for one option of a change: generation, whether it asked for linear
    /// light, and which option — in a range no other request of this backend uses.</summary>
    public static ulong ReplyTag(int generation, bool linear, int option) =>
        ReplyTagBase | ((ulong)(uint)generation << 8) | (linear ? 0x10UL : 0UL) | (ulong)option;

    /// <summary>Reads a tag back; false for a reply that is not one of these.</summary>
    public static bool TryReadReplyTag(ulong tag, out int generation, out bool linear)
    {
        generation = 0;
        linear = false;
        if ((tag & ReplyTagMask) != ReplyTagBase) return false;
        generation = (int)(uint)((tag >> 8) & 0xFFFF_FFFF);
        linear = (tag & 0x10) != 0;
        return true;
    }

    private const ulong ReplyTagBase = 0x7C00_0000_0000_0000UL;
    private const ulong ReplyTagMask = 0xFF00_0000_0000_0000UL;
}
