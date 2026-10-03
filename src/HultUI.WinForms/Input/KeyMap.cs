using System.Runtime.InteropServices;
using HultUI.Core.Events;

namespace HultUI.WinForms.Input;

/// <summary>
/// Windows key input in the framework's vocabulary, by physical key position.
/// </summary>
/// <remarks>
/// <para><see cref="KeyCode"/> names <em>positions</em>, not characters: the SDL backend
/// maps from scancodes, so <see cref="KeyCode.Z"/> means "the key where Z sits on a US
/// keyboard" whatever the active layout calls it. Win32 hands out virtual keys instead,
/// which are the layout's interpretation — the same physical key reports <c>W</c> on
/// AZERTY — so taking those at face value would have made one framework key code mean
/// two different things depending on which backend an application was hosted by.</para>
///
/// <para>So the layout-dependent keys are translated back to their position through the
/// scancode, and the rest are used as they are: arrows, function keys, editing keys and
/// modifiers have no layout interpretation to undo.</para>
///
/// <para>The consequence is worth stating plainly, because it is the same one HultUI
/// already has in its own windows: on AZERTY the key <em>labelled</em> Z sits where a US
/// keyboard has W, so it reports <see cref="KeyCode.W"/>. An application binding
/// Ctrl+Z gets the physical position, not the printed letter. Typed text is unaffected —
/// that arrives as characters through <c>WM_CHAR</c>, correctly for the layout.</para>
/// </remarks>
internal static class KeyMap
{
    private const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    internal static KeyCode ToKeyCode(Keys key)
    {
        // Letters, digits and punctuation are the keys a layout reassigns; ask Windows
        // where the key physically is and name it by that.
        if (key is (>= Keys.A and <= Keys.Z) or (>= Keys.D0 and <= Keys.D9) or (>= Keys.Oem1 and <= Keys.OemBackslash))
        {
            var byPosition = FromScanCode(MapVirtualKeyW((uint)key, MAPVK_VK_TO_VSC));
            if (byPosition != KeyCode.Unknown) return byPosition;
            // No scancode for this virtual key on this layout: fall through to the
            // virtual-key reading rather than reporting nothing at all.
        }

        return FromVirtualKey(key);
    }

    /// <summary>
    /// Position, from a PS/2 set 1 scan code. The table is the US layout's own key
    /// positions, which is exactly what <see cref="KeyCode"/> names.
    /// </summary>
    internal static KeyCode FromScanCode(uint scanCode) => scanCode switch
    {
        0x02 => KeyCode.Digit1, 0x03 => KeyCode.Digit2, 0x04 => KeyCode.Digit3,
        0x05 => KeyCode.Digit4, 0x06 => KeyCode.Digit5, 0x07 => KeyCode.Digit6,
        0x08 => KeyCode.Digit7, 0x09 => KeyCode.Digit8, 0x0A => KeyCode.Digit9,
        0x0B => KeyCode.Digit0,
        0x0C => KeyCode.Minus, 0x0D => KeyCode.Equals,

        0x10 => KeyCode.Q, 0x11 => KeyCode.W, 0x12 => KeyCode.E, 0x13 => KeyCode.R,
        0x14 => KeyCode.T, 0x15 => KeyCode.Y, 0x16 => KeyCode.U, 0x17 => KeyCode.I,
        0x18 => KeyCode.O, 0x19 => KeyCode.P,
        0x1A => KeyCode.BracketLeft, 0x1B => KeyCode.BracketRight,

        0x1E => KeyCode.A, 0x1F => KeyCode.S, 0x20 => KeyCode.D, 0x21 => KeyCode.F,
        0x22 => KeyCode.G, 0x23 => KeyCode.H, 0x24 => KeyCode.J, 0x25 => KeyCode.K,
        0x26 => KeyCode.L,
        0x27 => KeyCode.Semicolon, 0x28 => KeyCode.Quote, 0x29 => KeyCode.Backtick,
        0x2B => KeyCode.Backslash,

        0x2C => KeyCode.Z, 0x2D => KeyCode.X, 0x2E => KeyCode.C, 0x2F => KeyCode.V,
        0x30 => KeyCode.B, 0x31 => KeyCode.N, 0x32 => KeyCode.M,
        0x33 => KeyCode.Comma, 0x34 => KeyCode.Period, 0x35 => KeyCode.Slash,

        // 0x56 is the extra key ISO keyboards have beside the left Shift. SDL calls it
        // NonUsBackslash and the framework has no name for it, so neither has this.
        _ => KeyCode.Unknown,
    };

    private static KeyCode FromVirtualKey(Keys key) => key switch
    {
        >= Keys.A and <= Keys.Z => (KeyCode)((int)KeyCode.A + (key - Keys.A)),
        >= Keys.D0 and <= Keys.D9 => (KeyCode)((int)KeyCode.Digit0 + (key - Keys.D0)),
        // The keypad with NumLock on. With it off Windows reports the keys as Home, Up and so
        // on, and without the scan code they cannot be told from those, so they are named so.
        >= Keys.NumPad0 and <= Keys.NumPad9 => (KeyCode)((int)KeyCode.Numpad0 + (key - Keys.NumPad0)),
        Keys.Decimal => KeyCode.NumpadDecimal,
        Keys.Add => KeyCode.NumpadAdd,
        Keys.Subtract => KeyCode.NumpadSubtract,
        Keys.Multiply => KeyCode.NumpadMultiply,
        Keys.Divide => KeyCode.NumpadDivide,
        >= Keys.F1 and <= Keys.F12 => (KeyCode)((int)KeyCode.F1 + (key - Keys.F1)),
        >= Keys.F13 and <= Keys.F24 => (KeyCode)((int)KeyCode.F13 + (key - Keys.F13)),

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

        // Only reached when the scancode lookup found nothing. These names are the US
        // layout's, which is the same naming the framework's punctuation uses.
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
