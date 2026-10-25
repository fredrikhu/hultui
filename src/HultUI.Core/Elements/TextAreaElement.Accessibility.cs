using HultUI.Core.Accessibility;
using HultUI.Core.Primitives;

namespace HultUI.Core.Elements;

public partial class TextAreaElement
{
    // ── For a screen reader: the text pattern (plan 17, round 4) ──
    //
    // The text as a reader moves through it by character, word, line and paragraph: UIA's text
    // pattern, AT-SPI's Text. Its lines are the rows the area draws, its paragraphs the
    // '\n'-separated runs, its caret and selection the area's. As the rich text editor's
    // (RichTextEditorElement.Accessibility.cs), made when something asks, not with every tree: the
    // tree takes a version — the string, the caret, the anchor and the layout as they were — which
    // the differ compares (the string by identity: every edit makes a new one), and a keystroke is
    // told as a text change only, never as the whole text in a value change (UIA ValueValue, AT-SPI
    // accessible-value: 121,744 characters a keystroke at 2,000 lines), as 72a4f96's rule has it
    // for every node that offers text. A caret move is told with no tree built (TryPatchA11y). The
    // value is still read on request.
    //
    // The text is made from a version on the UI thread from the layout, with the drawn rows and
    // where each character is; on a bridge's thread from the string alone, its paragraphs as its
    // lines and no boxes, after which the UI thread makes it from the layout, and each next
    // version's as it is taken while a reader keeps reading. When only the caret or the selection
    // moved, the new text is the last one's with the new caret and selection, its tables shared.

    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private volatile SynchronizationContext? _uiContext;
    private AreaTextSource? _textSource;

    /// <summary>The version the applied tree has: written on the UI thread, read by the bridges'.</summary>
    private volatile AreaTextVersion? _textVersion;

    /// <summary>A bridge's thread read the text since a version was last taken.</summary>
    private volatile bool _readerAsked;

    /// <summary>Where the text's top-left is in the window, scroll included, as last drawn.
    /// Replaced, never changed, so a bridge's thread reads it whole.</summary>
    private volatile AreaFrame? _areaFrame;

    private sealed record AreaFrame(float X, float Y);

    internal override IAccessibleTextSource? AccessibleTextSource => _textSource ??= new AreaTextSource(this);

    private sealed class AreaTextSource(TextAreaElement owner) : IVersionedAccessibleTextSource
    {
        public AccessibleTextVersion CurrentVersion => owner.TakeTextVersion();

        public AccessibleText? AccessibleText
        {
            get
            {
                if (Environment.CurrentManagedThreadId == owner._uiThread) return owner.TakeTextVersion().Text;
                if (owner._textVersion is not { } version) return null;
                owner._readerAsked = true;
                if (!version.IsMadeFromLayout) owner.MakeFromLayoutSoon(version);
                return version.Text;
            }
        }
    }

    /// <summary>Tells the window the caret or the selection moved, while a reader listens: the
    /// applied tree takes the new version of the text and the bridge tells the move.</summary>
    private void CaretMoved()
    {
        if (AccessibilityClients.Active) NotifyAccessibilityPropertyChanged(AccessibilityProperties.TextSelection);
    }

