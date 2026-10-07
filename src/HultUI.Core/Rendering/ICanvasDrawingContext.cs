using System.Numerics;
using HultUI.Core.Primitives;

namespace HultUI.Core.Rendering;

/// <summary>
/// The drawing surface a <see cref="Elements.CanvasElement"/>'s callback is handed.
/// Coordinates are relative to the element's content box (<see cref="Bounds"/>).
/// </summary>
/// <remarks>
/// The members after <see cref="DrawText"/> have default implementations — no-ops, or the
/// nearest simpler call — so a context that only records or measures need not implement
/// them. The renderer implements all of them.
/// </remarks>
public interface ICanvasDrawingContext
{
    Rect Bounds { get; }
    void DrawRect(Rect rect, Color fill);
    void DrawLine(Point from, Point to, Color color, float strokeWidth);
    void DrawBezier(Point from, Point to, Color color, float strokeWidth);
    void DrawCircle(Point center, float radius, Color fill);
    void DrawCircleStroke(Point center, float radius, Color color, float strokeWidth);
    void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill);
    void DrawRoundedRectStroke(Rect rect, CornerRadius radius, Color color, float strokeWidth);
    void DrawText(string text, Point position, Color color, float fontSize);

    /// <summary>
    /// Draws <paramref name="sourceRect"/> of <paramref name="source"/> (in the image's own
    /// pixels) over <paramref name="destRect"/>, resampled as <paramref name="sampling"/> says —
    /// see <see cref="IRenderContext.DrawImage(IImageSource, Rect, Rect, ImageSampling)"/>. What
    /// a custom view of a picture is made of: an image viewer's zoom and pan are a source and a
    /// destination rectangle, its rotation a <see cref="PushTransform"/> around the call.
    /// </summary>
    void DrawImage(IImageSource source, Rect destRect, Rect sourceRect, ImageSampling sampling = ImageSampling.Default) { }

    /// <summary>
    /// Draws <paramref name="text"/> as <see cref="DrawText"/> does, over a halo: the glyphs'
    /// outline stroked <paramref name="haloWidth"/> wide in <paramref name="halo"/> first, so the
    /// text stays legible on any background — what labels drawn over a photograph or a dark
    /// image need. The default draws the text in the halo colour at eight offsets around it,
    /// then the text; the renderer strokes the real glyph outlines.
    /// </summary>
    void DrawText(string text, Point position, Color color, float fontSize, Color halo, float haloWidth)
    {
        if (halo.A > 0 && haloWidth > 0)
        {
            float d = haloWidth / 2f;
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                DrawText(text, new Point(position.X + (float)(Math.Cos(a) * d), position.Y + (float)(Math.Sin(a) * d)), halo, fontSize);
            }
        }
        DrawText(text, position, color, fontSize);
    }

    /// <summary>
    /// The advance width of <paramref name="text"/> as <see cref="DrawText"/> would draw it at
    /// <paramref name="fontSize"/>, in the canvas's units: for placing labels beside things
    /// drawn on the canvas (and keeping them apart). The default asks the window's measure
    /// context (<see cref="TextMeasure"/>) for the default sans-serif face; the renderer measures
    /// with the very font it draws with. 0 when nothing can measure.
    /// </summary>
    float MeasureText(string text, float fontSize) => TextMeasure.MeasureWidth(text, fontSize);

    /// <summary>Fills the closed polygon through <paramref name="points"/>, antialiased.</summary>
    void FillPolygon(ReadOnlySpan<Point> points, Color fill) { }

    /// <summary>Strokes the open line through <paramref name="points"/>, antialiased, round joins.</summary>
    void DrawPolyline(ReadOnlySpan<Point> points, Color color, float strokeWidth) { }

    /// <summary>
    /// Clips everything drawn until the matching <see cref="Pop"/> to <paramref name="rect"/>.
    /// A canvas is not clipped to its own bounds unless it asks.
    /// </summary>
    void PushClip(Rect rect) { }

    /// <summary>
    /// Concatenates <paramref name="matrix"/> onto the drawing transform until the matching
    /// <see cref="Pop"/>. The matrix works in this context's own coordinates (origin at the
    /// top-left of <see cref="Bounds"/>), so a rotation about the middle of the canvas is
    /// <c>Matrix3x2.CreateRotation(angle, new Vector2(Bounds.Width / 2, Bounds.Height / 2))</c>.
    /// </summary>
    void PushTransform(Matrix3x2 matrix) { }

    /// <summary>Undoes the most recent <see cref="PushClip"/> or <see cref="PushTransform"/>.</summary>
    void Pop() { }

    /// <summary>
    /// Undoes every push the callback left open. The canvas element calls it after the
    /// callback returns, so a callback that forgets a <see cref="Pop"/> cannot leak its clip
    /// or transform into the elements drawn after it.
    /// </summary>
    void PopAll() { }
}
