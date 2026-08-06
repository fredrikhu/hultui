using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

public interface IRenderContext
{
    void DrawRect(Rect rect, Color fill);
    void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth);

    /// <summary>
    /// Draws a drop shadow behind a (possibly rounded) box. Default is a no-op so
    /// non-visual render contexts (test doubles) need not implement it.
    /// </summary>
    void DrawBoxShadow(Rect box, CornerRadius radius, BoxShadow shadow) { }

    /// <summary>
    /// Fills a (possibly rounded) rect with a linear or radial gradient. Default is
    /// a no-op so non-visual render contexts need not implement it.
    /// </summary>
    void DrawGradientRect(Rect rect, CornerRadius radius, Gradient gradient) { }

    /// <summary>
    /// Draws a border with per-side widths and/or a dashed/dotted style. The simple
    /// uniform-solid case is handled by <see cref="DrawRoundedRect"/>; this covers
    /// the rest. Default is a no-op so non-visual render contexts need not implement it.
    /// </summary>
    void DrawBorder(Rect box, CornerRadius radius, Color color, Thickness widths, BorderStyle style) { }
    void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines);

    /// <summary>
    /// Draws text like <see cref="DrawText"/>, but colors the character ranges in
    /// <paramref name="spans"/> individually (uncovered ranges use the style's
    /// text color). Layout, wrapping, and truncation are identical to the plain
    /// overload. A null or empty <paramref name="spans"/> renders exactly like the
    /// plain overload. Default ignores the spans so non-visual render contexts need
    /// not implement it.
    /// </summary>
    void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines,
        IReadOnlyList<TextSpan>? spans)
        => DrawText(text, bounds, style, overflow, maxLines);

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

    /// <summary>
    /// Concatenates a 2D affine transform onto the current matrix (paired with
    /// <see cref="Pop"/>). Default is a no-op so non-visual contexts, whose Pop is
    /// also a no-op, stay balanced.
    /// </summary>
    void PushTransform(System.Numerics.Matrix3x2 matrix) { }

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
