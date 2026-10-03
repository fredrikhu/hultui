using HultUI.Core.Events;
using XTerm.Input;
using HKey = HultUI.Core.Events.KeyCode;
using HMods = HultUI.Core.Events.KeyModifiers;
using XKey = XTerm.Input.Key;
using XMods = XTerm.Input.KeyModifiers;
using XTerminal = XTerm.Terminal;

namespace HultUI.Terminal.Emulation;

/// <summary>
/// HultUI's key events to the bytes a terminal sends. Measured in the spike against xterm's own
/// encodings (49 of 49 with the patched engine): arrows, Home/End and F-keys with modifiers in
/// normal and application-cursor mode, Ctrl and Alt combinations, and the kitty keyboard
/// protocol's flags when a program pushes them.
/// </summary>
/// <remarks>
/// <para>Printable keys without Ctrl or Alt send nothing here: their text arrives as a
/// <see cref="TextInputEvent"/>, through the layout and the input method, so it is typed once.</para>
/// <para>A chord's character is the layout's (<see cref="KeyDownEvent.LayoutKey"/>), so Ctrl+Z is
/// the key labelled Z on QWERTZ and AZERTY, and Alt+Shift+comma is <c>ESC &lt;</c>. Where the
/// platform did not say, it is the character at the key's position on a US layout.</para>
/// <para>The keypad follows NumLock: on, its keys are the keypad's (<c>ESC O w</c> and so on
/// once a program sets application keypad mode, DECKPAM); off, they are Home, Up, PageUp and the
/// rest, as their twins. Its operators and Enter are the keypad's either way.</para>
/// </remarks>
internal static class TerminalKeys
{
    /// <param name="term">The engine, for its modes.</param>
    /// <param name="key">The key.</param>
    /// <param name="win32Input">The connection reads win32-input-mode events better than VT
    /// bytes (ConPTY): used for the keys <see cref="Win32Keys.Takes"/> while the program has the
    /// mode on.</param>
    public static string? Encode(XTerminal term, in TerminalKey key, bool win32Input = false)
    {
        var mods = key.Modifiers;

        if (term.KittyKeyboardActive)
        {
            var ev = new XTerm.Options.KeyEvent
            {
                Key = KittyName(key),
                Code = CodeName(key.Key),
                CtrlKey = (mods & HMods.Ctrl) != 0,
                AltKey = (mods & HMods.Alt) != 0,
                ShiftKey = (mods & HMods.Shift) != 0,
                MetaKey = (mods & HMods.Super) != 0,
            };
            var kitty = term.GenerateKittyKeyInput(ev, key.IsRepeat ? KittyKeyboardEventType.Repeat : KittyKeyboardEventType.Press);
            if (kitty is not null) return kitty;
        }

        if (win32Input && term.Win32InputMode && Win32Keys.Takes(key))
            return Win32Keys.Encode(key);

        // Windows types a character by its code with Alt and the keypad's digits (Alt, 6, 5 is
        // A): the character comes as text when Alt is let go, and the digits are not keys.
        if (OperatingSystem.IsWindows() && (mods & (HMods.Alt | HMods.Ctrl)) == HMods.Alt
            && key.Key is >= HKey.Numpad0 and <= HKey.Numpad9)
            return null;

        var xm = ModifiersOf(mods);
        if (Keypad(term, key, xm) is { } keypad)
            return keypad;
        if (Special(key.Key) is { } special)
            return Empty(term.GenerateKeyInput(special, xm));

        int ch = Character(key);
        if (ch == 0 || (mods & (HMods.Ctrl | HMods.Alt)) == 0) return null;
        if (ch > 0xFFFF)
            return (mods & HMods.Alt) != 0 ? "\x1b" + char.ConvertFromUtf32(ch) : null;
        return Empty(term.GenerateCharInput((char)ch, xm));
    }

