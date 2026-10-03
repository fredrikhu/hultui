using HultUI.Core.Events;
using XTerm;
using XTerm.Input;
using static XtermNet.Probe;
using HKey = HultUI.Core.Events.KeyCode;
using HMods = HultUI.Core.Events.KeyModifiers;
using XKey = XTerm.Input.Key;
using XMods = XTerm.Input.KeyModifiers;

namespace XtermNet;

/// <summary>
/// Question 6: HultUI's key events to the bytes a terminal sends. HultUI hands an element a
/// <see cref="InputEvent.KeyInput"/> (a key named by SCANCODE position, plus modifiers) and,
/// separately, a <see cref="InputEvent.TextInputInput"/> (layout- and IME-resolved text). The
/// mapping below is the one HultUI.Terminal would ship; the table checks it against xterm's own
/// encodings (ctlseqs, PC-style function keys), in normal and application cursor mode, and in the
/// Kitty keyboard protocol's first level.
/// </summary>
internal static class Q6
{
    private const string E = "\x1b";

    public static int Run()
    {
        var rows = new (string Name, HKey Key, HMods Mods, string? Expect, Action<Terminal>? Setup)[]
        {
            ("Enter", HKey.Enter, 0, "\r", null),
            ("Alt+Enter", HKey.Enter, HMods.Alt, E + "\r", null),
            ("Tab", HKey.Tab, 0, "\t", null),
            ("Shift+Tab", HKey.Tab, HMods.Shift, E + "[Z", null),
            ("Backspace", HKey.Backspace, 0, "\x7f", null),
            ("Ctrl+Backspace", HKey.Backspace, HMods.Ctrl, "\x08", null),
            ("Alt+Backspace", HKey.Backspace, HMods.Alt, E + "\x7f", null),
            ("Escape", HKey.Escape, 0, E, null),
            ("Up", HKey.Up, 0, E + "[A", null),
            ("Shift+Up", HKey.Up, HMods.Shift, E + "[1;2A", null),
            ("Alt+Up", HKey.Up, HMods.Alt, E + "[1;3A", null),
            ("Ctrl+Up", HKey.Up, HMods.Ctrl, E + "[1;5A", null),
            ("Ctrl+Shift+Left", HKey.Left, HMods.Ctrl | HMods.Shift, E + "[1;6D", null),
            ("Up, DECCKM", HKey.Up, 0, E + "OA", t => t.Write(E + "[?1h")),
            ("Ctrl+Up, DECCKM", HKey.Up, HMods.Ctrl, E + "[1;5A", t => t.Write(E + "[?1h")),
            ("Home", HKey.Home, 0, E + "[H", null),
            ("Home, DECCKM", HKey.Home, 0, E + "OH", t => t.Write(E + "[?1h")),
            ("End", HKey.End, 0, E + "[F", null),
            ("Ctrl+End", HKey.End, HMods.Ctrl, E + "[1;5F", null),
            ("Insert", HKey.Insert, 0, E + "[2~", null),
            ("Delete", HKey.Delete, 0, E + "[3~", null),
            ("Shift+Delete", HKey.Delete, HMods.Shift, E + "[3;2~", null),
            ("PageUp", HKey.PageUp, 0, E + "[5~", null),
            ("Ctrl+PageDown", HKey.PageDown, HMods.Ctrl, E + "[6;5~", null),
            ("F1", HKey.F1, 0, E + "OP", null),
            ("F4", HKey.F4, 0, E + "OS", null),
            ("Shift+F1", HKey.F1, HMods.Shift, E + "[1;2P", null),
            ("F5", HKey.F5, 0, E + "[15~", null),
            ("F6", HKey.F6, 0, E + "[17~", null),
            ("F12", HKey.F12, 0, E + "[24~", null),
            ("Ctrl+F5", HKey.F5, HMods.Ctrl, E + "[15;5~", null),
            ("Ctrl+A", HKey.A, HMods.Ctrl, "\x01", null),
            ("Ctrl+C", HKey.C, HMods.Ctrl, "\x03", null),
            ("Ctrl+Z", HKey.Z, HMods.Ctrl, "\x1a", null),
            ("Ctrl+Space", HKey.Space, HMods.Ctrl, "\0", null),
            ("Ctrl+[", HKey.BracketLeft, HMods.Ctrl, E, null),
            ("Ctrl+]", HKey.BracketRight, HMods.Ctrl, "\x1d", null),
            ("Ctrl+\\", HKey.Backslash, HMods.Ctrl, "\x1c", null),
            ("Ctrl+/", HKey.Slash, HMods.Ctrl, "\x1f", null),
            ("Alt+a", HKey.A, HMods.Alt, E + "a", null),
            ("Alt+Shift+a", HKey.A, HMods.Alt | HMods.Shift, E + "A", null),
            ("Ctrl+Alt+a", HKey.A, HMods.Ctrl | HMods.Alt, E + "\x01", null),
            ("a (text comes from TextInput)", HKey.A, 0, null, null),
            ("Shift+a (text comes from TextInput)", HKey.A, HMods.Shift, null, null),
            ("Space (text comes from TextInput)", HKey.Space, 0, null, null),
            ("kitty 1: Ctrl+C", HKey.C, HMods.Ctrl, E + "[99;5u", t => t.Write(E + "[>1u")),
            ("kitty 1: Escape", HKey.Escape, 0, E + "[27u", t => t.Write(E + "[>1u")),
            ("kitty 1: Alt+a", HKey.A, HMods.Alt, E + "[97;3u", t => t.Write(E + "[>1u")),
            ("kitty 1: Enter stays legacy", HKey.Enter, 0, "\r", t => t.Write(E + "[>1u")),
        };

        var c = new Checks();
        foreach (var (name, key, mods, expect, setup) in rows)
        {
            c.Add(name, () =>
            {
                var (t, _) = NewTerminal();
                setup?.Invoke(t);
                var got = TerminalKeys.Encode(t, key, mods);
                return (got == expect, $"{Show(got),-14} expected {Show(expect)}");
            });
        }
        var failed = c.Print();

        Log("");
        Log("HultUI key codes with no terminal meaning or no HultUI name:");
        Log($"  no keypad codes in HultUI.KeyCode (Numpad0-9, NumpadEnter, ...): DECKPAM application keypad cannot be honoured");
        Log($"  no F13-F24, no media keys; Pause/PrintScreen/Menu exist but xterm gives them nothing standard");
        return failed == 0 ? 0 : 1;
    }
}

