using HultUI.Core.Primitives;

namespace HultUI.Core.Platform;

/// <summary>Which way up a monitor is: wider than tall, or taller than wide.</summary>
public enum MonitorOrientation
{
    Landscape,
    Portrait,
}

/// <summary>
/// One monitor of the desktop as the platform reports it: where it is, the part of it windows
/// may use, how much the OS scales it, and whether it is the primary one. Read through
/// <c>Application.Monitors</c>; changes arrive as <c>Application.MonitorsChanged</c>.
/// </summary>
/// <remarks>
/// <para><b>In the desktop units <see cref="WindowPlacement"/> uses.</b> Windows reports physical
/// pixels (the process is per-monitor DPI aware); SDL elsewhere its window coordinates. The two
/// records describe the same desktop in the same numbers, so a placement can be computed from a
/// monitor's <see cref="WorkArea"/> and handed to <c>Window.Placement</c> — which is what
/// <c>Window.MoveToMonitor</c> does.</para>
/// <para><b>In no particular order.</b> The platform's own order is not the arrangement on the
/// desk; sort by <see cref="Bounds"/> (<see cref="InReadingOrder"/>) for left to right.</para>
/// </remarks>
/// <param name="Id">Stable for as long as the monitor stays connected: the device name on
/// Windows (<c>\\.\DISPLAY2</c>), SDL's display id elsewhere, whatever a headless session was
/// given. Not stable across a reboot or a re-plug; match saved settings by geometry.</param>
/// <param name="Name">A human name for the monitor, for a settings screen.</param>
/// <param name="Bounds">The whole monitor, on the desktop.</param>
/// <param name="WorkArea">The part of it windows may cover: <see cref="Bounds"/> less the task
/// bar, docks and panels. What a maximized window fills.</param>
/// <param name="Scale">The OS scale factor for the monitor: 1.0 at 96 DPI, 1.5 at 144.</param>
/// <param name="IsPrimary">The monitor with the task bar and the desktop origin.</param>
/// <param name="RefreshRate">Refresh rate in Hz, or 0 when the platform does not say.</param>
public sealed record MonitorInfo(
    string Id,
    string Name,
    Rect Bounds,
    Rect WorkArea,
    float Scale,
    bool IsPrimary,
    float RefreshRate = 0)
{
    /// <summary>Portrait when the monitor is taller than it is wide — rotated, as a monitor
    /// for reading tall pages usually is.</summary>
    public MonitorOrientation Orientation =>
        Bounds.Height > Bounds.Width ? MonitorOrientation.Portrait : MonitorOrientation.Landscape;

    /// <summary>The monitor's DPI: <see cref="Scale"/> × 96, rounded.</summary>
    public int Dpi => (int)MathF.Round(96f * Scale);

    /// <summary>The work area in layout units — the size a maximized window lays out in, when
    /// the bounds are physical pixels (Windows, a headless desktop).</summary>
    public Size WorkAreaLayoutSize => new(WorkArea.Width / Scale, WorkArea.Height / Scale);

    /// <summary>
    /// The monitor <paramref name="bounds"/> belong to: the one it overlaps most, or, overlapping
    /// none, the one nearest its centre. Null only when there are no monitors.
    /// </summary>
    public static MonitorInfo? For(Rect bounds, IReadOnlyList<MonitorInfo> monitors)
    {
        MonitorInfo? best = null;
        float bestOverlap = 0;
        foreach (var m in monitors)
        {
            float o = Overlap(bounds, m.Bounds);
            if (o > bestOverlap) { bestOverlap = o; best = m; }
        }
        if (best is not null) return best;

        float cx = bounds.X + bounds.Width / 2f, cy = bounds.Y + bounds.Height / 2f;
        float bestDistance = float.MaxValue;
        foreach (var m in monitors)
        {
            var a = m.Bounds;
            float dx = Math.Max(Math.Max(a.X - cx, 0), cx - a.Right);
            float dy = Math.Max(Math.Max(a.Y - cy, 0), cy - a.Bottom);
            float d = dx * dx + dy * dy;
            if (d < bestDistance) { bestDistance = d; best = m; }
        }
        return best;
    }

    /// <summary>
    /// <paramref name="monitors"/> as they stand on the desk: left to right, and top to bottom
    /// where two share a column — the order a settings screen numbers them in.
    /// </summary>
    public static IReadOnlyList<MonitorInfo> InReadingOrder(IEnumerable<MonitorInfo> monitors) =>
        monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();

    /// <summary>
    /// A placement on this monitor: <paramref name="width"/> × <paramref name="height"/> (shrunk
    /// to the work area when larger) centred on the work area, maximized or not. The restored
    /// bounds of a maximized window are on the monitor too, so un-maximizing it stays there.
    /// </summary>
    public WindowPlacement Center(int width, int height, bool maximized)
    {
        var area = WorkArea;
        int w = Math.Max(1, Math.Min(width, (int)area.Width));
        int h = Math.Max(1, Math.Min(height, (int)area.Height));
        int x = (int)(area.X + (area.Width - w) / 2);
        int y = (int)(area.Y + (area.Height - h) / 2);
        return new WindowPlacement(x, y, w, h, maximized);
    }

    private static float Overlap(Rect a, Rect b)
    {
        float w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        float h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return w > 0 && h > 0 ? w * h : 0;
    }
}

/// <summary>
/// Implemented by a windowing platform that can list the desktop's monitors. The framework
/// discovers it with a type test, as it does <see cref="IWindowChrome"/>; a platform without it
/// reports no monitors, and an app treats that as "one screen, wherever the window is".
/// </summary>
public interface IMonitorSource
{
    /// <summary>The monitors connected now. Cheap to read: the platform caches the list and
    /// refreshes it when the desktop changes.</summary>
    IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>
    /// Raised on the UI thread, from the event pump, after a monitor was connected or
    /// disconnected, moved, rotated, or changed resolution or scale. <see cref="Monitors"/>
    /// already has the new list.
    /// </summary>
    event Action? MonitorsChanged;
}

/// <summary>How <c>Window.MoveToMonitor</c> sizes the window on its new monitor.</summary>
public enum MonitorFit
{
    /// <summary>Keeps the window's size (shrunk to fit) and centres it on the work area.</summary>
    Centered,

    /// <summary>Maximizes it over the monitor's work area.</summary>
    Maximized,

    /// <summary>Covers the whole monitor with no chrome (windowed fullscreen).</summary>
    Fullscreen,
}