    /// <summary>
    /// The character a key types, for a chord: the layout's when the platform said
    /// (<see cref="KeyDownEvent.LayoutKey"/>), else the US layout's at the key's position, Shift
    /// upper-casing a letter. 0 for a key that types none.
    /// </summary>
    public static int Character(in TerminalKey key)
    {
        if (key.LayoutKey != 0) return key.LayoutKey;
        if (Printable(key.Key) is not { } ch) return 0;
        return (key.Modifiers & HMods.Shift) != 0 && ch is >= 'a' and <= 'z' ? char.ToUpperInvariant(ch) : ch;
    }

    /// <summary>A key whose plain press types a character, which arrives as text input: the
    /// printable keys, the keypad's operators, and its digits while NumLock is on.</summary>
    public static bool TypesText(in TerminalKey key) => key.Key switch
    {
        >= HKey.Numpad0 and <= HKey.Numpad9 or HKey.NumpadDecimal => (key.Modifiers & HMods.NumLock) != 0,
        HKey.NumpadAdd or HKey.NumpadSubtract or HKey.NumpadMultiply or HKey.NumpadDivide or HKey.NumpadEquals => true,
        _ => Character(key) != 0,
    };

    /// <summary>A key on the keypad.</summary>
    public static bool IsKeypad(HKey key) => key is >= HKey.Numpad0 and <= HKey.NumpadEquals;

    public static XMods ModifiersOf(HMods mods) =>
        ((mods & HMods.Shift) != 0 ? XMods.Shift : 0)
        | ((mods & HMods.Alt) != 0 ? XMods.Alt : 0)
        | ((mods & HMods.Ctrl) != 0 ? XMods.Control : 0);

    private static string? Empty(string s) => s.Length == 0 ? null : s;

    private static string? Keypad(XTerminal term, in TerminalKey key, XMods xm)
    {
        if (!IsKeypad(key.Key)) return null;
        bool numLock = (key.Modifiers & HMods.NumLock) != 0;
        XKey? x = key.Key switch
        {
            HKey.Numpad5 when !numLock => null, // Begin: no engine key, written below
            >= HKey.Numpad0 and <= HKey.Numpad9 or HKey.NumpadDecimal when !numLock => NavigationTwin(key.Key),
            >= HKey.Numpad0 and <= HKey.Numpad9 => XKey.Keypad0 + (key.Key - HKey.Numpad0),
            HKey.NumpadDecimal => XKey.KeypadDecimal,
            HKey.NumpadAdd => XKey.KeypadAdd,
            HKey.NumpadSubtract => XKey.KeypadSubtract,
            HKey.NumpadMultiply => XKey.KeypadMultiply,
            HKey.NumpadDivide => XKey.KeypadDivide,
            // Enter outside application keypad mode, so Alt+Enter keeps its ESC as on the main key.
            HKey.NumpadEnter => term.ApplicationKeypad ? XKey.KeypadEnter : XKey.Enter,
            _ => null,
        };
        if (x is { } k) return Empty(term.GenerateKeyInput(k, xm));
        return key.Key switch
        {
            // xterm's KP_Begin: CSI E, SS3 E in application cursor mode, CSI 1;m E modified.
            HKey.Numpad5 => xm != 0 ? $"\x1b[1;{1 + Bit(xm, XMods.Shift, 1) + Bit(xm, XMods.Alt, 2) + Bit(xm, XMods.Control, 4)}E"
                : term.ApplicationCursorKeys ? "\x1bOE" : "\x1b[E",
            // xterm's KP_Equal; the engine has no key for it.
            HKey.NumpadEquals => term.ApplicationKeypad ? "\x1bOX" : "=",
            _ => null,
        };

        static int Bit(XMods mods, XMods flag, int value) => (mods & flag) != 0 ? value : 0;
    }

    /// <summary>What a keypad key is with NumLock off.</summary>
    private static XKey NavigationTwin(HKey key) => key switch
    {
        HKey.Numpad7 => XKey.Home, HKey.Numpad8 => XKey.UpArrow, HKey.Numpad9 => XKey.PageUp,
        HKey.Numpad4 => XKey.LeftArrow, HKey.Numpad6 => XKey.RightArrow,
        HKey.Numpad1 => XKey.End, HKey.Numpad2 => XKey.DownArrow, HKey.Numpad3 => XKey.PageDown,
        HKey.Numpad0 => XKey.Insert,
        _ => XKey.Delete, // NumpadDecimal
    };

