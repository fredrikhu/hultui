using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public static class StyleResolver
{
    public static readonly ResolvedInheritedStyle DefaultStyle = new(
        FontFamily: "sans-serif",
        FontSize: 16f,
        FontWeight: FontWeight.Regular,
        TextColor: new Color(0, 0, 0, 255),
        TextDirection: TextDirection.Ltr,
        Cursor: CursorType.Default);

    public static void Resolve(Element root)
    {
        ResolveRecursive(root, DefaultStyle, null);
    }

    public static void Resolve(Element root, ResolvedInheritedStyle rootDefaults)
    {
        ResolveRecursive(root, rootDefaults, null);
    }

    public static void Resolve(Element root, Theme theme)
    {
        ResolveRecursive(root, theme.ToResolvedStyle(), theme);
    }

    /// <summary>Resolves <paramref name="element"/> as the style pass would as a child of a
    /// parent that resolved to <paramref name="parent"/> — for an element realized outside the
    /// pass that must come out the same as one the pass resolved.</summary>
    internal static void ResolveUnder(Element element, ResolvedInheritedStyle parent, Theme? theme)
    {
        ResolveRecursive(element, parent, theme);
    }

    private static void ResolveRecursive(Element element, ResolvedInheritedStyle parent, Theme? theme)
    {
        // The theme first. An element that takes its text style from the theme — a
        // button's label colour, size and weight, a toast's, a text field's — writes it
        // into its own inherited style here, and that is what this pass has to resolve, for
        // the element and for the children inheriting from it. The other way round, those
        // writes landed a pass late: a button's label drew in its parent's colour and size
        // until something else happened to dirty the button (being enabled, say), and one
        // that nothing touched again kept them for good. No ApplyTheme reads the resolved
        // style, so nothing depends on the old order.
        if (theme is not null)
            element.ApplyTheme(theme);

        var own = element.InheritedRead;
        var resolved = new ResolvedInheritedStyle(
            FontFamily: own.FontFamily ?? parent.FontFamily,
            FontSize: own.FontSize ?? parent.FontSize,
            FontWeight: own.FontWeight ?? parent.FontWeight,
            TextColor: own.TextColor ?? parent.TextColor,
            TextDirection: own.TextDirection ?? parent.TextDirection,
            Cursor: own.Cursor ?? parent.Cursor,
            FontSlant: own.FontSlant ?? parent.FontSlant,
            TextAlign: own.TextAlign ?? parent.TextAlign,
            Decoration: own.Decoration ?? parent.Decoration,
            LineHeight: own.LineHeight ?? parent.LineHeight,
            LetterSpacing: own.LetterSpacing ?? parent.LetterSpacing);

        // What text is measured with changed — a theme with another font size, a FontSize set on
        // a live element — so its measured size has: lay it out again. Without this the layout
        // kept the old size until something else touched the element (the style pass is the only
        // place that sees the change), and the measure cache, which answers for unchanged
        // elements, kept it for good. A colour or decoration changes nothing measured.
        if (!SameMetrics(element.ResolvedInherited, resolved))
            element.MarkLayoutDirty();
        element.ResolvedInherited = resolved;

        // By index: foreach over the Children interface boxes an enumerator per container.
        var children = element.Children;
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            // Propagate overlay host and focus manager down the tree
            child.OverlayHost = element.OverlayHost;
            child.FocusManager = element.FocusManager;

            if ((child.DirtyFlags & DirtyFlags.Style) != 0)
                ResolveRecursive(child, resolved, theme);
        }
    }

    /// <summary>Whether two styles measure text alike: everything shaping and line breaking
    /// read, nothing only painting reads (colour, decoration, alignment, cursor).</summary>
    internal static bool SameMetrics(in ResolvedInheritedStyle a, in ResolvedInheritedStyle b) =>
        a.FontSize == b.FontSize
        && a.FontWeight == b.FontWeight
        && a.FontSlant == b.FontSlant
        && a.LineHeight == b.LineHeight
        && a.LetterSpacing == b.LetterSpacing
        && a.TextDirection == b.TextDirection
        && string.Equals(a.FontFamily, b.FontFamily, StringComparison.Ordinal);
}
