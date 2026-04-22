using HultUI.Core.Elements;

namespace HultUI.Core.Events;

/// <summary>
/// Collects focusable elements and provides next/previous navigation for Tab/Shift+Tab.
/// Follows HTML <c>tabindex</c> semantics: positive values are sequenced first in ascending
/// order, then zero values in document order. Negative values are skipped (still focusable
/// programmatically via <see cref="Element.Focus"/>).
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
        var inDocOrder = new List<Element>();
        CollectRecursive(root, inDocOrder);

        // OrderBy is a stable sort, so equal TabIndex values preserve document order.
        // Mapping 0 → int.MaxValue places zero-indexed elements after all positive ones.
        return inDocOrder
            .OrderBy(e => e.TabIndex == 0 ? int.MaxValue : e.TabIndex)
            .ToList();
    }

    private static void CollectRecursive(Element element, List<Element> result)
    {
        if (element.IsFocusable && element.TabIndex >= 0)
            result.Add(element);

        foreach (var child in element.Children)
            CollectRecursive(child, result);
    }
}
