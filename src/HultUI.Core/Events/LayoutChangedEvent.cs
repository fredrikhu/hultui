using HultUI.Core.Primitives;

namespace HultUI.Core.Events;

/// <summary>
/// An element's laid-out box changed: its size, its place, or both. Raised on the element alone
/// (it does not bubble), after the frame's layout and before it is drawn, and the first time the
/// element is laid out (<see cref="Previous"/> is null then). Once for each layout of the frame
/// that moved it: once, unless a handler's change moves it again. Listen with
/// <c>.OnLayoutChanged(...)</c>.
///
/// <para>This is the hook for work that needs the laid-out size: fitting a toolbar's items to its
/// width, handing a pan its viewport. What a handler changes is laid out again in the same frame,
/// so the frame drawn shows it; a handler that keeps changing the layout is stopped after a few
/// rounds and the rest waits for the next frame. Scrolling moves no box (it is a translate when
/// drawing), so it raises nothing.</para>
/// </summary>
public sealed class LayoutChangedEvent : ElementEvent
{
    /// <summary>The element's border box now, in window coordinates, unscrolled
    /// (<see cref="Layout.LayoutResult.AbsoluteBorderBox"/>).</summary>
    public Rect Box { get; init; }

    /// <summary>The box the element had when this event last reached it; null the first time.</summary>
    public Rect? Previous { get; init; }

    /// <summary>Whether the width or height changed, not only the place. True the first time.</summary>
    public bool SizeChanged => Previous is not { } p || p.Width != Box.Width || p.Height != Box.Height;
}
