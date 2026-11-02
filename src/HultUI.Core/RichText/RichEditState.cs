using System.Collections.Immutable;
using System.Text;
using HultUI.Core.Elements;
using EditKind = HultUI.Core.RichText.RichUndoHistory.EditKind;

namespace HultUI.Core.RichText;

/// <summary>
/// Everything a rich text editor does that is not about pixels: the document, the selection,
/// the marks the next typed character takes, and every command — typing, Enter, Backspace,
/// paste, formatting, undo — as edits of the document (docs/plans/16-rich-text-editor.md).
/// </summary>
/// <remarks>
/// Kept apart from the element so the editing rules can be tested without a window, a layout or
/// a font. What needs layout — Up and Down, Home and End on a wrapped line, a click — is the
/// element's; it hands the result back here through <see cref="Select(RichSelection)"/>.
/// <para>Every edit goes through one primitive that replaces a run of blocks and records it, as
/// TextArea's <c>Replace</c> does for a string. Nothing may change <see cref="Document"/>
/// around it, or undo would be applied to blocks it does not describe.</para>
/// </remarks>
public sealed partial class RichEditState
{
    private readonly RichUndoHistory _history = new();
    private RichMarks? _pending;

    public RichEditState(RichDocument? document = null, RichTextFeatures features = RichTextFeatures.All)
    {
        Features = features;
        Document = RichTable.Normalize(document ?? RichDocument.Empty);
        Selection = RichSelection.Caret(Document.Start);
    }

    public RichDocument Document { get; private set; }
    public RichSelection Selection { get; private set; }

    /// <summary>What this editor lets a person make (<see cref="RichTextFeatures"/>).</summary>
    public RichTextFeatures Features { get; set; }

    /// <summary>The marks set with no selection to apply them to — Ctrl+B before typing — which
    /// the next characters typed take. Cleared when the caret moves.</summary>
    public RichMarks? PendingMarks => _pending;

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    /// <summary>After any edit, undo, redo or <see cref="Load"/>.</summary>
    public event Action? DocumentChanged;

    /// <summary>After the selection or the pending marks change, whether or not the document did.</summary>
    public event Action? SelectionChanged;

    // ── Document and selection ──

