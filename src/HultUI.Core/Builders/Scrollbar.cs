using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

/// <summary>A <see cref="ScrollbarElement"/> standing beside what it scrolls: a long document
/// drawn on a canvas, positioned in the app's own unit (a byte offset, a row index).</summary>
public static class Scrollbar
{
    public static ScrollbarElement Vertical(long value, long contentLength, long viewportLength,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        Make(ScrollbarAxis.Vertical, value, contentLength, viewportLength, file, line);

    public static ScrollbarElement Horizontal(long value, long contentLength, long viewportLength,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        Make(ScrollbarAxis.Horizontal, value, contentLength, viewportLength, file, line);

    // The lengths before the value, so the value is not clamped against an empty range.
    private static ScrollbarElement Make(ScrollbarAxis axis, long value, long contentLength, long viewportLength,
        string? file, int line)
    {
        var bar = ScrollbarElement.New(axis, file, line);
        bar.ContentLength = contentLength;
        bar.ViewportLength = viewportLength;
        bar.Value = value;
        return bar;
    }
}
