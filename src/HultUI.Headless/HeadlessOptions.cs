using HultUI.Core.Rendering;
using HultUI.Headless.Recording;

namespace HultUI.Headless;

/// <summary>Opt-in behaviour for a <see cref="HeadlessSession"/>.</summary>
public sealed class HeadlessOptions
{
    /// <summary>
    /// Captures every drawing call into <see cref="HeadlessWindow.RenderLog"/>
    /// for display-list snapshot testing. Rendering still happens normally, so
    /// pixel capture keeps working.
    /// </summary>
    public bool RecordDrawCalls { get; set; }

    /// <summary>
    /// Replaces real font measurement. Set to a
    /// <see cref="FixedMetricsMeasureContext"/> when snapshot baselines must
    /// match across operating systems — see that type for the trade-off.
    /// Requires <see cref="RecordDrawCalls"/>.
    /// </summary>
    public IMeasureContext? MeasureContextOverride { get; set; }

    /// <summary>
    /// Recording enabled with cross-platform deterministic text measurement:
    /// the configuration for snapshot baselines committed to a repo that CI
    /// runs on a different OS.
    /// </summary>
    public static HeadlessOptions DeterministicRecording() => new()
    {
        RecordDrawCalls = true,
        MeasureContextOverride = new FixedMetricsMeasureContext(),
    };
}
