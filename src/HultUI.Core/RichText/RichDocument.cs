using System.Collections.Immutable;
using System.Text;

namespace HultUI.Core.RichText;

/// <summary>
/// A rich text document: a list of blocks, never empty (docs/plans/16-rich-text-editor.md).
/// Immutable — an edit makes a new document that shares every block it did not touch — so an
/// app can keep one in a signal, compare two by reference, and hand one to an editor knowing it
/// will not change underneath.
/// </summary>
/// <remarks>
/// Positions are (block, offset) pairs (<see cref="RichPosition"/>). A <b>global offset</b> reads
/// the document as its plain text — the blocks joined by <c>'\n'</c> — which is how find, the
/// plain-text clipboard and assistive technology see it.
/// </remarks>
public sealed class RichDocument : IEquatable<RichDocument>
{
    /// <summary>One empty paragraph: what an editor holds before anything is typed.</summary>
    public static readonly RichDocument Empty = new([RichBlock.Paragraph()]);

    private int[]? _starts;   // global offset of each block, worked out on first use

    public RichDocument(IEnumerable<RichBlock> blocks)
    {
        var list = blocks.ToImmutableArray();
        Blocks = list.IsDefaultOrEmpty ? [RichBlock.Paragraph()] : list;
    }

    /// <summary>The blocks, in order. Never empty.</summary>
    public ImmutableArray<RichBlock> Blocks { get; }

    public int BlockCount => Blocks.Length;
    public RichBlock this[int block] => Blocks[block];

    /// <summary>The table block <paramref name="block"/> is a cell of; null outside one.</summary>
    public RichTableRange? TableAt(int block) => RichTable.At(Blocks, block);

    /// <summary>Each line of <paramref name="text"/> a paragraph of its own, unstyled.</summary>
    public static RichDocument FromPlainText(string text) =>
        new(text.ReplaceLineEndings("\n").Split('\n').Select(line => RichBlock.Paragraph(line)));

    private string? _plainText;

    /// <summary>The text with every block on its own line: the plain-text reading. Made once per
    /// document — an assistive technology can ask for it several times a frame.</summary>
    public string ToPlainText()
    {
        // Written straight into the string: through a builder, a long document's text was made twice.
        return _plainText ??= string.Create(Length, Blocks, static (chars, blocks) =>
        {
            int at = 0;
            for (int i = 0; i < blocks.Length; i++)
            {
                if (i > 0) chars[at++] = '\n';
                blocks[i].Text.AsSpan().CopyTo(chars[at..]);
                at += blocks[i].Text.Length;
            }
        });
    }

    /// <summary>The length of <see cref="ToPlainText"/>.</summary>
    public int Length
    {
        get
        {
            var starts = Starts;
            return starts[^1] + Blocks[^1].Text.Length;
        }
    }

    public RichPosition Start => new(0, 0);
    public RichPosition End => new(Blocks.Length - 1, Blocks[^1].Text.Length);

    /// <summary>The position clamped into the document: a block that exists, an offset inside it.</summary>
    public RichPosition Clamp(RichPosition p)
    {
        int block = Math.Clamp(p.Block, 0, Blocks.Length - 1);
        return new RichPosition(block, Math.Clamp(p.Offset, 0, Blocks[block].Text.Length));
    }

    public RichSelection Clamp(RichSelection s) => new(Clamp(s.Anchor), Clamp(s.Focus));

    /// <summary>The global offset of <paramref name="p"/> in <see cref="ToPlainText"/>.</summary>
    public int ToOffset(RichPosition p)
    {
        p = Clamp(p);
        return Starts[p.Block] + p.Offset;
    }

