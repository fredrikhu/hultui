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

public sealed class FocusEvent : ElementEvent;
public sealed class BlurEvent : ElementEvent;
