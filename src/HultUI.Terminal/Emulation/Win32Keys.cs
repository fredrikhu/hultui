using System.Runtime.InteropServices;
using HultUI.Core.Events;
using XTerm.Input;
using HKey = HultUI.Core.Events.KeyCode;
using HMods = HultUI.Core.Events.KeyModifiers;
using CKS = XTerm.Input.Win32InputGenerator.ControlKeyState;

namespace HultUI.Terminal.Emulation;

/// <summary>
/// Keys as win32-input-mode events (<c>CSI Vk ; Sc ; Uc ; Kd ; Cs ; Rc _</c>), which ConPTY asks
/// for with <c>CSI ? 9001 h</c>: a key as Windows itself would have handed it to a console, with
/// its virtual key, scan code, character and modifier state.
/// </summary>
/// <remarks>
/// <para>Only the keys whose VT bytes ConPTY has to guess at go this way (<see cref="Takes"/>).
/// A bare ESC may start a sequence, so ConPTY waits to see what follows; through Git's vim the
/// first key after a resize was lost in 2 to 9 runs of 10, and none of 10 as a win32 event. Alt+key
/// is ESC and the key, the same race. Ctrl+Space is NUL, which ConPTY reads as Ctrl+@, and
/// Ctrl+Backspace is BS, which it reads as Ctrl+H, so a program reading console key events (PSReadLine)
/// saw neither.</para>
/// <para>The scan code comes from the key's USB HID usage (SDL's scancode), through the fixed
/// HID-to-set-1 table Windows' own keyboard driver uses. The virtual key is the letter for a
/// letter key, Windows' answer for that scan code on the current layout otherwise, and a US
/// keyboard's off Windows (tests).</para>
/// </remarks>
internal static class Win32Keys
{
    /// <summary>Whether <paramref name="key"/> goes as a win32 event: Escape, Alt with a key
    /// that types or with Backspace, Enter and Space, and Ctrl+Space and Ctrl+Backspace.</summary>
    public static bool Takes(in TerminalKey key)
    {
        bool ctrl = (key.Modifiers & HMods.Ctrl) != 0, alt = (key.Modifiers & HMods.Alt) != 0;
        return key.Key switch
        {
            HKey.Escape => true,
            HKey.Space or HKey.Backspace when ctrl && !alt => true,
            HKey.Backspace or HKey.Enter or HKey.NumpadEnter or HKey.Space => alt,
            _ => alt && TerminalKeys.Character(key) != 0,
        };
    }

    /// <summary>The key pressed and released, as two win32 events.</summary>
    public static string Encode(in TerminalKey key)
    {
        int usage = key.ScanCode != 0 ? key.ScanCode : Usage(key.Key);
        ushort set1 = Set1(usage);
        ushort vk = VirtualKey(key, set1);
        char uc = Character(key);
        var state = State(key, set1);
        return Win32InputGenerator.GenerateKeyPress(vk, (ushort)(set1 & 0xFF), uc, state);
    }

    /// <summary>What Windows' own layout tables give for the key with these modifiers, which is
    /// what a console program reading key events expects to find.</summary>
    private static char Character(in TerminalKey key)
    {
        bool ctrl = (key.Modifiers & HMods.Ctrl) != 0, alt = (key.Modifiers & HMods.Alt) != 0;
        if (key.Key == HKey.Escape) return '\x1b';
        if (ctrl && alt) return '\0'; // Windows takes Ctrl+Alt for AltGr: no character on these keys
        return key.Key switch
        {
            HKey.Space => ' ',
            HKey.Backspace => ctrl ? '\x7f' : '\b',
            HKey.Enter or HKey.NumpadEnter => '\r',
            _ when TerminalKeys.Character(key) is var c and > 0 and <= 0xFFFF => (char)c,
            _ => '\0',
        };
    }

