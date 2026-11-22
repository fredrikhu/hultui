using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public sealed class ContextMenuItem
{
    public string Label { get; init; } = "";

    /// <summary>
    /// Whether <see cref="Label"/> marks its mnemonic with <c>&amp;</c>: <c>"&amp;Rename…"</c> is
    /// shown as "Rename…" with the R underlined when the menu is used from the keyboard, and R
    /// chooses it. <c>&amp;&amp;</c> is an ampersand. False, the default: the label is shown as
    /// written, which is right for text that comes from data (a file name, a title), where an
    /// ampersand is just an ampersand.
    /// </summary>
    public bool UseMnemonic { get; init; }

    /// <summary>The label as drawn and read out: <see cref="Label"/> without its marker where
    /// <see cref="UseMnemonic"/> is set, <see cref="Label"/> otherwise.</summary>
    public string DisplayLabel => UseMnemonic ? Parsed.Label : Label;

    /// <summary>The mnemonic, lower case, or '\0' for none.</summary>
    public char Mnemonic
    {
        get
        {
            if (!UseMnemonic) return '\0';
            var (label, index) = Parsed;
            if (index < 0) return '\0';
            char c = label[index];
            return char.IsSurrogate(c) ? '\0' : char.ToLowerInvariant(c);
        }
    }

    /// <summary>Where the mnemonic is in <see cref="DisplayLabel"/>; -1 for none.</summary>
    internal int MnemonicIndex => UseMnemonic ? Parsed.Index : -1;

    /// <summary>The underline under the mnemonic, for drawing <see cref="DisplayLabel"/>; null for none.</summary>
    internal TextSpan[]? MnemonicSpans
    {
        get
        {
            if (!UseMnemonic) return null;
            var parsed = Parsed;
            return parsed.Index < 0 ? null
                : parsed.Spans ??= [new TextSpan(parsed.Index, 1, Color.Transparent, Decoration: TextDecoration.Underline)];
        }
    }

    // Parsed once, when first asked, and only for an item that uses mnemonics (a dropdown makes
    // hundreds that do not): items are immutable. A reader's bridge may ask off the UI thread,
    // where parsing twice is harmless; label and index are published as one object, so no thread
    // sees one without the other. One field: a dropdown makes an item per entry each time it opens.
    private sealed record ParsedLabel(string Label, int Index)
    {
        public TextSpan[]? Spans;
    }
    private ParsedLabel? _parsed;

    private ParsedLabel Parsed => _parsed ??= Parse();

    private ParsedLabel Parse()
    {
        var (label, index) = Mnemonics.Parse(Label);
        return new ParsedLabel(label, index);
    }

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
    /// Enter or →) instead of running <see cref="Action"/>. A flyout's own items
    /// may have children too, at any depth; ← and Escape close one level.</summary>
    public IReadOnlyList<ContextMenuItem>? Children { get; init; }

    public bool HasChildren => Children is { Count: > 0 };

    public static ContextMenuItem Separator() => new() { IsSeparator = true };
}
