using System.Collections.Immutable;
using HultUI.Core.Primitives;
using EditKind = HultUI.Core.RichText.RichUndoHistory.EditKind;

namespace HultUI.Core.RichText;

public sealed partial class RichEditState
{
    // ── What the selection carries (a toolbar's state) ──

    /// <summary>
    /// The marks across the selection: a style is on only when every selected character has it,
    /// and a colour, size, face or link is set only when every character has the same one. With a
    /// collapsed selection, the marks typing would take.
    /// </summary>
    public RichMarks SelectionMarks
    {
        get
        {
            if (Selection.IsCollapsed) return TypingMarks;
            var s = Selection.Start;
            var e = Selection.End;
            RichMarks? acc = null;
            for (int i = s.Block; i <= e.Block; i++)
            {
                var b = Document[i];
                if (b.IsPreformatted || b.IsVoid) continue;
                int from = i == s.Block ? s.Offset : 0;
                int to = i == e.Block ? e.Offset : b.Text.Length;
                if (to <= from) continue;
                foreach (var run in RichSpans.Runs(b.Spans, b.Text.Length))
                {
                    if (run.End <= from || run.Start >= to) continue;
                    acc = acc is { } a ? Common(a, run.Marks) : run.Marks;
                }
            }
            return acc ?? TypingMarks;
        }
    }

    private static RichMarks Common(RichMarks a, RichMarks b) => new(
        a.Style & b.Style,
        a.Color == b.Color ? a.Color : null,
        a.Highlight == b.Highlight ? a.Highlight : null,
        a.Size == b.Size ? a.Size : null,
        a.Family == b.Family ? a.Family : null,
        a.Link == b.Link ? a.Link : null);

    /// <summary>Whether every block the selection touches is <paramref name="kind"/> (a heading
    /// of <paramref name="headingLevel"/>, for headings): what a block button shows as pressed.
    /// Rules, pictures and table cells are left out: no block format applies to them.</summary>
    public bool SelectionIs(BlockKind kind, int headingLevel = 0)
    {
        bool any = false;
        for (int i = Selection.Start.Block; i <= Selection.End.Block; i++)
        {
            var b = Document[i];
            if (b.IsVoid || b.IsTableCell) continue;
            if (b.Kind != kind || (kind == BlockKind.Heading && headingLevel > 0 && b.HeadingLevel != headingLevel))
                return false;
            any = true;
        }
        return any;
    }

    // ── Character formatting ──

    /// <summary>Turns <paramref name="style"/> on over the selection, or off when every selected
    /// character already has it. With no selection, for the characters typed next.</summary>
    public bool ToggleStyle(InlineStyle style)
    {
        if (!Features.Allows(RichTextFeaturesExtensions.FeatureOf(style))) return false;
        bool on = !SelectionMarks.Has(style);
        return ChangeMarks(m => m.With(style, on));
    }

    public bool SetColor(Color? color) =>
        Features.Allows(RichTextFeatures.Color) && ChangeMarks(m => m with { Color = color });

    public bool SetHighlight(Color? color) =>
        Features.Allows(RichTextFeatures.Highlight) && ChangeMarks(m => m with { Highlight = color });

    public bool SetSize(float? size) =>
        Features.Allows(RichTextFeatures.Size) && ChangeMarks(m => m with { Size = size });

    public bool SetFamily(string? family) =>
        Features.Allows(RichTextFeatures.Family) && ChangeMarks(m => m with { Family = family });

    /// <summary>Every character style off; links stay.</summary>
    public bool ClearFormatting() => ChangeMarks(m => new RichMarks(Link: m.Link));

    // Over the selection, or into the pending marks when there is none.
    private bool ChangeMarks(Func<RichMarks, RichMarks> change)
    {
        if (!Selection.IsCollapsed)
            return MapMarks(Selection.Start, Selection.End, change);

        var block = CurrentBlock;
        if (block.IsPreformatted || block.IsVoid) return false;
        var marks = Features.Restrict(change(TypingMarks));
        if (marks == TypingMarks) return false;
        _pending = marks;
        SelectionChanged?.Invoke();
        return true;
    }

