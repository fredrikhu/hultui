using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Icons;
using VesperCommander.Theme;

namespace VesperCommander;

/// <summary>
/// Small stateless builders shared across Vesper's views — glyphs, monospace
/// text, and keycaps — keeping view code declarative and on-theme. Mirrors the
/// Junction sample's <c>Ui</c> helper.
/// </summary>
public static class Ui
{
    /// <summary>
    /// Converts a nominal icon size — the numbers throughout this UI, chosen when icons
    /// were text glyphs — into the box a vector icon needs to look the same.
    ///
    /// <para>The Unicode stand-ins inked a mean 64% of their font size; Phosphor's paths
    /// ink about 72% of theirs. Reusing the old numbers as box sizes therefore drew every
    /// icon ~13% oversized. Applying the ratio once keeps the relative sizes the design
    /// chose and corrects only the systematic error. Same conversion Junction uses, so
    /// shared artwork comes out the same size in both apps.</para>
    /// </summary>
    public static float IconBox(float nominal) => MathF.Round(nominal * 0.88f * 2f) / 2f;

    /// <summary>
    /// A Phosphor glyph as real vector artwork. <paramref name="name"/> is a Phosphor icon
    /// name — use the constants in <see cref="Glyphs"/> rather than a literal, so a typo is
    /// caught by a test instead of silently rendering an empty box. <paramref name="size"/>
    /// is nominal; see <see cref="IconBox"/>.
    /// </summary>
    public static Element Icon(string name, float size, Color color) =>
        Phosphor.Draw(name, IconBox(size), color, Icons.DisplayScale);

    public static TextElement Mono(string text, float size, Color color) =>
        Text.New(text).FontFamily(Fonts.Mono).FontSize(size).TextColor(color);

    /// <summary>A body of file text in the face and size the user chose, rather than in the
    /// fixed monospace the chrome uses for names, sizes and dates.</summary>
    public static TextElement Code(string text, float size, string family, Color color) =>
        Text.New(text).FontFamily(family).FontSize(size).TextColor(color);

    /// <summary>Minimum height for a single-line <see cref="TextInputElement"/>: the theme
    /// hard-sets ~12px of vertical input padding, so a shorter box clips the 13.5px font's
    /// descenders. Every field should size to this (see <see cref="Field"/>).</summary>
    public const float FieldHeight = 30f;

    /// <summary>Configure a TextInput as a standard single-line field — grow to fill its
    /// row and use the non-clipping <see cref="FieldHeight"/>. Set placeholder / handlers /
    /// ref on the returned element. (Fields that need a fixed width instead of growing
    /// should set <c>.Width(...).Height(Ui.FieldHeight)</c> directly.)</summary>
    public static TextInputElement Field(TextInputElement input) => input.FlexGrow(1).Height(FieldHeight);

    /// <summary>A keycap chip (10px mono, hairline border) as in the design's <c>.kbd</c>.</summary>
    public static BoxElement Kbd(string text) =>
        Box.New()
            .Padding(new Thickness(1, 5, 2, 5))
            .CornerRadius(4f)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .Children(Mono(text, 10f, Nocturne.Neutral300));

    /// <summary>The canonical hover idiom: tint the background while hovered.
    /// Declarative (Interaction system): the tint is re-applied after any rebuild,
    /// so it survives mid-hover reconciliation, and un-hover restores the element's
    /// base snapshot. <paramref name="normal"/> documents the resting color at the
    /// call site; the restore itself comes from the base snapshot.</summary>
    public static T HoverBg<T>(this T el, Color hover, Color normal) where T : BoxElement =>
        el.OnHover(b => b.BackgroundColor = hover);

    // ── Buttons ──────────────────────────────────────────────────────────
    // The framework's ButtonElement, in the families Nocturne.ToHultTheme() gives its
    // ButtonTheme (mirrors Junction's Ui.*): Primary = accent outline, Secondary =
    // neutral outline, Ghost = borderless, Danger = mark-red outline. Chrome, hover,
    // focus, keyboard activation, the disabled state and the accessibility role are the
    // element's; these helpers add Vesper's content — an optional leading glyph beside a
    // label at the caller's size — and pass `enabled` through.

    /// <summary>The glyph colour of each family: what the label is drawn in, which the icon
    /// cannot inherit the way text does.</summary>
    private static Color ButtonForeground(ButtonVariant variant) => variant switch
    {
        ButtonVariant.Secondary => Nocturne.Text,
        ButtonVariant.Danger => Nocturne.MarkRed,
        _ => Nocturne.Accent,
    };

    private static ButtonElement Btn(ButtonVariant variant, string? icon, string label, float fontSize,
        Action onClick, bool enabled)
    {
        var b = Button.New(variant).Gap(6).Enabled(enabled);
        if (icon != null) b.AddChild(Icon(icon, fontSize + 1, ButtonForeground(variant)));
        b.AddChild(Text.New(label).FontSize(fontSize));
        b.OnClick(_ => onClick());
        return b;
    }

    public static ButtonElement PrimaryButton(string label, string? icon, Action onClick,
        bool enabled = true, float fontSize = 12.5f) =>
        Btn(ButtonVariant.Primary, icon, label, fontSize, onClick, enabled);

    public static ButtonElement SecondaryButton(string label, string? icon, Action onClick,
        bool enabled = true, float fontSize = 12.5f) =>
        Btn(ButtonVariant.Secondary, icon, label, fontSize, onClick, enabled);

    public static ButtonElement GhostButton(string label, string? icon, Action onClick,
        bool enabled = true, float fontSize = 12f) =>
        Btn(ButtonVariant.Ghost, icon, label, fontSize, onClick, enabled);

    public static ButtonElement DangerButton(string label, string? icon, Action onClick,
        bool enabled = true, float fontSize = 12.5f) =>
        Btn(ButtonVariant.Danger, icon, label, fontSize, onClick, enabled);

    /// <summary>A dialog-header app-mark: a 34px accent-800 tile with an accent-200
    /// glyph — Junction's modal-header idiom, so titled dialogs read as one family.</summary>
    public static BoxElement HeaderTile(string icon) =>
        Box.New().Width(34).Height(34).FlexShrink(0).CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Accent800)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Icon(icon, 16f, Nocturne.Accent200));

    /// <summary>
    /// The square icon-button shape at one size: a fixed box, no padding, the secondary
    /// family's hairline. One instance per size, because <see cref="ButtonElement.Style"/>
    /// compares by reference and a fresh object every build would be a style pass every build.
    /// </summary>
    private static readonly Dictionary<float, ButtonStyle> IconStyles = new();

    private static ButtonStyle IconStyle(float size)
    {
        if (!IconStyles.TryGetValue(size, out var style))
            IconStyles[size] = style = new ButtonStyle { Size = size, Padding = new Thickness(0) };
        return style;
    }

    /// <summary>A square icon-only button with a hairline border (Junction's IconButton).</summary>
    public static ButtonElement IconButton(string icon, Action onClick, string? tooltip = null, float size = 34f)
    {
        var b = Button.New(ButtonVariant.Secondary).Style(IconStyle(size));
        if (tooltip != null) b.Tooltip(tooltip);
        b.AddChild(Icon(icon, 15f, Nocturne.Text));
        b.OnClick(_ => onClick());
        return b;
    }
}
