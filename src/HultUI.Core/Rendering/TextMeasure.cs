using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

/// <summary>
/// Ambient text measurement usable from <c>Build()</c>, where no layout/measure
/// pass is running. The active window installs <see cref="Context"/> from its
/// render surface's measure context; the helpers measure a string in a given
/// style so application code can size, budget, or truncate text against real
/// glyph metrics instead of approximating by character count.
/// </summary>
/// <remarks>
/// This is main-thread ambient state, like the rest of the build/layout pipeline —
/// call it from the UI thread. Before the first window exists (or in a headless
/// setup that installs no context) the measure helpers return zero width, so
/// callers should treat a zero result as "unknown" and fall back gracefully.
/// </remarks>
public static class TextMeasure
{
    /// <summary>
    /// The measure context of the active window, or <c>null</c> before one exists.
    /// The framework sets this; application code normally only reads via the helpers.
    /// </summary>
    public static IMeasureContext? Context { get; set; }

    /// <summary>
    /// Measures a single line of <paramref name="text"/> in <paramref name="style"/>.
    /// Returns <see cref="Size.Zero"/> for empty text or when no context is installed.
    /// </summary>
    public static Size Measure(string text, ResolvedInheritedStyle style) =>
        string.IsNullOrEmpty(text) || Context is null
            ? Size.Zero
            : Context.MeasureText(text, style, float.PositiveInfinity, 1);

    /// <summary>Width of a single line of <paramref name="text"/> in <paramref name="style"/>.</summary>
    public static float MeasureWidth(string text, ResolvedInheritedStyle style) =>
        Measure(text, style).Width;

    /// <summary>
    /// Width of <paramref name="text"/> at a font size/family, without building a full
    /// style. Fills the fields that don't affect width from the resolver defaults.
    /// </summary>
    public static float MeasureWidth(string text, float fontSize,
        string fontFamily = "sans-serif", FontWeight fontWeight = FontWeight.Regular) =>
        MeasureWidth(text, StyleFor(fontSize, fontFamily, fontWeight));

    /// <summary>
    /// Builds a minimal <see cref="ResolvedInheritedStyle"/> for measurement from a
    /// font size/family/weight, using the resolver defaults for the rest.
    /// </summary>
    public static ResolvedInheritedStyle StyleFor(float fontSize,
        string fontFamily = "sans-serif", FontWeight fontWeight = FontWeight.Regular) =>
        StyleResolver.DefaultStyle with
        {
            FontSize = fontSize,
            FontFamily = fontFamily,
            FontWeight = fontWeight,
        };
}