    private bool MapMarks(RichPosition from, RichPosition to, Func<RichMarks, RichMarks> change)
    {
        var blocks = new List<RichBlock>(to.Block - from.Block + 1);
        bool changed = false;
        for (int i = from.Block; i <= to.Block; i++)
        {
            var b = Document[i];
            if (b.IsPreformatted || b.IsVoid) { blocks.Add(b); continue; }
            int s = i == from.Block ? from.Offset : 0;
            int e = i == to.Block ? to.Offset : b.Text.Length;
            var spans = RichSpans.Map(b.Spans, b.Text.Length, s, e, m => Features.Restrict(change(m)));
            if (spans.SequenceEqual(b.Spans)) { blocks.Add(b); continue; }
            blocks.Add(b with { Spans = spans });
            changed = true;
        }
        if (!changed) return false;
        Commit(from.Block, blocks.Count, blocks, Selection, EditKind.Other);
        return true;
    }

    // ── Links ──

    /// <summary>The link the character at <paramref name="p"/> belongs to, if any.</summary>
    public string? LinkAt(RichPosition p)
    {
        p = Document.Clamp(p);
        return Document[p.Block].MarksAt(p.Offset).Link;
    }

    /// <summary>The whole link around a caret at <paramref name="p"/>: the characters on either
    /// side of it that point at the same target. Null when neither does.</summary>
    public (RichPosition Start, RichPosition End, string Target)? LinkRangeAt(RichPosition p)
    {
        p = Document.Clamp(p);
        var b = Document[p.Block];
        string? target = b.MarksAt(p.Offset).Link ?? (p.Offset > 0 ? b.MarksAt(p.Offset - 1).Link : null);
        if (target is null) return null;

        int at = b.MarksAt(p.Offset).Link == target ? p.Offset : p.Offset - 1;
        int start = at, end = at + 1;
        while (start > 0 && b.MarksAt(start - 1).Link == target) start--;
        while (end < b.Text.Length && b.MarksAt(end).Link == target) end++;
        return (new RichPosition(p.Block, start), new RichPosition(p.Block, end), target);
    }

    /// <summary>Points the selection at <paramref name="target"/>, or with no selection the link
    /// the caret is in; null removes the link. False with no selection outside a link: there is
    /// nothing to point — <see cref="InsertLink"/> adds text with it.</summary>
    public bool SetLink(string? target)
    {
        if (!Features.Allows(RichTextFeatures.Links)) return false;
        if (!Selection.IsCollapsed)
            return MapMarks(Selection.Start, Selection.End, m => m with { Link = target });
        if (LinkRangeAt(Selection.Focus) is not { } link) return false;
        return MapMarks(link.Start, link.End, m => m with { Link = target });
    }

    /// <summary>Types <paramref name="text"/> over the selection as a link to <paramref name="target"/>.</summary>
    public bool InsertLink(string text, string target)
    {
        if (!Features.Allows(RichTextFeatures.Links) || text.Length == 0) return false;
        var block = Document[Selection.Start.Block];
        if (block.IsPreformatted || block.IsVoid) return InsertText(text);

        var marks = TypingMarks with { Link = target };
        var cut = CutSelection();
        Commit(cut, [InsertInline(cut.Block, cut.Offset, text, [new RichSpan(0, text.Length, marks)])], 0,
            cut.Offset + text.Length, EditKind.Other);
        return true;
    }

    // ── Block formatting ──