    /// <summary>The position at global offset <paramref name="offset"/>. An offset on a block
    /// separator belongs to the end of the block before it.</summary>
    public RichPosition ToPosition(int offset)
    {
        var starts = Starts;
        offset = Math.Clamp(offset, 0, Length);
        int lo = 0, hi = starts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (starts[mid] <= offset) lo = mid;
            else hi = mid - 1;
        }
        return new RichPosition(lo, Math.Min(offset - starts[lo], Blocks[lo].Text.Length));
    }

    /// <summary>The document with blocks <c>[index, index + removeCount)</c> replaced by
    /// <paramref name="insert"/>. Every other block is the same instance.</summary>
    public RichDocument ReplaceBlocks(int index, int removeCount, IEnumerable<RichBlock> insert)
    {
        // One copy of the document's block list per edit, straight into its final size.
        var inserted = insert as IReadOnlyCollection<RichBlock> ?? insert.ToList();
        var builder = ImmutableArray.CreateBuilder<RichBlock>(Blocks.Length - removeCount + inserted.Count);
        builder.AddRange(Blocks.AsSpan(0, index));
        builder.AddRange(inserted);
        builder.AddRange(Blocks.AsSpan()[(index + removeCount)..]);
        return new RichDocument(builder.MoveToImmutable());
    }

    /// <summary>The blocks between two positions, cut at both ends: what Copy takes.</summary>
    public RichFragment Slice(RichPosition from, RichPosition to)
    {
        from = Clamp(from);
        to = Clamp(to);
        if (to < from) (from, to) = (to, from);

        if (from.Block == to.Block)
            return new RichFragment([Blocks[from.Block].Slice(from.Offset, to.Offset)]);

        var blocks = ImmutableArray.CreateBuilder<RichBlock>(to.Block - from.Block + 1);
        var first = Blocks[from.Block];
        blocks.Add(first.Slice(from.Offset, first.Text.Length));
        for (int i = from.Block + 1; i < to.Block; i++)
            blocks.Add(Blocks[i]);
        blocks.Add(Blocks[to.Block].Slice(0, to.Offset));
        return new RichFragment(blocks.MoveToImmutable());
    }

    private int[] Starts
    {
        get
        {
            if (_starts is { } s) return s;
            var starts = new int[Blocks.Length];
            int at = 0;
            for (int i = 0; i < Blocks.Length; i++)
            {
                starts[i] = at;
                at += Blocks[i].Text.Length + 1;
            }
            return _starts = starts;
        }
    }

    public bool Equals(RichDocument? other) =>
        other is not null && (ReferenceEquals(this, other) || Blocks.SequenceEqual(other.Blocks));

    public override bool Equals(object? obj) => Equals(obj as RichDocument);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var b in Blocks) hash.Add(b);
        return hash.ToHashCode();
    }

    public override string ToString() => string.Join(" | ", Blocks.Select(b => b.ToString()));
}

/// <summary>A place between two characters: <paramref name="Offset"/> characters into block
/// <paramref name="Block"/>.</summary>
public readonly record struct RichPosition(int Block, int Offset) : IComparable<RichPosition>
{
    public int CompareTo(RichPosition other) =>
        Block != other.Block ? Block.CompareTo(other.Block) : Offset.CompareTo(other.Offset);

    public static bool operator <(RichPosition a, RichPosition b) => a.CompareTo(b) < 0;
    public static bool operator >(RichPosition a, RichPosition b) => a.CompareTo(b) > 0;
    public static bool operator <=(RichPosition a, RichPosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(RichPosition a, RichPosition b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Block}:{Offset}";
}

/// <summary>
/// A selection: <paramref name="Anchor"/> is where it started, <paramref name="Focus"/> where the
/// caret is now. Either may come first.
/// </summary>
public readonly record struct RichSelection(RichPosition Anchor, RichPosition Focus)
{
    public static RichSelection Caret(RichPosition at) => new(at, at);
    public static RichSelection Caret(int block, int offset) => Caret(new RichPosition(block, offset));

    public RichPosition Start => Anchor <= Focus ? Anchor : Focus;
    public RichPosition End => Anchor <= Focus ? Focus : Anchor;
    public bool IsCollapsed => Anchor == Focus;

    public override string ToString() => IsCollapsed ? $"[{Focus}]" : $"[{Anchor}→{Focus}]";
}

/// <summary>
/// A piece of a document: what Copy takes and Paste inserts. A fragment of one block is inline
/// content — it joins the block it lands in; more than one carries its blocks' formats along.
/// </summary>
public sealed record RichFragment(ImmutableArray<RichBlock> Blocks)
{
    /// <summary>A single unstyled run of text; <c>'\n'</c> starts a new paragraph.</summary>
    public static RichFragment FromPlainText(string text) =>
        new(text.ReplaceLineEndings("\n").Split('\n').Select(line => RichBlock.Paragraph(line)).ToImmutableArray());

    public bool IsInline => Blocks.Length == 1;

    /// <summary>The text, a block to a line — except that the cells of a table row share one,
    /// tab-separated, which is how a spreadsheet pastes them.</summary>
    public string ToPlainText()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Blocks.Length; i++)
        {
            var b = Blocks[i];
            if (i > 0)
                sb.Append(b.IsTableCell && Blocks[i - 1].IsTableCell && b.Column > 0 && b.Row == Blocks[i - 1].Row ? '\t' : '\n');
            sb.Append(b.Text);
        }
        return sb.ToString();
    }

    public bool Equals(RichFragment? other) =>
        other is not null && Blocks.SequenceEqual(other.Blocks);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var b in Blocks) hash.Add(b);
        return hash.ToHashCode();
    }
}
