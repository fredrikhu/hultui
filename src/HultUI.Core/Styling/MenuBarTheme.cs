using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// How a <see cref="Builders.MenuBar"/> looks. Colours left null come from the palette. The
/// menus it opens are context menus, and take <see cref="ContextMenuTheme"/>.
/// </summary>
public sealed class MenuBarTheme : IThemeExtension<MenuBarTheme>
{
    /// <summary>The bar's fill; null leaves it transparent, over whatever chrome it sits on.</summary>
    public Color? Background { get; init; }
    /// <summary>A rule under the bar; null draws none.</summary>
    public Color? Divider { get; init; }

    public float Height { get; init; } = 30f;
    public Thickness Padding { get; init; } = new(3, 4, 3, 4);
    public float Gap { get; init; } = 0f;

    public Thickness TitlePadding { get; init; } = new(0, 10, 0, 10);
    public float TitleCornerRadius { get; init; } = 4f;
    public float FontSize { get; init; } = 13f;
    /// <summary>The titles' colour; null inherits.</summary>
    public Color? Text { get; init; }
    /// <summary>A title under the pointer, or chosen from the keyboard; null takes the theme's
    /// surface variant.</summary>
    public Color? HoverBackground { get; init; }
    /// <summary>The title whose menu is open; null takes the hover fill.</summary>
    public Color? OpenBackground { get; init; }
    /// <summary>The open title's text; null keeps <see cref="Text"/>.</summary>
    public Color? OpenText { get; init; }

    public static MenuBarTheme CreateDefault(Theme theme) => new();
}
