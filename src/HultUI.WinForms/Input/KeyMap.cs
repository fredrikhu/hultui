using HultUI.Core.Events;

namespace HultUI.WinForms.Input;

/// <summary>
/// WinForms virtual keys in the framework's vocabulary.
/// </summary>
/// <remarks>
/// The SDL backend maps from physical scancodes, so its keys are layout-independent —
/// pressing the key where <c>Z</c> sits on a US keyboard reports <c>Z</c> on any
/// layout. Win32 virtual keys are the layout's interpretation instead, so the same key
/// on an AZERTY keyboard reports <c>W</c>. That difference is deliberate here: a
/// control inside a WinForms application should agree with every other control in it
/// about what the user pressed, and shortcuts are what the layout says they are.
/// </remarks>
internal static class KeyMap
{
    internal static KeyCode ToKeyCode(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => (KeyCode)((int)KeyCode.A + (key - Keys.A)),
        >= Keys.D0 and <= Keys.D9 => (KeyCode)((int)KeyCode.Digit0 + (key - Keys.D0)),
        // The numeric keypad's digits are the same characters; the framework has no
        // separate keypad vocabulary, so they land on the same codes.
        >= Keys.NumPad0 and <= Keys.NumPad9 => (KeyCode)((int)KeyCode.Digit0 + (key - Keys.NumPad0)),
        >= Keys.F1 and <= Keys.F12 => (KeyCode)((int)KeyCode.F1 + (key - Keys.F1)),

        Keys.Up => KeyCode.Up,
        Keys.Down => KeyCode.Down,
        Keys.Left => KeyCode.Left,
        Keys.Right => KeyCode.Right,
        Keys.Home => KeyCode.Home,
        Keys.End => KeyCode.End,
        Keys.PageUp => KeyCode.PageUp,
        Keys.PageDown => KeyCode.PageDown,

        Keys.Back => KeyCode.Backspace,
        Keys.Delete => KeyCode.Delete,
        Keys.Insert => KeyCode.Insert,
        Keys.Enter => KeyCode.Enter,          // same value as Keys.Return
        Keys.Tab => KeyCode.Tab,
        Keys.Escape => KeyCode.Escape,
        Keys.Space => KeyCode.Space,

        Keys.ShiftKey or Keys.LShiftKey => KeyCode.ShiftLeft,
        Keys.RShiftKey => KeyCode.ShiftRight,
        Keys.ControlKey or Keys.LControlKey => KeyCode.CtrlLeft,
        Keys.RControlKey => KeyCode.CtrlRight,
        Keys.Menu or Keys.LMenu => KeyCode.AltLeft,
        Keys.RMenu => KeyCode.AltRight,
        Keys.LWin => KeyCode.SuperLeft,
        Keys.RWin => KeyCode.SuperRight,

        // OEM keys are named for the US layout, which is what the framework's
        // punctuation vocabulary is named for too.
        Keys.Oemcomma => KeyCode.Comma,
        Keys.OemPeriod => KeyCode.Period,
        Keys.OemQuestion => KeyCode.Slash,
        Keys.OemBackslash or Keys.OemPipe => KeyCode.Backslash,
        Keys.OemSemicolon => KeyCode.Semicolon,
        Keys.OemQuotes => KeyCode.Quote,
        Keys.OemOpenBrackets => KeyCode.BracketLeft,
        Keys.OemCloseBrackets => KeyCode.BracketRight,
        Keys.OemMinus => KeyCode.Minus,
        Keys.Oemplus => KeyCode.Equals,
        Keys.Oemtilde => KeyCode.Backtick,

        Keys.CapsLock => KeyCode.CapsLock,
        Keys.NumLock => KeyCode.NumLock,
        Keys.Scroll => KeyCode.ScrollLock,
        Keys.PrintScreen => KeyCode.PrintScreen,
        Keys.Pause => KeyCode.Pause,
        Keys.Apps => KeyCode.Menu,

        _ => KeyCode.Unknown,
    };

    /// <summary>The modifier flags out of a <see cref="Keys"/> value's high bits.</summary>
    internal static KeyModifiers ToModifiers(Keys keyData)
    {
        var modifiers = KeyModifiers.None;
        if ((keyData & Keys.Shift) != 0) modifiers |= KeyModifiers.Shift;
        if ((keyData & Keys.Control) != 0) modifiers |= KeyModifiers.Ctrl;
        if ((keyData & Keys.Alt) != 0) modifiers |= KeyModifiers.Alt;
        if (Control.IsKeyLocked(Keys.CapsLock)) modifiers |= KeyModifiers.CapsLock;
        if (Control.IsKeyLocked(Keys.NumLock)) modifiers |= KeyModifiers.NumLock;
        return modifiers;
    }
}
