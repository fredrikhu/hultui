using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// How a key chip (<see cref="Builders.Kbd"/>) looks. Colours left null come from the palette;
/// an app sets its own once, with <c>theme.With(new KbdTheme { ... })</c> or the theme's
/// <see cref="Theme.Kbd"/>.
/// </summary>
public sealed class KbdTheme : IThemeExtension<KbdTheme>
{
    /// <summary>The fill; null takes the theme's surface. Transparent for an outline only.</summary>
    public Color? Background { get; init; }

    /// <summary>The outline; null takes the theme's border.</summary>
    public Color? Border { get; init; }

    /// <summary>The keys' colour; null takes the theme's secondary.</summary>
    public Color? Text { get; init; }

    /// <summary>The keys' face — keys read best in a monospace one; null inherits.</summary>
    public string? FontFamily { get; init; }

    public float FontSize { get; init; } = 10f;
    public float CornerRadius { get; init; } = 4f;
    public Thickness Padding { get; init; } = new(2, 5, 2, 5);

    public static KbdTheme CreateDefault(Theme theme) => new();
}
