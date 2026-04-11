using HultUI.Core.Primitives;

namespace HultUI.Core.Rendering;

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
}