    private static CKS State(in TerminalKey key, ushort set1)
    {
        var mods = key.Modifiers;
        var keys = key.ModifierKeys;
        CKS s = 0;
        if ((mods & HMods.Shift) != 0) s |= CKS.ShiftPressed;
        if ((keys & ModifierKeys.CtrlLeft) != 0) s |= CKS.LeftCtrlPressed;
        if ((keys & ModifierKeys.CtrlRight) != 0) s |= CKS.RightCtrlPressed;
        if ((keys & ModifierKeys.AltLeft) != 0) s |= CKS.LeftAltPressed;
        if ((keys & ModifierKeys.AltRight) != 0) s |= CKS.RightAltPressed;
        // Windows reports AltGr as both, the left Ctrl it fakes and the right Alt.
        if ((keys & ModifierKeys.AltGr) != 0) s |= CKS.RightAltPressed | CKS.LeftCtrlPressed;
        // A platform that did not say which key: the left one.
        if ((mods & HMods.Ctrl) != 0 && (s & (CKS.LeftCtrlPressed | CKS.RightCtrlPressed)) == 0) s |= CKS.LeftCtrlPressed;
        if ((mods & HMods.Alt) != 0 && (s & (CKS.LeftAltPressed | CKS.RightAltPressed)) == 0) s |= CKS.LeftAltPressed;
        if ((mods & HMods.NumLock) != 0) s |= CKS.NumLockOn;
        if ((mods & HMods.CapsLock) != 0) s |= CKS.CapsLockOn;
        if ((set1 & 0xFF00) == 0xE000) s |= CKS.EnhancedKey;
        return s;
    }

    private static ushort VirtualKey(in TerminalKey key, ushort set1)
    {
        // A letter key's virtual key is its letter on every layout (AZERTY's A is where US has Q).
        int c = TerminalKeys.Character(key);
        if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            return (ushort)char.ToUpperInvariant((char)c);
        if (OperatingSystem.IsWindows() && set1 != 0)
        {
            uint vk = MapVirtualKeyW(set1, MapvkVscToVkEx);
            if (vk != 0) return (ushort)vk;
        }
        return UsVirtualKey(key.Key);
    }

    private const uint MapvkVscToVkEx = 3;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    /// <summary>A US keyboard's virtual key for the keys <see cref="Takes"/> takes.</summary>
    internal static ushort UsVirtualKey(HKey key) => key switch
    {
        >= HKey.A and <= HKey.Z => (ushort)(0x41 + (key - HKey.A)),
        >= HKey.Digit0 and <= HKey.Digit9 => (ushort)(0x30 + (key - HKey.Digit0)),
        HKey.Escape => 0x1B, HKey.Space => 0x20, HKey.Backspace => 0x08, HKey.Enter or HKey.NumpadEnter => 0x0D, HKey.Tab => 0x09,
        HKey.Comma => 0xBC, HKey.Period => 0xBE, HKey.Slash => 0xBF, HKey.Backslash => 0xDC,
        HKey.Semicolon => 0xBA, HKey.Quote => 0xDE, HKey.BracketLeft => 0xDB, HKey.BracketRight => 0xDD,
        HKey.Minus => 0xBD, HKey.Equals => 0xBB, HKey.Backtick => 0xC0,
        _ => 0,
    };

    /// <summary>The USB HID usage (keyboard page) of a key <see cref="KeyCode"/> names, for a key
    /// the platform gave no scan code for; 0 for none.</summary>
    internal static int Usage(HKey key) => key switch
    {
        >= HKey.A and <= HKey.Z => 0x04 + (key - HKey.A),
        HKey.Digit0 => 0x27,
        >= HKey.Digit1 and <= HKey.Digit9 => 0x1E + (key - HKey.Digit1),
        HKey.Enter => 0x28, HKey.Escape => 0x29, HKey.Backspace => 0x2A, HKey.Tab => 0x2B, HKey.Space => 0x2C,
        HKey.Minus => 0x2D, HKey.Equals => 0x2E, HKey.BracketLeft => 0x2F, HKey.BracketRight => 0x30, HKey.Backslash => 0x31,
        HKey.Semicolon => 0x33, HKey.Quote => 0x34, HKey.Backtick => 0x35, HKey.Comma => 0x36, HKey.Period => 0x37, HKey.Slash => 0x38,
        HKey.CapsLock => 0x39,
        >= HKey.F1 and <= HKey.F12 => 0x3A + (key - HKey.F1),
        HKey.PrintScreen => 0x46, HKey.ScrollLock => 0x47, HKey.Pause => 0x48,
        HKey.Insert => 0x49, HKey.Home => 0x4A, HKey.PageUp => 0x4B, HKey.Delete => 0x4C, HKey.End => 0x4D, HKey.PageDown => 0x4E,
        HKey.Right => 0x4F, HKey.Left => 0x50, HKey.Down => 0x51, HKey.Up => 0x52,
        HKey.NumLock => 0x53, HKey.NumpadDivide => 0x54, HKey.NumpadMultiply => 0x55, HKey.NumpadSubtract => 0x56,
        HKey.NumpadAdd => 0x57, HKey.NumpadEnter => 0x58,
        HKey.Numpad0 => 0x62,
        >= HKey.Numpad1 and <= HKey.Numpad9 => 0x59 + (key - HKey.Numpad1),
        HKey.NumpadDecimal => 0x63, HKey.Menu => 0x65, HKey.NumpadEquals => 0x67,
        >= HKey.F13 and <= HKey.F24 => 0x68 + (key - HKey.F13),
        HKey.CtrlLeft => 0xE0, HKey.ShiftLeft => 0xE1, HKey.AltLeft => 0xE2, HKey.SuperLeft => 0xE3,
        HKey.CtrlRight => 0xE4, HKey.ShiftRight => 0xE5, HKey.AltRight => 0xE6, HKey.SuperRight => 0xE7,
        _ => 0,
    };

