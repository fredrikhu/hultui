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
/// Printable keys without Ctrl or Alt send nothing here: their text arrives as a
/// <see cref="TextInputEvent"/>, through the layout and the input method, so it is typed once.
/// HultUI's <see cref="HKey"/> is a scancode position, so Ctrl+letter means the letter at that
/// position on a US layout until the framework carries the layout key (seam K1).
/// </remarks>
internal static class TerminalKeys
{
    public static string? Encode(XTerminal term, in TerminalKey key)
    {
        var mods = key.Modifiers;

        if (term.KittyKeyboardActive)
        {
            var ev = new XTerm.Options.KeyEvent
            {
                Key = KittyName(key.Key, mods),
                Code = CodeName(key.Key),
                CtrlKey = (mods & HMods.Ctrl) != 0,
                AltKey = (mods & HMods.Alt) != 0,
                ShiftKey = (mods & HMods.Shift) != 0,
                MetaKey = (mods & HMods.Super) != 0,
            };
            var kitty = term.GenerateKittyKeyInput(ev, key.IsRepeat ? KittyKeyboardEventType.Repeat : KittyKeyboardEventType.Press);
            if (kitty is not null) return kitty;
        }

        var xm = ModifiersOf(mods);
        if (Special(key.Key) is { } special)
            return Empty(term.GenerateKeyInput(special, xm));

        if (Printable(key.Key) is { } ch)
        {
            if ((mods & (HMods.Ctrl | HMods.Alt)) == 0) return null;
            if ((mods & HMods.Shift) != 0 && ch is >= 'a' and <= 'z') ch = char.ToUpperInvariant(ch);
            return Empty(term.GenerateCharInput(ch, xm));
        }
        return null;
    }

    /// <summary>A printable key: its text comes from <see cref="TextInputEvent"/> unless Ctrl or
    /// Alt is held.</summary>
    public static bool IsPrintable(HKey key) => Printable(key) is not null;

    public static XMods ModifiersOf(HMods mods) =>
        ((mods & HMods.Shift) != 0 ? XMods.Shift : 0)
        | ((mods & HMods.Alt) != 0 ? XMods.Alt : 0)
        | ((mods & HMods.Ctrl) != 0 ? XMods.Control : 0);

    private static string? Empty(string s) => s.Length == 0 ? null : s;

    private static XKey? Special(HKey key) => key switch
    {
        HKey.Enter => XKey.Enter, HKey.Tab => XKey.Tab, HKey.Backspace => XKey.Backspace, HKey.Escape => XKey.Escape,
        HKey.Up => XKey.UpArrow, HKey.Down => XKey.DownArrow, HKey.Left => XKey.LeftArrow, HKey.Right => XKey.RightArrow,
        HKey.Home => XKey.Home, HKey.End => XKey.End, HKey.PageUp => XKey.PageUp, HKey.PageDown => XKey.PageDown,
        HKey.Insert => XKey.Insert, HKey.Delete => XKey.Delete,
        >= HKey.F1 and <= HKey.F12 => XKey.F1 + (key - HKey.F1),
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
        HKey.Backtick => "Backquote", HKey.Equals => "Equal",
        _ => key.ToString(),
    };

    /// <summary>The browser-style key value kitty's encoder reads (<c>ev.key</c>).</summary>
    private static string KittyName(HKey key, HMods mods) => key switch
    {
        HKey.Enter => "Enter", HKey.Tab => "Tab", HKey.Backspace => "Backspace", HKey.Escape => "Escape",
        HKey.Up => "ArrowUp", HKey.Down => "ArrowDown", HKey.Left => "ArrowLeft", HKey.Right => "ArrowRight",
        HKey.Home => "Home", HKey.End => "End", HKey.PageUp => "PageUp", HKey.PageDown => "PageDown",
        HKey.Insert => "Insert", HKey.Delete => "Delete",
        >= HKey.F1 and <= HKey.F12 => $"F{key - HKey.F1 + 1}",
        _ when Printable(key) is { } ch => ((mods & HMods.Shift) != 0 ? char.ToUpperInvariant(ch) : ch).ToString(),
        _ => "Unidentified",
    };
}
