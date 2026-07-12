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

    private static void ResolveRecursive(Element element, ResolvedInheritedStyle parent, Theme? theme)
    {
        var own = element.Inherited;
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

        element.ResolvedInherited = resolved;

        if (theme is not null)
            element.ApplyTheme(theme);

        foreach (var child in element.Children)
        {
            // Propagate overlay host and focus manager down the tree
            child.OverlayHost = element.OverlayHost;
            child.FocusManager = element.FocusManager;

            if ((child.DirtyFlags & DirtyFlags.Style) != 0)
                ResolveRecursive(child, resolved, theme);
        }
    }
}