    private static XKey? Special(HKey key) => key switch
    {
        HKey.Enter => XKey.Enter, HKey.Tab => XKey.Tab, HKey.Backspace => XKey.Backspace, HKey.Escape => XKey.Escape,
        HKey.Up => XKey.UpArrow, HKey.Down => XKey.DownArrow, HKey.Left => XKey.LeftArrow, HKey.Right => XKey.RightArrow,
        HKey.Home => XKey.Home, HKey.End => XKey.End, HKey.PageUp => XKey.PageUp, HKey.PageDown => XKey.PageDown,
        HKey.Insert => XKey.Insert, HKey.Delete => XKey.Delete,
        >= HKey.F1 and <= HKey.F12 => XKey.F1 + (key - HKey.F1),
        // VT220's codes stop at F20; xterm gives F21 to F24 none of their own.
        >= HKey.F13 and <= HKey.F20 => XKey.F13 + (key - HKey.F13),
        _ => null,
    };

    /// <summary>The character a key's US position types.</summary>
    private static char? Printable(HKey key) => key switch
    {
        >= HKey.A and <= HKey.Z => (char)('a' + (key - HKey.A)),
        >= HKey.Digit0 and <= HKey.Digit9 => (char)('0' + (key - HKey.Digit0)),
        HKey.Space => ' ', HKey.Comma => ',', HKey.Period => '.', HKey.Slash => '/', HKey.Backslash => '\\',
        HKey.Semicolon => ';', HKey.Quote => '\'', HKey.BracketLeft => '[', HKey.BracketRight => ']',
        HKey.Minus => '-', HKey.Equals => '=', HKey.Backtick => '`',
        _ => null,
    };

    /// <summary>The browser-style physical key name kitty's encoder reads (<c>ev.code</c>).</summary>
    private static string CodeName(HKey key) => key switch
    {
        >= HKey.A and <= HKey.Z => $"Key{(char)('A' + (key - HKey.A))}",
        >= HKey.Digit0 and <= HKey.Digit9 => $"Digit{key - HKey.Digit0}",
        HKey.Up => "ArrowUp", HKey.Down => "ArrowDown", HKey.Left => "ArrowLeft", HKey.Right => "ArrowRight",
        HKey.Backtick => "Backquote", HKey.Equals => "Equal", HKey.NumpadEquals => "NumpadEqual",
        HKey.Unknown => "",
        _ => key.ToString(),
    };

    /// <summary>The browser-style key value kitty's encoder reads (<c>ev.key</c>): the character
    /// the key types, Shift applied, or the key's name.</summary>
    private static string KittyName(in TerminalKey key)
    {
        if (IsKeypad(key.Key))
        {
            bool numLock = (key.Modifiers & HMods.NumLock) != 0;
            return key.Key switch
            {
                >= HKey.Numpad0 and <= HKey.Numpad9 when numLock => ((char)('0' + (key.Key - HKey.Numpad0))).ToString(),
                HKey.NumpadDecimal when numLock => ".",
                HKey.Numpad5 => "Clear",
                >= HKey.Numpad0 and <= HKey.Numpad9 or HKey.NumpadDecimal => SpecialName(NavigationTwin(key.Key)),
                HKey.NumpadAdd => "+", HKey.NumpadSubtract => "-", HKey.NumpadMultiply => "*", HKey.NumpadDivide => "/",
                HKey.NumpadEquals => "=",
                _ => "Enter",
            };
        }
        if (Special(key.Key) is { } special) return SpecialName(special);
        return Character(key) is var ch and not 0 ? char.ConvertFromUtf32(ch) : "Unidentified";
    }

    private static string SpecialName(XKey key) => key switch
    {
        XKey.UpArrow => "ArrowUp", XKey.DownArrow => "ArrowDown", XKey.LeftArrow => "ArrowLeft", XKey.RightArrow => "ArrowRight",
        _ => key.ToString(), // Enter, Tab, Backspace, Escape, Home, End, PageUp, PageDown, Insert, Delete, F1 to F20
    };
}
