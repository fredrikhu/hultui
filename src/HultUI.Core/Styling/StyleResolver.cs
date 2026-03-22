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
        ResolveRecursive(root, DefaultStyle);
    }

    public static void Resolve(Element root, ResolvedInheritedStyle rootDefaults)
    {
        ResolveRecursive(root, rootDefaults);
    }

    private static void ResolveRecursive(Element element, ResolvedInheritedStyle parent)
    {
        var own = element.Inherited;
        var resolved = new ResolvedInheritedStyle(
            FontFamily: own.FontFamily ?? parent.FontFamily,
            FontSize: own.FontSize ?? parent.FontSize,
            FontWeight: own.FontWeight ?? parent.FontWeight,
            TextColor: own.TextColor ?? parent.TextColor,
            TextDirection: own.TextDirection ?? parent.TextDirection,
            Cursor: own.Cursor ?? parent.Cursor);

        element.ResolvedInherited = resolved;

        foreach (var child in element.Children)
        {
            if (child.DirtyFlags.HasFlag(DirtyFlags.Style))
                ResolveRecursive(child, resolved);
        }
    }
}
