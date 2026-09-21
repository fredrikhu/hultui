using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The box every <see cref="Elements.ButtonElement"/> shares, and one <see cref="ButtonVariantTheme"/>
/// per variant for what differs between them.
///
/// <para>Every colour on a variant is nullable, so a one-knob override keeps the palette's:
/// <c>Theme.Dark.With(new ButtonTheme { Secondary = new() { Border = brand } })</c> recolours
/// the outline and leaves fill, text and hover as the palette resolves them. The palette
/// defaults are: <see cref="Primary"/> and <see cref="Danger"/> filled with their semantic
/// colour and no visible border; <see cref="Secondary"/> outlined with <see cref="Theme.Border"/>
/// on no fill, in the text colour; <see cref="Ghost"/> with neither, in the primary colour.</para>
///
/// <para>The border is one width for all variants, and a variant without a visible border
/// carries it transparent, so switching a button between variants never moves its
/// neighbours. Layout reads the width regardless of colour, and a fully transparent border
/// is not drawn.</para>
/// </summary>
public sealed class ButtonTheme : IThemeExtension<ButtonTheme>
{
    public CornerRadius CornerRadius { get; init; } = new(6f);
    public Thickness Padding { get; init; } = new(8, 16, 8, 16);
    public float FontSize { get; init; } = 14f;
    public FontWeight FontWeight { get; init; } = FontWeight.SemiBold;

    /// <summary>Border width for every variant; see the type remarks for why it is shared.</summary>
    public Thickness BorderWidth { get; init; } = new(1f);

    /// <summary>Hover multiplier for a <em>filled</em> variant's background.</summary>
    public float HoverDarken { get; init; } = 0.85f;

    /// <summary>Pressed multiplier for a <em>filled</em> variant's background.</summary>
    public float PressedDarken { get; init; } = 0.7f;

    /// <summary>Hover fill for an <em>unfilled</em> variant: its text colour at this alpha.</summary>
    public float HoverTint { get; init; } = 0.08f;

    /// <summary>Pressed fill for an <em>unfilled</em> variant: its text colour at this alpha.</summary>
    public float PressedTint { get; init; } = 0.16f;

    public ButtonVariantTheme Primary { get; init; } = new();
    public ButtonVariantTheme Secondary { get; init; } = new();
    public ButtonVariantTheme Danger { get; init; } = new();
    public ButtonVariantTheme Ghost { get; init; } = new();

    /// <summary>
    /// The sizes are literal rather than derived from the palette: they are the values this
    /// control already shipped with. The colours are all resolved from the palette by
    /// <see cref="Elements.ButtonElement"/>, so a theme that says nothing here already looks
    /// right in the dark.
    /// </summary>
    public static ButtonTheme CreateDefault(Theme theme) => new();

    /// <summary>This theme's knobs for one variant.</summary>
    public ButtonVariantTheme For(Elements.ButtonVariant variant) => variant switch
    {
        Elements.ButtonVariant.Secondary => Secondary,
        Elements.ButtonVariant.Danger => Danger,
        Elements.ButtonVariant.Ghost => Ghost,
        _ => Primary,
    };
}

/// <summary>
/// What one <see cref="Elements.ButtonVariant"/> looks like. Null means "what the palette
/// says for this variant" — see <see cref="ButtonTheme"/> for the resolution.
/// </summary>
public sealed class ButtonVariantTheme
{
    /// <summary>Fill at rest. Null: the variant's semantic colour for a filled variant, none
    /// for an outlined or ghost one.</summary>
    public Color? Background { get; init; }

    /// <summary>Fill under the pointer. Null: the rest fill darkened by
    /// <see cref="ButtonTheme.HoverDarken"/> when filled, otherwise the text colour at
    /// <see cref="ButtonTheme.HoverTint"/>.</summary>
    public Color? HoverBackground { get; init; }

    /// <summary>Fill while pressed. Null: as hover, with the pressed factors.</summary>
    public Color? PressedBackground { get; init; }

    /// <summary>Border at rest. Null: <see cref="Theme.Border"/> for the secondary variant,
    /// transparent for the rest.</summary>
    public Color? Border { get; init; }

    /// <summary>Border under the pointer. Null keeps <see cref="Border"/>.</summary>
    public Color? HoverBorder { get; init; }

    /// <summary>Label colour. Null: the semantic text colour for a filled variant, the theme's
    /// text colour for secondary, the primary colour for ghost.</summary>
    public Color? Text { get; init; }
}
