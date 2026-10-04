namespace HultUI.Core.Events;

[Flags]
public enum KeyModifiers : ushort
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Super = 1 << 3,
    CapsLock = 1 << 4,
    NumLock = 1 << 5,
}

/// <summary>
/// The modifier keys held, left and right apart, and AltGr. A finer reading of
/// <see cref="KeyModifiers"/>, on <see cref="KeyDownEvent.ModifierKeys"/>; it is a separate value
/// so that code comparing <see cref="KeyModifiers"/> with <c>==</c> keeps working.
/// </summary>
/// <remarks>
/// <see cref="None"/> with a modifier in <see cref="KeyModifiers"/> means the platform did not
/// say which key it was (headless input, the WinForms host, a synthesized key).
/// </remarks>
[Flags]
public enum ModifierKeys : ushort
{
    None = 0,
    ShiftLeft = 1 << 0,
    ShiftRight = 1 << 1,
    CtrlLeft = 1 << 2,
    CtrlRight = 1 << 3,
    AltLeft = 1 << 4,
    AltRight = 1 << 5,
    SuperLeft = 1 << 6,
    SuperRight = 1 << 7,

    /// <summary>
    /// The right Alt key working as AltGr, the key that types a layout's third character on a
    /// key (<c>@</c> on Swedish 2). Then neither <see cref="AltRight"/> nor
    /// <see cref="KeyModifiers.Alt"/> is set: AltGr is not Alt, and a shortcut on Alt+2 must not
    /// fire when a Swede types <c>@</c>.
    /// </summary>
    /// <remarks>
    /// X11 and Wayland report AltGr as a key of its own. Windows does not: under SDL it is the
    /// right Alt (SDL drops the left Ctrl Windows fakes ahead of it), and what gives it away is
    /// that it typed text, which Alt never does there. So on Windows AltGr on a key that types
    /// nothing reads as right Alt. Hosts that see Windows' own key messages (WinForms) report
    /// AltGr as Ctrl+Alt in <see cref="KeyModifiers"/> and do not set this.
    /// </remarks>
    AltGr = 1 << 8,
}

public enum KeyCode : ushort
{
    Unknown = 0,

    // Letters
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    // Numbers
    Digit0, Digit1, Digit2, Digit3, Digit4,
    Digit5, Digit6, Digit7, Digit8, Digit9,

    // Function keys
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    // Navigation
    Up, Down, Left, Right,
    Home, End, PageUp, PageDown,

    // Editing
    Backspace, Delete, Insert,
    Enter, Tab, Escape, Space,

    // Modifiers
    ShiftLeft, ShiftRight,
    CtrlLeft, CtrlRight,
    AltLeft, AltRight,
    SuperLeft, SuperRight,

    // Punctuation
    Comma, Period, Slash, Backslash,
    Semicolon, Quote, BracketLeft, BracketRight,
    Minus, Equals, Backtick,

    // Other
    CapsLock, NumLock, ScrollLock,
    PrintScreen, Pause, Menu,

    // Added at the end so no existing value moves.

    // The numeric keypad, by position: Numpad7 is the same key with NumLock off, where it
    // types nothing and means Home (KeyModifiers.NumLock says which).
    Numpad0, Numpad1, Numpad2, Numpad3, Numpad4,
    Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
    NumpadDecimal, NumpadAdd, NumpadSubtract, NumpadMultiply, NumpadDivide,
    NumpadEnter, NumpadEquals,

    // Function keys past F12, which some keyboards have and programs can send.
    F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
}
