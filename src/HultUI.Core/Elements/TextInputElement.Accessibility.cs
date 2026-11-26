using HultUI.Core.Accessibility;

namespace HultUI.Core.Elements;

public partial class TextInputElement
{
    // ── For a screen reader: the value as text, with its caret (docs/design/TALKBACK.md §10) ──
    //
    // The field offers its value as text a reader moves through by character and word (UIA's text
    // pattern, AT-SPI's Text, TalkBack's granularities), with the caret and the selection in it, as a
    // native edit control and a browser's <input> do. Before, its node had a value only, and no reader
    // knew where its caret was: TalkBack could not read it by character.
    //
    // As the text area's (TextAreaElement.Accessibility.cs), a tree takes a version — the string, the
    // caret and the anchor — which the differ compares, and the text is made from it when a reader
    // asks. Unlike the text area's, an edit is told as a value change as well as a text change: a
    // one-line value is short, and a browser tells an <input>'s value (AccessibleTextVersion.TellsValue).
    // A masked field offers no text: its value is bullets, and its caret's place says nothing.
    //
    // A move of the caret or the selection is told where it is made (CaretMoved), as the text area's
    // is: the keys, the pointer and its drag, the touch handles, a word selected by touch, a reader's
    // selection, CursorPosition and SelectAll. An edit tells its value, which takes the caret with it.
    // (Telling it from Render came a frame late, after the frame's accessibility pass, and not at all
    // for CursorPosition, which paints nothing.)

    private InputTextSource? _textSource;

    /// <summary>The version the tree took last: written on the UI thread, read by the bridges'.</summary>
    private volatile InputTextVersion? _textVersion;

    internal override IAccessibleTextSource? AccessibleTextSource => Masked ? null : _textSource ??= new InputTextSource(this);

    private sealed class InputTextSource(TextInputElement owner) : IVersionedAccessibleTextSource
    {
        public AccessibleTextVersion CurrentVersion => owner.TakeTextVersion();

        /// <summary>The text as the tree took it last, on any thread.</summary>
        public AccessibleText? AccessibleText => owner._textVersion?.Text;
    }

    /// <summary>The version of the text as it is now: the last one when nothing in it changed. On
    /// the UI thread, as a tree is built.</summary>
    private InputTextVersion TakeTextVersion()
    {
        var version = _textVersion;
        if (version is not null && ReferenceEquals(version.String, _text) && version.Cursor == _cursorPos
            && version.Anchor == _selectionStart)
            return version;
        _textVersion = version = new InputTextVersion(_text, _cursorPos, _selectionStart);
        return version;
    }

    /// <summary>Tells the window the caret or the selection moved, while a reader listens: the
    /// applied tree takes the new version of the text and the bridge tells the move. A move there and
    /// back in one frame tells nothing: the version is the same.</summary>
    private void CaretMoved()
    {
        if (AccessibilityClients.Active && !Masked && _textVersion is { } version
            && (version.Cursor != _cursorPos || version.Anchor != _selectionStart))
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.TextSelection);
    }

    /// <summary>The field's value as one tree was built with it: the string, the caret, the anchor.</summary>
    private sealed class InputTextVersion(string text, int cursor, int anchor) : AccessibleTextVersion
    {
        public readonly string String = text;
        public readonly int Cursor = cursor;
        public readonly int Anchor = anchor;
        private AccessibleText? _text;

        public override int Caret => Math.Clamp(Cursor, 0, String.Length);

        public override int SelectionStart => Cursor != Anchor ? Math.Clamp(Math.Min(Cursor, Anchor), 0, String.Length) : 0;

        public override int SelectionEnd => Cursor != Anchor ? Math.Clamp(Math.Max(Cursor, Anchor), 0, String.Length) : 0;

        // Every edit makes a new string; the same string is the same text.
        public override bool SameText(AccessibleTextVersion other) =>
            other is InputTextVersion o && (ReferenceEquals(String, o.String) || string.Equals(String, o.String, StringComparison.Ordinal));

        public override string? Value => String;

        public override string Characters => String;

        /// <summary>A one-line value, told as its value too (UIA's ValueValue), as a browser tells an
        /// &lt;input&gt;'s.</summary>
        public override bool TellsValue => true;

        // Made once, on whichever thread asks first; both make the same.
        public override AccessibleText Text =>
            Volatile.Read(ref _text) ?? Interlocked.CompareExchange(ref _text, Make(), null) ?? _text!;

        // Editable: a reader may set the value and move the caret.
        private AccessibleText Make() => new(String, Cursor, Anchor, Cursor, isReadOnly: false);
    }
}
