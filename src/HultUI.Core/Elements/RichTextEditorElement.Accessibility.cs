using System.Text;
using HultUI.Core.Accessibility;
using HultUI.Core.Primitives;
using HultUI.Core.RichText;

namespace HultUI.Core.Elements;

public partial class RichTextEditorElement
{
    // ── For a screen reader (docs/plans/16-rich-text-editor.md, stage 5) ──
    //
    // The document as text a reader moves through by character, word, line and paragraph: UIA's
    // text pattern, AT-SPI's Text. A block is a paragraph and its drawn lines are the lines; a
    // picture reads as its words; a table's cells are paragraphs in reading order.
    //
    // Made when something asks, not when the tree is built. The tree is built on every change a
    // reader could see, and the text of a 5,000-block document is 749,017 characters (1.5 MB) with
    // tables of its lines and paragraphs (~480 KB) on top: most of a keystroke's cost with a reader
    // listening, and all of it wasted for one that never reads (a recording bridge, a UI Automation
    // client that only follows the focus). So the tree takes a version: the document, the selection
    // and the layout as they were, and the caret's and selection's offsets, which is what the differ
    // compares (the document by identity; the text only when two documents are different instances
    // of the same length, as a formatting change makes). The text is made from a version when
    // something asks: on the UI thread from the layout, with its drawn lines and where each
    // character is; on a bridge's thread from the document alone, its paragraphs as its lines and
    // no boxes. A bridge's thread asking says a reader is reading: the UI thread then makes that
    // version's text from the layout, and the next version's as it takes it, so the reader's next
    // questions find the lines made — until a version goes unread. The geometry reads where the
    // editor is now from a record the render replaces, so scrolling needs no new text.

    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    // Where a bridge's thread asks the UI thread to make a version's text from the layout: the UI
    // thread's context when it last took a version (an editor can be made before the loop runs).
    private volatile SynchronizationContext? _uiContext;
    private EditorTextSource? _textSource;

    /// <summary>The version the last tree was built with: written on the UI thread, read by the
    /// bridges' threads.</summary>
    private volatile EditorTextVersion? _textVersion;

    /// <summary>A bridge's thread read the text since a version was last taken: a reader is
    /// reading, so the next version's text is made from the layout as it is taken.</summary>
    private volatile bool _readerAsked;

    /// <summary>Where the document's top-left is in the window, scroll included, as last drawn.
    /// Replaced, never changed, so a bridge thread reads it whole.</summary>
    private volatile ViewFrame? _viewFrame;

    private sealed record ViewFrame(float X, float Y);

    internal override IAccessibleTextSource? AccessibleTextSource => _textSource ??= new EditorTextSource(this);

    private sealed class EditorTextSource(RichTextEditorElement owner) : IVersionedAccessibleTextSource
    {
        // The tree's builder, on the UI thread.
        public AccessibleTextVersion CurrentVersion => owner.TakeTextVersion();

        public AccessibleText? AccessibleText
        {
            get
            {
                // On the UI thread: the text as it is now, from the layout.
                if (Environment.CurrentManagedThreadId == owner._uiThread) return owner.TakeTextVersion().Text;
                // A bridge's thread: the version the last tree was built with.
                if (owner._textVersion is not { } version) return null;
                owner._readerAsked = true;
                if (!version.IsMadeFromLayout) owner.MakeFromLayoutSoon(version);
                return version.Text;
            }
        }
    }

