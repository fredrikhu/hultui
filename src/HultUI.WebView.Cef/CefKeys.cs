using HultUI.Core.Events;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// HultUI's keys as CEF's key events want them (spike L0, question 5): <c>windows_key_code</c>
/// gives a page's <c>key</c> and <c>keyCode</c>, <c>native_key_code</c> (the XKB keycode) its
/// <c>code</c>. The physical key is HultUI's scan code (SDL's, the USB HID usage) when the
/// platform gave one, and the named key's otherwise.
/// </summary>
internal static class CefKeys
{
    /// <summary>The DOM code of a key <see cref="KeyCode"/> names; null for <see cref="KeyCode.Unknown"/>.</summary>
    public static string? DomCode(KeyCode key) => key switch
    {
        >= KeyCode.A and <= KeyCode.Z => "Key" + (char)('A' + (key - KeyCode.A)),
        >= KeyCode.Digit0 and <= KeyCode.Digit9 => key.ToString(),
        >= KeyCode.F1 and <= KeyCode.F12 => key.ToString(),
        KeyCode.Up => "ArrowUp", KeyCode.Down => "ArrowDown", KeyCode.Left => "ArrowLeft", KeyCode.Right => "ArrowRight",
        KeyCode.CtrlLeft => "ControlLeft", KeyCode.CtrlRight => "ControlRight",
        KeyCode.SuperLeft => "MetaLeft", KeyCode.SuperRight => "MetaRight",
        KeyCode.Equals => "Equal", KeyCode.Backtick => "Backquote", KeyCode.Menu => "ContextMenu",
        KeyCode.Unknown => null,
        _ => key.ToString(),   // Home, End, PageUp, Backspace, Enter, Tab, Escape, Space, ShiftLeft, Comma, …
    };

    /// <summary>The USB HID usage (keyboard page) of the key: its scan code, or the named key's.</summary>
    public static int Usage(KeyCode key, ushort scanCode)
        => scanCode != 0 ? scanCode : DomCode(key) is { } code ? DomCodes.Usage(code) : 0;

    /// <summary>The XKB keycode CEF wants as <c>native_key_code</c>; 0 when the key is unknown.</summary>
    public static int Native(KeyCode key, ushort scanCode) => DomCodes.Xkb(Usage(key, scanCode));

    /// <summary>
    /// The Windows virtual key. Modifiers are given without their side (VK_SHIFT, not
    /// VK_LSHIFT), as cefclient does; the side goes in the flags. A key HultUI has no name for
    /// is looked up by its DOM code (the keypad, F13 and up, the international keys).
    /// </summary>
    public static int WindowsKeyCode(KeyCode key, ushort scanCode) => key switch
    {
        >= KeyCode.A and <= KeyCode.Z => 0x41 + (key - KeyCode.A),
        >= KeyCode.Digit0 and <= KeyCode.Digit9 => 0x30 + (key - KeyCode.Digit0),
        >= KeyCode.F1 and <= KeyCode.F12 => 0x70 + (key - KeyCode.F1),
        KeyCode.Up => 0x26, KeyCode.Down => 0x28, KeyCode.Left => 0x25, KeyCode.Right => 0x27,
        KeyCode.Home => 0x24, KeyCode.End => 0x23, KeyCode.PageUp => 0x21, KeyCode.PageDown => 0x22,
        KeyCode.Backspace => 0x08, KeyCode.Delete => 0x2E, KeyCode.Insert => 0x2D, KeyCode.Enter => 0x0D,
        KeyCode.Tab => 0x09, KeyCode.Escape => 0x1B, KeyCode.Space => 0x20,
        KeyCode.ShiftLeft or KeyCode.ShiftRight => 0x10,
        KeyCode.CtrlLeft or KeyCode.CtrlRight => 0x11,
        KeyCode.AltLeft or KeyCode.AltRight => 0x12,
        KeyCode.SuperLeft => 0x5B, KeyCode.SuperRight => 0x5C,
        KeyCode.Comma => 0xBC, KeyCode.Period => 0xBE, KeyCode.Slash => 0xBF, KeyCode.Backslash => 0xDC,
        KeyCode.Semicolon => 0xBA, KeyCode.Quote => 0xDE, KeyCode.BracketLeft => 0xDB, KeyCode.BracketRight => 0xDD,
        KeyCode.Minus => 0xBD, KeyCode.Equals => 0xBB, KeyCode.Backtick => 0xC0,
        KeyCode.CapsLock => 0x14, KeyCode.NumLock => 0x90, KeyCode.ScrollLock => 0x91,
        KeyCode.PrintScreen => 0x2C, KeyCode.Pause => 0x13, KeyCode.Menu => 0x5D,
        _ => WindowsKeyCode(DomCodes.Code(scanCode)),
    };