/// <summary>
/// The mapping HultUI.Terminal would ship. Returns null for keys whose bytes come from the
/// TextInput event instead (printable keys without Ctrl or Alt), so text is typed once, through
/// the layout and the IME, and never twice.
/// </summary>
internal static class TerminalKeys
{
    public static string? Encode(Terminal t, HKey key, HMods mods)
    {
        var xm = (mods.HasFlag(HMods.Shift) ? XMods.Shift : 0)
               | (mods.HasFlag(HMods.Alt) ? XMods.Alt : 0)
               | (mods.HasFlag(HMods.Ctrl) ? XMods.Control : 0);

        if (t.KittyKeyboardActive)
        {
            var ev = new XTerm.Options.KeyEvent
            {
                Key = KittyName(key, mods),
                Code = CodeName(key),
                CtrlKey = mods.HasFlag(HMods.Ctrl),
                AltKey = mods.HasFlag(HMods.Alt),
                ShiftKey = mods.HasFlag(HMods.Shift),
                MetaKey = mods.HasFlag(HMods.Super),
            };
            var k = t.GenerateKittyKeyInput(ev);
            if (k is not null) return k;
        }

        if (Special(key) is { } special)
            return t.GenerateKeyInput(special, xm);

        if (Printable(key) is { } ch)
        {
            // Plain and shifted printables arrive as TextInput; only Ctrl and Alt make bytes here.
            if (!mods.HasFlag(HMods.Ctrl) && !mods.HasFlag(HMods.Alt)) return null;
            if (mods.HasFlag(HMods.Shift) && ch is >= 'a' and <= 'z') ch = char.ToUpperInvariant(ch);
            return t.GenerateCharInput(ch, xm);
        }
        return null;
    }

    private static XKey? Special(HKey key) => key switch
    {
        HKey.Enter => XKey.Enter, HKey.Tab => XKey.Tab, HKey.Backspace => XKey.Backspace, HKey.Escape => XKey.Escape,
        HKey.Up => XKey.UpArrow, HKey.Down => XKey.DownArrow, HKey.Left => XKey.LeftArrow, HKey.Right => XKey.RightArrow,
        HKey.Home => XKey.Home, HKey.End => XKey.End, HKey.PageUp => XKey.PageUp, HKey.PageDown => XKey.PageDown,
        HKey.Insert => XKey.Insert, HKey.Delete => XKey.Delete,
        >= HKey.F1 and <= HKey.F12 => XKey.F1 + (key - HKey.F1),
        _ => null,
    };

    /// <summary>The character a key's US position types: the only reading of a scancode HultUI offers.</summary>
    private static char? Printable(HKey key) => key switch
    {
        >= HKey.A and <= HKey.Z => (char)('a' + (key - HKey.A)),
        >= HKey.Digit0 and <= HKey.Digit9 => (char)('0' + (key - HKey.Digit0)),
        HKey.Space => ' ', HKey.Comma => ',', HKey.Period => '.', HKey.Slash => '/', HKey.Backslash => '\\',
        HKey.Semicolon => ';', HKey.Quote => '\'', HKey.BracketLeft => '[', HKey.BracketRight => ']',
        HKey.Minus => '-', HKey.Equals => '=', HKey.Backtick => '`',
        _ => null,
    };

    /// <summary>The browser-style physical key name (<c>ev.code</c>): HultUI's KeyCode is a scancode
    /// position, which is exactly what this names.</summary>
    private static string CodeName(HKey key) => key switch
    {
        >= HKey.A and <= HKey.Z => $"Key{(char)('A' + (key - HKey.A))}",
        >= HKey.Digit0 and <= HKey.Digit9 => $"Digit{key - HKey.Digit0}",
        HKey.Up => "ArrowUp", HKey.Down => "ArrowDown", HKey.Left => "ArrowLeft", HKey.Right => "ArrowRight",
        HKey.Backtick => "Backquote", HKey.Equals => "Equal",
        _ => key.ToString(),   // Enter, Escape, Tab, Space, Comma, Period, Slash, BracketLeft, F1 ... match already
    };

    /// <summary>The browser-style key value Kitty's encoder reads.</summary>
    private static string KittyName(HKey key, HMods mods) => key switch
    {
        HKey.Enter => "Enter", HKey.Tab => "Tab", HKey.Backspace => "Backspace", HKey.Escape => "Escape",
        HKey.Up => "ArrowUp", HKey.Down => "ArrowDown", HKey.Left => "ArrowLeft", HKey.Right => "ArrowRight",
        HKey.Home => "Home", HKey.End => "End", HKey.PageUp => "PageUp", HKey.PageDown => "PageDown",
        HKey.Insert => "Insert", HKey.Delete => "Delete",
        >= HKey.F1 and <= HKey.F12 => $"F{key - HKey.F1 + 1}",
        _ when Printable(key) is { } ch => (mods.HasFlag(HMods.Shift) ? char.ToUpperInvariant(ch) : ch).ToString(),
        _ => "Unidentified",
    };
}
