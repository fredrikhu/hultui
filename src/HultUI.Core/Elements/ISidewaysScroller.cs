namespace HultUI.Core.Elements;

/// <summary>
/// Something other than a scroll view that owns the sideways axis of the content shown in one.
///
/// <para>A <see cref="ScrollViewElement"/> has a horizontal offset of its own, and a plain one
/// scrolls sideways with it. A virtualized list does not: it pins its content width to the
/// viewport, so that offset can never leave zero, and an app that wants a wide list to pan
/// keeps the position elsewhere — <see cref="Builders.HorizontalPan"/>, a margin inside a
/// clipping cell. Nothing about that is visible to a reader, who sees a list with a horizontal
/// scrollbar under it and holds the middle button to the right of where they pressed. The
/// gesture drove the list's own offset, which had nowhere to go, and aiming right did nothing.</para>
///
/// <para>So a scroll view can be told who really scrolls it sideways
/// (<see cref="ScrollViewElement.SidewaysScroller"/>), and the middle-click autoscroll's
/// horizontal delta goes there instead. Implemented by <see cref="Builders.HorizontalPan"/>;
/// implement it on anything else that moves a scroll view's content sideways by other means.</para>
/// </summary>
public interface ISidewaysScroller
{
    /// <summary>Whether there is anywhere left to go sideways — what decides, together with the
    /// vertical axis, whether a middle press starts an autoscroll or is just a middle press.</summary>
    bool CanScrollSideways { get; }

    /// <summary>Moves the content by <paramref name="dx"/> — positive is further right through
    /// it, the same sense as <c>ScrollOffsetX</c>. Returns true when something actually moved.</summary>
    bool ScrollSidewaysBy(float dx);
}
