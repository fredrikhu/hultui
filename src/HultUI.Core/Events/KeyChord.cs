using System.Text;

namespace HultUI.Core.Events;

/// <summary>
/// A keyboard shortcut: a key and the modifiers held with it. Matches a <see cref="KeyDownEvent"/>
/// exactly, reads back from the text it writes, and is the one way the framework and the apps
/// spell a shortcut.
/// </summary>
/// <remarks>
/// <para><b>The modifiers must be exactly the chord's.</b> Ctrl+C is not C: a view that switched
/// on the bare key, as several did, checked out a branch on Ctrl+C. Super counts as Ctrl, so the
/// Mac's ⌘ works wherever the PC's Ctrl does; AltGr is not Alt (the platform already reports it
/// apart), and CapsLock and NumLock never count.</para>
/// <para><b>A letter is the letter printed on the key</b>, on whatever layout: Ctrl+Z is where
/// the Z is on a German keyboard — <see cref="KeyDownEvent.LayoutKey"/>, the same rule the
/// framework's text fields use. <b>Any other key is a position</b>: <see cref="KeyCode.Slash"/>
/// is the key a US keyboard has / on, which is always reachable but may carry another symbol.
/// <b>A character chord</b> (<see cref="Of(char, KeyModifiers)"/>) is the character the layout
/// types, whatever key and Shift it takes: Ctrl and + zooms in on a Swedish keyboard, where +
/// is a key of its own, as on a US one, where it is Shift and =. Shift is not compared for one.
/// A digit matches the number row and, with NumLock on, the keypad; Enter matches the keypad's
/// Enter.</para>
/// </remarks>
public readonly record struct KeyChord
{
    private const KeyModifiers Compared = KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift;

    /// <summary>The key, or <see cref="KeyCode.Unknown"/> for a character chord.</summary>
    public KeyCode Key { get; }

    /// <summary>The character, for a character chord; '\0' for a key.</summary>
    public char Char { get; }

    /// <summary>Ctrl, Alt and Shift; nothing else is kept.</summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>A key with modifiers. <see cref="KeyModifiers.Super"/> is taken as Ctrl.</summary>
    public KeyChord(KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (key == KeyCode.Unknown) throw new ArgumentOutOfRangeException(nameof(key), "A chord needs a key.");
        Key = key;
        Modifiers = Normalize(modifiers);
    }

    private KeyChord(char c, KeyModifiers modifiers)
    {
        Char = c;
        Modifiers = Normalize(modifiers) & ~KeyModifiers.Shift;
    }

    /// <summary>
    /// The chord for a character as the layout types it — for a shortcut that means the symbol,
    /// as zoom means +, rather than a position. A letter or a digit is the key chord for it.
    /// </summary>
    public static KeyChord Of(char c, KeyModifiers modifiers = KeyModifiers.None)
    {
        if (c is >= 'a' and <= 'z') return new(Offset(KeyCode.A, c - 'a'), modifiers);
        if (c is >= 'A' and <= 'Z') return new(Offset(KeyCode.A, c - 'A'), modifiers);
        if (c is >= '0' and <= '9') return new(Offset(KeyCode.Digit0, c - '0'), modifiers);
        if (char.IsControl(c) || char.IsWhiteSpace(c))
            throw new ArgumentOutOfRangeException(nameof(c), "A character chord needs a character that is typed.");
        return new KeyChord(c, modifiers);
    }

    /// <summary>True for a chord that names a character rather than a key.</summary>
    public bool IsCharacter => Char != '\0';

    private static KeyModifiers Normalize(KeyModifiers m) =>
        ((m & KeyModifiers.Super) != 0 ? m | KeyModifiers.Ctrl : m) & Compared;

    /// <summary>Whether <paramref name="e"/> is this chord.</summary>
    public bool Matches(KeyDownEvent e)
    {
        var held = Normalize(e.Modifiers);
        if (IsCharacter)
            return (held & ~KeyModifiers.Shift) == Modifiers && Typed(e) == Char;
        if (held != Modifiers) return false;
        return Key switch
        {
            >= KeyCode.A and <= KeyCode.Z => e.ShortcutLetter == (char)('a' + (Key - KeyCode.A)),
            >= KeyCode.Digit0 and <= KeyCode.Digit9 => e.Digit == Key - KeyCode.Digit0,
            KeyCode.Enter => e.Key is KeyCode.Enter or KeyCode.NumpadEnter,
            _ => e.Key == Key,
        };
    }

    /// <summary>The character the key typed: the layout's, else the US one at its position.</summary>
    private static char Typed(KeyDownEvent e)
    {
        if (e.LayoutKey is > 0 and < 0x10000) return (char)e.LayoutKey;
        bool shift = (e.Modifiers & KeyModifiers.Shift) != 0;
        return UsCharacter(e.Key, shift);
    }

    /// <summary>A modifier key on its own — what a shortcut capture ignores while it waits for
    /// the real key.</summary>
    public static bool IsModifierKey(KeyCode key) => key is
        KeyCode.ShiftLeft or KeyCode.ShiftRight or KeyCode.CtrlLeft or KeyCode.CtrlRight
        or KeyCode.AltLeft or KeyCode.AltRight or KeyCode.SuperLeft or KeyCode.SuperRight
        or KeyCode.CapsLock or KeyCode.NumLock;

    /// <summary>The chord a key press is, for capturing a new binding; null for a modifier on
    /// its own. Always a key chord: a press is a position.</summary>
    public static KeyChord? From(KeyDownEvent e) =>
        IsModifierKey(e.Key) || e.Key == KeyCode.Unknown ? null : new KeyChord(e.Key, e.Modifiers);

    // ──── Text ────

    /// <summary>
    /// The chord as people read it and as settings store it: <c>Ctrl+Shift+P</c>, <c>Alt+↑</c>,
    /// <c>Ctrl+,</c>, and a bare letter as typed, <c>m</c>. <see cref="Parse"/> reads it back.
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        if ((Modifiers & KeyModifiers.Ctrl) != 0) sb.Append("Ctrl+");
        if ((Modifiers & KeyModifiers.Alt) != 0) sb.Append("Alt+");
        if ((Modifiers & KeyModifiers.Shift) != 0) sb.Append("Shift+");
        // A letter on its own is written in lower case, as typed: "m" reads as the key, where
        // "M" reads as Shift and m.
        if (!IsCharacter && Modifiers == KeyModifiers.None && Key is >= KeyCode.A and <= KeyCode.Z)
            sb.Append((char)('a' + (Key - KeyCode.A)));
        else
            sb.Append(IsCharacter ? Char.ToString() : Name(Key));
        return sb.ToString();
    }

    /// <summary>
    /// Reads a chord written by <see cref="ToString"/>, and the older spellings settings files
    /// hold: modifier names in any case and with <c>Cmd</c>, <c>Super</c> or <c>Control</c>, key
    /// names as <see cref="KeyCode"/> spells them (<c>Digit1</c>, <c>PageUp</c>), and
    /// <c>PgUp</c>/<c>PgDn</c>. A single character that is a US key's unshifted symbol is that
    /// key; any other is a character chord. Null for anything else.
    /// </summary>
    public static KeyChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var rest = text.Trim();
        var modifiers = KeyModifiers.None;
        while (true)
        {
            int plus = rest.IndexOf('+');
            if (plus <= 0 || plus == rest.Length - 1) break;   // "+" alone, or a trailing key of "+"
            var name = rest[..plus].Trim();
            var m = name.ToLowerInvariant() switch
            {
                "ctrl" or "control" or "cmd" or "super" or "⌘" => KeyModifiers.Ctrl,
                "alt" or "option" or "⌥" => KeyModifiers.Alt,
                "shift" or "⇧" => KeyModifiers.Shift,
                _ => KeyModifiers.None,
            };
            if (m == KeyModifiers.None) break;
            modifiers |= m;
            rest = rest[(plus + 1)..].Trim();
        }

        if (KeyFromName(rest) is { } key) return new KeyChord(key, modifiers);
        if (rest.Length == 1 && !char.IsControl(rest[0]) && !char.IsWhiteSpace(rest[0]))
            return Of(rest[0], modifiers);
        return null;
    }

    private static string Name(KeyCode key) => key switch
    {
        >= KeyCode.A and <= KeyCode.Z => ((char)('A' + (key - KeyCode.A))).ToString(),
        >= KeyCode.Digit0 and <= KeyCode.Digit9 => ((char)('0' + (key - KeyCode.Digit0))).ToString(),
        >= KeyCode.Numpad0 and <= KeyCode.Numpad9 => "Num " + (char)('0' + (key - KeyCode.Numpad0)),
        KeyCode.Up => "↑",
        KeyCode.Down => "↓",
        KeyCode.Left => "←",
        KeyCode.Right => "→",
        KeyCode.PageUp => "PgUp",
        KeyCode.PageDown => "PgDn",
        KeyCode.Escape => "Esc",
        KeyCode.NumpadDecimal => "Num .",
        KeyCode.NumpadAdd => "Num +",
        KeyCode.NumpadSubtract => "Num -",
        KeyCode.NumpadMultiply => "Num *",
        KeyCode.NumpadDivide => "Num /",
        KeyCode.NumpadEnter => "Num Enter",
        KeyCode.NumpadEquals => "Num =",
        _ when UsCharacter(key, shift: false) is not '\0' and var c => c.ToString(),
        _ => key.ToString(),
    };

    private static KeyCode? KeyFromName(string name)
    {
        if (name.Length == 1)
        {
            char c = name[0];
            if (c is >= 'a' and <= 'z') return Offset(KeyCode.A, c - 'a');
            if (c is >= 'A' and <= 'Z') return Offset(KeyCode.A, c - 'A');
            if (c is >= '0' and <= '9') return Offset(KeyCode.Digit0, c - '0');
            foreach (var (k, symbol) in UsSymbols)
                if (symbol == c) return k;
            return c switch { '↑' => KeyCode.Up, '↓' => KeyCode.Down, '←' => KeyCode.Left, '→' => KeyCode.Right, _ => null };
        }
        switch (name.ToLowerInvariant())
        {
            case "pgup": return KeyCode.PageUp;
            case "pgdn": return KeyCode.PageDown;
            case "esc": return KeyCode.Escape;
            case "del": return KeyCode.Delete;
            case "ins": return KeyCode.Insert;
            case "return" or "↵": return KeyCode.Enter;
        }
        if (name.StartsWith("Num ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = name[4..].Trim();
            if (rest.Length == 1 && rest[0] is >= '0' and <= '9') return Offset(KeyCode.Numpad0, rest[0] - '0');
            return rest switch
            {
                "." => KeyCode.NumpadDecimal, "+" => KeyCode.NumpadAdd, "-" => KeyCode.NumpadSubtract,
                "*" => KeyCode.NumpadMultiply, "/" => KeyCode.NumpadDivide, "=" => KeyCode.NumpadEquals,
                _ when rest.Equals("Enter", StringComparison.OrdinalIgnoreCase) => KeyCode.NumpadEnter,
                _ => null,
            };
        }
        return Enum.TryParse<KeyCode>(name, ignoreCase: true, out var key) && key != KeyCode.Unknown
               && !char.IsDigit(name[0]) ? key : null;
    }

    private static KeyCode Offset(KeyCode first, int n) => (KeyCode)((int)first + n);

    private static readonly (KeyCode Key, char Symbol)[] UsSymbols =
    [
        (KeyCode.Comma, ','), (KeyCode.Period, '.'), (KeyCode.Slash, '/'), (KeyCode.Backslash, '\\'),
        (KeyCode.Semicolon, ';'), (KeyCode.Quote, '\''), (KeyCode.BracketLeft, '['),
        (KeyCode.BracketRight, ']'), (KeyCode.Minus, '-'), (KeyCode.Equals, '='), (KeyCode.Backtick, '`'),
    ];

    /// <summary>The character a key types on a US layout — what a press is taken to type when
    /// the platform did not say (headless input).</summary>
    private static char UsCharacter(KeyCode key, bool shift) => key switch
    {
        >= KeyCode.Digit0 and <= KeyCode.Digit9 => shift ? ")!@#$%^&*("[key - KeyCode.Digit0] : (char)('0' + (key - KeyCode.Digit0)),
        >= KeyCode.Numpad0 and <= KeyCode.Numpad9 => (char)('0' + (key - KeyCode.Numpad0)),
        KeyCode.Comma => shift ? '<' : ',',
        KeyCode.Period => shift ? '>' : '.',
        KeyCode.Slash => shift ? '?' : '/',
        KeyCode.Backslash => shift ? '|' : '\\',
        KeyCode.Semicolon => shift ? ':' : ';',
        KeyCode.Quote => shift ? '"' : '\'',
        KeyCode.BracketLeft => shift ? '{' : '[',
        KeyCode.BracketRight => shift ? '}' : ']',
        KeyCode.Minus => shift ? '_' : '-',
        KeyCode.Equals => shift ? '+' : '=',
        KeyCode.Backtick => shift ? '~' : '`',
        KeyCode.NumpadDecimal => '.',
        KeyCode.NumpadAdd => '+',
        KeyCode.NumpadSubtract => '-',
        KeyCode.NumpadMultiply => '*',
        KeyCode.NumpadDivide => '/',
        KeyCode.NumpadEquals => '=',
        _ => '\0',
    };
}
