using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using VesperCommander.Theme;

namespace VesperCommander;

/// <summary>
/// Small stateless builders shared across Vesper's views — glyphs, monospace
/// text, and keycaps — keeping view code declarative and on-theme. Mirrors the
/// Junction sample's <c>Ui</c> helper.
/// </summary>
public static class Ui
{
    public static TextElement Icon(string glyph, float size, Color color) =>
        Text.New(glyph).FontSize(size).TextColor(color);

    public static TextElement Mono(string text, float size, Color color) =>
        Text.New(text).FontFamily(Fonts.Mono).FontSize(size).TextColor(color);

    /// <summary>A keycap chip (10px mono, hairline border) as in the design's <c>.kbd</c>.</summary>
    public static BoxElement Kbd(string text) =>
        Box.New()
            .Padding(new Thickness(1, 5, 2, 5))
            .CornerRadius(4f)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .Children(Mono(text, 10f, Nocturne.Neutral300));

    /// <summary>The canonical hover idiom: swap background on enter/leave.</summary>
    public static T HoverBg<T>(this T el, Color hover, Color normal) where T : BoxElement
    {
        el.OnPointerEnter(_ => el.BackgroundColor = hover);
        el.OnPointerLeave(_ => el.BackgroundColor = normal);
        return el;
    }
}
