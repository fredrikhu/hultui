using HultUI.Core.Layout;
using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// The box every <see cref="Elements.ButtonElement"/> shares, and one <see cref="ButtonStyle"/>
/// per variant for what differs between them.
///
/// <para>Every knob on a style is nullable, so a one-knob override keeps the rest:
/// <c>Theme.Dark.With(new ButtonTheme { Secondary = new() { Border = brand } })</c> recolours
/// the outline and leaves fill, text, hover and geometry as the theme resolves them. The
/// palette defaults are: <see cref="Primary"/> and <see cref="Danger"/> filled with their
/// semantic colour and no visible border; <see cref="Secondary"/> outlined with
/// <see cref="Theme.Border"/> on no fill, in the text colour; <see cref="Ghost"/> with
/// neither, in the primary colour.</para>
///
/// <para>A button resolves each knob in this order: its own <see cref="Elements.ButtonElement.Style"/>,
/// then the variant's style here, then the shared value on this theme, then the palette.
/// A reusable look is therefore a <see cref="ButtonStyle"/> an app declares once — a round
/// icon button, a toolbar button — and applies per element, while the variant keeps
/// supplying whatever colours the style does not name.</para>
///
/// <para>The border is one width for all variants, and a variant without a visible border
/// carries it transparent, so switching a button between variants never moves its
/// neighbours. Layout reads the width regardless of colour, and a fully transparent border
/// is not drawn. A style that sets its own <see cref="ButtonStyle.BorderWidth"/> has opted
/// out of that guarantee knowingly.</para>
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

    /// <summary>
    /// What a disabled button is drawn at, applied to the whole button including its glyph
    /// and label. Opacity rather than a second set of colours, so a style needs no disabled
    /// twin and an icon drawn in its own colour dims with the rest.
    /// </summary>
    public float DisabledOpacity { get; init; } = 0.45f;

    public ButtonStyle Primary { get; init; } = new();
    public ButtonStyle Secondary { get; init; } = new();
    public ButtonStyle Danger { get; init; } = new();
    public ButtonStyle Ghost { get; init; } = new();

    /// <summary>
    /// The sizes are literal rather than derived from the palette: they are the values this
    /// control already shipped with. The colours are all resolved from the palette by
    /// <see cref="Elements.ButtonElement"/>, so a theme that says nothing here already looks
    /// right in the dark.
    /// </summary>
    public static ButtonTheme CreateDefault(Theme theme) => new();

    /// <summary>This theme's style for one variant.</summary>
    public ButtonStyle For(Elements.ButtonVariant variant) => variant switch
    {
        Elements.ButtonVariant.Secondary => Secondary,
        Elements.ButtonVariant.Danger => Danger,
        Elements.ButtonVariant.Ghost => Ghost,
        _ => Primary,
    };
}

/// <summary>
/// What a button looks like: colours and geometry, every one optional. Null means "what
/// the next layer says" — the variant's style in the theme, then the theme's shared
/// values, then the palette; see <see cref="ButtonTheme"/> for the order.
///
/// <para>Declare one as a static and apply it with <c>.Style(...)</c> to make a reusable
/// look, or set it on a <see cref="ButtonTheme"/> variant to restyle every button of that
/// variant. A style names only what it changes, so it follows a light or dark palette
/// for everything else.</para>
/// </summary>
public sealed class ButtonStyle
{
    // ── Colours ──────────────────────────────────────────────────────────

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

    // ── Geometry ─────────────────────────────────────────────────────────

    /// <summary>Null: <see cref="ButtonTheme.Padding"/>.</summary>
    public Thickness? Padding { get; init; }

    /// <summary>Null: <see cref="ButtonTheme.CornerRadius"/>, or half the box when
    /// <see cref="Circular"/>.</summary>
    public CornerRadius? CornerRadius { get; init; }

    /// <summary>Null: <see cref="ButtonTheme.BorderWidth"/>, the width every variant shares.</summary>
    public Thickness? BorderWidth { get; init; }

    /// <summary>Null: <see cref="ButtonTheme.FontSize"/>. A child with its own size wins
    /// over this, as it does over any inherited size.</summary>
    public float? FontSize { get; init; }

    /// <summary>Null: <see cref="ButtonTheme.FontWeight"/>.</summary>
    public FontWeight? FontWeight { get; init; }

    /// <summary>A fixed square box, for an icon-only button. Null: sized by content.</summary>
    public float? Size { get; init; }

    /// <summary>Null: no minimum.</summary>
    public Dimension? MinWidth { get; init; }

    /// <summary>Null: no minimum.</summary>
    public Dimension? MinHeight { get; init; }

    /// <summary>
    /// Round: the corner radius is half the box. With <see cref="Size"/> the radius is known
    /// up front and the window's focus ring follows it; without, it is taken from the laid-out
    /// box at paint time and the ring keeps the theme's radius.
    /// </summary>
    public bool? Circular { get; init; }
}
