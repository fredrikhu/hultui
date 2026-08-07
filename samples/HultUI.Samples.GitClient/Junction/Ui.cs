using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Junction;

/// <summary>
/// Small, stateless builders shared across the Junction views — chips, icon
/// glyphs, monospace text, and the Nocturne button families
/// (<c>.btn-primary</c> / <c>.btn-secondary</c> / <c>.btn-ghost</c> /
/// <c>.btn-icon</c>). Keeps the view code declarative and on-theme.
/// </summary>
public static class Ui
{
    // ── Text / glyphs ────────────────────────────────────────────────────

    public static TextElement Icon(string glyph, float size, Color color) =>
        Text.New(glyph).FontSize(size).TextColor(color);

    public static TextElement Mono(string text, float size, Color color) =>
        Text.New(text).FontFamily(Fonts.Mono).FontSize(size).TextColor(color);

    // ── Tags / chips ─────────────────────────────────────────────────────

    public static BoxElement Tag(string label, Color bg, Color fg, float fontSize = 11f) =>
        Box.New()
            .Padding(new Thickness(3, 9, 3, 9))
            .CornerRadius(Nocturne.RadiusMd * 0.75f)
            .Background(bg)
            .AlignItems(AlignItems.Center)
            .Children(Text.New(label).FontSize(fontSize).TextColor(fg));

    public static BoxElement TagOutline(string label, Color color, float fontSize = 11f)
    {
        var box = Box.New()
            .Padding(new Thickness(2, 8, 2, 8))
            .CornerRadius(Nocturne.RadiusMd * 0.75f)
            .BorderColor(color).BorderWidth(1f)
            .AlignItems(AlignItems.Center)
            .Children(Text.New(label).FontSize(fontSize).TextColor(color));
        return box;
    }

    /// <summary>A label chip with an optional leading icon (feature / wip / remote /
    /// tag / HEAD). Pass a null/empty glyph for a text-only chip (local branches).</summary>
    public static BoxElement Chip(string? glyph, string label, Color bg, Color fg, bool outline = false)
    {
        var box = Flex.Row(gap: 3)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(2, 7, 2, 7))
            .CornerRadius(Nocturne.RadiusMd * 0.75f);
        if (outline) box.BorderColor(fg).BorderWidth(1f);
        else box.Background(bg);
        if (!string.IsNullOrEmpty(glyph)) box.AddChild(Icon(glyph, 9f, fg));
        box.AddChild(Text.New(label).FontSize(9.5f).TextColor(fg));
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
