using HultUI.Core.Primitives;
using HultUI.Core.Elements;

namespace HultUI.Core.Events;

public abstract class PointerEvent : ElementEvent
{
    /// <summary>Position in logical (CSS-like) coordinates relative to the window.</summary>
    public Point Position { get; init; }

    /// <summary>Position relative to the current target element's content box.</summary>
    public Point LocalPosition
    {
        get
        {
            var box = CurrentTarget.Layout.AbsoluteContentBox;
            return new(Position.X - box.X, Position.Y - box.Y);
        }
    }

    /// <summary>Left mouse button (normalized value 0).</summary>
    public const int LeftButton = 0;
    /// <summary>Middle mouse button (normalized value 1).</summary>
    public const int MiddleButton = 1;
    /// <summary>Right mouse button (normalized value 2).</summary>
    public const int RightButton = 2;
    /// <summary>Back / X1 side button (normalized value 4).</summary>
    public const int BackButton = 4;
    /// <summary>Forward / X2 side button (normalized value 5).</summary>
    public const int ForwardButton = 5;

    /// <summary>
    /// Which mouse button was involved. Compare against the named constants
    /// (<see cref="LeftButton"/>, <see cref="MiddleButton"/>, <see cref="RightButton"/>,
    /// <see cref="BackButton"/>, <see cref="ForwardButton"/>) rather than raw ints.
    /// </summary>
    public int Button { get; init; }

    /// <summary>Modifier keys held during the event.</summary>
    public KeyModifiers Modifiers { get; init; }
}

public sealed class PointerDownEvent : PointerEvent
{
    /// <summary>
    /// Which press of a repeated-click run this is: 1 for a single press, 2 for the press
    /// that completes a double-click, 3 for a triple, and so on. Counted on the press rather
    /// than on the release so a gesture that starts on the second press — word-granular
    /// selection, which then keeps extending as the pointer moves — can begin before the
    /// button comes back up. Non-primary buttons are always 1.
    /// </summary>
    public int ClickCount { get; init; } = 1;

    /// <summary>
    /// Sends every move and the release of this press to the element handling it,
    /// wherever the pointer goes, until the button comes up: a slider thumb dragged
    /// above or below its track keeps following. Hover enter/leave stand still
    /// meanwhile and catch up on the release. Call it from a handler for this event;
    /// the window reads the request once the dispatch completes.
    /// </summary>
    public void CapturePointer() => CaptureTarget = CurrentTarget;

    /// <summary>The element that asked to capture the pointer, or null.</summary>
    public Element? CaptureTarget { get; private set; }
}
public sealed class PointerUpEvent : PointerEvent;

public sealed class PointerMoveEvent : PointerEvent
{
    /// <summary>Movement delta since last move event.</summary>
    public Point Delta { get; init; }
}

public sealed class PointerEnterEvent : PointerEvent;
public sealed class PointerLeaveEvent : PointerEvent;

public sealed class ClickEvent : PointerEvent
{
    public int ClickCount { get; init; } = 1;
}

public sealed class DoubleClickEvent : PointerEvent;

public sealed class ScrollEvent : ElementEvent
{
    public Point Position { get; init; }

    /// <summary>Position relative to the current target element's content box.</summary>
    public Point LocalPosition
    {
        get
        {
            var box = CurrentTarget.Layout.AbsoluteContentBox;
            return new(Position.X - box.X, Position.Y - box.Y);
        }
    }

    public float DeltaX { get; init; }
    public float DeltaY { get; init; }
    public KeyModifiers Modifiers { get; init; }

    // ──── Scrolling ────
    //
    // A wheel event bubbles all the way up whoever uses it, and the scrollers on the way share
    // it out: each takes the axes it can move on and leaves the rest. A list at the end of its
    // range therefore hands the wheel to the page around it, a horizontal strip inside a vertical
    // page passes the vertical motion through, and an app handler on the same element still runs
    // after the element's own — which StopPropagation would prevent, since it also ends the
    // element's remaining handlers. Anything that scrolls on the wheel follows the same three
    // steps: ask IsFor, move by RemainingDeltaX/Y, and report what moved with Consume.

    /// <summary>The wheel's motion along the scrolling axes. The same as <see cref="DeltaX"/> and
    /// <see cref="DeltaY"/>, except that a vertical wheel turned with Shift held reads as
    /// sideways: the way a mouse without a tilt wheel reaches a horizontal scrollbar, as in a
    /// browser.</summary>
    public float ScrollDeltaX => ShiftTurnsSideways ? DeltaY : DeltaX;

