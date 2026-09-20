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
}