    /// <summary>
    /// The PS/2 set 1 scan code of a USB HID usage, 0xE0 in the high byte for an extended key:
    /// the translation Windows' keyboard driver makes (Microsoft's "USB HID to PS/2 Scan Code
    /// Translation Table"). 0 for a usage with none.
    /// </summary>
    internal static ushort Set1(int usage) => usage switch
    {
        >= 0x04 and <= 0x1D => Letters[usage - 0x04],
        >= 0x1E and <= 0x26 => (ushort)(0x02 + (usage - 0x1E)), // 1 to 9
        0x27 => 0x0B, // 0
        0x28 => 0x1C, 0x29 => 0x01, 0x2A => 0x0E, 0x2B => 0x0F, 0x2C => 0x39,
        0x2D => 0x0C, 0x2E => 0x0D, 0x2F => 0x1A, 0x30 => 0x1B, 0x31 => 0x2B, 0x32 => 0x2B,
        0x33 => 0x27, 0x34 => 0x28, 0x35 => 0x29, 0x36 => 0x33, 0x37 => 0x34, 0x38 => 0x35,
        0x39 => 0x3A,
        >= 0x3A and <= 0x43 => (ushort)(0x3B + (usage - 0x3A)), // F1 to F10
        0x44 => 0x57, 0x45 => 0x58, // F11, F12
        0x46 => 0xE037, 0x47 => 0x46, 0x48 => 0xE11D, // Pause is E1 1D 45, which Windows keys as E11D
        0x49 => 0xE052, 0x4A => 0xE047, 0x4B => 0xE049, 0x4C => 0xE053, 0x4D => 0xE04F, 0x4E => 0xE051,
        0x4F => 0xE04D, 0x50 => 0xE04B, 0x51 => 0xE050, 0x52 => 0xE048,
        0x53 => 0x45, // NumLock
        0x54 => 0xE035, 0x55 => 0x37, 0x56 => 0x4A, 0x57 => 0x4E, 0x58 => 0xE01C,
        0x59 => 0x4F, 0x5A => 0x50, 0x5B => 0x51, 0x5C => 0x4B, 0x5D => 0x4C,
        0x5E => 0x4D, 0x5F => 0x47, 0x60 => 0x48, 0x61 => 0x49, 0x62 => 0x52, 0x63 => 0x53,
        0x64 => 0x56, // the key ISO keyboards have beside the left Shift
        0x65 => 0xE05D, 0x67 => 0x59,
        >= 0x68 and <= 0x72 => (ushort)(0x64 + (usage - 0x68)), // F13 to F23
        0x73 => 0x76, // F24
        0x85 => 0x7E, 0x87 => 0x73, 0x88 => 0x70, 0x89 => 0x7D, 0x8A => 0x79, 0x8B => 0x7B, // international
        0xE0 => 0x1D, 0xE1 => 0x2A, 0xE2 => 0x38, 0xE3 => 0xE05B,
        0xE4 => 0xE01D, 0xE5 => 0x36, 0xE6 => 0xE038, 0xE7 => 0xE05C,
        _ => 0,
    };

    // A to Z.
    private static ReadOnlySpan<ushort> Letters =>
    [
        0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32,
        0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C,
    ];
}
