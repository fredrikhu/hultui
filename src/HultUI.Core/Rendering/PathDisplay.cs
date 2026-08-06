using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

/// <summary>
/// Helpers for fitting a file-system path into a limited horizontal space by
/// collapsing its middle — "C:\Users\me\src\proj" → "C:\…\src\proj" — keeping the
/// root and as many trailing segments as fit. Width is measured, not estimated, so
/// the result fills the budget without wrapping or over-truncating.
/// </summary>
public static class PathDisplay
{
    private const string Ellipsis = "…";

    /// <summary>
    /// Collapses <paramref name="path"/> to fit <paramref name="budget"/> pixels,
    /// measuring candidate strings with <paramref name="measure"/> (returns the pixel
    /// width of a string). Keeps the first segment (drive/root) and the longest run of
    /// trailing segments that fits, joining them with an ellipsis. Returns the full
    /// path unchanged when it already fits or has too few segments to collapse.
    /// </summary>
    /// <param name="separator">Path separator; defaults to the platform separator.</param>
    public static string Collapse(string path, float budget, Func<string, float> measure, char separator = '\0')
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (separator == '\0') separator = System.IO.Path.DirectorySeparatorChar;
        if (measure(path) <= budget) return path;

        var segments = path.Split(separator);
        // Need at least root + two middle-or-more to have anything worth dropping.
        if (segments.Length <= 2) return path;

        string head = segments[0];
        string sep = separator.ToString();
        string prefix = head + sep + Ellipsis;

        // Grow the kept tail from the end while "head\…\<tail>" still fits.
        int firstKept = segments.Length; // exclusive-of-nothing → nothing kept yet
        for (int i = segments.Length - 1; i >= 1; i--)
        {
            string candidate = prefix + sep + string.Join(sep, segments[i..]);
            if (measure(candidate) <= budget)
                firstKept = i;
            else
                break;
        }

        if (firstKept > segments.Length - 1)
            // Not even the last segment fits alongside the head — show "head\…".
            return prefix;

        return prefix + sep + string.Join(sep, segments[firstKept..]);
    }

    /// <summary>
    /// Collapses <paramref name="path"/> using the ambient <see cref="TextMeasure"/>
    /// at the given font. Call from <c>Build()</c>. Falls back to the full path when no
    /// measure context is installed yet (measured width is zero, so nothing collapses).
    /// </summary>
    public static string Collapse(string path, float budget, float fontSize,
        string fontFamily = "sans-serif", FontWeight fontWeight = FontWeight.Regular, char separator = '\0')
    {
        var style = TextMeasure.StyleFor(fontSize, fontFamily, fontWeight);
        return Collapse(path, budget, s => TextMeasure.MeasureWidth(s, style), separator);
    }
}
