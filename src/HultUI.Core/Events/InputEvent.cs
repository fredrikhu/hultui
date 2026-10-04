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
    /// <summary>The pointer left the window. No position: it is somewhere else now.</summary>
    public record PointerLeftWindowInput() : InputEvent;
    /// <param name="KeyCode">The key, by its position on a US layout.</param>
    /// <param name="Modifiers">The modifiers held with it.</param>
    /// <param name="IsDown">Pressed rather than released.</param>
    /// <param name="IsRepeat">The key held down, repeating.</param>
    /// <param name="ScanCode">The physical key as the platform reports it: SDL's scancode, which
    /// is the USB HID usage on the keyboard page. 0 when the platform gave none.</param>
    /// <param name="LayoutKey">The character the key types on the layout; see
    /// <see cref="KeyDownEvent.LayoutKey"/>. 0 for none, or not known.</param>
    /// <param name="ModifierKeys">The modifier keys held, left and right apart; see
    /// <see cref="KeyDownEvent.ModifierKeys"/>.</param>
    /// <param name="Text">The text this keystroke typed, which the platform sends next as a
    /// <see cref="TextInputInput"/>; see <see cref="KeyDownEvent.Text"/>. Null when not known.</param>
    public record KeyInput(KeyCode KeyCode, KeyModifiers Modifiers, bool IsDown, bool IsRepeat, ushort ScanCode = 0,
        int LayoutKey = 0, ModifierKeys ModifierKeys = ModifierKeys.None, string? Text = null) : InputEvent;
    public record TextInputInput(string Text) : InputEvent;
    public record TextEditingInput(string Text, int SegmentStart, int SegmentLength) : InputEvent;
    /// <summary>An OS drag entered the window.</summary>
    /// <param name="NativeData">The platform's own data object for the drag, when the platform
    /// has one to give (<see cref="DragData.NativeData"/>); zero otherwise.</param>
    public record DropBeginInput(nint NativeData = 0) : InputEvent;
    /// <summary>Modifiers are sampled from the live keyboard state, not from a key
    /// event: during an OS drag the source application owns the input stream, so no
    /// key press reaches us — but Shift/Ctrl still decide copy-vs-move.</summary>
    /// <param name="X">The pointer, in layout units from the window's top left.</param>
    /// <param name="Y">The pointer, in layout units from the window's top left.</param>
    /// <param name="Modifiers">The keyboard's modifiers at this step.</param>
    /// <param name="Feedback">Filled in while the window handles the event: whether an element
    /// under the point accepts the drag, which a platform that must answer the OS at once
    /// (Windows' drop target) passes on.</param>
    public record DropPositionInput(float X, float Y, KeyModifiers Modifiers = KeyModifiers.None, DropFeedback? Feedback = null) : InputEvent;
    public record DropFileInput(string Path) : InputEvent;
    public record DropTextInput(string Text) : InputEvent;
    /// <summary>The drag was dropped at the last position.</summary>
    /// <param name="Modifiers">The keyboard's modifiers at the drop.</param>
    /// <param name="Feedback">Filled in while the window handles the event: whether an element took the drop.</param>
    public record DropCompleteInput(KeyModifiers Modifiers = KeyModifiers.None, DropFeedback? Feedback = null) : InputEvent;
    /// <summary>The drag left the window, or was called off, without a drop.</summary>
    public record DropCancelInput() : InputEvent;
}
