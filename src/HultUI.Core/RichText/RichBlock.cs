using System.Collections.Immutable;
using HultUI.Core.Primitives;

namespace HultUI.Core.RichText;

/// <summary>What a block is: its chrome, its spacing, and how Enter and Backspace treat it.</summary>
public enum BlockKind : byte
{
    Paragraph,
    /// <summary>A heading of <see cref="RichBlock.HeadingLevel"/> 1–6.</summary>
    Heading,
    BulletItem,
    NumberedItem,
    /// <summary>A list item with a check box (<see cref="RichBlock.Checked"/>).</summary>
    TaskItem,
    Quote,
    /// <summary>Preformatted text in the code face; <c>'\n'</c> stays inside the block and
    /// <see cref="RichBlock.Info"/> names the language.</summary>
    CodeBlock,
    /// <summary>A horizontal rule. It holds no text.</summary>
    Rule,
    /// <summary>Source the editor cannot represent — HTML, a picture inside a line of text — kept
    /// verbatim and shown as plain text, so loading a document never loses what it cannot draw.
    /// <see cref="RichBlock.Info"/> names the source format.</summary>
    Raw,
    /// <summary>A picture on a line of its own (<see cref="RichBlock.Image"/>). It holds no text.</summary>
    Image,
    /// <summary>One cell of a table, at <see cref="RichBlock.Row"/> and <see cref="RichBlock.Column"/>.
    /// A table is a run of cells in reading order, row by row; a cell at (0, 0) starts a new one,
    /// and row 0 is its header (<see cref="RichTable"/>).</summary>
    TableCell,
}

public enum BlockAlign : byte { Start, Center, End }

/// <summary>Character styles that are on or off.</summary>
[Flags]
public enum InlineStyle : byte
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strike = 8,
    /// <summary>Inline code: the code face and background.</summary>
    Code = 16,
}

/// <summary>
/// The formatting on a run of characters. <c>default</c> is plain text. These are what the text
/// <i>is</i>; how each looks (the code face, the link colour) is the theme's, so a theme change
/// never rewrites a document.
/// </summary>
public readonly record struct RichMarks(
    InlineStyle Style = InlineStyle.None,
    Color? Color = null,
    Color? Highlight = null,
    float? Size = null,
    string? Family = null,
    string? Link = null)
{
    public static RichMarks None => default;

    public bool IsNone => this == default;

    public bool Has(InlineStyle style) => (Style & style) == style && style != InlineStyle.None;

    public RichMarks With(InlineStyle style, bool on) =>
        this with { Style = on ? Style | style : Style & ~style };

    public override string ToString()
    {
        var parts = new List<string>();
        if (Style != InlineStyle.None) parts.Add(Style.ToString());
        if (Color is { } c) parts.Add($"color={c}");
        if (Highlight is { } h) parts.Add($"highlight={h}");
        if (Size is { } s) parts.Add($"size={s}");
        if (Family is { } f) parts.Add($"family={f}");
        if (Link is { } l) parts.Add($"link={l}");
        return parts.Count == 0 ? "plain" : string.Join(",", parts);
    }
}

/// <summary>
/// A picture: where it comes from, what it shows in words, and how wide it asks to be drawn.
/// </summary>
/// <param name="Source">Whatever the app's image resolver understands — a URL, a path.</param>
/// <param name="Alt">What it shows, in words: read aloud, and drawn while it loads.</param>
/// <param name="Title">The tooltip a browser gives it.</param>
/// <param name="Width">The width it asks for in layout units, as <c>width="600"</c> on an HTML
/// image; null is its own. It is still scaled down to fit.</param>
/// <param name="Link">Where a click on it goes, for a picture that is a link.</param>
public sealed record RichImage(string Source, string Alt = "", string? Title = null, float? Width = null, string? Link = null);

/// <summary>A run of <paramref name="Length"/> characters from <paramref name="Start"/> carrying
/// <paramref name="Marks"/>.</summary>
public readonly record struct RichSpan(int Start, int Length, RichMarks Marks)
{
    public int End => Start + Length;
}

/// <summary>
/// One block of a <see cref="RichDocument"/>: a paragraph, a heading, a list item… Its
/// <see cref="Spans"/> are sorted, do not overlap, are never empty or plain, and two that meet
/// never carry the same marks — <see cref="WithContent"/> keeps them so, which makes two blocks
/// that read the same compare equal.
/// </summary>
public sealed record RichBlock
{
    public BlockKind Kind { get; init; }
    public string Text { get; init; } = "";
    public ImmutableArray<RichSpan> Spans { get; init; } = [];

    /// <summary>1–6 for a <see cref="BlockKind.Heading"/>; 0 otherwise.</summary>
    public int HeadingLevel { get; init; }

    /// <summary>How deeply nested: a list item under another, a quote inside a quote, or a
    /// paragraph continuing a list item at that depth.</summary>
    public int Indent { get; init; }

    public BlockAlign Align { get; init; }

    /// <summary>Whether a <see cref="BlockKind.TaskItem"/> is done.</summary>
    public bool Checked { get; init; }

