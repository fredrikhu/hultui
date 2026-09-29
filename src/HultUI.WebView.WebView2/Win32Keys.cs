using HultUI.Core.Events;

namespace HultUI.WebView.WebView2;

/// <summary>
/// Windows key messages in the framework's vocabulary, by physical position — the same
/// rule, and the same tables, as the WinForms host's <c>KeyMap</c>: <see cref="KeyCode"/>
/// names positions (the SDL backend maps from scancodes), so letters, digits and punctuation
/// are named from the scan code, and keys a layout never moves from the virtual key. Copied
/// rather than shared because that one is written against WinForms' <c>Keys</c>, which this
/// package must not reference.
/// </summary>
internal static class Win32Keys
{
    /// <param name="virtualKey">The message's virtual key.</param>
    /// <param name="scanCode">PS/2 set 1 scan code: lParam bits 16–23.</param>
    public static KeyCode ToKeyCode(uint virtualKey, uint scanCode)
    {
        bool layoutKey = virtualKey is (>= 0x41 and <= 0x5A) or (>= 0x30 and <= 0x39) or (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2;
        if (layoutKey && FromScanCode(scanCode) is var byPosition and not KeyCode.Unknown)
            return byPosition;
        return FromVirtualKey(virtualKey);
    }

    public static KeyCode FromScanCode(uint scanCode) => scanCode switch
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
        _ => KeyCode.Unknown,
    };

    public static KeyCode FromVirtualKey(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A => (KeyCode)((int)KeyCode.A + (int)(vk - 0x41)),
        >= 0x30 and <= 0x39 => (KeyCode)((int)KeyCode.Digit0 + (int)(vk - 0x30)),
        >= 0x60 and <= 0x69 => (KeyCode)((int)KeyCode.Digit0 + (int)(vk - 0x60)),   // keypad digits
        >= 0x70 and <= 0x7B => (KeyCode)((int)KeyCode.F1 + (int)(vk - 0x70)),

        0x26 => KeyCode.Up, 0x28 => KeyCode.Down, 0x25 => KeyCode.Left, 0x27 => KeyCode.Right,
        0x24 => KeyCode.Home, 0x23 => KeyCode.End, 0x21 => KeyCode.PageUp, 0x22 => KeyCode.PageDown,
        0x08 => KeyCode.Backspace, 0x2E => KeyCode.Delete, 0x2D => KeyCode.Insert,
        0x0D => KeyCode.Enter, 0x09 => KeyCode.Tab, 0x1B => KeyCode.Escape, 0x20 => KeyCode.Space,

        0x10 or 0xA0 => KeyCode.ShiftLeft, 0xA1 => KeyCode.ShiftRight,
        0x11 or 0xA2 => KeyCode.CtrlLeft, 0xA3 => KeyCode.CtrlRight,
        0x12 or 0xA4 => KeyCode.AltLeft, 0xA5 => KeyCode.AltRight,
        0x5B => KeyCode.SuperLeft, 0x5C => KeyCode.SuperRight,

        0xBC => KeyCode.Comma, 0xBE => KeyCode.Period, 0xBF => KeyCode.Slash,
        0xDC or 0xE2 => KeyCode.Backslash, 0xBA => KeyCode.Semicolon, 0xDE => KeyCode.Quote,
        0xDB => KeyCode.BracketLeft, 0xDD => KeyCode.BracketRight, 0xBD => KeyCode.Minus,
        0xBB => KeyCode.Equals, 0xC0 => KeyCode.Backtick,

        0x14 => KeyCode.CapsLock, 0x90 => KeyCode.NumLock, 0x91 => KeyCode.ScrollLock,
        0x2C => KeyCode.PrintScreen, 0x13 => KeyCode.Pause, 0x5D => KeyCode.Menu,
        _ => KeyCode.Unknown,
    };

    /// <summary>True for a key that only modifies others.</summary>
    public static bool IsModifier(uint vk) => vk is 0x10 or 0x11 or 0x12 or (>= 0xA0 and <= 0xA5) or 0x5B or 0x5C;

    /// <summary>
    /// Whether the application gets a say: Ctrl or Alt held, or a function key. Typing,
    /// arrows, Escape and Tab stay the page's — an app's arrow handler must not scroll a list
    /// behind the page while the user moves a caret in it.
    /// </summary>
    public static bool IsShortcut(uint vk, KeyModifiers modifiers) =>
        !IsModifier(vk) && ((modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0 || vk is >= 0x70 and <= 0x7B);
}
