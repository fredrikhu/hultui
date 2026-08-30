using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// The little downward mark on the closed face of a dropdown, a date picker and a time picker.
///
/// <para>Shared rather than copied because the three controls deliberately look identical when
/// closed — a form with three differently-shaped boxes in a row is the thing
/// <see cref="DropdownTheme"/> exists to prevent — and a chevron drawn two ways in two files
/// drifts apart the first time one of them is touched.</para>
/// </summary>
internal static class ChevronPainter
{
    /// <summary>The glyph the triangle shape draws. Text, so it inherits the control's font
    /// and picks up whatever that font's designer decided a small solid triangle looks like.</summary>
    internal const string Glyph = "▾";

    /// <summary>
    /// How wide to reserve. The stroked chevron is drawn rather than typed, so its width comes
    /// off the font size instead of off a glyph: 0.62em is what puts it at the same optical
    /// weight as the surrounding text at every size.
    /// </summary>
    internal static float Width(ChevronShape shape, float fontSize, IMeasureContext ctx, ResolvedInheritedStyle style)
        => shape == ChevronShape.Stroke
            ? MathF.Round(fontSize * 0.62f)
            : ctx.MeasureText(Glyph, style, float.MaxValue, 1).Width;

    /// <summary>Same width, for the render pass, which has a different context type.</summary>
    internal static float Width(ChevronShape shape, float fontSize, IRenderContext ctx, ResolvedInheritedStyle style)
        => shape == ChevronShape.Stroke
            ? MathF.Round(fontSize * 0.62f)
            : ctx.MeasureText(Glyph, style).Width;

    /// <summary>
    /// Draws the chevron centred in <paramref name="area"/>, which is the full-height strip
    /// reserved for it at the right-hand end of the control.
    /// </summary>
    internal static void Draw(
        IRenderContext ctx, ChevronShape shape, Rect area, Color color, float strokeWidth,
        ResolvedInheritedStyle style, float fontSize)
    {
        if (shape == ChevronShape.Triangle)
        {
            var size = ctx.MeasureText(Glyph, style);
            var rect = new Rect(
                area.X + (area.Width - size.Width) / 2f,
                area.Y + (area.Height - size.Height) / 2f,
                size.Width,
                size.Height);
            ctx.DrawText(Glyph, rect, style, TextOverflow.Clip, 1);
            return;
        }

        // Two strokes meeting at a point, the proportions Fluent and most other current design
        // systems use: as wide as it is tall twice over, and slightly above centre, because a
        // chevron centred on its bounding box reads as sitting low next to a line of text.
        float w = MathF.Round(fontSize * 0.62f);
        float h = w / 2f;
        float cx = area.X + area.Width / 2f;
        float cy = area.Y + area.Height / 2f;
        float top = cy - h / 2f;
        float bottom = cy + h / 2f;

        ctx.DrawLine(new Point(cx - w / 2f, top), new Point(cx, bottom), color, strokeWidth);
        ctx.DrawLine(new Point(cx, bottom), new Point(cx + w / 2f, top), color, strokeWidth);
    }
}
