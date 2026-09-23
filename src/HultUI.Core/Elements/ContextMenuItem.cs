namespace HultUI.Core.Elements;

public sealed class ContextMenuItem
{
    public string Label { get; init; } = "";
    public Action? Action { get; init; }
    public bool IsSeparator { get; init; }
    public bool IsDisabled { get; init; }
    public string? ShortcutHint { get; init; }

    /// <summary>Optional leading glyph (rendered as text; the app maps its own
    /// icon set to characters). When any item in a menu sets one, every item
    /// reserves an icon column so labels stay aligned.</summary>
    public string? Glyph { get; init; }

    /// <summary>
    /// Whether this item is <i>a choice</i>, and whether it is the one in force: true
    /// draws the theme's check mark in the icon column, false leaves the column empty,
    /// and <c>null</c> — the default — means the item is not a choice at all. An item that
    /// can be off reserves the column while it is off, so turning it on moves nothing.
    ///
    /// <para>This is how a current choice is marked, rather than by writing a bullet into
    /// <see cref="Label"/>: assistive tech is told the item is checked, where a label
    /// beginning "• " is simply read out as punctuation — and the spaces its unchecked
    /// siblings need in order to line up are read out with them.</para>
    /// </summary>
    public bool? IsChecked { get; init; }

    /// <summary>
    /// Optional leading icon as artwork, taking precedence over <see cref="Glyph"/>.
    /// Reserves the same icon column.
    ///
    /// <para>It is a factory over the colour rather than a plain image because the menu
    /// resolves that colour per item state — normal, disabled, or danger — and a
    /// pre-tinted raster could not follow it. The app is handed the resolved colour and
    /// returns matching artwork; icon sets that cache per colour (as HultUI.Icons does)
    /// make the repeat calls free.</para>
    /// </summary>
    public Func<Primitives.Color, Rendering.IImageSource?>? Icon { get; init; }

    /// <summary>Destructive items paint their glyph and label in the theme's
    /// danger colour.</summary>
    public bool IsDanger { get; init; }

    /// <summary>Nested items: the item opens a flyout submenu (hover, click,
    /// Enter or →) instead of running <see cref="Action"/>. One level of
    /// nesting is supported.</summary>
    public IReadOnlyList<ContextMenuItem>? Children { get; init; }

    public bool HasChildren => Children is { Count: > 0 };

    public static ContextMenuItem Separator() => new() { IsSeparator = true };
}
