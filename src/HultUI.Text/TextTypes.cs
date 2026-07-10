using HultUI.Core.Primitives;

namespace HultUI.Text;

/// <summary>
/// Result of text layout: a list of positioned lines.
/// </summary>
public sealed class TextLayout
{
    public static readonly TextLayout Empty = new([], Size.Zero);

    public IReadOnlyList<TextLine> Lines { get; }
    public Size Size { get; }

    public TextLayout(IReadOnlyList<TextLine> lines, Size size)
    {
        Lines = lines;
        Size = size;
    }
}

/// <summary>
/// A single line of text, containing one or more shaped runs.
/// </summary>
public sealed class TextLine
{
    public IReadOnlyList<ShapedRun> Runs { get; }
    public float Width { get; }
    public float Ascent { get; }
    public float Descent { get; }
    public float Leading { get; }
    public float Height => Ascent + Descent + Leading;
    public float OffsetY { get; internal set; }

    /// <summary>Index of this line's first character in the original string.</summary>
    public int Start { get; internal set; }

    /// <summary>
    /// Number of characters of the original string this line spans, including any
    /// trailing whitespace consumed at the wrap point (but not an explicit
    /// newline, which sits between lines). Used to map selection offsets and
    /// pointer positions back to the source text.
    /// </summary>
    public int Length { get; internal set; }

    public TextLine(IReadOnlyList<ShapedRun> runs, float width, float ascent, float descent, float leading)
    {
        Runs = runs;
        Width = width;
        Ascent = ascent;
        Descent = descent;
        Leading = leading;
    }
}

/// <summary>
/// A run of shaped text — a contiguous segment rendered with a single font.
/// </summary>
public sealed class ShapedRun
{
    public string Text { get; }
    public IResolvedFont Font { get; }
    public float FontSize { get; }
    public float Width { get; }
    public float OffsetX { get; internal set; }

    /// <summary>Glyph positions relative to the run's origin, for drawing.</summary>
    public GlyphPosition[] Glyphs { get; }

    public ShapedRun(string text, IResolvedFont font, float fontSize, float width, GlyphPosition[] glyphs)
    {
        Text = text;
        Font = font;
        FontSize = fontSize;
        Width = width;
        Glyphs = glyphs;
    }
}

/// <summary>
/// A positioned glyph within a shaped run.
/// </summary>
public readonly record struct GlyphPosition(uint GlyphId, float X, float Y, float Advance);
