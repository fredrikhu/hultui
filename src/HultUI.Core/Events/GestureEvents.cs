using HultUI.Core.Primitives;

namespace HultUI.Core.Events;

/// <summary>The axis a pan moves along (docs/design/GESTURES.md).</summary>
public enum PanAxis
{
    Horizontal,
    Vertical,
    Both,
}

/// <summary>
/// A finger held still for the long-press time (500 ms) on this element or inside it. The press
/// it began has been cancelled (<see cref="PointerCancelEvent"/>), so no click follows the
/// release. Bubbles like a click. Where no element on the way has a handler for it, the window
/// opens the element's context menu instead, or else shows its tooltip while the finger stays down.
/// </summary>
public sealed class LongPressEvent : PointerEvent;

/// <summary>A finger that pressed this element moved past the touch slop along its pan axis, and
/// the element took it (<c>OnPan</c>). The press has been cancelled; every later move of that
/// finger is a <see cref="PanUpdateEvent"/> here.</summary>
public sealed class PanStartEvent : PointerEvent;

/// <summary>The finger an element's pan took moved.</summary>
public sealed class PanUpdateEvent : PointerEvent
{
    /// <summary>The finger's movement since the last update, in layout units. The axis the pan
    /// was not given reads 0.</summary>
    public Point Delta { get; init; }

    /// <summary>The finger's movement since it pressed, along the pan's axis.</summary>
    public Point Total { get; init; }
}

/// <summary>The finger an element's pan took lifted, or the system took it.</summary>
public sealed class PanEndEvent : PointerEvent
{
    /// <summary>How fast the finger was moving as it lifted, in layout units a second, fitted over
    /// its last 100 ms. Zero after a cancel. An element that wants a throw animates from it.</summary>
    public Point Velocity { get; init; }

    /// <summary>The system took the finger: nothing the pan was doing should be committed.</summary>
    public bool Cancelled { get; init; }
}

/// <summary>Asks an element which axis its pan handlers move along. The window's question, not an
/// event that happens: <c>OnPan</c> answers it beside the handlers it adds, so the answer moves
/// with them when a build patches the element.</summary>
internal sealed class PanAxisQuery : ElementEvent
{
    public PanAxis? Axis { get; set; }
}
