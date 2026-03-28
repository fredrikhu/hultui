using HultUI.Core.Elements;

namespace HultUI.Core.Events;

/// <summary>
/// Collects focusable elements in document order (depth-first, pre-order)
/// and provides next/previous navigation for Tab/Shift+Tab.
/// </summary>
internal static class FocusTraversal
{
    public static Element? FindNext(Element root, Element? current)
    {
        var focusable = CollectFocusable(root);
        if (focusable.Count == 0) return null;

        if (current is null)
            return focusable[0];

        int index = focusable.IndexOf(current);
        if (index < 0)
            return focusable[0];

        return focusable[(index + 1) % focusable.Count];
    }

    public static Element? FindPrevious(Element root, Element? current)
    {
        var focusable = CollectFocusable(root);
        if (focusable.Count == 0) return null;

        if (current is null)
            return focusable[^1];

        int index = focusable.IndexOf(current);
        if (index < 0)
            return focusable[^1];

        return focusable[(index - 1 + focusable.Count) % focusable.Count];
    }

    private static List<Element> CollectFocusable(Element root)
    {
        var result = new List<Element>();
        CollectRecursive(root, result);
        return result;
    }

    private static void CollectRecursive(Element element, List<Element> result)
    {
        if (element.IsFocusable)
            result.Add(element);

        foreach (var child in element.Children)
            CollectRecursive(child, result);
    }
}
