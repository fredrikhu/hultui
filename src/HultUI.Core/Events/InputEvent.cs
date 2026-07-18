namespace HultUI.Core.Events;

/// <summary>
/// Platform-level input events, before hit-testing. Positions and deltas are in
/// layout units — converting from native pixel coordinates is the platform
/// window's job (see the SDL3 EventTranslator's InputScale), so everything from
/// hit-testing onward is DPI-agnostic. Wheel deltas are scroll amounts, not
/// positions, and are never scaled.
/// </summary>
public abstract record InputEvent
{
    public record MouseButtonInput(float X, float Y, int Button, bool IsDown, KeyModifiers Modifiers) : InputEvent;
    public record MouseMoveInput(float X, float Y, float DeltaX, float DeltaY, KeyModifiers Modifiers) : InputEvent;
    public record MouseWheelInput(float MouseX, float MouseY, float DeltaX, float DeltaY, KeyModifiers Modifiers) : InputEvent;
    public record KeyInput(KeyCode KeyCode, KeyModifiers Modifiers, bool IsDown, bool IsRepeat) : InputEvent;
    public record TextInputInput(string Text) : InputEvent;
    public record TextEditingInput(string Text, int SegmentStart, int SegmentLength) : InputEvent;
    public record DropBeginInput() : InputEvent;
    public record DropPositionInput(float X, float Y) : InputEvent;
    public record DropFileInput(string Path) : InputEvent;
    public record DropTextInput(string Text) : InputEvent;
    public record DropCompleteInput() : InputEvent;
}