    // Called from Render: where the document is drawn now. A version taken before there was a
    // layout to read, or at another width or theme, has lines and boxes that are not these: the
    // tree is built again, which takes a new one. The tree is built before the frame is drawn,
    // so the first version an editor gives is always taken without its layout.
    private void NoteViewFrame()
    {
        var box = WindowContentBox;
        var frame = new ViewFrame(box.X, box.Y - _scrollY);
        if (frame != _viewFrame) _viewFrame = frame;
        if (_textVersion is { } version && AccessibilityClients.Active
            && (version.Key.Width != _laidOutWidth || version.Key.Stamp != _laidOutStamp || !version.LaidOut))
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.Text);
    }

    /// <summary>The version of the text as it is now: the last one when nothing it holds has
    /// changed, else a new one. On the UI thread.</summary>
    private EditorTextVersion TakeTextVersion()
    {
        bool laidOut = EnsureFreshLayout();
        _uiContext = SynchronizationContext.Current;
        var key = new TextKey(_state.Document, _state.Selection, _laidOutWidth, _laidOutStamp, _scrollY, _readOnly);
        var version = _textVersion;
        if (version is not null && version.Key == key) return version;

        int first = -1, last = -1;
        if (laidOut && _blocks.Length == key.Document.BlockCount)
        {
            first = BlockAt(_scrollY);
            last = BlockAt(_scrollY + Layout.AbsoluteContentBox.Height);
            if (_blocks[first].Table is { } t) first = t.Range.Start;
            if (_blocks[last].Table is { } u) last = u.Range.End - 1;
        }
        _textVersion = version = new EditorTextVersion(this, key, first, last);
        // A reader read the last one: make this one's now, while the layout is this one's.
        if (_readerAsked)
        {
            _readerAsked = false;
            version.MakeFromLayout();
        }
        return version;
    }

    /// <summary>Has the UI thread make <paramref name="version"/>'s text from the layout, if it is
    /// still the latest when it gets there; if the editor has changed since it was taken (it was
    /// taken before the first layout, say), the tree is built again, which takes a new version and
    /// makes it, since a reader asked. From a bridge's thread.</summary>
    private void MakeFromLayoutSoon(EditorTextVersion version) =>
        _uiContext?.Post(static state =>
        {
            var v = (EditorTextVersion)state!;
            var owner = v.Owner;
            if (!ReferenceEquals(owner._textVersion, v) || v.MakeFromLayout() is not null) return;
            if (owner.IsHosted) owner.NotifyAccessibilityPropertyChanged(AccessibilityProperties.Text);
        }, version);

    /// <summary>Whether the editor is still as <paramref name="key"/> has it; on the UI thread.</summary>
    private bool IsAsTaken(TextKey key)
    {
        EnsureFreshLayout();
        return key == new TextKey(_state.Document, _state.Selection, _laidOutWidth, _laidOutStamp, _scrollY, _readOnly);
    }

    private readonly record struct TextKey(RichDocument Document, RichSelection Selection, float Width, int Stamp,
        float Scroll, bool ReadOnly);

    /// <summary>
    /// The text as one tree was built with it: cheap to take on every rebuild, compared by the
    /// differ, made into an <see cref="AccessibleText"/> only when something asks.
    /// </summary>
    private sealed class EditorTextVersion : AccessibleTextVersion
    {
        public readonly RichTextEditorElement Owner;
        public readonly TextKey Key;

        // The text's length, and the caret, the anchor and the part on screen as offsets in it,
        // as the text has them, so the differ compares what it compared before.
        private readonly int _length, _focus, _anchor, _visibleStart, _visibleEnd;

        private AccessibleText? _text;
        private volatile bool _fromLayout;
        private string? _plain;

        public EditorTextVersion(RichTextEditorElement owner, TextKey key, int first, int last)
        {
            Owner = owner;
            Key = key;
            LaidOut = first >= 0;
            // By adding the blocks' lengths up, with nothing allocated: a document makes a table of
            // its block starts for the first offset asked of it, and every edit makes a new one.
            var doc = key.Document;
            var (focus, anchor) = (key.Selection.Focus, key.Selection.Anchor);
            int at = 0;
            _focus = _anchor = -1;
            _visibleStart = 0;
            _visibleEnd = -1;
            for (int i = 0; i < doc.BlockCount; i++)
            {
                if (i > 0) at++;
                var block = doc[i];
                bool picture = block.Kind == BlockKind.Image;
                if (i == focus.Block) _focus = at + (picture ? 0 : focus.Offset);
                if (i == anchor.Block) _anchor = at + (picture ? 0 : anchor.Offset);
                if (i == first) _visibleStart = at;
                at += picture ? PictureWords(block.Image).Length : block.Text.Length;
                if (i == last) _visibleEnd = at;
            }
            _length = at;
        }

        /// <summary>Whether it was taken with a layout to read: lines as drawn, boxes.</summary>
        public bool LaidOut { get; }

        /// <summary>Whether its text was made from the layout, with its drawn lines and boxes.</summary>
        public bool IsMadeFromLayout => _fromLayout;

        // As AccessibleText keeps them: the caret clamped into the text, a selection ordered and
        // clamped, none where the anchor is the caret.
        public override int Caret => _focus < 0 ? -1 : Math.Min(_focus, _length);

        public override int SelectionStart =>
            _focus >= 0 && _anchor >= 0 && _focus != _anchor ? Math.Clamp(Math.Min(_focus, _anchor), 0, _length) : 0;

        public override int SelectionEnd =>
            _focus >= 0 && _anchor >= 0 && _focus != _anchor ? Math.Clamp(Math.Max(_focus, _anchor), 0, _length) : 0;

        public override bool SameText(AccessibleTextVersion other) =>
            other is EditorTextVersion o
            && (ReferenceEquals(Key.Document, o.Key.Document)
                // Another document with the same text, as a formatting change makes: only then is
                // the text made and compared, as every text was before.
                || (_length == o._length && string.Equals(PlainText, o.PlainText, StringComparison.Ordinal)));

        /// <summary>The document's plain text, the editor's value; any thread.</summary>
        public override string? Value => Key.Document.ToPlainText();

        /// <summary>The text, a picture's words where it has none of its own; any thread.</summary>
        private string PlainText => _plain ??= TextOf(Key.Document);

        public override AccessibleText Text
        {
            get
            {
                if (_fromLayout) return Volatile.Read(ref _text)!;
                if (Environment.CurrentManagedThreadId == Owner._uiThread && MakeFromLayout() is { } made) return made;
                return Volatile.Read(ref _text) ?? MakeFromDocument();
            }
        }

        /// <summary>On the UI thread: the text from the layout, while the editor is as this version
        /// has it; null once it is not.</summary>
        public AccessibleText? MakeFromLayout()
        {
            if (_fromLayout) return _text;
            if (!Owner.IsAsTaken(Key)) return null;
            var text = Owner.BuildAccessibleText(Key.Document, LaidOut);
            Volatile.Write(ref _text, text);
            _fromLayout = true;
            return text;
        }

        // Any thread: from the document alone. Its paragraphs are its lines, and nothing says where
        // a character is.
        private AccessibleText MakeFromDocument()
        {
            var text = new AccessibleText(PlainText, _focus, _anchor, _focus, geometry: null,
                _visibleStart, _visibleEnd, isReadOnly: Key.ReadOnly);
            return Interlocked.CompareExchange(ref _text, text, null) ?? text;
        }
    }

    /// <summary>The text of <paramref name="doc"/>: its plain text, a picture's words where it has
    /// none of its own.</summary>
    private static string TextOf(RichDocument doc)
    {
        bool pictures = false;
        foreach (var b in doc.Blocks) pictures |= b.Kind == BlockKind.Image;
        if (!pictures) return doc.ToPlainText();
        var sb = new StringBuilder(doc.Length + 64);
        for (int i = 0; i < doc.BlockCount; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(doc[i].Kind == BlockKind.Image ? PictureWords(doc[i].Image) : doc[i].Text);
        }
        return sb.ToString();
    }

    /// <summary>The words a picture reads as: its own, or what it is.</summary>
    private static string PictureWords(RichImage? image) =>
        image is { Alt.Length: > 0 } ? image.Alt.ReplaceLineEndings(" ") : "Picture";

    private AccessibleText BuildAccessibleText(RichDocument doc, bool laidOut)
    {
        // The text: the plain text, and a picture's words where it has none of its own.
        int count = doc.BlockCount;
        var starts = new int[count];
        string text;
        bool pictures = false;
        foreach (var b in doc.Blocks) pictures |= b.Kind == BlockKind.Image;
        if (!pictures)
        {
            text = doc.ToPlainText();
            for (int i = 0; i < count; i++) starts[i] = doc.ToOffset(new RichPosition(i, 0));
        }
        else
        {
            var sb = new StringBuilder(doc.Length + 64);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append('\n');
                starts[i] = sb.Length;
                sb.Append(doc[i].Kind == BlockKind.Image ? PictureWords(doc[i].Image) : doc[i].Text);
            }
            text = sb.ToString();
        }

        int Offset(RichPosition p) => starts[p.Block] + (doc[p.Block].Kind == BlockKind.Image ? 0 : p.Offset);
        var s = _state.Selection;
        int visibleStart = 0, visibleEnd = -1;
        List<int>? lineStarts = null;
        EditorGeometry? geometry = null;

        if (laidOut && _blocks.Length == count)
        {
            // The lines as drawn, and where each is: from the layout, made now, read later.
            var lines = new LineGeometry(count * 2);
            int first = BlockAt(_scrollY), last = BlockAt(_scrollY + Layout.AbsoluteContentBox.Height);
            if (_blocks[first].Table is { } t) first = t.Range.Start;
            if (_blocks[last].Table is { } u) last = u.Range.End - 1;
            visibleStart = starts[first];
            visibleEnd = starts[last] + (doc[last].Kind == BlockKind.Image ? PictureWords(doc[last].Image).Length : doc[last].Text.Length);

            for (int i = 0; i < count; i++)
            {
                var bl = _blocks[i];
                bool visible = i >= first - 1 && i <= last + 1;
                if (bl.Paragraph is { } para)
                {
                    for (int l = 0; l < para.Lines.Count; l++)
                    {
                        var line = para.Lines[l];
                        float left = bl.TextX + para.LineLeft(l);
                        float[]? xs = null;
                        if (visible)
                        {
                            // Where each character boundary of the line is drawn, from its left.
                            xs = new float[line.Length + 1];
                            for (int k = 0; k <= line.Length; k++)
                                xs[k] = para.CaretX(l, line.Start + k) - para.LineLeft(l);
                        }
                        lines.Add(starts[i] + line.Start, bl.Top + bl.TextY + line.OffsetY, line.Height, left, line.Width, line.Length, xs);
                    }
                }
                else if (bl.Block.Kind == BlockKind.Image)
                {
                    var r = bl.PictureRect;
                    lines.Add(starts[i], bl.Top, bl.Height, r.X, r.Width, PictureWords(bl.Block.Image).Length, null);
                }
                else
                    lines.Add(starts[i], bl.Top, bl.Height, bl.TextX, 0, 0, null);
            }
            lineStarts = lines.Starts;
            geometry = new EditorGeometry(this, lines);
        }

        return new AccessibleText(text, Offset(s.Focus), Offset(s.Anchor), Offset(s.Focus), geometry,
            visibleStart, visibleEnd, isReadOnly: _readOnly, lineStarts: lineStarts);
    }

    /// <summary>Each drawn line of a snapshot: where it starts in the text, its box in document
    /// coordinates, and for the lines on screen when it was made, where each character boundary
    /// is. A line off screen then puts its characters evenly across its width.</summary>
    private sealed class LineGeometry(int capacity)
    {
        public readonly List<int> Starts = new(capacity);
        public readonly List<(float Top, float Height, float Left, float Width, int Length, float[]? Xs)> Boxes = new(capacity);

        public void Add(int start, float top, float height, float left, float width, int length, float[]? xs)
        {
            // A line that starts where the one before did (an empty block's) is that line.
            if (Starts.Count > 0 && Starts[^1] >= start) return;
            Starts.Add(start);
            Boxes.Add((top, height, left, width, length, xs));
        }

        public int LineOf(int offset)
        {
            int i = Starts.BinarySearch(offset);
            return Math.Max(0, i >= 0 ? i : ~i - 1);
        }

        public float X(int line, int k)
        {
            var b = Boxes[line];
            k = Math.Clamp(k, 0, b.Length);
            return b.Xs is { } xs ? xs[Math.Min(k, xs.Length - 1)] : b.Length == 0 ? 0 : b.Width * k / b.Length;
        }
    }

    /// <summary>Character boxes from a snapshot's lines, placed where the editor is drawn now.</summary>
    private sealed class EditorGeometry(RichTextEditorElement owner, LineGeometry lines) : AccessibleTextGeometry
    {
        public override Rect CharacterBounds(AccessibleText text, int offset)
        {
            if (owner._viewFrame is not { } frame || lines.Starts.Count == 0) return default;
            offset = Math.Clamp(offset, 0, text.Length);
            int line = lines.LineOf(offset);
            var box = lines.Boxes[line];
            int k = offset - lines.Starts[line];
            // At the line's end — its '\n', the text's end — a box of no width where a caret stands.
            int next = k >= box.Length ? k : text.NextCharacter(offset) - lines.Starts[line];
            float x0 = lines.X(line, k), x1 = lines.X(line, next);
            return new Rect(frame.X + box.Left + Math.Min(x0, x1), frame.Y + box.Top, Math.Abs(x1 - x0), box.Height);
        }

        public override int OffsetAt(AccessibleText text, Point point)
        {
            if (owner._viewFrame is not { } frame) return -1;
            float x = point.X - frame.X, y = point.Y - frame.Y;
            // The line under the point; in a table's row, the one whose cell holds it.
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < lines.Boxes.Count; i++)
            {
                var b = lines.Boxes[i];
                if (y < b.Top || y >= b.Top + b.Height) continue;
                float distance = x < b.Left ? b.Left - x : x > b.Left + b.Width ? x - b.Left - b.Width : 0;
                if (distance < bestDistance) { best = i; bestDistance = distance; }
            }
            if (best < 0) return -1;
            var line = lines.Boxes[best];
            int nearest = 0;
            float nearestDistance = float.MaxValue;
            for (int k = 0; k <= line.Length; k++)
            {
                float d = Math.Abs(line.Left + lines.X(best, k) - x);
                if (d < nearestDistance) { nearest = k; nearestDistance = d; }
            }
            return Math.Min(text.Length, lines.Starts[best] + nearest);
        }
    }

    // ── What the keyboard did, said ──

    // A shortcut that changes formatting changes nothing a reader hears: it says what it did.
    private void AnnounceStyle(InlineStyle style, string name)
    {
        if (!AccessibilityClients.Active) return;
        Announce(_state.SelectionMarks.Has(style) ? name + " on" : name + " off");
    }

    private void AnnounceBlock()
    {
        if (!AccessibilityClients.Active) return;
        var b = _state.CurrentBlock;
        Announce(b.Kind switch
        {
            BlockKind.Heading => $"Heading {b.HeadingLevel}",
            BlockKind.BulletItem => "Bulleted list",
            BlockKind.NumberedItem => "Numbered list",
            BlockKind.TaskItem => "Task list",
            BlockKind.Quote => "Quote",
            BlockKind.CodeBlock => "Code block",
            _ => "Paragraph",
        });
    }

    /// <summary>
    /// What an assistive tool's set-value does (UIA's value pattern): the document becomes
    /// <paramref name="value"/> as plain paragraphs, one edit that can be undone. Refused in a
    /// read-only editor.
    /// </summary>
    internal void SetValueFromAssistiveTool(string value)
    {
        if (_readOnly) return;
        _state.ReplaceDocument(RichDocument.FromPlainText(value));
    }
}
