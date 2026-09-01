using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Theme;

using HultUI.Icons;

namespace Junction;

/// <summary>
/// Small, stateless builders shared across the Junction views — chips, icon
/// glyphs, monospace text, and the Nocturne button families
/// (<c>.btn-primary</c> / <c>.btn-secondary</c> / <c>.btn-ghost</c> /
/// <c>.btn-icon</c>). Keeps the view code declarative and on-theme.
/// </summary>
public static class Ui
{
    // ── Text / glyphs ────────────────────────────────────────────────────

    /// <summary>
    /// Converts a nominal icon size — the numbers throughout this UI, which were chosen
    /// when icons were text glyphs — into the box a vector icon needs to look the same.
    ///
    /// <para>Measured rather than guessed. The Unicode stand-ins inked a mean 64% of
    /// their font size (they are drawn well inside the em box); Phosphor's paths ink
    /// about 72% of theirs. Reusing the old numbers as box sizes therefore drew every
    /// icon ~13% oversized. Applying the ratio once keeps every relative size the design
    /// chose — a 9 badge against a 15 button — and corrects only the systematic error.</para>
    /// </summary>
    public static float IconBox(float nominal) => MathF.Round(nominal * 0.88f * 2f) / 2f;

    /// <summary>
    /// Device pixels per layout unit, kept in step with the window by
    /// <see cref="JunctionApp"/>. Icons rasterize at this scale so the renderer never
    /// resamples them; at 1.0 that is layout size, at 1.5 it is half again as many
    /// pixels for the same on-screen size.
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

    /// <summary>Drop cached glyph rasters after a theme change. They are rasterized in the
    /// colour they were asked for, so a scheme change leaves every one of them tinted for the
    /// palette that has just been replaced. Both caches: the chrome and dialog glyphs, and
    /// the file-tree icons, which bake the accent and neutral ramps.</summary>
    public static void InvalidateIconCache()
    {
        Phosphor.InvalidateCache();
        HultUI.Icons.FileIcons.InvalidateCache();
    }

    /// <summary>
    /// A Phosphor glyph as real vector artwork. <paramref name="name"/> is a Phosphor
    /// icon name — use the constants in <see cref="Icons"/> rather than a literal, so a
    /// typo is caught by JunctionIconNameTests instead of silently rendering an empty box.
    /// <paramref name="size"/> is nominal; see <see cref="IconBox"/>.
    /// </summary>
    public static Element Icon(string name, float size, Color color) =>
        Phosphor.Draw(name, IconBox(size), color, DisplayScale);

    public static TextElement Mono(string text, float size, Color color) =>
        Text.New(text).FontFamily(Fonts.Mono).FontSize(size).TextColor(color);

    /// <summary>A body of code in the family and size the user chose, rather than in the
    /// fixed monospace the chrome uses for SHAs and branch names.</summary>
    public static TextElement Code(string text, float size, string family, Color color) =>
        Text.New(text).FontFamily(family).FontSize(size).TextColor(color);

    // ── Tags / chips ─────────────────────────────────────────────────────
    // All three keep their label on one line, whatever the row does around them.
    // FlexShrink(0) alone is not enough: in a tight row the chip is *measured*
    // under the leftover width, the label wraps during that measurement, and a
    // no-shrink chip then faithfully keeps the two-line size. MaxLines(1) makes
    // the label ellipsize instead ("origi/n" was on screen in the Branches tab).

