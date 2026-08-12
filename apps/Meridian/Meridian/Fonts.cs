namespace Meridian;

/// <summary>
/// Font-family names used across the Meridian UI. The Azimuth sheet calls for Inter
/// (UI) + JetBrains Mono; neither is bundled, so — per the precedent set in Junction —
/// the UI falls back to the platform default sans and only the monospace family is
/// pinned to a face the system is sure to have. Bundling the design's faces is a
/// separate decision (installer size + AOT), tracked in the implementation plan.
/// </summary>
public static class Fonts
{
    /// <summary>Monospace for URLs, JSON, paths, chips, key hints — anything greppable.</summary>
    public static readonly string Mono =
        OperatingSystem.IsWindows() ? "Consolas"
        : OperatingSystem.IsMacOS() ? "Menlo"
        : "monospace";
}
