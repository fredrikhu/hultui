using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The step buttons of a <see cref="Elements.NumericUpDownElement"/>. The field's box
/// itself reuses <see cref="DropdownTheme"/>, like the date and time pickers, so a form
/// row of dropdowns and number fields shares one shape.
/// </summary>
public sealed class NumericUpDownTheme : IThemeExtension<NumericUpDownTheme>
{
    /// <summary>Width of the up/down strip at the right-hand end.</summary>
    public float ButtonWidth { get; init; } = 22f;
    /// <summary>Chevron colour; null takes the theme's secondary.</summary>
    public Color? ChevronColor { get; init; }
    public float ChevronStrokeWidth { get; init; } = 1.25f;
    /// <summary>Fill behind a held button; null takes the theme's surface variant.</summary>
    public Color? PressedBackground { get; init; }

    /// <summary>How long a button must be held before it starts repeating.</summary>
    public float RepeatDelaySeconds { get; init; } = 0.4f;
    /// <summary>Seconds between repeated steps while held.</summary>
    public float RepeatIntervalSeconds { get; init; } = 0.06f;

    public static NumericUpDownTheme CreateDefault(Theme theme) => new();
}
