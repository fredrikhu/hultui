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

    /// <summary>
    /// Lays out <paramref name="text"/> into lines under the same wrapping rules
    /// <see cref="DrawText"/> uses, and returns each line's source character range
    /// and vertical placement. Selectable text uses this to map pointer positions
    /// and selection offsets across wrapped lines. Returns an empty list for empty
    /// text.
    /// </summary>
    IReadOnlyList<TextLineBox> LayoutTextLines(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines);

    void PushClip(Rect clipRect);
    void PushRoundedClip(Rect clipRect, CornerRadius radius);
    void PushTranslate(float x, float y);
    void PushOpacity(float opacity);
    void Pop();

    ICanvasDrawingContext CreateCanvasContext(Rect bounds);

    void BeginFrame(float width, float height, float scaleFactor, Color? clearColor = null);
    void EndFrame();
}

/// <summary>
/// One laid-out line of text: its character range in the source string
/// (<see cref="Start"/>, <see cref="Length"/>), its vertical offset from the text
/// origin (<see cref="OffsetY"/>), and its <see cref="Height"/> and drawn
/// <see cref="Width"/>. Line-relative x positions are recovered by measuring
/// substrings of the source text with <see cref="IRenderContext.MeasureText"/>.
/// </summary>
public readonly record struct TextLineBox(int Start, int Length, float OffsetY, float Height, float Width);

public interface IImageSource
{
    Size IntrinsicSize { get; }
    bool IsLoaded { get; }
    string? AlternateText { get; }
}
