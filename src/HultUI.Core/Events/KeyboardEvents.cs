namespace HultUI.Core.Events;

public sealed class KeyDownEvent : ElementEvent
{
    public KeyCode Key { get; init; }
    public KeyModifiers Modifiers { get; init; }
    public bool IsRepeat { get; init; }

    /// <summary>
    /// The physical key as the platform reports it, for keys <see cref="Key"/> has no name for
    /// (the keypad, F13 and up, media and international keys) and for anything that must tell
    /// the key to another system: SDL's scancode, which is the USB HID usage on the keyboard
    /// page. 0 when the platform gave none (headless input, a synthesized key).
    /// </summary>
    public ushort ScanCode { get; init; }

    /// <summary>
    /// The character the key types on the user's layout, as a code point, with Shift, CapsLock
    /// and AltGr applied and Ctrl and Alt ignored: <c>'z'</c> for the key <see cref="Key"/> calls
    /// <see cref="KeyCode.Y"/> on a German keyboard, <c>'Z'</c> with Shift, <c>'&lt;'</c> for
    /// Shift and the comma key on a US one. This is a web page's <c>key</c> where
    /// <see cref="Key"/> is its <c>code</c>: match a shortcut such as Ctrl+Z against this (a
    /// letter in either case), not against the position. Letter keys on a non-Latin layout
    /// (Russian, Greek) give their Latin letter, and the French number row its digits, as SDL
    /// names them, so shortcuts work there too. 0 for a key that types no character (arrows,
    /// Enter, the keypad) and when the platform did not say.
    /// </summary>
    public int LayoutKey { get; init; }

    /// <summary>
    /// The modifier keys held, left and right apart, and AltGr; <see cref="KeyModifiers"/> for
    /// a key the platform did not say which of.
    /// </summary>
    public ModifierKeys ModifierKeys { get; init; }

    /// <summary>
    /// The text this keystroke typed, which arrives next as a <see cref="TextInputEvent"/> (it is
    /// still sent; this is a look ahead, so a key and its character can be handled as one). Empty
    /// when the key types nothing: a Ctrl or Alt chord, an arrow, a dead key. Null when the
    /// platform cannot tell, or the text comes some other way, as through an input method:
    /// then a <see cref="TextInputEvent"/> may or may not follow.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>The digit key pressed, 0 to 9: the number row's, or the keypad's while NumLock is
    /// on. Null for any other key.</summary>
    public int? Digit => Key switch
    {
        >= KeyCode.Digit0 and <= KeyCode.Digit9 => Key - KeyCode.Digit0,
        >= KeyCode.Numpad0 and <= KeyCode.Numpad9 when (Modifiers & KeyModifiers.NumLock) != 0 => Key - KeyCode.Numpad0,
        _ => null,
    };

    /// <summary>The Latin letter this key names in a Ctrl chord, lower case: the layout's
    /// (<see cref="LayoutKey"/>) when the platform said, else the one at the key's position on a
    /// US keyboard. '\0' for any other key. What the built-in fields match Ctrl+Z, Ctrl+A and
    /// the clipboard chords against, so they follow the letters printed on the keys.</summary>
    internal char ShortcutLetter => LayoutKey switch
    {
        >= 'a' and <= 'z' => (char)LayoutKey,
        >= 'A' and <= 'Z' => (char)(LayoutKey + ('a' - 'A')),
        0 when Key is >= KeyCode.A and <= KeyCode.Z => (char)('a' + (Key - KeyCode.A)),
        _ => '\0',
    };

    /// <summary>The same key as a new event, nothing handled or stopped: what an element that
    /// takes keys for itself offers its ancestors first (a terminal, a web view).</summary>
    internal KeyDownEvent CopyUnhandled() => new()
    {
        Key = Key,
        Modifiers = Modifiers,
        IsRepeat = IsRepeat,
        ScanCode = ScanCode,
        LayoutKey = LayoutKey,
        ModifierKeys = ModifierKeys,
        Text = Text,
    };
}

public sealed class KeyUpEvent : ElementEvent
{
    public KeyCode Key { get; init; }
    public KeyModifiers Modifiers { get; init; }

    /// <summary>The physical key; see <see cref="KeyDownEvent.ScanCode"/>.</summary>
    public ushort ScanCode { get; init; }
}

public sealed class TextInputEvent : ElementEvent
{
    public string Text { get; init; } = "";
}

/// <summary>
/// Fired while the OS IME is composing a preedit string (CJK input, dead keys, etc.).
/// <see cref="Text"/> is the current preedit; <see cref="SegmentStart"/>/<see cref="SegmentLength"/>
/// mark the range the IME is actively editing. An empty <see cref="Text"/> means the
/// composition was cancelled or is about to commit.
/// </summary>
public sealed class TextEditingEvent : ElementEvent
{
    public string Text { get; init; } = "";
    public int SegmentStart { get; init; }
    public int SegmentLength { get; init; }
}

/// <summary>An element gained keyboard focus. Delivered to that element only.</summary>
public sealed class FocusEvent : ElementEvent
{
    /// <summary>How focus arrived. An element that hosts its own tab order — a web page —
    /// starts at its first stop for <see cref="FocusReason.Next"/> and its last for
    /// <see cref="FocusReason.Previous"/>.</summary>
    public FocusReason Reason { get; init; }
}

/// <summary>Why an element gained focus.</summary>
public enum FocusReason : byte
{
    /// <summary>Code moved focus (<c>Element.Focus()</c>), or no reason was recorded.</summary>
    Programmatic,
    /// <summary>A pointer press on the element or inside it.</summary>
    Pointer,
    /// <summary>Tab: focus moved forward through the tab order.</summary>
    Next,
    /// <summary>Shift+Tab: focus moved backward through the tab order.</summary>
    Previous,
}
public sealed class BlurEvent : ElementEvent;
