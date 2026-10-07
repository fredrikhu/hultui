using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using static HultUI.Headless.Recording.OpFormat;

namespace HultUI.Headless.Recording;

/// <summary>
/// Records the immediate-mode drawing an element does inside a canvas element,
/// into the same log as the retained-mode ops so custom-drawn content is
/// covered by snapshots too. Canvas ops are prefixed <c>Canvas.</c>.
/// </summary>
internal sealed class RecordingCanvasContext : ICanvasDrawingContext
{
    private readonly ICanvasDrawingContext _inner;
    private readonly RenderLog _log;
    private readonly int _depth;

    public RecordingCanvasContext(ICanvasDrawingContext inner, RenderLog log, int depth)
    {
        _inner = inner;
        _log = log;
        _depth = depth;
    }

    public Rect Bounds => _inner.Bounds;

    private void Record(string name, string args) => _log.Add(new RenderOp("Canvas." + name, args, _depth));

    public void DrawRect(Rect rect, Color fill)
    {
        Record("DrawRect", $"{Fmt(rect)} {Fmt(fill)}");
        _inner.DrawRect(rect, fill);
    }

    public void DrawLine(Point from, Point to, Color color, float strokeWidth)
    {
        Record("DrawLine", $"{Fmt(from)}->{Fmt(to)} {Fmt(color)} width={F(strokeWidth)}");
        _inner.DrawLine(from, to, color, strokeWidth);
    }

    public void DrawBezier(Point from, Point to, Color color, float strokeWidth)
    {
        Record("DrawBezier", $"{Fmt(from)}->{Fmt(to)} {Fmt(color)} width={F(strokeWidth)}");
        _inner.DrawBezier(from, to, color, strokeWidth);
    }

    public void DrawCircle(Point center, float radius, Color fill)
    {
        Record("DrawCircle", $"{Fmt(center)} r={F(radius)} {Fmt(fill)}");
        _inner.DrawCircle(center, radius, fill);
    }

    public void DrawCircleStroke(Point center, float radius, Color color, float strokeWidth)
    {
        Record("DrawCircleStroke", $"{Fmt(center)} r={F(radius)} {Fmt(color)} width={F(strokeWidth)}");
        _inner.DrawCircleStroke(center, radius, color, strokeWidth);
    }

    public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill)
    {
        Record("DrawRoundedRect", $"{Fmt(rect)}{Radius(radius)} {Fmt(fill)}");
        _inner.DrawRoundedRect(rect, radius, fill);
    }

    public void DrawRoundedRectStroke(Rect rect, CornerRadius radius, Color color, float strokeWidth)
    {
        Record("DrawRoundedRectStroke", $"{Fmt(rect)}{Radius(radius)} {Fmt(color)} width={F(strokeWidth)}");
        _inner.DrawRoundedRectStroke(rect, radius, color, strokeWidth);
    }

    public void DrawText(string text, Point position, Color color, float fontSize)
    {
        Record("DrawText", $"{Quoted(text)} {Fmt(position)} size={F(fontSize)} {Fmt(color)}");
        _inner.DrawText(text, position, color, fontSize);
    }

    public void DrawText(string text, Point position, Color color, float fontSize, Color halo, float haloWidth)
    {
        Record("DrawText", $"{Quoted(text)} {Fmt(position)} size={F(fontSize)} {Fmt(color)} halo={Fmt(halo)} width={F(haloWidth)}");
        _inner.DrawText(text, position, color, fontSize, halo, haloWidth);
    }

    // Not recorded: measuring draws nothing.
    public float MeasureText(string text, float fontSize) => _inner.MeasureText(text, fontSize);

    public void DrawImage(IImageSource source, Rect destRect, Rect sourceRect, ImageSampling sampling = ImageSampling.Default)
    {
        Record("DrawImage",
            $"{Fmt(destRect)} source={Fmt(sourceRect)} sampling={sampling} intrinsic={F(source.IntrinsicSize.Width)}x{F(source.IntrinsicSize.Height)}"
            + (source is IPixelImageSource frames ? $" version={frames.Version}" : "")
            + (source.IsLoaded ? "" : " (not loaded)"));
        _inner.DrawImage(source, destRect, sourceRect, sampling);
    }

    public void FillPolygon(ReadOnlySpan<Point> points, Color fill)
    {
        Record("FillPolygon", $"{Points(points)} {Fmt(fill)}");
        _inner.FillPolygon(points, fill);
    }

    public void DrawPolyline(ReadOnlySpan<Point> points, Color color, float strokeWidth)
    {
        Record("DrawPolyline", $"{Points(points)} {Fmt(color)} width={F(strokeWidth)}");
        _inner.DrawPolyline(points, color, strokeWidth);
    }

    public void PushClip(Rect rect)
    {
        Record("PushClip", Fmt(rect));
        _inner.PushClip(rect);
    }

    public void PushTransform(System.Numerics.Matrix3x2 m)
    {
        Record("PushTransform", $"[{F(m.M11)},{F(m.M12)},{F(m.M21)},{F(m.M22)},{F(m.M31)},{F(m.M32)}]");
        _inner.PushTransform(m);
    }

    public void Pop()
    {
        Record("Pop", "");
        _inner.Pop();
    }

    // Not recorded: it runs after every canvas callback, and a balanced callback (the
    // usual case) gets nothing from it — logging it would add a line to every canvas.
    public void PopAll() => _inner.PopAll();

    private static string Points(ReadOnlySpan<Point> points)
    {
        var parts = new string[points.Length];
        for (int i = 0; i < points.Length; i++) parts[i] = Fmt(points[i]);
        return string.Join(" ", parts);
    }
}
