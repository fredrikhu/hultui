using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class ContextMenuTheme : IThemeExtension<ContextMenuTheme>
{
    public Color Background { get; init; } = new(255, 255, 255);        // white
    public Color BorderColor { get; init; } = new(226, 232, 240);       // slate-200
    public Color TextColor { get; init; } = new(15, 23, 42);            // slate-900
    public Color DisabledTextColor { get; init; } = new(148, 163, 184); // slate-400
    public Color DangerTextColor { get; init; } = new(220, 38, 38);     // red-600
    public Color HoverBackground { get; init; } = new(241, 245, 249);   // slate-100
    public Color SeparatorColor { get; init; } = new(226, 232, 240);    // slate-200
    public Color ShortcutTextColor { get; init; } = new(148, 163, 184); // slate-400
    public float CornerRadius { get; init; } = 6f;
    public float BorderWidth { get; init; } = 1f;
    public Thickness Padding { get; init; } = new(4, 0);
    public Thickness ItemPadding { get; init; } = new(6, 12);
    public float FontSize { get; init; } = 13f;
    public float ShortcutFontSize { get; init; } = 12f;
    public float MinWidth { get; init; } = 160f;
    public float SeparatorMargin { get; init; } = 4f;
    public float ShortcutGap { get; init; } = 24f;
    public float IconGap { get; init; } = 9f;

    /// <summary>The mark drawn in the icon column for the item in force
    /// (<see cref="Elements.ContextMenuItem.IsChecked"/>). Text, so an app with an icon
    /// font can hand over a real tick instead.</summary>
    public string CheckGlyph { get; init; } = "•";

    /// <summary>
    /// The built-in defaults are literal rather than derived from the palette: they are the
    /// values these controls already shipped with, and deriving them here would repaint
    /// every existing app. New extensions should prefer deriving from the theme — see
    /// <see cref="IThemeExtension{TSelf}"/>.
    /// </summary>
    public static ContextMenuTheme CreateDefault(Theme theme) => new();
}