    /// <summary>A code block's language, or a raw block's source format.</summary>
    public string? Info { get; init; }

    /// <summary>The picture an <see cref="BlockKind.Image"/> block shows.</summary>
    public RichImage? Image { get; init; }

    /// <summary>A <see cref="BlockKind.TableCell"/>'s row in its table, from 0 (the header).</summary>
    public int Row { get; init; }

    /// <summary>A <see cref="BlockKind.TableCell"/>'s column in its table, from 0.</summary>
    public int Column { get; init; }

    /// <summary>The most a block may be indented.</summary>
    public const int MaxIndent = 8;

    public static RichBlock Paragraph(string text = "", params RichSpan[] spans) =>
        new RichBlock { Kind = BlockKind.Paragraph }.WithContent(text, spans);

    public static RichBlock Heading(int level, string text = "", params RichSpan[] spans) =>
        new RichBlock { Kind = BlockKind.Heading, HeadingLevel = Math.Clamp(level, 1, 6) }.WithContent(text, spans);

    public static RichBlock Of(BlockKind kind, string text = "", int indent = 0) =>
        new RichBlock { Kind = kind, Indent = indent, HeadingLevel = kind == BlockKind.Heading ? 1 : 0 }
            .WithContent(kind is BlockKind.Rule or BlockKind.Image ? "" : text, []);

    public static RichBlock Picture(RichImage image, int indent = 0) =>
        new() { Kind = BlockKind.Image, Image = image, Indent = indent };

    public static RichBlock Cell(int row, int column, string text = "", params RichSpan[] spans) =>
        new RichBlock { Kind = BlockKind.TableCell, Row = row, Column = column }.WithContent(text, spans);

    public bool IsListItem => Kind is BlockKind.BulletItem or BlockKind.NumberedItem or BlockKind.TaskItem;

    /// <summary>A rule or a picture: there is nothing in it to type into, and it is selected,
    /// stepped over and deleted whole.</summary>
    public bool IsVoid => Kind is BlockKind.Rule or BlockKind.Image;

    public bool IsTableCell => Kind == BlockKind.TableCell;

    /// <summary>Whether text in it is preformatted: no character styles, the code face.</summary>
    public bool IsPreformatted => Kind is BlockKind.CodeBlock or BlockKind.Raw;

    /// <summary>The marks on the character at <paramref name="offset"/>; plain past either end.</summary>
    public RichMarks MarksAt(int offset)
    {
        foreach (var s in Spans)
        {
            if (offset < s.Start) break;
            if (offset < s.End) return s.Marks;
        }
        return RichMarks.None;
    }

    /// <summary>The same block — kind, depth, alignment, check — holding other text.</summary>
    public RichBlock WithContent(string text, IEnumerable<RichSpan> spans) =>
        this with { Text = text, Spans = RichSpans.Normalize(spans, text.Length) };

    /// <summary>The characters <c>[from, to)</c> as a block of the same format.</summary>
    public RichBlock Slice(int from, int to)
    {
        from = Math.Clamp(from, 0, Text.Length);
        to = Math.Clamp(to, from, Text.Length);
        if (from == 0 && to == Text.Length) return this;
        return this with { Text = Text[from..to], Spans = RichSpans.Slice(Spans, from, to) };
    }

    /// <summary>This block's text followed by <paramref name="tail"/>'s, in this block's format.</summary>
    public RichBlock Append(RichBlock tail) =>
        tail.Text.Length == 0 ? this
        : this with { Text = Text + tail.Text, Spans = RichSpans.Concat(Spans, Text.Length, tail.Spans) };

    /// <summary>Whether two blocks have the same format, whatever their text.</summary>
    public bool SameFormat(RichBlock other) =>
        Kind == other.Kind && HeadingLevel == other.HeadingLevel && Indent == other.Indent
        && Align == other.Align && Checked == other.Checked && Info == other.Info
        && Image == other.Image && Row == other.Row && Column == other.Column;

    /// <summary>This block's format with <paramref name="other"/>'s content.</summary>
    public RichBlock FormatOf(RichBlock other) => this with { Text = other.Text, Spans = other.Spans };

    public bool Equals(RichBlock? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (SameFormat(other) && Text == other.Text && Spans.SequenceEqual(other.Spans)));

    public override int GetHashCode() => HashCode.Combine(Kind, Text, Indent, HeadingLevel, Spans.Length);

    public override string ToString()
    {
        string kind = Kind switch
        {
            BlockKind.Heading => $"h{HeadingLevel}",
            BlockKind.TaskItem => Checked ? "task[x]" : "task[ ]",
            BlockKind.Image => $"image({Image?.Source})",
            BlockKind.TableCell => $"cell[{Row},{Column}]",
            _ => Kind.ToString(),
        };
        if (Indent > 0) kind += $">{Indent}";
        if (Align != BlockAlign.Start) kind += $"@{Align}";
        if (Info is { } info) kind += $"({info})";
        var spans = Spans.Length == 0 ? "" : " {" + string.Join(" ", Spans.Select(s => $"{s.Start}+{s.Length}:{s.Marks}")) + "}";
        return $"{kind} \"{Text}\"{spans}";
    }
}
