using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// How a <see cref="Builders.CommandPalette"/> looks. Colours left null come from the palette,
/// so an app that sets nothing gets a palette in its theme's colours.
/// </summary>
public sealed class CommandPaletteTheme : IThemeExtension<CommandPaletteTheme>
{
    public float Width { get; init; } = 560f;
    public float MaxHeight { get; init; } = 440f;
    /// <summary>How far below the window's top the card sits.</summary>
    public float TopOffset { get; init; } = 90f;

    /// <summary>The dimming behind the card; null takes the dialog default.</summary>
    public Color? Scrim { get; init; }

    /// <summary>The card's fill; null takes the theme's surface.</summary>
    public Color? Background { get; init; }
    /// <summary>The card's outline; null takes the theme's border.</summary>
    public Color? Border { get; init; }
    public float CornerRadius { get; init; } = 8f;
    /// <summary>The rules between the query, the list and the footer; null takes the border.</summary>
    public Color? Divider { get; init; }

    public float HeaderHeight { get; init; } = 48f;
    /// <summary>The query field's look over the theme's text field; null keeps that.</summary>
    public TextInputStyle? Field { get; init; }
    public float FieldFontSize { get; init; } = 13.5f;

    public Thickness ListPadding { get; init; } = new(6);
    /// <summary>A row with a title only, and one with a detail line under it.</summary>
    public float RowHeight { get; init; } = 32f;
    public float DetailRowHeight { get; init; } = 46f;
    public float RowCornerRadius { get; init; } = 6f;
    public Thickness RowPadding { get; init; } = new(0, 10, 0, 10);
    public float RowGap { get; init; } = 10f;
    /// <summary>The chosen row; null takes the theme's primary, faint.</summary>
    public Color? SelectedBackground { get; init; }
    /// <summary>A row under the pointer; null takes the theme's surface variant.</summary>
    public Color? HoverBackground { get; init; }

    public float TitleFontSize { get; init; } = 13f;
    /// <summary>The title; null inherits the text colour.</summary>
    public Color? Title { get; init; }
    public float DetailFontSize { get; init; } = 11f;
    /// <summary>The detail line; null takes the theme's secondary.</summary>
    public Color? Detail { get; init; }

    /// <summary>The group, as a column or a tag; null takes the theme's secondary.</summary>
    public Color? Group { get; init; }
    /// <summary>A trailing group tag's fill; null takes the theme's surface variant.</summary>
    public Color? GroupBackground { get; init; }
    public float GroupFontSize { get; init; } = 10f;
    /// <summary>The group's face; a leading column reads best in a monospace one. Null inherits.</summary>
    public string? GroupFontFamily { get; init; }
    /// <summary>A leading group column's width.</summary>
    public float GroupWidth { get; init; } = 84f;

    /// <summary>"No matches" and the footer; null takes the theme's secondary.</summary>
    public Color? Muted { get; init; }
    public float MutedFontSize { get; init; } = 11f;

    public static CommandPaletteTheme CreateDefault(Theme theme) => new();
}
