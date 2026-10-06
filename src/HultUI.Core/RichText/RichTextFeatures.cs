namespace HultUI.Core.RichText;

/// <summary>
/// What an editor lets a person make. Shortcuts, the toolbar and paste all respect it, so a
/// document never holds something its owner cannot save: a Markdown editor offers no underline,
/// because Markdown has none.
/// </summary>
[Flags]
public enum RichTextFeatures : uint
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Underline = 1 << 2,
    Strike = 1 << 3,
    InlineCode = 1 << 4,
    Color = 1 << 5,
    Highlight = 1 << 6,
    Size = 1 << 7,
    Family = 1 << 8,
    Links = 1 << 9,
    Headings = 1 << 10,
    Lists = 1 << 11,
    TaskLists = 1 << 12,
    Quotes = 1 << 13,
    CodeBlocks = 1 << 14,
    Rules = 1 << 15,
    Alignment = 1 << 16,
    /// <summary>Typing <c># </c>, <c>- </c>, <c>**bold**</c> and the like turns into formatting.</summary>
    MarkdownShortcuts = 1 << 17,

    All = (1u << 18) - 1,

    /// <summary>What Markdown can say: no underline, colour, highlight, size, face or alignment.</summary>
    Markdown = All & ~(Underline | Color | Highlight | Size | Family | Alignment),
}

public static class RichTextFeaturesExtensions
{
    public static bool Allows(this RichTextFeatures features, RichTextFeatures feature) =>
        (features & feature) == feature;

    /// <summary>The feature a character style needs.</summary>
    public static RichTextFeatures FeatureOf(InlineStyle style) => style switch
    {
        InlineStyle.Bold => RichTextFeatures.Bold,
        InlineStyle.Italic => RichTextFeatures.Italic,
        InlineStyle.Underline => RichTextFeatures.Underline,
        InlineStyle.Strike => RichTextFeatures.Strike,
        InlineStyle.Code => RichTextFeatures.InlineCode,
        _ => RichTextFeatures.None,
    };

    /// <summary>The feature a block kind needs; paragraphs and raw blocks need none.</summary>
    public static RichTextFeatures FeatureOf(BlockKind kind) => kind switch
    {
        BlockKind.Heading => RichTextFeatures.Headings,
        BlockKind.BulletItem or BlockKind.NumberedItem => RichTextFeatures.Lists,
        BlockKind.TaskItem => RichTextFeatures.TaskLists,
        BlockKind.Quote => RichTextFeatures.Quotes,
        BlockKind.CodeBlock => RichTextFeatures.CodeBlocks,
        BlockKind.Rule => RichTextFeatures.Rules,
        _ => RichTextFeatures.None,
    };

    /// <summary><paramref name="marks"/> without whatever <paramref name="features"/> does not allow.</summary>
    public static RichMarks Restrict(this RichTextFeatures features, RichMarks marks)
    {
        var style = marks.Style;
        foreach (var s in new[] { InlineStyle.Bold, InlineStyle.Italic, InlineStyle.Underline, InlineStyle.Strike, InlineStyle.Code })
            if (!features.Allows(FeatureOf(s))) style &= ~s;
        return new RichMarks(
            style,
            features.Allows(RichTextFeatures.Color) ? marks.Color : null,
            features.Allows(RichTextFeatures.Highlight) ? marks.Highlight : null,
            features.Allows(RichTextFeatures.Size) ? marks.Size : null,
            features.Allows(RichTextFeatures.Family) ? marks.Family : null,
            features.Allows(RichTextFeatures.Links) ? marks.Link : null);
    }

    /// <summary><paramref name="block"/> made allowable: a kind it does not allow becomes a
    /// paragraph (a rule disappears into an empty one), and its marks lose what it does not
    /// allow.</summary>
    public static RichBlock Restrict(this RichTextFeatures features, RichBlock block)
    {
        var b = block;
        if (!features.Allows(FeatureOf(b.Kind)))
            b = b with
            {
                Kind = b.Kind == BlockKind.TaskItem && features.Allows(RichTextFeatures.Lists)
                    ? BlockKind.BulletItem : BlockKind.Paragraph,
                HeadingLevel = 0, Checked = false, Info = null,
            };
        if (!features.Allows(RichTextFeatures.Alignment) && b.Align != BlockAlign.Start)
            b = b with { Align = BlockAlign.Start };
        if (b.Spans.Length > 0)
        {
            var spans = b.Spans.Select(s => s with { Marks = features.Restrict(s.Marks) });
            b = b.WithContent(b.Text, spans);
        }
        return b;
    }
}
