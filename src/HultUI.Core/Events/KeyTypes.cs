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
}
