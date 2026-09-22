namespace Aperture;

/// <summary>
/// Font families for the Aperture UI. Nothing is bundled — the precedent the other three
/// apps set — so the interface uses the platform's default sans and only the monospace
/// family is pinned to a face every system has. Mono is rarer here than in the workbench
/// apps: it is for figures that should line up (runtimes, bitrates, the server version),
/// not for titles, which are the artwork's neighbours and want the reading face.
/// </summary>
public static class Fonts
{
    public static readonly string Mono =
        OperatingSystem.IsWindows() ? "Consolas"
        : OperatingSystem.IsMacOS() ? "Menlo"
        : "monospace";
}
