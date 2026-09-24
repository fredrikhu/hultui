using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// A render context that draws nothing, for a test that wants to watch one kind of call.
/// Override the two or three methods the test is about and ignore the rest — the interface
/// is wide, and a stub per test file is a stub to fix every time it grows.
/// </summary>
public class NullRenderContext : IRenderContext
{
    public virtual void DrawRect(Rect rect, Color fill) { }

    public virtual void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke,
        Thickness strokeWidth) { }

    public virtual void DrawText(string text, Rect bounds, ResolvedInheritedStyle style,
        TextOverflow overflow, int? maxLines) { }

    public virtual void DrawImage(IImageSource source, Rect destRect, ImageFit fit) { }

    public virtual void DrawImage(IImageSource source, Rect destRect, Rect sourceRect) { }

    public virtual void DrawLine(Point from, Point to, Color color, float strokeWidth) { }

    public virtual Size MeasureText(string text, ResolvedInheritedStyle style) =>
        new(text.Length * style.FontSize * 0.6f, style.FontSize);

    public virtual IReadOnlyList<TextLineBox> LayoutTextLines(string text, ResolvedInheritedStyle style,
        float maxWidth, int? maxLines) =>
        string.IsNullOrEmpty(text) ? [] : [new TextLineBox(0, text.Length, 0, style.FontSize, text.Length * style.FontSize * 0.6f)];

    public virtual void PushClip(Rect clipRect) { }
    public virtual void PushRoundedClip(Rect clipRect, CornerRadius radius) { }
    public virtual void PushTranslate(float x, float y) { }
    public virtual void PushOpacity(float opacity) { }
    public virtual void Pop() { }
    public virtual ICanvasDrawingContext CreateCanvasContext(Rect bounds) => throw new NotSupportedException();
    public virtual void BeginFrame(float width, float height, float scaleFactor, Color? clearColor = null) { }
    public virtual void EndFrame() { }
}
