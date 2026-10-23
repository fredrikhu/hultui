namespace HultUI.Headless;

/// <summary>
/// Windows-like windows for <see cref="HeadlessOptions.WindowsFrame"/>: a headless window that
/// behaves the way HultUI's SDL3 window does on Windows, so placement code that passed headless
/// and broke on real monitors fails headless too. Takes effect on a session with
/// <see cref="HeadlessOptions.Monitors"/>.
/// </summary>
/// <remarks>
/// <para><b>Outer rectangles.</b> <c>Window.Placement</c> is the window's outer rectangle, as
/// <c>GetWindowRect</c> gives it: a <see cref="Core.Styling.WindowStyle.Borderless"/> window (the
/// custom frame) keeps the invisible resize borders of the standard frame round its sides and
/// bottom, so its content is smaller than its placement by <see cref="ResizeBorderAt"/> on the
/// left, right and bottom. Maximized, it reaches past its work area by the border on every side,
/// and its content is the work area. A <see cref="Core.Styling.WindowStyle.Default"/> window is
/// modelled with no borders.</para>
/// <para><b>Posted maximize and restore.</b> The custom frame maximizes and restores with a posted
/// <c>WM_SYSCOMMAND</c>, so on a borderless window <c>Maximize</c> and <c>Restore</c> land on the
/// next pump, in the order they were made. A placement set on a shown maximized window posts its
/// restore and moves the window at once, as <c>SDL3Window.Placement</c> does: the restore lands
/// after the move and takes the window back to the bounds it had before it was maximized. A
/// window not shown yet is maximized at once (shown maximized and cloaked).</para>
/// <para><b>Monitors and scale.</b> The window is on the monitor its outer rectangle overlaps
/// most and takes that monitor's scale, and its borders are that scale's: a move to a monitor
/// at another scale changes the window's <c>DisplayScale</c> and raises <c>Window.Resized</c>
/// on the next frame, as the real display-scale change does.</para>
/// <para><b>Moves.</b> <see cref="HeadlessWindow.Moved"/> is raised from the pump after the
/// window's outer position changed, as SDL's window-moved event is, and
/// <see cref="HeadlessWindow.MoveByUser"/> stands in for the person dragging the window.</para>
/// </remarks>
public sealed class HeadlessWindowsFrame
{
    /// <summary>
    /// The invisible resize border, in pixels, at each display scale: Windows 11's standard
    /// frame (<c>SM_CXFRAME</c> + <c>SM_CXPADDEDBORDER</c>), 8 at 100 %, 9 at 125 %, 11 at
    /// 150 % and 13 at 200 %. Taken at the scale of the window's own monitor, as the custom frame
    /// takes it (<c>GetSystemMetricsForDpi</c> at <c>GetDpiForWindow</c>), not the primary's.
    /// </summary>
    public IReadOnlyDictionary<float, int> ResizeBorders { get; init; } = new Dictionary<float, int>
    {
        [1f] = 8, [1.25f] = 9, [1.5f] = 11, [2f] = 13,
    };

    /// <summary>The resize border at <paramref name="scale"/>: the one listed for it, else for
    /// the nearest scale listed; 0 with none listed.</summary>
    public int ResizeBorderAt(float scale)
    {
        int border = 0;
        float best = float.MaxValue;
        foreach (var (s, b) in ResizeBorders)
        {
            float d = Math.Abs(s - scale);
            if (d < best) { best = d; border = b; }
        }
        return border;
    }
}
