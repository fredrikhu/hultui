using HultUI.Core.Primitives;

namespace HultUI.Core.Events;

public abstract class DragDropEvent : ElementEvent
{
    public Point Position { get; init; }
    public DragData DragData { get; init; } = null!;
}

/// <summary>Fired on the source element when an internal drag begins.</summary>
public sealed class DragStartEvent : DragDropEvent;

/// <summary>Fired (direct, non-bubbling) when a drag enters an element's bounds.</summary>
public sealed class DragEnterEvent : DragDropEvent;

/// <summary>Fired (direct, non-bubbling) when a drag leaves an element's bounds.</summary>
public sealed class DragLeaveEvent : DragDropEvent;

/// <summary>
/// Fired (bubbles) while a drag is over an element.
/// Set <see cref="Accepted"/> to true to indicate this element can receive the drop.
/// </summary>
public sealed class DragOverEvent : DragDropEvent
{
    public bool Accepted { get; set; }
}

/// <summary>Fired (bubbles) when data is dropped on an element that accepted the drag.</summary>
public sealed class DropEvent : DragDropEvent;

/// <summary>Fired (direct, non-bubbling) on the source element after an internal drag ends.</summary>
public sealed class DragEndEvent : DragDropEvent
{
    public bool WasAccepted { get; init; }
}