    // Called from Render once the rows are laid out: where the text is drawn now. A version taken
    // before there were rows to read (before the first frame), or with rows since wrapped again (a
    // width, a font), is replaced by one with these rows, telling nothing.
    private void NoteAreaFrame(Rect contentBox)
    {
        // A new record only when it moved: a steady repaint allocates nothing (FrameAllocationTests).
        float x = contentBox.X, y = contentBox.Y - _scrollOffsetY;
        if (_areaFrame is not { } frame || frame.X != x || frame.Y != y) _areaFrame = new AreaFrame(x, y);
        if (_textVersion is { } version && AccessibilityClients.Active
            && (!version.Key.LaidOut || version.Key.Lines != _linesVersion))
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.TextSelection);
    }

    private AreaKey CurrentKey()
    {
        bool laidOut = _renderCtx is not null;
        float lineHeight = 0;
        if (laidOut)
        {
            // At the width Render wraps at, so taking a version never wraps the rows another way.
            var style = ResolvedInherited;
            GetLineStarts(Layout.AbsoluteContentBox.Width, style);
            lineHeight = GetLineHeight(style);
        }
        return new AreaKey(_text, _cursorPos, _selectionStart, laidOut, laidOut ? _linesVersion : -1, lineHeight,
            _scrollOffsetY, Layout.AbsoluteContentBox.Height, ReadOnly);
    }

    /// <summary>The version of the text as it is now: the last one when nothing it holds has
    /// changed, else a new one. On the UI thread.</summary>
    private AreaTextVersion TakeTextVersion()
    {
        _uiContext = SynchronizationContext.Current;
        var key = CurrentKey();
        var version = _textVersion;
        if (version is not null && version.Key == key) return version;
        _textVersion = version = new AreaTextVersion(this, key)
        {
            ShareFrom = version?.MadeOrSharing is { } made && made.Key.DiffersOnlyInSelection(key) ? made : null,
        };
        if (_readerAsked)
        {
            _readerAsked = false;
            version.MakeFromLayout();
        }
        return version;
    }

    private void MakeFromLayoutSoon(AreaTextVersion version) =>
        _uiContext?.Post(static state =>
        {
            var v = (AreaTextVersion)state!;
            var owner = v.Owner;
            if (!ReferenceEquals(owner._textVersion, v) || v.MakeFromLayout() is not null) return;
            if (owner.IsHosted) owner.NotifyAccessibilityPropertyChanged(AccessibilityProperties.TextSelection);
        }, version);

    /// <summary>The string, the caret, the anchor, and what the rows and the part on screen were
    /// laid out with.</summary>
    private readonly record struct AreaKey(string Text, int Cursor, int Anchor, bool LaidOut, int Lines,
        float LineHeight, float Scroll, float Height, bool ReadOnly)
    {
        public bool DiffersOnlyInSelection(AreaKey other) =>
            ReferenceEquals(Text, other.Text) && LaidOut == other.LaidOut && Lines == other.Lines
            && LineHeight == other.LineHeight && Scroll == other.Scroll && Height == other.Height
            && ReadOnly == other.ReadOnly;
    }

    /// <summary>The area's text as one tree was built with it: cheap to take, compared by the
    /// differ, made into an <see cref="AccessibleText"/> only when something asks.</summary>
    private sealed class AreaTextVersion(TextAreaElement owner, AreaKey key) : AccessibleTextVersion
    {
        public readonly TextAreaElement Owner = owner;
        public readonly AreaKey Key = key;

        private AccessibleText? _text;
        private volatile bool _fromLayout;

        public bool IsMadeFromLayout => _fromLayout;

        /// <summary>A version taken before this one, of the same string and rows, whose text was
        /// made from the layout: this one's is made from that one's. Dropped once this one is made.
        /// UI thread only.</summary>
        public AreaTextVersion? ShareFrom { get; set; }

        public AreaTextVersion? MadeOrSharing => _fromLayout ? this : ShareFrom;

        // As AccessibleText keeps them.
        public override int Caret => Math.Min(Math.Max(Key.Cursor, 0), Key.Text.Length);

        public override int SelectionStart => Key.Cursor != Key.Anchor
            ? Math.Clamp(Math.Min(Key.Cursor, Key.Anchor), 0, Key.Text.Length) : 0;

        public override int SelectionEnd => Key.Cursor != Key.Anchor
            ? Math.Clamp(Math.Max(Key.Cursor, Key.Anchor), 0, Key.Text.Length) : 0;

        // Every edit makes a new string; the same string is the same text. Two strings are compared
        // only when they are not the same instance (a text handed in again).
        public override bool SameText(AccessibleTextVersion other) =>
            other is AreaTextVersion o && (ReferenceEquals(Key.Text, o.Key.Text) || string.Equals(Key.Text, o.Key.Text, StringComparison.Ordinal));

        public override string? Value => Key.Text;

        public override string Characters => Key.Text;

        public override AccessibleText Text
        {
            get
            {
                if (_fromLayout) return Volatile.Read(ref _text)!;
                if (Environment.CurrentManagedThreadId == Owner._uiThread && MakeFromLayout() is { } made) return made;
                return Volatile.Read(ref _text) ?? MakeFromString();
            }
        }

        /// <summary>On the UI thread: the text from the layout, while the area is as this version
        /// has it; null once it is not.</summary>
        public AccessibleText? MakeFromLayout()
        {
            if (_fromLayout) return _text;
            if (Owner.CurrentKey() != Key) return null;
            var from = ShareFrom;
            ShareFrom = null;
            var text = from is { _fromLayout: true }
                ? new AccessibleText(from._text!, Key.Cursor, Key.Anchor, Key.Cursor)
                : Owner.BuildAccessibleText(Key);
            Volatile.Write(ref _text, text);
            _fromLayout = true;
            return text;
        }

        // Any thread: from the string alone, its paragraphs as its lines, no boxes.
        private AccessibleText MakeFromString()
        {
            var text = new AccessibleText(Key.Text, Key.Cursor, Key.Anchor, Key.Cursor, isReadOnly: Key.ReadOnly);
            return Interlocked.CompareExchange(ref _text, text, null) ?? text;
        }
    }

    /// <summary>The text from the layout: the rows as drawn, the part on screen, and where each
    /// character of the rows on screen (and one either side) is drawn. On the UI thread, with the
    /// area as <paramref name="key"/> has it.</summary>
    private AccessibleText BuildAccessibleText(AreaKey key)
    {
        if (!key.LaidOut || _renderCtx is null)
            return new AccessibleText(key.Text, key.Cursor, key.Anchor, key.Cursor, isReadOnly: key.ReadOnly);

        var style = ResolvedInherited;
        var starts = GetLineStarts(Layout.AbsoluteContentBox.Width, style);
        float lh = key.LineHeight;
        int rows = starts.Count;
        int first = Math.Clamp((int)(key.Scroll / lh), 0, rows - 1);
        int last = Math.Clamp((int)((key.Scroll + key.Height) / lh), first, rows - 1);
        int from = Math.Max(0, first - 1), to = Math.Min(rows - 1, last + 1);

        // Where each character boundary of the rows on screen is drawn, from the row's left; and
        // the average width of a character there, which places the others.
        var xs = new float[to - from + 1][];
        float width = 0;
        int chars = 0;
        for (int row = from; row <= to; row++)
        {
            var (lineStart, lineEnd) = VisualLineRange(starts, row);
            var x = xs[row - from] = new float[lineEnd - lineStart + 1];
            for (int k = 1; k < x.Length; k++)
                x[k] = CaretXOnLine(row, lineStart, lineEnd, lineStart + k, style);
            width += x[^1];
            chars += x.Length - 1;
        }
        float charWidth = chars > 0 ? width / chars : style.FontSize * 0.6f;

        var (visibleStart, _) = VisualLineRange(starts, first);
        var (_, visibleEnd) = VisualLineRange(starts, last);
        return new AccessibleText(key.Text, key.Cursor, key.Anchor, key.Cursor,
            new AreaGeometry(this, lh, from, xs, charWidth), visibleStart, visibleEnd, key.ReadOnly, starts);
    }

    /// <summary>Character boxes from a snapshot of the rows, placed where the area is drawn now.
    /// The text's lines are the area's rows, one row height each.</summary>
    private sealed class AreaGeometry(TextAreaElement owner, float lineHeight, int firstRow, float[][] xs, float charWidth)
        : AccessibleTextGeometry
    {
        // The row's text without its '\n': where a row's characters end.
        private static int RowLength(AccessibleText text, int row) => text.LineEnd(row) - text.LineStart(row);

        private float X(AccessibleText text, int row, int k)
        {
            k = Math.Clamp(k, 0, RowLength(text, row));
            int i = row - firstRow;
            return i >= 0 && i < xs.Length && k < xs[i].Length ? xs[i][k] : k * charWidth;
        }

        public override Rect CharacterBounds(AccessibleText text, int offset)
        {
            if (owner._areaFrame is not { } frame) return default;
            offset = Math.Clamp(offset, 0, text.Length);
            int row = text.LineAt(offset);
            int start = text.LineStart(row), length = RowLength(text, row);
            int k = offset - start;
            // At the row's end — its '\n', the text's end — a box of no width where a caret stands.
            int next = k >= length ? k : text.NextCharacter(offset) - start;
            float x0 = X(text, row, k), x1 = X(text, row, next);
            return new Rect(frame.X + Math.Min(x0, x1), frame.Y + row * lineHeight, Math.Abs(x1 - x0), lineHeight);
        }

        public override int OffsetAt(AccessibleText text, Point point)
        {
            if (owner._areaFrame is not { } frame || lineHeight <= 0) return -1;
            float y = point.Y - frame.Y, x = point.X - frame.X;
            if (y < 0) return -1;
            int row = (int)(y / lineHeight);
            if (row >= text.LineCount) return -1;
            int start = text.LineStart(row), length = RowLength(text, row);
            // The character whose box holds the point: in right-to-left text the boundaries run
            // the other way, and a surrogate pair is one box.
            for (int at = start; at < start + length; at = text.NextCharacter(at))
            {
                float a = X(text, row, at - start), b = X(text, row, text.NextCharacter(at) - start);
                if (x >= Math.Min(a, b) && x < Math.Max(a, b)) return at;
            }
            // Beside the row's text: the nearest boundary.
            int nearest = 0;
            float best = float.MaxValue;
            for (int k = 0; k <= length; k++)
            {
                float d = Math.Abs(X(text, row, k) - x);
                if (d < best) { best = d; nearest = k; }
            }
            return start + nearest;
        }
    }
}
