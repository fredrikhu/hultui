namespace HultUI.Core.Events;

public sealed class KeyDownEvent : ElementEvent
{
    public KeyCode Key { get; init; }
    public KeyModifiers Modifiers { get; init; }
    public bool IsRepeat { get; init; }
}

public sealed class KeyUpEvent : ElementEvent
{
    public KeyCode Key { get; init; }
    public KeyModifiers Modifiers { get; init; }
}

public sealed class TextInputEvent : ElementEvent
{
    public string Text { get; init; } = "";
}

/// <summary>
/// Fired while the OS IME is composing a preedit string (CJK input, dead keys, etc.).
/// <see cref="Text"/> is the current preedit; <see cref="SegmentStart"/>/<see cref="SegmentLength"/>
/// mark the range the IME is actively editing. An empty <see cref="Text"/> means the
/// composition was cancelled or is about to commit.
/// </summary>
public sealed class TextEditingEvent : ElementEvent
{
    public string Text { get; init; } = "";
    public int SegmentStart { get; init; }
    public int SegmentLength { get; init; }
}

/// <summary>An element gained keyboard focus. Delivered to that element only.</summary>
public sealed class FocusEvent : ElementEvent
{
    /// <summary>How focus arrived. An element that hosts its own tab order — a web page —
    /// starts at its first stop for <see cref="FocusReason.Next"/> and its last for
    /// <see cref="FocusReason.Previous"/>.</summary>
    public FocusReason Reason { get; init; }
}

/// <summary>Why an element gained focus.</summary>
public enum FocusReason : byte
{
    /// <summary>Code moved focus (<c>Element.Focus()</c>), or no reason was recorded.</summary>
    Programmatic,
    /// <summary>A pointer press on the element or inside it.</summary>
    Pointer,
    /// <summary>Tab: focus moved forward through the tab order.</summary>
    Next,
    /// <summary>Shift+Tab: focus moved backward through the tab order.</summary>
    Previous,
}
public sealed class BlurEvent : ElementEvent;
