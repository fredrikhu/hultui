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
    /// Draws every piece of text in the fonts bundled with this assembly (DejaVu Sans in four
    /// styles and DejaVu Sans Mono, whatever family a style names), as glyph outlines through Skia's own path
    /// rasteriser, and never falls back to an installed font. A frame then comes out the same on
    /// Windows and Linux — the configuration <see cref="GoldenImage.Match(HeadlessWindow, string, GoldenTolerance?, string)"/> baselines are captured in.
    /// Text measures in the same fonts, so layout is the same too. A character the fonts lack
    /// (CJK, emoji) draws as the missing-glyph box.
    /// </summary>
    public bool PinnedText { get; set; }

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

    /// <summary>
    /// Pixels that match across operating systems: the configuration for golden images
    /// (<see cref="GoldenImage.Match(HeadlessWindow, string, GoldenTolerance?, string)"/>) committed to a repo that CI renders on another OS.
    /// </summary>
    public static HeadlessOptions Golden() => new() { PinnedText = true };
}
