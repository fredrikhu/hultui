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
    public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines)
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
}
