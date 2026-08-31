using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// The bright line along the bottom of a text field, shared by
/// <see cref="TextInputElement"/> and <see cref="TextAreaElement"/> — the two controls that read
/// <see cref="TextInputTheme"/> and would otherwise grow one copy of this each.
/// </summary>
internal static class TextFieldUnderline
{
    /// <summary>
    /// Draws the underline inside <paramref name="box"/>, following its rounded corners.
    ///
    /// <para>A clipped stroke rather than a filled bar: the renderer draws a single-sided border
    /// as a plain rectangle, ignoring the corner radius, so a bottom-only border on a rounded
    /// field pokes out past both corners. Stroking the whole outline and clipping to the bottom
    /// band keeps the ends curving inward with the box, which is what the real control does.</para>
    /// </summary>
    public static void Draw(IRenderContext ctx, Rect box, CornerRadius radius, Color color, float thickness)
    {
        if (color.A == 0 || thickness <= 0f) return;

        // Inset by half the stroke so the whole line lands inside the box. A centred stroke on
        // the border box would hang half of itself outside, and the underline is the one edge
        // where that is obvious — it would sit a pixel below everything it lines up with.
        float half = thickness / 2f;
        var path = new Rect(box.X + half, box.Y + half, box.Width - thickness, box.Height - thickness);
        float r = MathF.Max(0f, radius.BottomLeft - half);

        ctx.PushClip(new Rect(box.X, box.Bottom - thickness, box.Width, thickness));
        ctx.DrawRoundedRect(path, new CornerRadius(r), Color.Transparent, color, new Thickness(thickness));
        ctx.Pop();
    }
}
