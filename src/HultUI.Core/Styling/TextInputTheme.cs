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

/// <summary>
/// What one text field looks like, every knob optional: the element's own
/// <see cref="Elements.TextInputElement.Style"/> (or <see cref="Elements.TextAreaElement.Style"/>),
/// resolved ahead of <see cref="TextInputTheme"/>, which resolves ahead of the palette.
///
/// <para>Declare one as a static and apply it with <c>.Style(...)</c> for a reusable look —
/// a bordered form field, a compact grid cell — where the theme's field is flush. A style
/// names only what it changes, so it follows a light or dark palette for the rest, and a
/// height the style does not name is left to the element, which centres its line inside
/// whatever height it was given. This is what a bordered wrapper box used to be for.</para>
/// </summary>
public sealed class TextInputStyle
{
    // ── Colours ──────────────────────────────────────────────────────────

    /// <summary>Fill at rest. Null: <see cref="TextInputTheme.Background"/>, then the palette's background.</summary>
    public Color? Background { get; init; }

    /// <summary>Fill while focused. Null: <see cref="TextInputTheme.FocusBackground"/>, then the rest fill.</summary>
    public Color? FocusBackground { get; init; }

    /// <summary>Border at rest. Null: <see cref="TextInputTheme.BorderColor"/>, then <see cref="Theme.Border"/>.</summary>
    public Color? Border { get; init; }

    /// <summary>Border while focused. Null: <see cref="TextInputTheme.FocusBorderColor"/>, then <see cref="Theme.Primary"/>.</summary>
    public Color? FocusBorder { get; init; }

    /// <summary>The bottom edge; see <see cref="TextInputTheme.UnderlineColor"/>. Null: the theme's.</summary>
    public Color? Underline { get; init; }

    /// <summary>The bottom edge while focused. Null: the theme's, then <see cref="Underline"/>.</summary>
    public Color? FocusUnderline { get; init; }

    /// <summary>Null: <see cref="TextInputTheme.PlaceholderColor"/>, then <see cref="Theme.Secondary"/>.</summary>
    public Color? Placeholder { get; init; }

    /// <summary>Null: <see cref="TextInputTheme.CursorColor"/>, then the text colour.</summary>
    public Color? Cursor { get; init; }

    /// <summary>Null: <see cref="TextInputTheme.SelectionColor"/>, then the primary colour at low alpha.</summary>
    public Color? Selection { get; init; }

    // ── Geometry ─────────────────────────────────────────────────────────

    /// <summary>Null: <see cref="TextInputTheme.Padding"/>.</summary>
    public Thickness? Padding { get; init; }

    /// <summary>Null: <see cref="TextInputTheme.BorderWidth"/>.</summary>
    public Thickness? BorderWidth { get; init; }

    /// <summary>Null: <see cref="TextInputTheme.CornerRadius"/>.</summary>
    public CornerRadius? CornerRadius { get; init; }

    /// <summary>Null: <see cref="TextInputTheme.FontSize"/>. A builder <c>.FontSize()</c> on the
    /// element wins over both, as it always has.</summary>
    public float? FontSize { get; init; }

    /// <summary>Null: <see cref="TextInputTheme.UnderlineThickness"/>.</summary>
    public float? UnderlineThickness { get; init; }

    /// <summary>Null: <see cref="TextInputTheme.FocusUnderlineThickness"/>.</summary>
    public float? FocusUnderlineThickness { get; init; }
}
