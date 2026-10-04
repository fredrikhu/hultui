using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// How a <see cref="Builders.FindBar"/> looks. Every colour left null comes from the palette, so
/// an app that sets nothing gets a bar in its theme's colours; an app with a design of its own
/// sets the knobs here once, with <c>theme.With(new FindBarTheme { ... })</c>.
/// </summary>
public sealed class FindBarTheme : IThemeExtension<FindBarTheme>
{
    /// <summary>The bar's fill; null takes the theme's surface.</summary>
    public Color? Background { get; init; }

    /// <summary>The rule under the bar, which separates it from the text it searches; null
    /// takes the theme's border.</summary>
    public Color? Divider { get; init; }

    /// <summary>Which edges the rule is on; a bar above its text has it along the bottom.</summary>
    public Thickness DividerWidth { get; init; } = new(0, 0, 1, 0);

    public Thickness Padding { get; init; } = new(5, 12, 5, 12);

    /// <summary>Space between the bar's parts.</summary>
    public float Gap { get; init; } = 8f;

    /// <summary>The query field's look, over the theme's text field; null keeps that.</summary>
    public TextInputStyle? Field { get; init; }
    /// <summary>The query's face — a code view's bar reads best in its monospace. Null inherits.</summary>
    public string? FieldFontFamily { get; init; }

    /// <summary>The match count and error text. Null font family inherits.</summary>
    public string? StatusFontFamily { get; init; }
    public float StatusFontSize { get; init; } = 11f;
    /// <summary>The count's colour; null takes the theme's secondary.</summary>
    public Color? StatusColor { get; init; }
    /// <summary>"no matches" and a pattern error; null takes the theme's danger colour.</summary>
    public Color? StatusErrorColor { get; init; }

    /// <summary>The match-case and regular-expression chips, off and on. Null keeps the
    /// theme's secondary and primary buttons.</summary>
    public ButtonStyle? Toggle { get; init; }
    public ButtonStyle? ToggleOn { get; init; }
    /// <summary>The chips' labels ("Aa", ".*") read best in a monospace face; null inherits.</summary>
    public string? ToggleFontFamily { get; init; }

    /// <summary>The previous, next and close buttons; null keeps the theme's secondary button.</summary>
    public ButtonStyle? Button { get; init; }

    public static FindBarTheme CreateDefault(Theme theme) => new();
}
