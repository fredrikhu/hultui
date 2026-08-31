using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class TextInputTheme : IThemeExtension<TextInputTheme>
{
    public CornerRadius CornerRadius { get; init; } = new(4f);
    public Thickness Padding { get; init; } = new(4, 8, 4, 8);
    public Thickness BorderWidth { get; init; } = new(1f);
    public float FontSize { get; init; } = 14f;
    public Color? Background { get; init; }
    public Color? BorderColor { get; init; }
    public Color? FocusBorderColor { get; init; }
    public Color? PlaceholderColor { get; init; }
    public Color? SelectionColor { get; init; }
    public Color? CursorColor { get; init; }

    /// <summary>
    /// A separate colour for the bottom edge — the line a text field announces itself with.
    ///
    /// <para>Not the same thing as the two-tone border a button has, and measurably so: on a
    /// dark WinUI text box the sides are <c>#303030</c> and the bottom is <c>#9A9A9A</c>, which
    /// is <c>ControlStrongFillColorDefault</c> rather than a slightly darker stroke. It is the
    /// single loudest thing about the control at rest, and drawing it as an ordinary border
    /// makes a text box look like a disabled one.</para>
    ///
    /// <para>Null draws no underline at all, which is what these controls did before this
    /// existed — so a theme that says nothing about it is unchanged.</para>
    /// </summary>
    public Color? UnderlineColor { get; init; }

    /// <summary>The underline while focused. Null keeps <see cref="UnderlineColor"/>.</summary>
    public Color? FocusUnderlineColor { get; init; }

    public float UnderlineThickness { get; init; } = 1f;

    /// <summary>Thicker while focused: WinUI grows the accent underline to 2px and leaves the
    /// other three sides exactly as they were, which is the part that is easy to get wrong by
    /// lighting the whole ring instead.</summary>
    public float FocusUnderlineThickness { get; init; } = 2f;

    /// <summary>Fill while focused — WinUI's <c>ControlFillColorInputActive</c>, which is
    /// *darker* than the resting fill on a dark theme rather than lighter. Null keeps
    /// <see cref="Background"/>.</summary>
    public Color? FocusBackground { get; init; }

    /// <summary>
    /// The built-in defaults are literal rather than derived from the palette: they are the
    /// values these controls already shipped with, and deriving them here would repaint
    /// every existing app. New extensions should prefer deriving from the theme — see
    /// <see cref="IThemeExtension{TSelf}"/>.
    /// </summary>
    public static TextInputTheme CreateDefault(Theme theme) => new();
}
