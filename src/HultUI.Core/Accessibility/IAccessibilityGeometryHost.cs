namespace HultUI.Core.Accessibility;

/// <summary>
/// Supplies live window geometry a platform accessibility bridge needs to report
/// element bounds in the units assistive tech expects. Implemented in the
/// windowing layer (which owns the platform window) and read by the bridges.
/// </summary>
public interface IAccessibilityGeometryHost
{
    /// <summary>
    /// Display scale (device pixels per layout unit). Element bounds are stored
    /// in layout units, but AT-SPI / UIA report device pixels, so bounds are
    /// multiplied by this before being exposed.
    /// </summary>
    float Scale { get; }
}
