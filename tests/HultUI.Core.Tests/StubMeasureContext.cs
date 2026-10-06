using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Tests;

/// <summary>
/// Returns fixed-size text measurements for testing layout without a rendering backend.
/// Default: 7px per character width, 14px height (monospace approximation).
/// </summary>
public sealed class StubMeasureContext(float charWidth = 7f, float lineHeight = 14f) : IMeasureContext
{
    // Overflow is accepted and ignored: both modes truncate to the same box, and this
    // stub reports geometry, not which characters survive.
    public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow = TextOverflow.Clip)
    {
        if (string.IsNullOrEmpty(text))
            return Size.Zero;

        float totalWidth = text.Length * charWidth;
        if (totalWidth <= maxWidth)
            return new Size(totalWidth, lineHeight);

        // Simple line wrapping for testing
        int charsPerLine = Math.Max(1, (int)(maxWidth / charWidth));
        int lineCount = (int)Math.Ceiling((double)text.Length / charsPerLine);
        if (maxLines.HasValue)
            lineCount = Math.Min(lineCount, maxLines.Value);

        float width = Math.Min(totalWidth, maxWidth);
        return new Size(width, lineCount * lineHeight);
    }

    // One line that fits is the same at any width it fits in, as in the real pipeline; so the
    // layout tests exercise measurements kept for a range of widths.
    public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow, IReadOnlyList<TextSpan>? spans, out float sameFromWidth)
    {
        var size = MeasureText(text, style, maxWidth, maxLines, overflow);
        float totalWidth = (text?.Length ?? 0) * charWidth;
        sameFromWidth = totalWidth <= maxWidth ? totalWidth : float.NaN;
        return size;
    }

    /// <summary>
    /// The widest word, matching what the real pipeline answers. The default
    /// interface implementation would measure at zero width and, because
    /// <see cref="MeasureText"/> above wraps mid-word, report one character — which
    /// is exactly the under-reporting this override exists to stop tests from
    /// inheriting.
    /// </summary>
    public float MinContentTextWidth(string text, ResolvedInheritedStyle style,
        IReadOnlyList<TextSpan>? spans = null)
    {
        float widest = 0f;
        foreach (var word in text.Split(' ', '\n'))
            widest = Math.Max(widest, word.Length * charWidth);
        return widest;
    }
}
