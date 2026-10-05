using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// How a <see cref="Builders.Popover"/>'s card looks, and how far it keeps from its anchor and
/// from the window's edges. Colours left null come from the palette.
/// </summary>
public sealed class PopoverTheme : IThemeExtension<PopoverTheme>
{
    /// <summary>The card's fill; null takes the palette's surface.</summary>
    public Color? Background { get; init; }
    /// <summary>The card's border; null takes the palette's border colour.</summary>
    public Color? Border { get; init; }
    public float BorderWidth { get; init; } = 1f;
    public float CornerRadius { get; init; } = 8f;
    public Thickness Padding { get; init; } = new(12);
    /// <summary>Drawn behind the card, back to front; null draws none.</summary>
    public IReadOnlyList<BoxShadow>? Shadows { get; init; } = [new BoxShadow(0, 8, 24, 0, new Color(0, 0, 0, 48))];

    /// <summary>Between the anchor and the card.</summary>
    public float Gap { get; init; } = 6f;
    /// <summary>The least the card keeps from the window's edges.</summary>
    public float WindowMargin { get; init; } = 8f;

    public static PopoverTheme CreateDefault(Theme theme) => new();
}