    /// <summary>Replaces the document from outside — an app loading a file, a rebuild handing in
    /// a new value. The history is forgotten: its steps describe the old blocks. A table that
    /// arrives ragged is made rectangular (<see cref="RichTable.Normalize(RichDocument)"/>): every
    /// table command counts on it.</summary>
    public void Load(RichDocument document)
    {
        if (ReferenceEquals(document, Document)) return;
        Document = RichTable.Normalize(document);
        _history.Clear();
        _pending = null;
        Selection = document.Clamp(Selection);
        DocumentChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    /// <summary>Replaces the whole document as an edit — one step, which Undo gives back — rather
    /// than as a new value (<see cref="Load"/>): what an assistive tool setting the text does.
    /// The caret goes to the end.</summary>
    public void ReplaceDocument(RichDocument document)
    {
        var blocks = RichTable.Normalize(document).Blocks;
        Commit(0, Document.BlockCount, blocks, RichSelection.Caret(new RichDocument(blocks).End), EditKind.Other);
    }

    /// <summary>Moves the selection. Ends the current undo run and drops pending marks when the
    /// caret actually moves.</summary>
    public void Select(RichSelection selection)
    {
        selection = Document.Clamp(selection);
        if (selection == Selection) return;
        Selection = selection;
        _history.Seal();
        _pending = null;
        SelectionChanged?.Invoke();
    }

    public void Select(RichPosition anchor, RichPosition focus) => Select(new RichSelection(anchor, focus));

    /// <summary>Moves the caret to <paramref name="to"/>, or the selection's focus when
    /// <paramref name="extend"/>.</summary>
    public void MoveTo(RichPosition to, bool extend) =>
        Select(extend ? Selection with { Focus = to } : RichSelection.Caret(to));

    public void SelectAll() => Select(Document.Start, Document.End);

    /// <summary>The selected part of the document; empty for a collapsed selection.</summary>
    public RichFragment SelectedFragment => Document.Slice(Selection.Start, Selection.End);

    public string SelectedText => Selection.IsCollapsed ? "" : SelectedFragment.ToPlainText();

    /// <summary>The block the caret is in.</summary>
    public RichBlock CurrentBlock => Document[Selection.Focus.Block];

    // ── Logical caret steps (the element adds the ones that need layout) ──

    /// <summary>One character — or with <paramref name="word"/>, one word — to the left; from a
    /// block's start, the end of the block before.</summary>
    public RichPosition StepLeft(RichPosition p, bool word = false)
    {
        p = Document.Clamp(p);
        if (p.Offset > 0)
        {
            var text = Document[p.Block].Text;
            return p with { Offset = word ? WordBoundary.WordLeft(text, p.Offset) : PreviousCharacter(text, p.Offset) };
        }
        return p.Block > 0 ? new RichPosition(p.Block - 1, Document[p.Block - 1].Text.Length) : p;
    }

    /// <summary>One character or word to the right; from a block's end, the start of the next.</summary>
    public RichPosition StepRight(RichPosition p, bool word = false)
    {
        p = Document.Clamp(p);
        var text = Document[p.Block].Text;
        if (p.Offset < text.Length)
            return p with { Offset = word ? WordBoundary.WordRight(text, p.Offset) : NextCharacter(text, p.Offset) };
        return p.Block < Document.BlockCount - 1 ? new RichPosition(p.Block + 1, 0) : p;
    }

    // Never between the halves of a surrogate pair.
    internal static int PreviousCharacter(string text, int pos) =>
        pos >= 2 && char.IsLowSurrogate(text[pos - 1]) && char.IsHighSurrogate(text[pos - 2]) ? pos - 2 : Math.Max(0, pos - 1);

    internal static int NextCharacter(string text, int pos) =>
        pos + 1 < text.Length && char.IsHighSurrogate(text[pos]) && char.IsLowSurrogate(text[pos + 1])
            ? pos + 2 : Math.Min(text.Length, pos + 1);

    // ── Typing ──

    /// <summary>
    /// The marks a character typed now would take: the pending marks if any, else those of the
    /// character before the caret — except that a link or inline code does not grow past its
    /// end, so typing after a link types plain text. Nothing in preformatted blocks.
    /// </summary>
    public RichMarks TypingMarks
    {
        get
        {
            if (_pending is { } pending) return pending;
            var at = Selection.Start;
            var block = Document[at.Block];
            if (block.IsPreformatted || block.IsVoid || block.Text.Length == 0) return RichMarks.None;

            var marks = at.Offset > 0 ? block.MarksAt(at.Offset - 1) : block.MarksAt(0);
            bool inside = at.Offset > 0 && at.Offset < block.Text.Length;
            var next = inside ? block.MarksAt(at.Offset) : RichMarks.None;
            if (marks.Link is not null && next.Link != marks.Link)
                marks = marks with { Link = null };
            if (marks.Has(InlineStyle.Code) && !next.Has(InlineStyle.Code))
                marks = marks.With(InlineStyle.Code, false);
            return Features.Restrict(marks);
        }
    }

    /// <summary>Types <paramref name="text"/> over the selection. A line break in it starts new
    /// paragraphs, as a paste of plain text would; in a code block or a table cell it stays a
    /// line break.</summary>
    public bool InsertText(string text)
    {
        if (text.Length == 0) return false;
        text = text.ReplaceLineEndings("\n");
        var marks = TypingMarks;

        if (text.Contains('\n') && !Document[Selection.Start.Block].IsPreformatted)
        {
            var lines = text.Split('\n').Select(line =>
                RichBlock.Paragraph(line, marks.IsNone ? [] : [new RichSpan(0, line.Length, marks)]));
            return InsertFragment(new RichFragment([.. lines]));
        }

        bool collapsed = Selection.IsCollapsed;
        var cut = CutSelection();
        var block = cut.Block;
        if (block.IsVoid)
        {
            // Typing on a rule or a picture types into a new paragraph after it.
            var para = RichBlock.Paragraph(text, marks.IsNone ? [] : [new RichSpan(0, text.Length, marks)]);
            Commit(cut, [block, para], 1, text.Length, EditKind.Other);
            return true;
        }

        var spans = block.IsPreformatted || marks.IsNone
            ? ImmutableArray<RichSpan>.Empty
            : [new RichSpan(0, text.Length, marks)];
        var edited = InsertInline(block, cut.Offset, text, spans);
        Commit(cut, [edited], 0, cut.Offset + text.Length, EditKind.Typing, text, keepPending: collapsed);

        if (Features.Allows(RichTextFeatures.MarkdownShortcuts) && text.Length == 1)
            ApplyTypingShortcut(text[0]);
        return true;
    }

    /// <summary>Inserts a piece of a document over the selection — what Paste does. One block
    /// joins the block it lands in; more carry their formats, the first joining the text before
    /// the caret and the last the text after it. A rule, a picture or a table stands apart. Into
    /// a table cell, everything becomes the cell's text, a block to a line.</summary>
    public bool InsertFragment(RichFragment fragment)
    {
        var restricted = fragment.Blocks.Select(b => Features.Restrict(b) with { }).ToList();
        if (restricted.Count == 0) return false;

        var cut = CutSelection();
        var block = cut.Block;
        int offset = cut.Offset;
        if (block.IsPreformatted)
        {
            string text = string.Join("\n", restricted.Where(b => !b.IsVoid).Select(b => b.Text));
            Commit(cut, [InsertInline(block, offset, text, [])], 0, offset + text.Length, EditKind.Other);
            return true;
        }
        if (block.IsTableCell)
        {
            var (text, spans) = Flatten(restricted);
            Commit(cut, [InsertInline(block, offset, text, spans)], 0, offset + text.Length, EditKind.Other);
            return true;
        }

        // A table among the blocks is made whole: a copy can start or end inside one.
        var blocks = RichTable.Normalize(restricted) ?? restricted;
        if (block.IsVoid)
        {
            Commit(cut, [block, .. blocks], blocks.Count, blocks[^1].Text.Length, EditKind.Other);
            return true;
        }
        if (restricted.Count == 1 && !restricted[0].IsVoid)
        {
            var b = restricted[0];
            Commit(cut, [InsertInline(block, offset, b.Text, b.IsPreformatted ? [] : b.Spans)], 0, offset + b.Text.Length,
                EditKind.Other);
            return true;
        }

        var head = block.Slice(0, offset);
        var tail = block.Slice(offset, block.Text.Length);
        var result = new List<RichBlock>();

        var first = blocks[0];
        if (StandsApart(first))
        {
            if (head.Text.Length > 0) result.Add(head);
            result.Add(first);
        }
        else
            result.Add(head.Text.Length == 0 ? first : Join(head, first));

        for (int i = 1; i < blocks.Count - 1; i++)
            result.Add(blocks[i]);

        int caretBlock, caretOffset;
        var last = blocks[^1];
        if (blocks.Count == 1 || StandsApart(last))
        {
            if (blocks.Count > 1) result.Add(last);
            result.Add(tail with { });
            (caretBlock, caretOffset) = (result.Count - 1, 0);
        }
        else
        {
            // An empty last block — a copy that ended at the start of a block — gives the text
            // after the caret nothing to take its format from, so it keeps its own.
            result.Add(last.Text.Length == 0 ? tail with { } : Join(last, tail));
            (caretBlock, caretOffset) = (result.Count - 1, last.Text.Length);
        }
        Commit(cut, result, caretBlock, caretOffset, EditKind.Other);
        return true;
    }

    // What no text joins: a rule, a picture, a table's cell.
    private static bool StandsApart(RichBlock b) => b.IsVoid || b.IsTableCell;

    // Blocks as one cell's text: a line each, pictures and rules left out.
    private static (string Text, ImmutableArray<RichSpan> Spans) Flatten(IReadOnlyList<RichBlock> blocks)
    {
        var sb = new StringBuilder();
        var spans = new List<RichSpan>();
        bool any = false;
        foreach (var b in blocks)
        {
            if (b.IsVoid) continue;
            if (any) sb.Append('\n');
            any = true;
            if (!b.IsPreformatted)
                foreach (var s in b.Spans)
                    spans.Add(s with { Start = s.Start + sb.Length });
            sb.Append(b.Text);
        }
        return (sb.ToString(), RichSpans.Normalize(spans, sb.Length));
    }

    // ── Deleting ──

    public bool DeleteSelection()
    {
        if (Selection.IsCollapsed) return false;
        var cut = CutSelection();
        Commit(cut, [cut.Block], 0, cut.Offset, EditKind.Other);
        return true;
    }

    /// <summary>
    /// Backspace. Inside text, the character (or word) before the caret. At a block's start, one
    /// step back towards a plain paragraph — a list item, heading or quote drops its format, an
    /// indented paragraph comes out a level — and only then does it join the block before. A
    /// table's cells never join anything; after a picture, the picture is selected first.
    /// </summary>
    public bool DeleteBackward(bool word = false)
    {
        if (!Selection.IsCollapsed) return DeleteSelection();
        var p = Selection.Focus;
        var block = Document[p.Block];

        if (block.IsVoid)
            return RemoveVoid(p.Block);

        if (p.Offset > 0)
        {
            int target = word ? WordBoundary.WordLeft(block.Text, p.Offset) : PreviousCharacter(block.Text, p.Offset);
            Commit(p.Block, 1, [Remove(block, target, p.Offset)], RichSelection.Caret(p.Block, target),
                EditKind.DeleteBackward);
            return true;
        }

        if (block.IsTableCell) return false;

        if (block.Kind != BlockKind.Paragraph)
        {
            // The text stays where it was drawn: a list item's text sits one level in from its
            // marker, so the paragraph it becomes is indented one level more than the item.
            var plain = block with
            {
                Kind = BlockKind.Paragraph, HeadingLevel = 0, Checked = false, Info = null,
                Indent = block.IsListItem ? Math.Min(block.Indent + 1, RichBlock.MaxIndent) : block.Indent,
            };
            Commit(p.Block, 1, [plain], Selection, EditKind.Other);
            return true;
        }
        if (block.Indent > 0)
        {
            Commit(p.Block, 1, [block with { Indent = block.Indent - 1 }], Selection, EditKind.Other);
            return true;
        }
        if (p.Block == 0) return false;

        var prev = Document[p.Block - 1];
        if (prev.IsTableCell || prev.Kind == BlockKind.Image)
        {
            // Into the table's last cell, or onto the picture; an empty block is not kept.
            var into = RichSelection.Caret(p.Block - 1, prev.Text.Length);
            if (block.Text.Length == 0) Commit(p.Block, 1, [], into, EditKind.Other);
            else Select(into);
            return true;
        }
        if (prev.IsVoid)
            return RemoveVoid(p.Block - 1, caretAfter: RichSelection.Caret(p.Block - 1, 0));
        Commit(p.Block - 1, 2, [Join(prev, block)], RichSelection.Caret(p.Block - 1, prev.Text.Length),
            EditKind.DeleteBackward);
        return true;
    }

    /// <summary>Delete: the character or word after the caret; at a block's end, the next block
    /// joins this one (or this one goes, when it is empty, so the next keeps its format). Nothing
    /// joins a table; a picture after the caret is selected first.</summary>
    public bool DeleteForward(bool word = false)
    {
        if (!Selection.IsCollapsed) return DeleteSelection();
        var p = Selection.Focus;
        var block = Document[p.Block];

        if (block.IsVoid)
            return RemoveVoid(p.Block);

        if (p.Offset < block.Text.Length)
        {
            int target = word ? WordBoundary.WordRight(block.Text, p.Offset) : NextCharacter(block.Text, p.Offset);
            Commit(p.Block, 1, [Remove(block, p.Offset, target)], Selection, EditKind.DeleteForward);
            return true;
        }
        if (block.IsTableCell || p.Block == Document.BlockCount - 1) return false;

        var next = Document[p.Block + 1];
        if (block.Text.Length == 0 && (next.Kind == BlockKind.Image || next.IsTableCell))
        {
            // This block goes and what follows moves up under the caret.
            Commit(p.Block, 1, [], RichSelection.Caret(p.Block, 0), EditKind.DeleteForward);
            return true;
        }
        if (next.Kind == BlockKind.Image)
        {
            Select(RichSelection.Caret(p.Block + 1, 0));
            return true;
        }
        if (next.IsTableCell) return false;
        if (next.IsVoid)
            return RemoveVoid(p.Block + 1, caretAfter: Selection);
        if (block.Text.Length == 0)
        {
            Commit(p.Block, 1, [], RichSelection.Caret(p.Block, 0), EditKind.DeleteForward);
            return true;
        }
        Commit(p.Block, 2, [Join(block, next)], Selection, EditKind.DeleteForward);
        return true;
    }

    // A rule or a picture goes as a whole. The caret lands at the end of the block before it, or
    // at the start of the one after when it was first.
    private bool RemoveVoid(int index, RichSelection? caretAfter = null)
    {
        var caret = caretAfter ?? (index > 0
            ? RichSelection.Caret(index - 1, Document[index - 1].Text.Length)
            : RichSelection.Caret(index, 0));
        Commit(index, 1, [], caret, EditKind.Other);
        return true;
    }

    // ── Enter ──

    /// <summary>
    /// Enter. Splits the block at the caret; the new block continues a list (unchecked) or a
    /// quote, and after a heading is a paragraph. An empty list item or quote comes out a level,
    /// then leaves the list. In a code block Enter is a line break, and on an empty last line it
    /// leaves the block. In a table it goes to the cell below, and from the last row out of the
    /// table (<see cref="CellBelow"/>).
    /// </summary>
    public bool SplitBlock()
    {
        if (Features.Allows(RichTextFeatures.MarkdownShortcuts) && Selection.IsCollapsed && ApplyEnterShortcut())
            return true;

        var cut = CutSelection();
        var block = cut.Block;
        int offset = cut.Offset;
        if (block.IsTableCell)
        {
            if (!Selection.IsCollapsed) DeleteSelection();
            return CellBelow();
        }
        if (block.IsVoid)
        {
            Commit(cut, [block, RichBlock.Paragraph() with { Indent = block.Indent }], 1, 0, EditKind.Other);
            return true;
        }

        if (block.IsPreformatted)
        {
            if (block.Kind == BlockKind.CodeBlock && offset == block.Text.Length && block.Text.EndsWith('\n'))
            {
                var kept = block.Slice(0, block.Text.Length - 1);
                Commit(cut, [kept, RichBlock.Paragraph() with { Indent = block.Indent }], 1, 0, EditKind.Other);
                return true;
            }
            Commit(cut, [InsertInline(block, offset, "\n", [])], 0, offset + 1, EditKind.Other);
            return true;
        }

        if (block.Text.Length == 0 && (block.IsListItem || block.Kind == BlockKind.Quote))
        {
            var lifted = block.Indent > 0
                ? block with { Indent = block.Indent - 1 }
                : block with { Kind = BlockKind.Paragraph, Checked = false };
            Commit(cut, [lifted], 0, 0, EditKind.Other);
            return true;
        }

        var head = block.Slice(0, offset);
        var tail = block.Slice(offset, block.Text.Length);
        if (ReferenceEquals(head, tail)) tail = tail with { };
        if (block.Kind == BlockKind.Heading)
        {
            // After a heading comes body text; a line opened above one is not a heading either.
            if (tail.Text.Length == 0) tail = tail with { Kind = BlockKind.Paragraph, HeadingLevel = 0 };
            else if (head.Text.Length == 0) head = head with { Kind = BlockKind.Paragraph, HeadingLevel = 0 };
        }
        if (tail.Kind == BlockKind.TaskItem) tail = tail with { Checked = false };

        Commit(cut, [head, tail], 1, 0, EditKind.Other, keepPending: true);
        return true;
    }

    /// <summary>Shift+Enter: a line break inside the block. A heading has none, so it splits.</summary>
    public bool InsertLineBreak()
    {
        var block = Document[Selection.Start.Block];
        if (block.Kind == BlockKind.Heading || block.IsVoid) return SplitBlock();
        var cut = CutSelection();
        var b = cut.Block;
        var marks = b.IsPreformatted ? RichMarks.None : TypingMarks;
        Commit(cut, [InsertInline(b, cut.Offset, "\n", marks.IsNone ? [] : [new RichSpan(0, 1, marks)])], 0, cut.Offset + 1,
            EditKind.Other, keepPending: true);
        return true;
    }

    // ── Undo ──

    public bool Undo()
    {
        var doc = Document;
        if (!_history.Undo(ref doc, out var selection)) return false;
        Restore(doc, selection);
        return true;
    }

    public bool Redo()
    {
        var doc = Document;
        if (!_history.Redo(ref doc, out var selection)) return false;
        Restore(doc, selection);
        return true;
    }

    private void Restore(RichDocument doc, RichSelection selection)
    {
        Document = doc;
        Selection = doc.Clamp(selection);
        _pending = null;
        DocumentChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    // ── The one way the document changes ──

    /// <summary>
    /// Replaces blocks <c>[index, index + removeCount)</c> with <paramref name="blocks"/>, moves the
    /// selection to <paramref name="after"/> and records the step. Pending marks survive only
    /// when <paramref name="keepPending"/>: typing at the caret keeps typing in them.
    /// </summary>
    private void Commit(int index, int removeCount, IReadOnlyList<RichBlock> blocks, RichSelection after,
        EditKind kind, string typed = "", bool keepPending = false)
    {
        // A document is never empty; removing its last block leaves a paragraph in its place.
        if (blocks.Count == 0 && removeCount == Document.BlockCount)
            blocks = [RichBlock.Paragraph()];

        var before = ImmutableArray.Create(Document.Blocks, index, removeCount);
        var inserted = blocks.ToImmutableArray();
        var doc = Document.ReplaceBlocks(index, removeCount, inserted);

        _history.Record(kind, index, before, inserted, Selection, after, typed);
        Document = doc;
        Selection = doc.Clamp(after);
        if (!keepPending) _pending = null;
        DocumentChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// The selection taken out: the run of blocks it touched, and what is left in their place.
    /// The first block left is the one the caret is in, at <see cref="Offset"/> — the block an edit
    /// then works on; any after it are a table's cells, emptied rather than removed.
    /// </summary>
    private readonly record struct Cut(int Index, int Count, IReadOnlyList<RichBlock> Blocks, int Offset)
    {
        public RichBlock Block => Blocks[0];

        /// <summary>What is left, with the caret's block replaced by <paramref name="replacement"/>.</summary>
        public List<RichBlock> With(IReadOnlyList<RichBlock> replacement)
        {
            var list = new List<RichBlock>(replacement.Count + Blocks.Count - 1);
            list.AddRange(replacement);
            for (int i = 1; i < Blocks.Count; i++) list.Add(Blocks[i]);
            return list;
        }
    }

    /// <summary>Commits <paramref name="cut"/> with its caret block replaced by
    /// <paramref name="replacement"/>, the caret <paramref name="caretOffset"/> into the
    /// <paramref name="caretBlock"/>th block of it.</summary>
    private void Commit(Cut cut, IReadOnlyList<RichBlock> replacement, int caretBlock, int caretOffset, EditKind kind,
        string typed = "", bool keepPending = false) =>
        Commit(cut.Index, cut.Count, cut.With(replacement), RichSelection.Caret(cut.Index + caretBlock, caretOffset), kind,
            typed, keepPending);

    /// <summary>
    /// The selection taken out. Within one block, its characters; across blocks, everything
    /// between, the text before the selection joining the text after it — unless a table is at
    /// either end. A table's cells are emptied as far as the selection reaches, never removed or
    /// joined, and the text outside the table keeps a block of its own. A table wholly inside the
    /// selection goes with the blocks around it.
    /// </summary>
    private Cut CutSelection()
    {
        var s = Selection.Start;
        var e = Selection.End;
        var first = Document[s.Block];
        if (s.Block == e.Block)
            return new Cut(s.Block, 1, [s.Offset == e.Offset ? first : Remove(first, s.Offset, e.Offset)], s.Offset);

        int count = e.Block - s.Block + 1;
        var last = Document[e.Block];
        var startTable = Document.TableAt(s.Block);
        var endTable = Document.TableAt(e.Block);
        if (startTable is null && endTable is null)
        {
            var tail = last.Slice(e.Offset, last.Text.Length);
            if (first.IsVoid) return new Cut(s.Block, count, [ReferenceEquals(tail, last) ? last with { } : tail], 0);
            var head = first.Slice(0, s.Offset);
            if (last.IsVoid) return new Cut(s.Block, count, [ReferenceEquals(head, first) ? first with { } : head], s.Offset);
            return new Cut(s.Block, count, [Join(head, tail)], s.Offset);
        }

        var blocks = new List<RichBlock>();
        int i = s.Block;
        if (startTable is { } st)
        {
            for (int stop = Math.Min(e.Block, st.End - 1); i <= stop; i++)
                blocks.Add(Emptied(i, s, e));
        }
        else
        {
            if (!first.IsVoid)
            {
                var head = first.Slice(0, s.Offset);
                blocks.Add(ReferenceEquals(head, first) ? first with { } : head);
            }
            i++;
        }
        if (i <= e.Block)
        {
            if (endTable is { } et)
            {
                for (int j = Math.Max(i, et.Start); j <= e.Block; j++)
                    blocks.Add(Emptied(j, s, e));
            }
            else if (!last.IsVoid)
            {
                var tail = last.Slice(e.Offset, last.Text.Length);
                blocks.Add(ReferenceEquals(tail, last) ? last with { } : tail);
            }
        }
        // A rule or picture the selection started on is gone: the caret starts what is left.
        return new Cut(s.Block, count, blocks, startTable is null && first.IsVoid ? 0 : s.Offset);
    }

    // The cell at `index` without the part of it between `s` and `e`.
    private RichBlock Emptied(int index, RichPosition s, RichPosition e)
    {
        var cell = Document[index];
        int from = index == s.Block ? s.Offset : 0;
        int to = index == e.Block ? e.Offset : cell.Text.Length;
        return from >= to ? cell : Remove(cell, from, to);
    }

    // ── Block arithmetic ──

    /// <summary><paramref name="a"/> with <paramref name="b"/>'s text after its own, in
    /// <paramref name="a"/>'s format; into preformatted text, without <paramref name="b"/>'s marks.</summary>
    private static RichBlock Join(RichBlock a, RichBlock b)
    {
        if (b.Text.Length == 0) return a with { };
        if (a.IsPreformatted) return a with { Text = a.Text + b.Text };
        return a.Append(b.IsPreformatted ? b with { Spans = [] } : b);
    }

    private static RichBlock Remove(RichBlock block, int from, int to)
    {
        var head = block.Slice(0, from);
        var tail = block.Slice(to, block.Text.Length);
        return tail.Text.Length == 0 ? (ReferenceEquals(head, block) ? head with { } : head) : head.Append(tail);
    }

    // The block with text, carrying spans (from 0), put in at offset; into preformatted text,
    // without them. Its text is made in one string and its spans in one array of their number
    // (RichSpans.Insert). The block sliced at the offset and joined around the text, as it was, made
    // four strings and four span builders a key typed, each copied out and the slices' grown by
    // doubling: 6.4 times the bytes of the spans (plan 17, round 6). Internal for the test that holds
    // it to those slices and joins.
    internal static RichBlock InsertInline(RichBlock block, int offset, string text, ImmutableArray<RichSpan> spans)
    {
        offset = Math.Clamp(offset, 0, block.Text.Length);
        // Nothing put in: the block as it was, a copy of it unless the offset is its end, as joining
        // it around nothing gave.
        if (text.Length == 0)
            return offset == block.Text.Length ? block : block with { };
        return block with
        {
            Text = string.Concat(block.Text.AsSpan(0, offset), text, block.Text.AsSpan(offset)),
            Spans = RichSpans.Insert(block.Spans, block.Text.Length, offset, block.IsPreformatted ? [] : spans,
                text.Length),
        };
    }
}
