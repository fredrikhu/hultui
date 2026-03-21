using HultUI.Core.Primitives;

namespace HultUI.Core.Events;

public abstract class PointerEvent : ElementEvent
{
    /// <summary>Position in logical (CSS-like) coordinates relative to the window.</summary>
    public Point Position { get; init; }

    /// <summary>Which mouse button was involved (0=left, 1=middle, 2=right).</summary>
    public int Button { get; init; }

    /// <summary>Modifier keys held during the event.</summary>
    public KeyModifiers Modifiers { get; init; }
}

public sealed class PointerDownEvent : PointerEvent;
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
    public float DeltaX { get; init; }
    public float DeltaY { get; init; }
    public KeyModifiers Modifiers { get; init; }
}