    /// <inheritdoc cref="ScrollDeltaX"/>
    public float ScrollDeltaY => ShiftTurnsSideways ? 0 : DeltaY;

    private bool ShiftTurnsSideways => DeltaX == 0 && (Modifiers & KeyModifiers.Shift) != 0;

    /// <summary>Whether a scroller nearer the pointer has already used the horizontal motion.</summary>
    public bool ConsumedX { get; private set; }

    /// <summary>Whether a scroller nearer the pointer has already used the vertical motion.</summary>
    public bool ConsumedY { get; private set; }

    /// <summary><see cref="ScrollDeltaX"/>, or 0 once something has used it.</summary>
    public float RemainingDeltaX => ConsumedX ? 0 : ScrollDeltaX;

    /// <summary><see cref="ScrollDeltaY"/>, or 0 once something has used it.</summary>
    public float RemainingDeltaY => ConsumedY ? 0 : ScrollDeltaY;

    /// <summary>
    /// Whether <paramref name="scroller"/> may use this event. False while a wheel gesture that
    /// started on another scroller is still going: a page being scrolled keeps scrolling when a
    /// list inside it passes under the pointer, rather than the list taking over halfway down —
    /// and a field that changes its value on the wheel is not changed by a page scrolling past it.
    /// </summary>
    public bool IsFor(Element scroller) => Latched is null || Latched == scroller;

    /// <summary>
    /// Reports what <paramref name="scroller"/> did with the motion it was offered: an axis it
    /// moved on is used up, and the scrollers further out see nothing left there. An axis it
    /// could not move on is left for them — that is the hand-off. The one exception is the
    /// scroller a gesture is latched to, which keeps every axis it was offered whether it moved
    /// or not: a flick that reaches the end of a list stops there instead of carrying on with
    /// the page. Pass true for an axis the element used in some other way, such as a field that
    /// steps its value.
    /// </summary>
    public void Consume(Element scroller, bool movedX, bool movedY)
    {
        bool latched = Latched == scroller;
        bool x = !ConsumedX && ScrollDeltaX != 0 && (movedX || latched);
        bool y = !ConsumedY && ScrollDeltaY != 0 && (movedY || latched);
        if (!x && !y) return;
        ConsumedX |= x;
        ConsumedY |= y;
        ConsumedBy ??= scroller;
    }

    /// <summary>The scroller the current wheel gesture belongs to; set by the window, null when
    /// this event starts a gesture. See <see cref="WheelGesture"/>.</summary>
    internal Element? Latched { get; init; }

    /// <summary>The first scroller that used this event — the one a new gesture latches to.</summary>
    internal Element? ConsumedBy { get; private set; }
}

/// <summary>
/// Which scroller a run of wheel events belongs to. A gesture — the notches of one spin of the
/// wheel, a touchpad swipe and the inertia after it — goes to the scroller its first event was
/// used by, for as long as events keep coming less than <see cref="Gap"/> apart and the pointer
/// stays over that scroller. A browser does the same and calls it latching.
///
/// <para>Without it, handing the wheel on at the end of a range has two bad effects: a flick
/// down a list carries on with the page the moment the list runs out, and scrolling a page
/// stops dead when a list inside it arrives under the pointer, because the list takes over.
/// With it, both need a pause — the next gesture is free to go elsewhere.</para>
/// </summary>
internal sealed class WheelGesture
{
    /// <summary>The longest pause between two events of one gesture.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(300);

    private Element? _latched;
    private long _lastMs = long.MinValue;

    /// <summary>The scroller an event aimed at <paramref name="target"/> at <paramref name="nowMs"/>
    /// belongs to, or null when it starts a new gesture.</summary>
    public Element? LatchFor(Element target, long nowMs)
    {
        if (_latched is null || nowMs - _lastMs > Gap.TotalMilliseconds) return _latched = null;
        // The pointer has to still be over it. This also lets go of a scroller a rebuild
        // replaced, which nothing can be over any more.
        for (var e = target; e is not null; e = e.Parent)
            if (e == _latched) return _latched;
        return _latched = null;
    }

    /// <summary>Records a dispatched event: the gesture stays with its scroller, or a new one
    /// latches to whatever used this event first — or to nothing, if nothing did.</summary>
    public void Dispatched(ScrollEvent evt, long nowMs)
    {
        _lastMs = nowMs;
        _latched = evt.Latched ?? evt.ConsumedBy;
    }
}
