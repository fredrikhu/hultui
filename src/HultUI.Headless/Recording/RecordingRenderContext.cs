using System.Numerics;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using static HultUI.Headless.Recording.OpFormat;

namespace HultUI.Headless.Recording;

/// <summary>
/// An <see cref="IRenderContext"/> decorator that records every drawing call
/// into a <see cref="RenderLog"/> and forwards it to an inner context.
/// </summary>
/// <remarks>
/// <para>
/// Recording the display list rather than the resulting pixels gives snapshot
/// tests that read as text: a diff says "this rect moved 4px and changed
/// colour" instead of handing you two images to flip between. Text baselines
/// also delta-compress in git, unlike PNGs.
/// </para>
/// <para>
/// Forwarding to an inner context means the frame still renders normally, so
/// recording can be enabled alongside pixel capture, and the measurement calls
/// (<see cref="MeasureText"/>, <see cref="LayoutTextLines"/>) return real
/// answers — they are queries, not drawing, and are deliberately not recorded.
/// </para>
/// </remarks>
public sealed class RecordingRenderContext : IRenderContext
{
    private readonly IRenderContext _inner;
    private int _depth;

    public RecordingRenderContext(IRenderContext inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>The recorded display list.</summary>
    public RenderLog Log { get; } = new();

    private void Record(string name, string args = "") => Log.Add(new RenderOp(name, args, _depth));

    // ──── Frame ────

    public void BeginFrame(float width, float height, float scaleFactor, Color? clearColor = null)
    {
        // A frame always starts at depth 0; if a previous frame leaked an
        // unbalanced push, resetting here keeps this frame's log readable
        // rather than propagating the indentation error forward.
        _depth = 0;
        Record("BeginFrame",
            $"{F(width)}x{F(height)} scale={F(scaleFactor)}"
            + (clearColor is { } c ? $" clear={Fmt(c)}" : ""));
        _depth = 1;
        _inner.BeginFrame(width, height, scaleFactor, clearColor);
    }

    public void EndFrame()
    {
        _depth = 0;
        Record("EndFrame");
        _inner.EndFrame();
    }

    // ──── Shapes ────

    public void DrawRect(Rect rect, Color fill)
    {
        Record("DrawRect", $"{Fmt(rect)} {Fmt(fill)}");
        _inner.DrawRect(rect, fill);
    }

    public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth)
    {
        var args = $"{Fmt(rect)}{Radius(radius)} fill={Fmt(fill)}";
        if (stroke.A != 0 && strokeWidth != Thickness.Zero)
            args += $" stroke={Fmt(stroke)} width={Fmt(strokeWidth)}";
        Record("DrawRoundedRect", args);
        _inner.DrawRoundedRect(rect, radius, fill, stroke, strokeWidth);
    }

    public void DrawBoxShadow(Rect box, CornerRadius radius, BoxShadow shadow)
    {
        Record("DrawBoxShadow",
            $"{Fmt(box)}{Radius(radius)} offset=({F(shadow.OffsetX)},{F(shadow.OffsetY)}) " +
            $"blur={F(shadow.Blur)} spread={F(shadow.Spread)} {Fmt(shadow.Color)}");
        _inner.DrawBoxShadow(box, radius, shadow);
    }

    public void DrawGradientRect(Rect rect, CornerRadius radius, Gradient gradient)
    {
        var stops = string.Join(", ",
            gradient.Stops.Select(s => $"{F(s.Offset)}:{Fmt(s.Color)}"));
        Record("DrawGradientRect",
            $"{Fmt(rect)}{Radius(radius)} {gradient.Kind} angle={F(gradient.AngleDegrees)} [{stops}]");
        _inner.DrawGradientRect(rect, radius, gradient);
    }

    public void DrawBorder(Rect box, CornerRadius radius, Color color, Thickness widths, BorderStyle style)
    {
        Record("DrawBorder",
            $"{Fmt(box)}{Radius(radius)} {Fmt(color)} width={Fmt(widths)} {style}");
        _inner.DrawBorder(box, radius, color, widths, style);
    }