    /// <summary>Makes every block the selection touches <paramref name="kind"/>. Paragraphs made
    /// into code become one code block, a line each; a code block made into anything else
    /// becomes a block per line. <see cref="BlockKind.Rule"/> inserts a rule. Pictures and table
    /// cells keep what they are.</summary>
    public bool SetBlockKind(BlockKind kind, int headingLevel = 1)
    {
        if (kind == BlockKind.Rule) return InsertRule();
        if (kind is BlockKind.Image or BlockKind.TableCell) return false;
        if (!Features.Allows(RichTextFeaturesExtensions.FeatureOf(kind))) return false;

        int first = Selection.Start.Block, last = Selection.End.Block, count = last - first + 1;
        var old = ImmutableArray.Create(Document.Blocks, first, count);

        if (kind is BlockKind.CodeBlock or BlockKind.Raw)
        {
            // Joining lines across a table would take its cells apart, and a picture has no line
            // to give.
            if (old.Any(b => b.IsTableCell || b.Kind == BlockKind.Image)) return false;
            // One preformatted block, lines joined; a position keeps its place in the joined text.
            var starts = new int[count];
            var lines = new List<string>();
            int at = 0;
            string? info = null;
            for (int i = 0; i < count; i++)
            {
                starts[i] = at;
                if (old[i].IsVoid) continue;
                lines.Add(old[i].Text);
                at += old[i].Text.Length + 1;
                info ??= old[i].Kind == kind ? old[i].Info : null;
            }
            var joined = new RichBlock { Kind = kind, Text = string.Join("\n", lines), Indent = old[0].Indent, Info = info };
            RichPosition MapJoined(RichPosition p) =>
                new(first, starts[p.Block - first] + Math.Min(p.Offset, old[p.Block - first].Text.Length));
            Commit(first, count, [joined], new RichSelection(MapJoined(Selection.Anchor), MapJoined(Selection.Focus)),
                EditKind.Other);
            return true;
        }

        var result = new List<RichBlock>();
        // For each old block, where its pieces landed: (offset the piece starts at, new block).
        var pieces = new List<(int Start, int Block)>[count];
        for (int i = 0; i < count; i++)
        {
            var b = old[i];
            pieces[i] = [];
            if (b.IsVoid || b.IsTableCell) { pieces[i].Add((0, result.Count)); result.Add(b); continue; }
            if (b.IsPreformatted)
            {
                int at = 0;
                foreach (var line in b.Text.Split('\n'))
                {
                    pieces[i].Add((at, result.Count));
                    result.Add(Retype(RichBlock.Paragraph(line) with { Indent = b.Indent, Align = b.Align }, kind, headingLevel));
                    at += line.Length + 1;
                }
                continue;
            }
            pieces[i].Add((0, result.Count));
            result.Add(Retype(b, kind, headingLevel));
        }

        RichPosition MapSplit(RichPosition p)
        {
            var list = pieces[p.Block - first];
            var piece = list[0];
            foreach (var candidate in list)
                if (candidate.Start <= p.Offset) piece = candidate;
            return new RichPosition(first + piece.Block, p.Offset - piece.Start);
        }

        if (result.Count == count && result.Select((b, i) => b.Equals(old[i])).All(same => same)) return false;
        Commit(first, count, result, new RichSelection(MapSplit(Selection.Anchor), MapSplit(Selection.Focus)),
            EditKind.Other);
        return true;
    }

    private static RichBlock Retype(RichBlock b, BlockKind kind, int headingLevel) => b with
    {
        Kind = kind,
        HeadingLevel = kind == BlockKind.Heading ? Math.Clamp(headingLevel, 1, 6) : 0,
        Checked = kind == BlockKind.TaskItem && b.Kind == BlockKind.TaskItem && b.Checked,
        Info = null,
    };

    /// <summary><see cref="SetBlockKind"/>, or back to paragraphs when the selection already is
    /// that kind: what a toolbar's list or heading button does.</summary>
    public bool ToggleBlockKind(BlockKind kind, int headingLevel = 1) =>
        SelectionIs(kind, kind == BlockKind.Heading ? headingLevel : 0)
            ? SetBlockKind(BlockKind.Paragraph)
            : SetBlockKind(kind, headingLevel);

    /// <summary>A rule at the caret: the block splits around it, and the caret goes after it. Not
    /// in a table.</summary>
    public bool InsertRule()
    {
        if (!Features.Allows(RichTextFeatures.Rules)) return false;
        var cut = CutSelection();
        if (cut.Block.IsTableCell) return false;
        return InsertApart(cut, [RichBlock.Of(BlockKind.Rule, indent: cut.Block.Indent)], caretInside: false);
    }