    private static int WindowsKeyCode(string? code) => code switch
    {
        null => 0,
        "NumpadEnter" => 0x0D,
        "NumpadMultiply" => 0x6A, "NumpadAdd" => 0x6B, "NumpadSubtract" => 0x6D,
        "NumpadDecimal" => 0x6E, "NumpadDivide" => 0x6F, "NumpadEqual" => 0xBB, "NumpadComma" => 0x6C,
        "IntlBackslash" => 0xE2, "IntlRo" => 0xC1, "IntlYen" => 0xDC,
        _ when code.StartsWith("Numpad") && code.Length == 7 && char.IsDigit(code[6]) => 0x60 + (code[6] - '0'),
        _ when code.StartsWith('F') && int.TryParse(code[1..], out var n) && n is >= 13 and <= 24 => 0x7C + (n - 13),
        _ => 0,
    };

    /// <summary>A key whose press types a control character SDL sends no text for: Windows
    /// sends it as WM_CHAR, and Chromium acts on it there (Enter submits a form, adds a line).</summary>
    public static char ControlCharacter(KeyCode key, ushort scanCode) => key switch
    {
        KeyCode.Enter => '\r',
        KeyCode.Tab => '\t',
        KeyCode.Backspace => '\b',
        KeyCode.Escape => '\x1b',
        _ when DomCodes.Code(scanCode) == "NumpadEnter" => '\r',
        _ => '\0',
    };

    /// <summary>CEF's event flags for HultUI's modifiers, the keypad, and a modifier key's side.</summary>
    public static uint Flags(KeyModifiers modifiers, KeyCode key = KeyCode.Unknown, ushort scanCode = 0)
    {
        cef_event_flags_t f = 0;
        if (modifiers.HasFlag(KeyModifiers.Shift)) f |= cef_event_flags_t.EVENTFLAG_SHIFT_DOWN;
        if (modifiers.HasFlag(KeyModifiers.Ctrl)) f |= cef_event_flags_t.EVENTFLAG_CONTROL_DOWN;
        if (modifiers.HasFlag(KeyModifiers.Alt)) f |= cef_event_flags_t.EVENTFLAG_ALT_DOWN;
        if (modifiers.HasFlag(KeyModifiers.Super)) f |= cef_event_flags_t.EVENTFLAG_COMMAND_DOWN;
        if (modifiers.HasFlag(KeyModifiers.CapsLock)) f |= cef_event_flags_t.EVENTFLAG_CAPS_LOCK_ON;
        if (modifiers.HasFlag(KeyModifiers.NumLock)) f |= cef_event_flags_t.EVENTFLAG_NUM_LOCK_ON;
        if (DomCodes.Code(Usage(key, scanCode)) is { } code && code.StartsWith("Numpad")) f |= cef_event_flags_t.EVENTFLAG_IS_KEY_PAD;
        if (key is KeyCode.ShiftLeft or KeyCode.CtrlLeft or KeyCode.AltLeft or KeyCode.SuperLeft) f |= cef_event_flags_t.EVENTFLAG_IS_LEFT;
        if (key is KeyCode.ShiftRight or KeyCode.CtrlRight or KeyCode.AltRight or KeyCode.SuperRight) f |= cef_event_flags_t.EVENTFLAG_IS_RIGHT;
        return (uint)f;
    }
}
