namespace VesperCommander;

/// <summary>
/// Font-family names used across Vesper Commander. The Nocturne design calls for
/// Inter (UI) + a monospace stack for every path, size, date, and file name.
/// Inter isn't bundled, so UI text falls back to the platform default sans; only
/// the monospace family is pinned to a concrete face the system is sure to have.
/// </summary>
public static class Fonts
{
    /// <summary>Monospace for paths, file names, sizes, and dates.</summary>
    public static readonly string Mono =
        OperatingSystem.IsWindows() ? "Consolas"
        : OperatingSystem.IsMacOS() ? "Menlo"
        : "monospace";
}
