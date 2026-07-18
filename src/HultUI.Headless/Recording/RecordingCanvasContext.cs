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
}
