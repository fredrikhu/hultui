using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

/// <summary>
/// The drawing surface handed to a CanvasElement's callback. One reusable instance per
/// render context, drawing through the context's pooled Skia objects — a scrolling
/// commit graph is dozens of canvases × several draws every frame, and this path used
/// to allocate an SKPaint (and worse) per draw call while the main renderer had been
/// pooling for ages. Safe because the callback runs inside the owner's render pass:
/// the owner is not mid-draw, every pooled object is reset before each use, and the
/// context is only valid for the duration of the callback it was handed to.
/// </summary>
internal sealed class SkiaCanvasDrawingContext : ICanvasDrawingContext
{
    private readonly SkiaRenderContext _owner;

    public Rect Bounds { get; internal set; }

    internal SkiaCanvasDrawingContext(SkiaRenderContext owner) => _owner = owner;

    private SKCanvas Canvas => _owner.Canvas;
    private SKPaint Paint => _owner.PooledPaint;

    private SKPaint Fill(Color color)
    {
        var paint = Paint;
        paint.Reset();
        paint.Color = ToSkColor(color);
        paint.IsAntialias = true;
        return paint;
    }

    private SKPaint Stroke(Color color, float strokeWidth)
    {
        var paint = Fill(color);
        paint.IsStroke = true;
        paint.StrokeWidth = strokeWidth;
        return paint;
    }

    public void DrawRect(Rect rect, Color fill)
    {
        if (fill.A == 0) return;
        // The one draw that was never antialiased: axis-aligned fills need no AA and
        // keeping it off preserves the exact pixels this always produced.
        var paint = Paint;
        paint.Reset();
        paint.Color = ToSkColor(fill);
        Canvas.DrawRect(ToAbsSkRect(rect), paint);
    }

    public void DrawLine(Point from, Point to, Color color, float strokeWidth)
    {
        if (color.A == 0) return;
        Canvas.DrawLine(
            Bounds.X + from.X, Bounds.Y + from.Y,
            Bounds.X + to.X, Bounds.Y + to.Y,
            Stroke(color, strokeWidth));
    }

    public void DrawBezier(Point from, Point to, Color color, float strokeWidth)
    {
        if (color.A == 0) return;
        var path = _owner.PooledPath;
        path.Rewind();
        path.MoveTo(Bounds.X + from.X, Bounds.Y + from.Y);
        path.CubicTo(
            Bounds.X + from.X, float.Lerp(Bounds.Y + from.Y, Bounds.Y + to.Y, 0.5f),
            Bounds.X + to.X, float.Lerp(Bounds.Y + from.Y, Bounds.Y + to.Y, 0.5f),
            Bounds.X + to.X, Bounds.Y + to.Y);
        Canvas.DrawPath(path, Stroke(color, strokeWidth));
    }

    public void DrawCircle(Point center, float radius, Color fill)
    {
        if (fill.A == 0) return;
        Canvas.DrawCircle(Bounds.X + center.X, Bounds.Y + center.Y, radius, Fill(fill));
    }

    public void DrawCircleStroke(Point center, float radius, Color color, float strokeWidth)
    {
        if (color.A == 0) return;
        Canvas.DrawCircle(Bounds.X + center.X, Bounds.Y + center.Y, radius, Stroke(color, strokeWidth));
    }

    public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill)
    {
        if (fill.A == 0) return;
        Canvas.DrawRoundRect(_owner.ToSkRoundRect(ToAbsSkRect(rect), radius), Fill(fill));
    }

    public void DrawRoundedRectStroke(Rect rect, CornerRadius radius, Color color, float strokeWidth)
    {
        if (color.A == 0) return;
        Canvas.DrawRoundRect(_owner.ToSkRoundRect(ToAbsSkRect(rect), radius), Stroke(color, strokeWidth));
    }

    public void DrawText(string text, Point position, Color color, float fontSize)
    {
        if (string.IsNullOrEmpty(text) || color.A == 0) return;
        var font = _owner.PooledCanvasFont;
        font.Size = fontSize;
        if (_owner.CanvasTypeface is { } pinned) font.Typeface = pinned;
        float x = Bounds.X + position.X, y = Bounds.Y + position.Y + fontSize;
        if (_owner.TextAsOutlines)
        {
            // As the text renderer does under TextAsOutlines: the same pixels on every OS.
            using var path = font.GetTextPath(text, new SKPoint(x, y));
            Canvas.DrawPath(path, Fill(color));
            return;
        }
        Canvas.DrawText(text, x, y, font, Fill(color));
    }

    // Coordinates in ICanvasDrawingContext are relative to Bounds
    private SKRect ToAbsSkRect(Rect r) =>
        new(Bounds.X + r.X, Bounds.Y + r.Y, Bounds.X + r.X + r.Width, Bounds.Y + r.Y + r.Height);

    private static SKColor ToSkColor(Color c) => new(c.R, c.G, c.B, c.A);
}
