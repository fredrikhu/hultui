namespace HultUI.Core.Events;

public abstract class ElementEvent
{
    public EventPhase Phase { get; internal set; }
    public Elements.Element Target { get; internal set; } = null!;
    public Elements.Element CurrentTarget { get; internal set; } = null!;
    public bool Handled { get; set; }
    public bool StopPropagation { get; set; }
}

public enum EventPhase : byte
{
    Capture,
    Target,
    Bubble
}

public enum EventType : byte
{
    PointerDown,
    PointerUp,
    PointerMove,
    PointerEnter,
    PointerLeave,
    Click,
    DoubleClick,
    Scroll,
    KeyDown,
    KeyUp,
    TextInput,
    Focus,
    Blur,
    DragStart,
    DragEnter,
    DragLeave,
    DragOver,
    Drop,
    DragEnd
}