    /// <summary>A picture at the caret, on a line of its own: the block splits around it, and
    /// the picture is selected. Not in a table.</summary>
    public bool InsertImage(RichImage image)
    {
        if (!Features.Allows(RichTextFeatures.Images)) return false;
        var cut = CutSelection();
        if (cut.Block.IsTableCell) return false;
        return InsertApart(cut, [RichBlock.Picture(image, cut.Block.Indent)], caretInside: true);
    }

    /// <summary>Changes the picture at <paramref name="block"/> — its source, its words, its
    /// width.</summary>
    public bool SetImage(int block, RichImage image)
    {
        if (block < 0 || block >= Document.BlockCount || Document[block].Kind != BlockKind.Image) return false;
        var b = Document[block];
        if (b.Image == image) return false;
        Commit(block, 1, [b with { Image = image }], Selection, EditKind.Other);
        return true;
    }

    /// <summary>The picture the caret is on, and its block; null elsewhere.</summary>
    public (int Block, RichImage Image)? CurrentImage =>
        Selection.IsCollapsed && CurrentBlock is { Kind: BlockKind.Image, Image: { } image }
            ? (Selection.Focus.Block, image) : null;

    // Blocks that stand apart — a rule, a picture, a table — put in at the caret: the block it is
    // in splits around them and keeps a line after them. The caret goes into the first of them,
    // or after them all.
    private bool InsertApart(Cut cut, IReadOnlyList<RichBlock> blocks, bool caretInside)
    {
        var block = cut.Block;
        if (block.IsVoid)
        {
            Commit(cut, [block, .. blocks, RichBlock.Paragraph() with { Indent = block.Indent }],
                caretInside ? 1 : 1 + blocks.Count, 0, EditKind.Other);
            return true;
        }
        var head = block.Slice(0, cut.Offset);
        var tail = block.Slice(cut.Offset, block.Text.Length);
        if (ReferenceEquals(head, tail)) tail = tail with { };
        var result = new List<RichBlock>();
        if (head.Text.Length > 0) result.Add(head);
        int at = result.Count;
        result.AddRange(blocks);
        result.Add(tail);
        Commit(cut, result, caretInside ? at : result.Count - 1, 0, EditKind.Other);
        return true;
    }

    /// <summary>Aligns every block the selection touches. In a table it aligns the whole column
    /// of each cell touched — Markdown's tables align columns, not cells — which needs
    /// <see cref="RichTextFeatures.Tables"/> rather than <see cref="RichTextFeatures.Alignment"/>.</summary>
    public bool SetAlign(BlockAlign align)
    {
        bool blocks = Features.Allows(RichTextFeatures.Alignment), cells = Features.Allows(RichTextFeatures.Tables);
        int first = Selection.Start.Block, last = Selection.End.Block;
        int from = first, to = last;
        var columns = new HashSet<(int Table, int Column)>();
        RichTableRange? table = null;
        for (int i = first; cells && i <= last; i++)
        {
            if (!Document[i].IsTableCell) continue;
            var t = TableOf(i, ref table);
            columns.Add((t.Start, t.ColumnOf(i)));
            from = Math.Min(from, t.Start);
            to = Math.Max(to, t.End - 1);
        }
        if (columns.Count == 0 && !blocks) return false;

        var result = new List<RichBlock>(to - from + 1);
        bool changed = false;
        for (int i = from; i <= to; i++)
        {
            var b = Document[i];
            bool aligns;
            if (b.IsTableCell)
            {
                var t = TableOf(i, ref table);
                aligns = columns.Contains((t.Start, t.ColumnOf(i)));
            }
            else
                aligns = blocks && i >= first && i <= last && !b.IsVoid;
            if (aligns && b.Align != align)
            {
                result.Add(b with { Align = align });
                changed = true;
            }
            else
                result.Add(b);
        }
        if (!changed) return false;
        Commit(from, result.Count, result, Selection, EditKind.Other);
        return true;
    }

    /// <summary>One level deeper — never more than one past the block before it, so a list item
    /// always has a parent item to nest under.</summary>
    public bool Indent() => ChangeIndent(+1);

    public bool Outdent() => ChangeIndent(-1);

