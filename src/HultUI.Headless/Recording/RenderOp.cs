using System.Globalization;
using System.Text;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Headless.Recording;

/// <summary>
/// One recorded drawing call: the operation name, its formatted arguments, and
/// the clip/transform nesting depth it was issued at.
/// </summary>
/// <remarks>
/// Arguments are stored pre-formatted. That keeps the log cheap and diff-friendly;
/// assertions that need structured values can filter by <see cref="Name"/> and
/// parse, or the record can grow a typed payload later.
/// </remarks>
public sealed record RenderOp(string Name, string Args, int Depth)
{
    /// <summary>The op as one indented log line.</summary>
    public override string ToString()
        => Args.Length == 0
            ? new string(' ', Depth * 2) + Name
            : new string(' ', Depth * 2) + Name + " " + Args;
}

/// <summary>
/// Culture-invariant, rounded formatting for the values that appear in a render
/// log. Rounding to two decimals absorbs the last-bit float noise that layout
/// arithmetic produces without hiding real geometry changes.
/// </summary>
/// <remarks>
/// Helpers are named so they never shadow a type or namespace in scope — these
/// are consumed through <c>using static</c>, where a method called <c>Text</c>
/// or <c>Thickness</c> collides with <c>HultUI.Text</c> and
/// <c>Primitives.Thickness</c>.
/// </remarks>
internal static class OpFormat
{
    public static string F(float value)
    {
        var rounded = MathF.Round(value, 2);
        // Negative zero formats as "-0" and would diff against a "0" that came
        // out of otherwise identical arithmetic — collapse the two.
        if (rounded == 0f) rounded = 0f;
        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string Fmt(Rect r)
        => $"({F(r.X)},{F(r.Y)} {F(r.Width)}x{F(r.Height)})";

    public static string Fmt(Point p) => $"({F(p.X)},{F(p.Y)})";

    public static string Fmt(Color c)
        => c.A == 255
            ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

    public static string Radius(CornerRadius r)
        => r.IsZero ? "" :
           r.IsUniform ? $" r={F(r.TopLeft)}"
                       : $" r=({F(r.TopLeft)},{F(r.TopRight)},{F(r.BottomRight)},{F(r.BottomLeft)})";

    public static string Fmt(Thickness t)
        => t.Top == t.Right && t.Right == t.Bottom && t.Bottom == t.Left
            ? F(t.Top)
            : $"({F(t.Top)},{F(t.Right)},{F(t.Bottom)},{F(t.Left)})";

    /// <summary>
    /// Text style, reduced to the properties that affect what is drawn. Font
    /// family is included because it changes glyphs; defaults (regular weight,
    /// upright, no decoration, ltr) are omitted to keep lines short.
    /// </summary>
    public static string Style(ResolvedInheritedStyle s)
    {
        var sb = new StringBuilder();
        sb.Append("size=").Append(F(s.FontSize));
        sb.Append(" font=").Append(s.FontFamily);
        if (s.FontWeight != FontWeight.Regular) sb.Append(" weight=").Append(s.FontWeight);
        if (s.FontSlant != FontSlant.Normal) sb.Append(' ').Append(s.FontSlant);
        if (s.Decoration != TextDecoration.None) sb.Append(' ').Append(s.Decoration);
        if (s.TextAlign != TextAlign.Left) sb.Append(" align=").Append(s.TextAlign);
        if (s.TextDirection != TextDirection.Ltr) sb.Append(' ').Append(s.TextDirection);
        if (s.LineHeight != 0f) sb.Append(" lineHeight=").Append(F(s.LineHeight));
        if (s.LetterSpacing != 0f) sb.Append(" letterSpacing=").Append(F(s.LetterSpacing));
        sb.Append(" color=").Append(Fmt(s.TextColor));
        return sb.ToString();
    }

    /// <summary>Quotes and escapes drawn text so newlines can't break the one-op-per-line format.</summary>
    public static string Quoted(string text)
        => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"")
                      .Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
}