    public void DrawLine(Point from, Point to, Color color, float strokeWidth)
    {
        Record("DrawLine", $"{Fmt(from)}->{Fmt(to)} {Fmt(color)} width={F(strokeWidth)}");
        _inner.DrawLine(from, to, color, strokeWidth);
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

    private static string Points(ReadOnlySpan<Point> points)
    {
        var parts = new string[points.Length];
        for (int i = 0; i < points.Length; i++) parts[i] = Fmt(points[i]);
        return "[" + string.Join(" ", parts) + "]";
    }

    public void DrawImage(IImageSource source, Rect destRect, ImageFit fit)
    {
        // Image identity isn't exposed through IImageSource, so record the
        // observable facts: intrinsic size, load state, and fit. A frame source
        // adds its version, which is how a test asserts that frame n was painted.
        Record("DrawImage",
            $"{Fmt(destRect)} {fit} intrinsic={F(source.IntrinsicSize.Width)}x{F(source.IntrinsicSize.Height)}"
            + (source is IPixelImageSource frames ? $" version={frames.Version}" : "")
            + (source is IGpuImageSource ? " gpu" : "")
            + (source.IsLoaded ? "" : " (not loaded)"));
        _inner.DrawImage(source, destRect, fit);
    }

    // The interface's default draws the whole image with Fill: without this the recorded call
    // lost its source rectangle, and so did the pixels drawn under recording.
    public void DrawImage(IImageSource source, Rect destRect, Rect sourceRect)
    {
        Record("DrawImage",
            $"{Fmt(destRect)} source={Fmt(sourceRect)} intrinsic={F(source.IntrinsicSize.Width)}x{F(source.IntrinsicSize.Height)}"
            + (source is IPixelImageSource frames ? $" version={frames.Version}" : "")
            + (source.IsLoaded ? "" : " (not loaded)"));
        _inner.DrawImage(source, destRect, sourceRect);
    }

    // ──── Text ────

    public void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines)
    {
        Record("DrawText", $"{Quoted(text)} {Fmt(bounds)} {Style(style)}{OverflowSuffix(overflow, maxLines)}");
        _inner.DrawText(text, bounds, style, overflow, maxLines);
    }

    public void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow,
        int? maxLines, IReadOnlyList<TextSpan>? spans)
    {
        if (spans is null || spans.Count == 0)   // no spans ⇒ record it as a plain DrawText
        {
            DrawText(text, bounds, style, overflow, maxLines);
            return;
        }
        var spanText = string.Join(", ", spans.Select(s =>
            $"{s.Start}+{s.Length}:{Fmt(s.Color)}" + (s.Background.A > 0 ? $"/bg:{Fmt(s.Background)}" : "")));
        Record("DrawText",
            $"{Quoted(text)} {Fmt(bounds)} {Style(style)}{OverflowSuffix(overflow, maxLines)} spans=[{spanText}]");
        _inner.DrawText(text, bounds, style, overflow, maxLines, spans);
    }

    private static string OverflowSuffix(TextOverflow overflow, int? maxLines)
    {
        var s = overflow == TextOverflow.Clip ? "" : $" overflow={overflow}";
        if (maxLines is { } n) s += $" maxLines={n}";
        return s;
    }

    // Queries, not drawing — forwarded unrecorded so the log stays a pure
    // display list. (Their results still show up, in the geometry they produce.)
    public Size MeasureText(string text, ResolvedInheritedStyle style)
        => _inner.MeasureText(text, style);

    public IReadOnlyList<TextLineBox> LayoutTextLines(string text, ResolvedInheritedStyle style,
        float maxWidth, int? maxLines)
        => _inner.LayoutTextLines(text, style, maxWidth, maxLines);

    public FontMetrics MeasureFont(ResolvedInheritedStyle style) => _inner.MeasureFont(style);

    public bool HasGlyph(ResolvedInheritedStyle style, int codePoint) => _inner.HasGlyph(style, codePoint);

    // ──── State stack ────
    // Each push records at the current depth then indents; Pop outdents first,
    // so a push/pop pair brackets its children in the log.

    public void PushClip(Rect clipRect)
    {
        Record("PushClip", Fmt(clipRect));
        _depth++;
        _inner.PushClip(clipRect);
    }

    public void PushRoundedClip(Rect clipRect, CornerRadius radius)
    {
        Record("PushRoundedClip", $"{Fmt(clipRect)}{Radius(radius)}");
        _depth++;
        _inner.PushRoundedClip(clipRect, radius);
    }

    public void PushTranslate(float x, float y)
    {
        Record("PushTranslate", $"{F(x)},{F(y)}");
        _depth++;
        _inner.PushTranslate(x, y);
    }

    public void PushOpacity(float opacity)
    {
        Record("PushOpacity", F(opacity));
        _depth++;
        _inner.PushOpacity(opacity);
    }

    public void PushTransform(Matrix3x2 matrix)
    {
        Record("PushTransform",
            $"[{F(matrix.M11)},{F(matrix.M12)},{F(matrix.M21)},{F(matrix.M22)},{F(matrix.M31)},{F(matrix.M32)}]");
        _depth++;
        _inner.PushTransform(matrix);
    }

    public void Pop()
    {
        _depth = Math.Max(0, _depth - 1);
        Record("Pop");
        _inner.Pop();
    }

    // ──── Canvas ────

    public ICanvasDrawingContext CreateCanvasContext(Rect bounds)
    {
        Record("CreateCanvasContext", Fmt(bounds));
        return new RecordingCanvasContext(_inner.CreateCanvasContext(bounds), Log, _depth + 1);
    }
}
