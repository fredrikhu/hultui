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
    // picture reads as its words; a table's cells are paragraphs in reading order. The snapshot
    // is made on the UI thread, from the layout, when the accessibility tree is built, and the
    // bridges read it on their own threads. Its geometry reads where the editor is now from a
    // record the render replaces, so scrolling needs no new snapshot.

    private readonly int _uiThread = Environment.CurrentManagedThreadId;
    private EditorTextSource? _textSource;
    private volatile AccessibleText? _accessibleText;
    private (RichDocument? Document, RichSelection Selection, float Width, int Stamp, float Scroll, bool ReadOnly) _accessibleKey;

    /// <summary>Where the document's top-left is in the window, scroll included, as last drawn.
    /// Replaced, never changed, so a bridge thread reads it whole.</summary>
    private volatile ViewFrame? _viewFrame;

    private sealed record ViewFrame(float X, float Y);

    internal override IAccessibleTextSource? AccessibleTextSource => _textSource ??= new EditorTextSource(this);

    private sealed class EditorTextSource(RichTextEditorElement owner) : IAccessibleTextSource
    {
        public AccessibleText? AccessibleText
        {
            get
            {
                // Made where the layout is: on the UI thread, when the tree is built. A bridge's
                // thread gets the one made last.
                if (Environment.CurrentManagedThreadId == owner._uiThread) owner.RefreshAccessibleText();
                return owner._accessibleText;
            }
        }
    }

    // Called from Render: where the document is drawn now. A snapshot made before there was a
    // layout to read, or at another width or theme, has lines and boxes that are not these: the
    // tree is built again, which makes a new one. The tree is built before the frame is drawn,
    // so the first one an editor gets is always made without its layout.
    private void NoteViewFrame()
    {
        var box = WindowContentBox;
        var frame = new ViewFrame(box.X, box.Y - _scrollY);
        if (frame != _viewFrame) _viewFrame = frame;
        if (_accessibleText is not null && AccessibilityClients.Active
            && (_accessibleKey.Width != _laidOutWidth || _accessibleKey.Stamp != _laidOutStamp || _accessibleText.Geometry is null))
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.Text);
    }

    private void RefreshAccessibleText()
    {
        bool laidOut = EnsureFreshLayout();
        var key = (_state.Document, _state.Selection, _laidOutWidth, _laidOutStamp, _scrollY, _readOnly);
        if (_accessibleText is not null && key == _accessibleKey) return;
        _accessibleKey = key;
        _accessibleText = BuildAccessibleText(_state.Document, laidOut);
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