    private static TextElement TagLabel(string label, float fontSize, Color fg) =>
        Text.New(label).FontSize(fontSize).TextColor(fg)
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1);

    public static BoxElement Tag(string label, Color bg, Color fg, float fontSize = 11f) =>
        Box.New()
            .Padding(new Thickness(3, 9, 3, 9))
            .CornerRadius(Nocturne.RadiusMd * 0.75f)
            .Background(bg)
            .AlignItems(AlignItems.Center)
            .FlexShrink(0)
            .Children(TagLabel(label, fontSize, fg));

    public static BoxElement TagOutline(string label, Color color, float fontSize = 11f)
    {
        var box = Box.New()
            .Padding(new Thickness(2, 8, 2, 8))
            .CornerRadius(Nocturne.RadiusMd * 0.75f)
            .BorderColor(color).BorderWidth(1f)
            .AlignItems(AlignItems.Center)
            .FlexShrink(0)
            .Children(TagLabel(label, fontSize, color));
        return box;
    }

    /// <summary>A label chip with an optional leading icon (feature / wip / remote /
    /// tag / HEAD). Pass a null/empty glyph for a text-only chip (local branches).</summary>
    public static BoxElement Chip(string? glyph, string label, Color bg, Color fg, bool outline = false)
    {
        var box = Flex.Row(gap: 3)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(2, 7, 2, 7))
            .CornerRadius(Nocturne.RadiusMd * 0.75f)
            .FlexShrink(0);
        // An outline rather than a border, so an outlined chip and a filled one with the same
        // label measure the same — a border would make every outlined chip 2px wider.
        if (outline) box.Outline(1f, fg);
        else box.Background(bg);
        if (!string.IsNullOrEmpty(glyph)) box.AddChild(Icon(glyph, 9f, fg));
        box.AddChild(TagLabel(label, 9.5f, fg));
        return box;
    }

    // ── Hover ────────────────────────────────────────────────────────────

    // Declarative hover tint (Interaction system): re-applied after any rebuild, so
    // it survives mid-hover reconciliation; un-hover restores the element's base
    // snapshot. `normal` documents the resting color; the restore itself comes from
    // the base snapshot.
    public static T HoverBg<T>(this T el, Color hover, Color normal) where T : BoxElement =>
        el.OnHover(b => b.BackgroundColor = hover);

    // ── Buttons ──────────────────────────────────────────────────────────

    private static BoxElement BtnBase(Action onClick)
    {
        var b = Flex.Row(gap: 6)
            .AlignItems(AlignItems.Center)
            .JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(5, 10, 5, 10))
            .CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer);
        b.OnClick(_ => onClick());
        return b;
    }

    private static BoxElement WithContent(BoxElement b, string? glyph, string? label, Color fg, float fontSize)
    {
        if (glyph != null) b.AddChild(Icon(glyph, fontSize + 1f, fg));
        if (label != null) b.AddChild(Text.New(label).FontSize(fontSize).FontWeight(FontWeight.Medium).TextColor(fg));
        return b;
    }

    public static BoxElement PrimaryButton(string label, string? glyph, Action onClick, float fontSize = 13f)
    {
        var b = BtnBase(onClick).BorderColor(Nocturne.Accent).BorderWidth(1f);
        WithContent(b, glyph, label, Nocturne.Accent, fontSize);
        return b.HoverBg(Nocturne.Alpha(Nocturne.Accent, 0.12), Color.Transparent);
    }

    public static BoxElement SecondaryButton(string label, string? glyph, Action onClick, float fontSize = 13f)
    {
        var b = BtnBase(onClick).BorderColor(Nocturne.Divider).BorderWidth(1f);
        WithContent(b, glyph, label, Nocturne.Text, fontSize);
        return b.HoverBg(Nocturne.Alpha(Nocturne.Text, 0.07), Color.Transparent);
    }

    public static BoxElement GhostButton(string label, string? glyph, Action onClick, float fontSize = 12f)
    {
        var b = BtnBase(onClick).Padding(new Thickness(3, 4, 3, 4));
        WithContent(b, glyph, label, Nocturne.Accent, fontSize);
        return b.HoverBg(Nocturne.Alpha(Nocturne.Accent, 0.10), Color.Transparent);
    }

    public static BoxElement IconButton(string glyph, Action onClick, string? tooltip = null, float size = 34f)
    {
        var b = BtnBase(onClick)
            .Width(size).Height(size)
            .Padding(new Thickness(0))
            .BorderColor(Nocturne.Divider).BorderWidth(1f);
        b.AddChild(Icon(glyph, 15f, Nocturne.Text));
        if (tooltip != null) b.Tooltip(tooltip);
        return b.HoverBg(Nocturne.Alpha(Nocturne.Text, 0.07), Color.Transparent);
    }

    /// <summary>
    /// An icon button that cannot be pressed: the same footprint, dimmed, with no click
    /// handler and no hover tint. <paramref name="reason"/> becomes the tooltip, because a
    /// control that does nothing has to say why rather than just look inert.
    /// </summary>
    public static BoxElement DisabledIconButton(string glyph, string reason, float size = 34f)
    {
        var b = Flex.Row(gap: 6)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Width(size).Height(size)
            .CornerRadius(Nocturne.RadiusMd)
            .BorderColor(Nocturne.Alpha(Nocturne.Divider, 0.55)).BorderWidth(1f)
            .Tooltip(reason);
        b.AddChild(Icon(glyph, 15f, Nocturne.On(0.26)));
        return b;
    }

    /// <summary>A square repository "logo" tile (coloured initial).</summary>
    public static BoxElement InitialTile(string name, Color bg, Color fg, float size, float fontSize)
    {
        var initial = string.IsNullOrEmpty(name) ? "?" : char.ToUpperInvariant(name[0]).ToString();
        return Box.New()
            .Width(size).Height(size).FlexShrink(0)
            .CornerRadius(size >= 30 ? Nocturne.RadiusMd : Nocturne.RadiusRow)
            .Background(bg)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Text.New(initial).FontSize(fontSize).FontWeight(FontWeight.SemiBold).TextColor(fg));
    }

    /// <summary>Deterministic per-repo tile colours, mirroring the design's palette.</summary>
    public static (Color Bg, Color Fg) RepoTileColors(string path)
    {
        (Color, Color)[] palette =
        [
            (Nocturne.Accent800, Nocturne.Accent100),
            (Nocturne.Accent2_800, Nocturne.Accent2_100),
            (Nocturne.Neutral800, Nocturne.Neutral100),
            (Nocturne.Neutral800, Nocturne.Neutral200),
            (Nocturne.Accent900, Nocturne.Accent200),
            (Nocturne.Accent2_900, Nocturne.Accent2_200),
        ];
        var key = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
        uint h = 2166136261u;
        foreach (var c in key) { h ^= c; h *= 16777619u; }
        return palette[(int)(h % (uint)palette.Length)];
    }
}
