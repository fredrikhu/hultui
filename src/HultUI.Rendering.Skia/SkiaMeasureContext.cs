using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

public sealed class SkiaMeasureContext : IMeasureContext
{
    public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines)
    {
        if (string.IsNullOrEmpty(text))
            return Size.Zero;

        using var typeface = SKTypeface.FromFamilyName(style.FontFamily,
            ToSkFontWeight(style.FontWeight), SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var font = new SKFont(typeface, style.FontSize);
        using var paint = new SKPaint();

        var metrics = font.Metrics;
        float lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;

        // Measure the full text width
        float totalWidth = font.MeasureText(text, paint);

        if (totalWidth <= maxWidth)
            return new Size(totalWidth, lineHeight);

        // Simple word-wrapping approximation
        float avgCharWidth = totalWidth / text.Length;
        int charsPerLine = Math.Max(1, (int)(maxWidth / avgCharWidth));
        int lineCount = (int)Math.Ceiling((double)text.Length / charsPerLine);

        if (maxLines.HasValue)
            lineCount = Math.Min(lineCount, maxLines.Value);

        float width = Math.Min(totalWidth, maxWidth);
        return new Size(width, lineCount * lineHeight);
    }

    private static SKFontStyleWeight ToSkFontWeight(FontWeight weight) => weight switch
    {
        FontWeight.Thin => SKFontStyleWeight.Thin,
        FontWeight.Light => SKFontStyleWeight.Light,
        FontWeight.Regular => SKFontStyleWeight.Normal,
        FontWeight.Medium => SKFontStyleWeight.Medium,
        FontWeight.SemiBold => SKFontStyleWeight.SemiBold,
        FontWeight.Bold => SKFontStyleWeight.Bold,
        FontWeight.ExtraBold => SKFontStyleWeight.ExtraBold,
        FontWeight.Black => SKFontStyleWeight.Black,
        _ => SKFontStyleWeight.Normal
    };
}
