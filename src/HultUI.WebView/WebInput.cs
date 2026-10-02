using HultUI.Core.Events;

namespace HultUI.WebView;

public enum WebPointerAction : byte { Move, Down, Up, Wheel, Leave }

/// <summary>A step of a drag over the page.</summary>
public enum WebDragAction : byte { Enter, Over, Leave, Drop }

/// <summary>
/// A drag over the page from outside HultUI — files from the desktop, or a drag the page
/// itself started, which comes back to the window as the operating system's.
/// </summary>
/// <param name="Action">Which step.</param>
/// <param name="X">Device pixels from the page's left edge.</param>
/// <param name="Y">Device pixels from the page's top edge.</param>
/// <param name="NativeData">The platform's data object for the drag (<see cref="DragData.NativeData"/>):
/// on Windows an <c>IDataObject*</c>, valid for the call. Zero when the platform gave none.</param>
/// <param name="Files">What the platform read from it: files.</param>
/// <param name="Text">And text.</param>
/// <param name="Modifiers">The keyboard modifiers held, which pick copy, move or link.</param>
public readonly record struct WebDragInput(
    WebDragAction Action,
    int X, int Y,
    nint NativeData,
    IReadOnlyList<string> Files,
    string? Text,
    KeyModifiers Modifiers);

/// <summary>Mouse buttons, as a set.</summary>
[Flags]
public enum WebPointerButtons : byte
{
    None = 0,
    Left = 1 << 0,
    Middle = 1 << 1,
    Right = 1 << 2,
    Back = 1 << 3,
    Forward = 1 << 4,
}

/// <summary>A pointer event for the page.</summary>
/// <param name="Action">What happened.</param>
/// <param name="X">Device pixels from the page's left edge.</param>
/// <param name="Y">Device pixels from the page's top edge.</param>
/// <param name="Button">The button that went down or up; <see cref="WebPointerButtons.None"/> otherwise.</param>
/// <param name="Held">The buttons down after this event.</param>
/// <param name="ClickCount">1 for a press, 2 for the second press of a double click.</param>
/// <param name="WheelX">Wheel notches, as HultUI's <c>ScrollEvent</c> reports them — fractional on
/// precise devices — with the scroll view's sense: positive scrolls toward the left.</param>
/// <param name="WheelY">Wheel notches; positive is the wheel turned away from the user (scroll up).</param>
/// <param name="Modifiers">The keyboard modifiers held.</param>
public readonly record struct WebPointerInput(
    WebPointerAction Action,
    int X, int Y,
    WebPointerButtons Button = WebPointerButtons.None,
    WebPointerButtons Held = WebPointerButtons.None,
    int ClickCount = 0,
    float WheelX = 0, float WheelY = 0,
    KeyModifiers Modifiers = KeyModifiers.None);

/// <summary>A key for an engine that takes forwarded keys.</summary>
/// <param name="ScanCode">The physical key, <c>KeyDownEvent.ScanCode</c>: SDL's scancode, the USB
/// HID usage. 0 when the platform gave none; the engine then works from <paramref name="Key"/>.</param>
public readonly record struct WebKeyInput(bool IsDown, KeyCode Key, KeyModifiers Modifiers, bool IsRepeat = false, ushort ScanCode = 0);

/// <summary>Text from the window's input method.</summary>
/// <param name="Text">The committed text, or the composition so far (empty: cancelled).</param>
/// <param name="IsComposition">True while the input method is still composing.</param>
/// <param name="SegmentStart">Where the composition's selected segment starts, as SDL reports it.</param>
/// <param name="SegmentLength">The selected segment's length.</param>
public readonly record struct WebTextInput(string Text, bool IsComposition = false, int SegmentStart = 0, int SegmentLength = 0);