    private bool ChangeIndent(int delta)
    {
        int first = Selection.Start.Block, last = Selection.End.Block;
        var blocks = new List<RichBlock>();
        bool changed = false;
        for (int i = first; i <= last; i++)
        {
            var b = Document[i];
            int indent = b.Indent;
            if (!b.IsVoid && !b.IsTableCell)
            {
                if (delta < 0) indent = Math.Max(0, indent - 1);
                else
                {
                    int previous = i == 0 ? -1 : i == first ? Document[i - 1].Indent : blocks[^1].Indent;
                    indent = Math.Max(indent, Math.Min(indent + 1, Math.Min(previous + 1, RichBlock.MaxIndent)));
                }
            }
            if (indent != b.Indent) { blocks.Add(b with { Indent = indent }); changed = true; }
            else blocks.Add(b);
        }
        if (!changed) return false;
        Commit(first, blocks.Count, blocks, Selection, EditKind.Other);
        return true;
    }

    /// <summary>Ticks or unticks the task item at <paramref name="block"/>.</summary>
    public bool ToggleChecked(int block)
    {
        if (block < 0 || block >= Document.BlockCount || Document[block].Kind != BlockKind.TaskItem) return false;
        var b = Document[block];
        Commit(block, 1, [b with { Checked = !b.Checked }], Selection, EditKind.Other);
        return true;
    }

    // Each block the selection touches through `change`; one step if any changed.
    private bool MapBlocks(Func<RichBlock, RichBlock> change)
    {
        int first = Selection.Start.Block, last = Selection.End.Block;
        var blocks = new List<RichBlock>();
        bool changed = false;
        for (int i = first; i <= last; i++)
        {
            var b = Document[i];
            var c = change(b);
            changed |= !ReferenceEquals(b, c);
            blocks.Add(c);
        }
        if (!changed) return false;
        Commit(first, blocks.Count, blocks, Selection, EditKind.Other);
        return true;
    }

    // ── Markdown shortcuts ──
    //
    // Each is a step of its own after the keystroke that triggered it, so Ctrl+Z gives back the
    // characters as typed: "# " undoes to "# ", not to nothing.

    private void ApplyTypingShortcut(char typed)
    {
        var p = Selection.Focus;
        var block = Document[p.Block];
        if (block.IsPreformatted || block.IsVoid) return;
        if (typed == ' ' && TryBlockShortcut(p, block)) return;
        if (typed is '*' or '_' or '`' or '~') TryInlineShortcut(p, block);
    }

    private bool TryBlockShortcut(RichPosition p, RichBlock block)
    {
        string before = block.Text[..p.Offset];
        BlockKind? kind = null;
        int level = 0;
        bool check = false;

        if (block.Kind == BlockKind.Paragraph)
        {
            if (before.Length is >= 2 and <= 7 && before[^1] == ' ' && before[..^1].All(c => c == '#'))
            {
                kind = BlockKind.Heading;
                level = before.Length - 1;
            }
            else if (before is "- " or "* " or "+ ") kind = BlockKind.BulletItem;
            else if (IsNumberMarker(before)) kind = BlockKind.NumberedItem;
            else if (IsTaskMarker(before, out check)) kind = BlockKind.TaskItem;
            else if (before == "> ") kind = BlockKind.Quote;
        }
        else if (block.Kind == BlockKind.BulletItem && IsTaskMarker(before, out check))
            kind = BlockKind.TaskItem;

        if (kind is not { } k || !Features.Allows(RichTextFeaturesExtensions.FeatureOf(k))) return false;

        var converted = block.Slice(before.Length, block.Text.Length) with { Kind = k, HeadingLevel = level, Checked = check };
        if (ReferenceEquals(converted, block)) converted = converted with { };
        _history.Seal();
        Commit(p.Block, 1, [converted], RichSelection.Caret(p.Block, 0), EditKind.Other);
        return true;
    }

    private static bool IsNumberMarker(string s) =>
        s.Length is >= 3 and <= 11 && s[^1] == ' ' && s[^2] is '.' or ')' && s[..^2].All(char.IsAsciiDigit);

    private static bool IsTaskMarker(string s, out bool check)
    {
        check = s is "[x] " or "[X] ";
        return s is "[ ] " or "[] " || check;
    }

