using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

internal sealed class SkiaCanvasDrawingContext : ICanvasDrawingContext
{
    private readonly SKCanvas _canvas;

    public Rect Bounds { get; }

    internal SkiaCanvasDrawingContext(SKCanvas canvas, Rect bounds)
    {
        _canvas = canvas;
        Bounds = bounds;
    }

    public void DrawRect(Rect rect, Color fill)
    {
        if (fill.A == 0) return;
        using var paint = new SKPaint { Color = ToSkColor(fill) };
        _canvas.DrawRect(ToAbsSkRect(rect), paint);
    }

    public void DrawLine(Point from, Point to, Color color, float strokeWidth)
    {
        if (color.A == 0) return;
        using var paint = new SKPaint
        {
            Color = ToSkColor(color),
            IsStroke = true,
            StrokeWidth = strokeWidth,
            IsAntialias = true
        };
        _canvas.DrawLine(
            Bounds.X + from.X, Bounds.Y + from.Y,
            Bounds.X + to.X, Bounds.Y + to.Y,
            paint);
    }

    public void DrawBezier(Point from, Point to, Color color, float strokeWidth)
    {
        if (color.A == 0) return;
        using var paint = new SKPaint
        {
            Color = ToSkColor(color),
            IsStroke = true,
            StrokeWidth = strokeWidth,
            IsAntialias = true
        };
        using var path = new SKPath();
        path.MoveTo(Bounds.X + from.X, Bounds.Y + from.Y);
        path.CubicTo(
            Bounds.X + from.X, float.Lerp(Bounds.Y + from.Y, Bounds.Y + to.Y, 0.5f),
            Bounds.X + to.X, float.Lerp(Bounds.Y + from.Y, Bounds.Y + to.Y, 0.5f),
            Bounds.X + to.X, Bounds.Y + to.Y);
        _canvas.DrawPath(path, paint);
    }

    public void DrawCircle(Point center, float radius, Color fill)
    {
        if (fill.A == 0) return;
        using var paint = new SKPaint { Color = ToSkColor(fill), IsAntialias = true };
        _canvas.DrawCircle(Bounds.X + center.X, Bounds.Y + center.Y, radius, paint);
    }

    public void DrawCircleStroke(Point center, float radius, Color color, float strokeWidth)
    {
        if (color.A == 0) return;
        using var paint = new SKPaint
        {
            Color = ToSkColor(color),
            IsStroke = true,
            StrokeWidth = strokeWidth,
            IsAntialias = true
        };
        _canvas.DrawCircle(Bounds.X + center.X, Bounds.Y + center.Y, radius, paint);
    }

    public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill)
    {
        if (fill.A == 0) return;
        using var paint = new SKPaint { Color = ToSkColor(fill), IsAntialias = true };
        var skRect = ToAbsSkRect(rect);
        using var rrect = new SKRoundRect();
        rrect.SetRectRadii(skRect,
        [
            new SKPoint(radius.TopLeft, radius.TopLeft),
            new SKPoint(radius.TopRight, radius.TopRight),
            new SKPoint(radius.BottomRight, radius.BottomRight),
            new SKPoint(radius.BottomLeft, radius.BottomLeft)
        ]);
        _canvas.DrawRoundRect(rrect, paint);
    }

    public void DrawRoundedRectStroke(Rect rect, CornerRadius radius, Color color, float strokeWidth)
    {
        if (color.A == 0) return;
        using var paint = new SKPaint
        {
            Color = ToSkColor(color),
            IsStroke = true,
            StrokeWidth = strokeWidth,
            IsAntialias = true
        };
        var skRect = ToAbsSkRect(rect);
        using var rrect = new SKRoundRect();
        rrect.SetRectRadii(skRect,
        [
            new SKPoint(radius.TopLeft, radius.TopLeft),
            new SKPoint(radius.TopRight, radius.TopRight),
            new SKPoint(radius.BottomRight, radius.BottomRight),
            new SKPoint(radius.BottomLeft, radius.BottomLeft)
        ]);
        _canvas.DrawRoundRect(rrect, paint);
    }

    public void DrawText(string text, Point position, Color color, float fontSize)
    {
        if (string.IsNullOrEmpty(text) || color.A == 0) return;
        using var paint = new SKPaint { Color = ToSkColor(color), IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, fontSize);
        _canvas.DrawText(text, Bounds.X + position.X, Bounds.Y + position.Y + fontSize, font, paint);
    }

    // Coordinates in ICanvasDrawingContext are relative to Bounds
    private SKRect ToAbsSkRect(Rect r) =>
        new(Bounds.X + r.X, Bounds.Y + r.Y, Bounds.X + r.X + r.Width, Bounds.Y + r.Y + r.Height);

    private static SKColor ToSkColor(Color c) => new(c.R, c.G, c.B, c.A);
}
