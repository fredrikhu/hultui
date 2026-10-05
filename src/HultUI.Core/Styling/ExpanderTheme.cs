using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>Which end of an expander's header the chevron sits at, and so which way it turns.</summary>
public enum ExpanderChevron : byte
{
    /// <summary>Before the title, pointing right when closed and down when open — a
    /// disclosure triangle, as in a file tree or a settings page's "Advanced".</summary>
    Leading,
    /// <summary>At the far end, pointing down when closed and up when open — WinUI's
    /// expander card.</summary>
    Trailing,
}

/// <summary>
/// How a <see cref="Elements.ExpanderElement"/> looks. The default is a plain disclosure: a
/// chevron and a title, a hover fill, no card. A border, a header fill and a body fill make it
/// a card, its header and body joined by a rule. Colours left null come from the palette.
/// </summary>
public sealed class ExpanderTheme : IThemeExtension<ExpanderTheme>
{
    public ExpanderChevron Chevron { get; init; } = ExpanderChevron.Leading;
    /// <summary>The chevron's width; it is half as tall.</summary>
    public float ChevronSize { get; init; } = 9f;
    public float ChevronStrokeWidth { get; init; } = 1.5f;
    /// <summary>Null takes the palette's secondary colour.</summary>
    public Color? ChevronColor { get; init; }

    public float HeaderMinHeight { get; init; } = 32f;
    public Thickness HeaderPadding { get; init; } = new(4, 8, 4, 8);
    /// <summary>Between the chevron, the title and the actions.</summary>
    public float HeaderGap { get; init; } = 8f;
    /// <summary>The header's fill; null leaves it transparent.</summary>
    public Color? HeaderBackground { get; init; }
    /// <summary>The header under the pointer; null takes the palette's surface variant.</summary>
    public Color? HoverBackground { get; init; }
    /// <summary>The header while pressed; null keeps the hover fill.</summary>
    public Color? PressedBackground { get; init; }
    public float FontSize { get; init; } = 14f;
    public FontWeight FontWeight { get; init; } = FontWeight.Medium;
    /// <summary>The title's colour; null inherits.</summary>
    public Color? Text { get; init; }

    /// <summary>
    /// Around the body. Null on the leading side lines the body up under the title — the
    /// header's padding, the chevron and the gap — which is what a disclosure wants; a card
    /// sets it outright.
    /// </summary>
    public Thickness? BodyPadding { get; init; }
    public Color? BodyBackground { get; init; }

    /// <summary>A border round the whole expander, with a rule of the same colour between
    /// header and body while it is open; null draws none.</summary>
    public Color? Border { get; init; }
    public float BorderWidth { get; init; } = 1f;
    /// <summary>The corners of the whole: the header's alone when closed, shared between
    /// header (top) and body (bottom) when open.</summary>
    public float CornerRadius { get; init; } = 4f;

    /// <summary>How long opening and closing take: the body's height and the chevron's turn
    /// move together. Zero snaps.</summary>
    public float TransitionSeconds { get; init; } = 0.18f;

    public static ExpanderTheme CreateDefault(Theme theme) => new();
}