    // `code`, **bold**, __bold__, *italic*, _italic_, ~~strike~~ — on the closing mark, the
    // nearest opening one before it on the same block.
    private static readonly (string Mark, InlineStyle Style)[] InlineMarks =
    [
        ("**", InlineStyle.Bold), ("__", InlineStyle.Bold), ("~~", InlineStyle.Strike),
        ("`", InlineStyle.Code), ("*", InlineStyle.Italic), ("_", InlineStyle.Italic),
    ];

    private void TryInlineShortcut(RichPosition p, RichBlock block)
    {
        string text = block.Text;
        int end = p.Offset;
        foreach (var (mark, style) in InlineMarks)
        {
            int close = end - mark.Length;
            if (close < 1 || !text.AsSpan(close, mark.Length).SequenceEqual(mark)) continue;
            // A single mark that is half of a double one belongs to the double.
            if (mark.Length == 1 && text[close - 1] == mark[0]) continue;
            if (!Features.Allows(RichTextFeaturesExtensions.FeatureOf(style))) continue;

            int open = FindOpening(text, mark, close);
            if (open < 0) continue;
            int contentStart = open + mark.Length;
            string content = text[contentStart..close];
            if (content.Length == 0 || char.IsWhiteSpace(content[0]) || char.IsWhiteSpace(content[^1])
                || content.Contains('\n'))
                continue;
            if (style != InlineStyle.Code && block.MarksAt(open).Has(InlineStyle.Code)) continue;

            var without = Remove(Remove(block, close, end), open, contentStart);
            var styled = without with
            {
                Spans = RichSpans.Map(without.Spans, without.Text.Length, open, open + content.Length,
                    m => m.With(style, true)),
            };
            _history.Seal();
            Commit(p.Block, 1, [styled], RichSelection.Caret(p.Block, open + content.Length), EditKind.Other);
            // What follows is typed without the style just made.
            var after = styled.MarksAt(open + content.Length - 1).With(style, false);
            _pending = Features.Restrict(after);
            SelectionChanged?.Invoke();
            return;
        }
    }

    // The nearest opening mark before `close` with at least one character between them. A single
    // mark that is half of a double one is not an opening; an underscore opens only at a word's
    // start, so snake_case stays as typed.
    private static int FindOpening(string text, string mark, int close)
    {
        for (int i = close - mark.Length - 1; i >= 0; i--)
        {
            if (!text.AsSpan(i, mark.Length).SequenceEqual(mark)) continue;
            if (mark.Length == 1 && ((i > 0 && text[i - 1] == mark[0]) || text[i + 1] == mark[0])) continue;
            if (mark[0] == '_' && i > 0 && char.IsLetterOrDigit(text[i - 1])) return -1;
            return i;
        }
        return -1;
    }

    // "```lang" or "---" on a line of its own, then Enter.
    private bool ApplyEnterShortcut()
    {
        var p = Selection.Focus;
        var block = Document[p.Block];
        if (block.Kind != BlockKind.Paragraph || p.Offset != block.Text.Length) return false;
        string t = block.Text;

        if (t.StartsWith("```", StringComparison.Ordinal) && Features.Allows(RichTextFeatures.CodeBlocks)
            && !t.AsSpan(3).Contains('`') && !t.AsSpan(3).Trim().Contains(' '))
        {
            string lang = t[3..].Trim();
            var code = new RichBlock { Kind = BlockKind.CodeBlock, Indent = block.Indent, Info = lang.Length > 0 ? lang : null };
            _history.Seal();
            Commit(p.Block, 1, [code], RichSelection.Caret(p.Block, 0), EditKind.Other);
            return true;
        }
        if (t is "---" or "***" or "___" && Features.Allows(RichTextFeatures.Rules))
        {
            _history.Seal();
            Commit(p.Block, 1,
                [RichBlock.Of(BlockKind.Rule, indent: block.Indent), RichBlock.Paragraph() with { Indent = block.Indent }],
                RichSelection.Caret(p.Block + 1, 0), EditKind.Other);
            return true;
        }
        return false;
    }
}
