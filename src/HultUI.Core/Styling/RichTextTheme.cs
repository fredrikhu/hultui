using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// How a <see cref="Elements.RichTextEditorElement"/> draws a document: heading sizes, the code
/// face and its background, quotes, links, lists and the space between blocks. The field around
/// the document — border, padding, focus — is the text field's (<see cref="TextInputTheme"/>).
/// Colours left null come from the palette, so a scheme change restyles every document without
/// rewriting one: marks say what text is, this says how it looks.
/// </summary>
public sealed class RichTextTheme : IThemeExtension<RichTextTheme>
{
    /// <summary>Heading sizes as multiples of the body size, levels 1 to 6.</summary>
    public IReadOnlyList<float> HeadingScale { get; init; } = [1.75f, 1.45f, 1.25f, 1.1f, 1f, 0.9f];
    public FontWeight HeadingWeight { get; init; } = FontWeight.SemiBold;
    /// <summary>Space above a heading, as a multiple of its own size; none for the first block.</summary>
    public float HeadingSpaceAbove { get; init; } = 0.6f;

    /// <summary>Space after a paragraph, a quote or a code block.</summary>
    public float BlockSpacing { get; init; } = 8f;
    /// <summary>Space between two list items.</summary>
    public float ListItemSpacing { get; init; } = 3f;
    /// <summary>One level of nesting: a list marker's column, a nested quote's step.</summary>
    public float IndentWidth { get; init; } = 24f;

    public string CodeFamily { get; init; } = "monospace";
    /// <summary>Code's size as a multiple of the text around it.</summary>
    public float CodeScale { get; init; } = 0.92f;
    /// <summary>Behind inline code and code blocks. Null takes a faint tint of the text colour,
    /// which shows on any field background.</summary>
    public Color? CodeBackground { get; init; }
    public Thickness CodeBlockPadding { get; init; } = new(8, 10, 8, 10);
    public float CodeBlockCornerRadius { get; init; } = 4f;

    /// <summary>Null takes the palette's primary colour.</summary>
    public Color? LinkColor { get; init; }

    public float QuoteBarWidth { get; init; } = 3f;
    /// <summary>Between a quote's bar and its text.</summary>
    public float QuoteGap { get; init; } = 10f;
    /// <summary>Null takes a tint of the text colour.</summary>
    public Color? QuoteBarColor { get; init; }
    /// <summary>Null takes the palette's secondary colour.</summary>
    public Color? QuoteTextColor { get; init; }

    /// <summary>A bullet's diameter, as a multiple of the body size.</summary>
    public float BulletScale { get; init; } = 0.34f;
    /// <summary>A task's check box, as a multiple of the body size.</summary>
    public float CheckboxScale { get; init; } = 1f;
    /// <summary>A ticked box's fill. Null takes the palette's primary colour.</summary>
    public Color? CheckedColor { get; init; }
    /// <summary>A done task's text. Null takes the palette's secondary colour.</summary>
    public Color? CheckedTextColor { get; init; }
    /// <summary>Whether a done task's text is struck through.</summary>
    public bool StrikeCheckedText { get; init; } = true;

    /// <summary>Null takes a tint of the text colour.</summary>
    public Color? RuleColor { get; init; }
    public float RuleThickness { get; init; } = 1f;
    /// <summary>The height a rule takes, with its line through the middle.</summary>
    public float RuleHeight { get; init; } = 17f;

    /// <summary>Source the editor keeps verbatim (HTML, a picture inside a line). Null takes the
    /// palette's secondary colour.</summary>
    public Color? RawTextColor { get; init; }

    /// <summary>Behind a find bar's matches, and behind the current one. Null takes amber, the
    /// colour find marks text with almost everywhere.</summary>
    public Color? FindHitColor { get; init; }
    public Color? FindCurrentColor { get; init; }

    /// <summary>The tallest a picture is drawn; a taller one is scaled down, narrowing with it.</summary>
    public float ImageMaxHeight { get; init; } = 480f;
    /// <summary>Around a selected picture.</summary>
    public float ImageRingWidth { get; init; } = 2f;
    /// <summary>Null takes the palette's primary colour: a field's own focus border can be a
    /// grey that does not show against a picture.</summary>
    public Color? ImageRingColor { get; init; }

    /// <summary>Inside a table cell, around its text.</summary>
    public Thickness TableCellPadding { get; init; } = new(5, 10, 5, 10);
    /// <summary>The table's grid. Null takes a tint of the text colour.</summary>
    public Color? TableBorderColor { get; init; }
    public float TableBorderWidth { get; init; } = 1f;
    /// <summary>Behind the header row. Null takes a faint tint of the text colour.</summary>
    public Color? TableHeaderBackground { get; init; }
    public FontWeight TableHeaderWeight { get; init; } = FontWeight.SemiBold;
    /// <summary>The narrowest and widest a column's share of the width is weighted, from its
    /// widest cell on one line: short columns stay legible, one long cell does not starve the rest.</summary>
    public float TableColumnMinWeight { get; init; } = 48f;
    public float TableColumnMaxWeight { get; init; } = 360f;

    public static RichTextTheme CreateDefault(Theme theme) => new();

    /// <summary>The scale for heading <paramref name="level"/>, clamped to the list's ends.</summary>
    public float HeadingScaleOf(int level) =>
        HeadingScale.Count == 0 ? 1f : HeadingScale[Math.Clamp(level - 1, 0, HeadingScale.Count - 1)];
}
