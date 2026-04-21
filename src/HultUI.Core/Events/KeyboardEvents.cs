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

public sealed class FocusEvent : ElementEvent;
public sealed class BlurEvent : ElementEvent;
