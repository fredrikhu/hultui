namespace GitClient.Junction;

/// <summary>
/// Font-family names used across the Junction UI. The design calls for Inter
/// (UI) + a monospace stack (SHAs, branch names, diff code). Inter isn't
/// bundled, so the UI falls back to the platform default sans; only the
/// monospace family is pinned to a concrete face the system is sure to have.
/// </summary>
public static class Fonts
{
    /// <summary>Monospace for SHAs, branch leaf titles, and diff code.</summary>
    public static readonly string Mono =
        OperatingSystem.IsWindows() ? "Consolas"
        : OperatingSystem.IsMacOS() ? "Menlo"
        : "monospace";
}
