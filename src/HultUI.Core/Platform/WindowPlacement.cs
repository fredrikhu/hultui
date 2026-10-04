using HultUI.Core.Primitives;

namespace HultUI.Core.Platform;

/// <summary>
/// Where a window sits on the desktop and whether it is maximized: what an app saves as it
/// closes and gives back as it starts, so the window opens where the person left it.
/// </summary>
/// <remarks>
/// <para><b>The bounds are the restored ones.</b> A maximized or full-screen window reports the
/// rectangle it goes back to, not the screen it covers, so un-maximizing after a restart lands
/// where it did before the restart.</para>
/// <para><b>In the platform's desktop units, and only for it.</b> Windows reports the outer
/// window rectangle in physical pixels; SDL elsewhere reports the client area in its window
/// coordinates — pixels on X11, logical units on Wayland, which never reports a position or
/// accepts one. Save the value and hand it back on the same machine; do not compute with it.</para>
/// <para><b>Restoring never strands a window.</b> A monitor that is gone, smaller or moved
/// would leave saved bounds partly or wholly off screen, and a window whose title bar is out of
/// reach cannot be dragged back. The platform runs the bounds through <see cref="FitTo"/>
/// against the work areas it has now.</para>
/// </remarks>
/// <param name="X">Left edge of the restored bounds.</param>
/// <param name="Y">Top edge of the restored bounds.</param>
/// <param name="Width">Width of the restored bounds.</param>
/// <param name="Height">Height of the restored bounds.</param>
/// <param name="Maximized">Whether the window is maximized over those bounds. A window that
/// was minimized from maximized reports true; one that was full screen reports what it was
/// before.</param>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized)
{
    /// <summary>How much of the top edge must be on a work area for the window to count as in
    /// reach: enough to grab and drag.</summary>
    public const int MinReachableWidth = 100;

    /// <summary>The band along the top edge that must be on screen: the caption, or the part
    /// of a custom title bar a drag starts from.</summary>
    public const int CaptionBand = 32;

    /// <summary>How far above a work area's top the window's top edge may sit and still count.
    /// A window's outer rectangle includes resize borders the OS draws nothing in.</summary>
    public const int TopTolerance = 8;

    /// <summary>Whether the bounds describe a window at all — a hand-edited or truncated
    /// settings file can carry zeros.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUsable => Width > 0 && Height > 0;

    /// <summary>
    /// These bounds, moved and shrunk as little as it takes to put the window within reach on
    /// <paramref name="workAreas"/> — the usable part of each monitor, in the same units.
    /// </summary>
    /// <remarks>
    /// <para>The window belongs to the work area it overlaps most, or, overlapping none, to the
    /// one nearest its centre. It shrinks to fit that area. It then stays where it is if its top
    /// edge is in reach on any work area (a window may span monitors), slides inside its area if
    /// it overlaps it, and is centred on it if it does not — the monitor it was on is gone.</para>
    /// <para>With no work areas — the platform could not say — the bounds come back unchanged.</para>
    /// </remarks>
    public WindowPlacement FitTo(IReadOnlyList<Rect> workAreas)
    {
        if (workAreas.Count == 0) return this;

        var area = HomeArea(workAreas);
        int w = Math.Min(Width, (int)area.Width);
        int h = Math.Min(Height, (int)area.Height);
        int x = X, y = Y;

        bool shrunk = w != Width || h != Height;
        if (!shrunk && InReach(x, y, w, workAreas)) return this;

        if (Overlap(new Rect(x, y, w, h), area) > 0)
        {
            // Slide inside: the least movement that puts the whole window on its monitor.
            x = Math.Clamp(x, (int)area.X, (int)(area.X + area.Width) - w);
            y = Math.Clamp(y, (int)area.Y, (int)(area.Y + area.Height) - h);
        }
        else
        {
            x = (int)(area.X + (area.Width - w) / 2);
            y = (int)(area.Y + (area.Height - h) / 2);
        }
        return this with { X = x, Y = y, Width = w, Height = h };
    }

    private Rect HomeArea(IReadOnlyList<Rect> workAreas)
    {
        var bounds = new Rect(X, Y, Width, Height);
        Rect best = workAreas[0];
        float bestOverlap = 0;
        foreach (var a in workAreas)
        {
            float o = Overlap(bounds, a);
            if (o > bestOverlap) { bestOverlap = o; best = a; }
        }
        if (bestOverlap > 0) return best;

        float cx = X + Width / 2f, cy = Y + Height / 2f;
        float bestDistance = float.MaxValue;
        foreach (var a in workAreas)
        {
            float dx = Math.Max(Math.Max(a.X - cx, 0), cx - (a.X + a.Width));
            float dy = Math.Max(Math.Max(a.Y - cy, 0), cy - (a.Y + a.Height));
            float d = dx * dx + dy * dy;
            if (d < bestDistance) { bestDistance = d; best = a; }
        }
        return best;
    }

    /// <summary>Whether the top edge of a window at (x, y), w wide, can be grabbed on some work
    /// area: on it by at least <see cref="MinReachableWidth"/> (or the whole width, for a
    /// narrower window), with the caption band between that area's top and bottom.</summary>
    private static bool InReach(int x, int y, int w, IReadOnlyList<Rect> workAreas)
    {
        int need = Math.Min(w, MinReachableWidth);
        foreach (var a in workAreas)
        {
            float across = Math.Min(x + w, a.X + a.Width) - Math.Max(x, a.X);
            if (across >= need && y >= a.Y - TopTolerance && y + CaptionBand <= a.Y + a.Height)
                return true;
        }
        return false;
    }

    private static float Overlap(Rect a, Rect b)
    {
        float w = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        float h = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        return w > 0 && h > 0 ? w * h : 0;
    }
}
