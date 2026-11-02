using System.Globalization;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;

namespace HultUI.Headless;

/// <summary>
/// Builds simulated monitors for <see cref="HeadlessOptions.Monitors"/> and
/// <see cref="HeadlessSession.SetMonitors"/>: one at a time, or a row of them standing side by
/// side the way a multi-monitor desk is set up.
/// <code>
/// var desk = HeadlessMonitors.Row((1920, 1080), (2560, 1440), (1440, 2560));   // the last one rotated
/// using var session = HeadlessSession.Create(options: new HeadlessOptions { Monitors = desk });
/// </code>
/// </summary>
public static class HeadlessMonitors
{
    /// <summary>
    /// One monitor of <paramref name="width"/> × <paramref name="height"/> pixels at
    /// (<paramref name="x"/>, <paramref name="y"/>). A <paramref name="taskBar"/> height takes
    /// that much off the bottom of its work area.
    /// </summary>
    public static MonitorInfo Create(string id, int x, int y, int width, int height, float scale = 1f,
        bool primary = false, int taskBar = 0, string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        var bounds = new Rect(x, y, width, height);
        var work = new Rect(x, y, width, Math.Max(1, height - taskBar));
        return new MonitorInfo(id, name ?? id, bounds, work, scale, primary, 60f);
    }

    /// <summary>
    /// Monitors standing in a row, left to right, top edges aligned, each starting where the
    /// previous one ends. The first is the primary and carries a task bar of
    /// <paramref name="taskBar"/> pixels; ids are <c>DISPLAY1</c>, <c>DISPLAY2</c>, … in order.
    /// </summary>
    public static IReadOnlyList<MonitorInfo> Row(int taskBar, params (int Width, int Height)[] sizes)
    {
        var monitors = new List<MonitorInfo>(sizes.Length);
        int x = 0;
        for (int i = 0; i < sizes.Length; i++)
        {
            var (w, h) = sizes[i];
            string id = "DISPLAY" + (i + 1).ToString(CultureInfo.InvariantCulture);
            monitors.Add(Create(id, x, 0, w, h, primary: i == 0, taskBar: i == 0 ? taskBar : 0,
                name: "Display " + (i + 1).ToString(CultureInfo.InvariantCulture)));
            x += w;
        }
        return monitors;
    }

    /// <summary>A row of monitors, as the overload taking a task bar, with no task bar.</summary>
    public static IReadOnlyList<MonitorInfo> Row(params (int Width, int Height)[] sizes) => Row(0, sizes);
}
