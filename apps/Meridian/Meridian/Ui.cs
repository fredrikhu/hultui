using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Icons;
using Meridian.Theme;

namespace Meridian;

/// <summary>
/// Small, stateless builders shared across the Meridian views — icon glyphs,
/// monospace text, key-hint chips. Keeps the view code declarative and on-theme.
/// </summary>
public static class Ui
{
    /// <summary>
    /// Converts a nominal icon size into the box a vector icon needs to look the
    /// same. Same measured ratio the other apps use: Phosphor's paths ink ~72% of
    /// their box where the design's nominal sizes assumed ~64% inking.
    /// </summary>
    public static float IconBox(float nominal) => MathF.Round(nominal * 0.88f * 2f) / 2f;

    /// <summary>
    /// Device pixels per layout unit, kept in step with the window by
    /// <see cref="MeridianApp"/>. Icons rasterize at this scale so the renderer
    /// never resamples them.
    /// </summary>
    public static float DisplayScale
    {
        get => _displayScale;
        set
        {
            if (MathF.Abs(_displayScale - value) < 0.001f) return;
            _displayScale = value;
            // Every cached raster was built for the old scale and would now be resampled.
            Phosphor.InvalidateCache();
        }
    }
    private static float _displayScale = 1f;

    /// <summary>A Phosphor glyph as vector artwork. Use the constants in
    /// <see cref="Icons"/> so a typo is caught by MeridianIconTests.</summary>
    public static Element Icon(string name, float size, Color color) =>
        Phosphor.Draw(name, IconBox(size), color, DisplayScale);

    public static TextElement Mono(string text, float size, Color color) =>
        Text.New(text).FontFamily(Fonts.Mono).FontSize(size).TextColor(color);

    /// <summary>
    /// A chip's label — one line, always. <c>FlexShrink(0)</c> on the chip box
    /// is not enough on its own: in a tight row the chip is <i>measured</i>
    /// under the leftover width, the label wraps during that measurement, and
    /// a no-shrink chip then faithfully keeps the two-line size. MaxLines(1)
    /// makes it ellipsize instead. (Junction hit exactly this — a remote chip
    /// rendered as "origi/n" — so every chip here bakes in both halves.)
    /// </summary>
    public static TextElement ChipLabel(string text, float size, Color color) =>
        Mono(text, size, color).TextOverflow(TextOverflow.Ellipsis).MaxLines(1);

    /// <summary>
    /// Declarative hover tint (Interaction system): re-applied after any rebuild, so
    /// it survives mid-hover reconciliation; un-hover restores the element's base
    /// snapshot. <paramref name="normal"/> documents the resting colour; the restore
    /// itself comes from the base snapshot.
    /// </summary>
    public static T HoverBg<T>(this T el, Color hover, Color normal) where T : BoxElement =>
        el.OnHover(b => b.BackgroundColor = hover);

    /// <summary>
    /// A box the pointer passes straight through — CSS <c>pointer-events: none</c>.
    /// Hit testing skips the whole subtree, so a decorative overlay can span the
    /// window without stealing clicks from what is underneath.
    ///
    /// <para>The toast needs this: it is a full-width strip pinned to the bottom
    /// of the window, and it was swallowing clicks on any control near the
    /// bottom edge (found when a "Generate request" button stopped responding
    /// while an import toast was up).</para>
    /// </summary>
    public sealed class PassThroughBox : BoxElement
    {
        public override bool HitTest(Point point) => false;
    }

    /// <summary>Key-hint chip: mono 10, az-border box (the sheet's kbd idiom).</summary>
    public static BoxElement Kbd(string text) =>
        Box.New()
            .Padding(new Thickness(2, 5, 2, 5))
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(Azimuth.Border).BorderWidth(1f)
            .Children(Mono(text, 10f, Azimuth.TextMuted));
}
