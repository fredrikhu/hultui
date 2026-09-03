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
    /// <param name="wrap">
    /// Whether reaching the end returns to the first element. True is what a window
    /// wants — Tab cycles forever, because there is nowhere else for focus to go. False
    /// is what an <em>embedded</em> tree wants: returning null at the end is how the
    /// host learns that Tab should leave the HultUI region and carry on into whatever
    /// is next in its own tab order.
    /// </param>
    public static Element? FindNext(Element root, Element? current, bool wrap = true)
    {
        var focusable = CollectFocusable(root);
        if (focusable.Count == 0) return null;

        if (current is null)
            return focusable[0];

        int index = focusable.IndexOf(current);
        if (index < 0)
            return focusable[0];

        int next = index + 1;
        if (next >= focusable.Count)
            return wrap ? focusable[0] : null;
        return focusable[next];
    }

    /// <inheritdoc cref="FindNext"/>
    public static Element? FindPrevious(Element root, Element? current, bool wrap = true)
    {
        var focusable = CollectFocusable(root);
        if (focusable.Count == 0) return null;

        if (current is null)
            return focusable[^1];

        int index = focusable.IndexOf(current);
        if (index < 0)
            return focusable[^1];

        int previous = index - 1;
        if (previous < 0)
            return wrap ? focusable[^1] : null;
        return focusable[previous];
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
