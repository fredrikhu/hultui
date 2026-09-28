namespace HultUI.Media.Mpv;

/// <summary>
/// What mpv has been told about the target it draws into, and — separately — what the frame
/// already sitting in that target was drawn with.
///
/// <para>The two differ for as long as mpv has no new frame to draw. A window moved from an SDR
/// monitor to an HDR one asks for linear light at once, but a paused film keeps the frame it
/// rendered as an encoded picture. Labelling that frame with the new setting drew encoded values
/// as light — dark and crushed — for as long as the film stayed paused. So the label follows the
/// frame, and a change of setting is reported so the caller draws the frame again.</para>
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

    /// <summary>Whether mpv has been told to write linear light.</summary>
    public bool Linear { get; private set; }

    /// <summary>The <c>target-peak</c> mpv has been told, when <see cref="Linear"/>.</summary>
    public float PeakNits { get; private set; }

    private bool _frameLinear;

    /// <summary>
    /// Records what the target wants now. True when that differs from what mpv was last told:
    /// the caller must set the options, and must draw the frame again even if mpv has nothing
    /// new, because the frame in the target was drawn for the old ones.
    /// </summary>
    public bool Update(bool wantsLinearLight, float peakNits)
    {
        float peak = Math.Clamp(peakNits > 0f ? peakNits : AssumedPeakNits, MinPeakNits, MaxPeakNits);
        if (wantsLinearLight == Linear && (!Linear || peak == PeakNits)) return false;

        Linear = wantsLinearLight;
        PeakNits = wantsLinearLight ? peak : 0f;
        return true;
    }

    /// <summary>A frame was drawn into the target under the current options.</summary>
    public void FrameDrawn() => _frameLinear = Linear;

    /// <summary>What 1.0 means in the frame in the target — see
    /// <see cref="HultUI.Core.Rendering.GpuFrame.ReferenceWhiteNits"/>.</summary>
    public float ReferenceWhiteNits => _frameLinear ? LibplaceboReferenceWhiteNits : 0f;
}
