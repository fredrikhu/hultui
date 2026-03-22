using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

public interface IRenderContext
{
    void DrawRect(Rect rect, Color fill);
    void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth);
    void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines);
    void DrawImage(IImageSource source, Rect destRect, ImageFit fit);
    void DrawLine(Point from, Point to, Color color, float strokeWidth);

    /// <summary>
    /// Measures the width and height of a text string using the same font metrics as DrawText.
    /// Used by elements like TextInput that need precise cursor/selection positioning.
    /// </summary>
    Size MeasureText(string text, ResolvedInheritedStyle style);

    void PushClip(Rect clipRect);
    void PushRoundedClip(Rect clipRect, CornerRadius radius);
    void PushTranslate(float x, float y);
    void PushOpacity(float opacity);
    void Pop();

    ICanvasDrawingContext CreateCanvasContext(Rect bounds);

    void BeginFrame(float width, float height, float scaleFactor, Color? clearColor = null);
    void EndFrame();
}

public interface IImageSource
{
    Size IntrinsicSize { get; }
    bool IsLoaded { get; }
    string? AlternateText { get; }
}
