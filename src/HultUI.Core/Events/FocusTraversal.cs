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
        var scratch = RentList();
        try
        {
            var focusable = CollectFocusable(root, scratch);
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
        finally { ReturnList(scratch); }
    }

    /// <inheritdoc cref="FindNext"/>
    public static Element? FindPrevious(Element root, Element? current, bool wrap = true)
    {
        var scratch = RentList();
        try
        {
            var focusable = CollectFocusable(root, scratch);
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
        finally { ReturnList(scratch); }
    }

    // Every Tab walks the whole tree into a list. Grown from empty each time, that list was
    // most of what a Tab allocated (~8 KB of arrays for 300 fields), so one list is kept per
    // thread and emptied after each use: it holds no element between Tabs, only its array,
    // sized for the most focusable elements one tree has had. A nested call (there is none
    // today) would find the slot empty and use a list of its own.
    [ThreadStatic] private static List<Element>? _scratch;

    private static List<Element> RentList()
    {
        var list = _scratch ?? [];
        _scratch = null;
        return list;
    }

    private static void ReturnList(List<Element> list)
    {
        list.Clear();
        _scratch = list;
    }

    private static List<Element> CollectFocusable(Element root, List<Element> inDocOrder)
    {
        CollectRecursive(root, inDocOrder);

        // Positive values are rare. Without one, every element collected has TabIndex 0, the
        // sort below keeps document order, and sorting it only cost a key array, a map and a
        // second list on every Tab.
        for (int i = 0; i < inDocOrder.Count; i++)
        {
            if (inDocOrder[i].TabIndex <= 0) continue;

            // OrderBy is a stable sort, so equal TabIndex values preserve document order.
            // Mapping 0 → int.MaxValue places zero-indexed elements after all positive ones.
            return inDocOrder
                .OrderBy(e => e.TabIndex == 0 ? int.MaxValue : e.TabIndex)
                .ToList();
        }
        return inDocOrder;
    }

    private static void CollectRecursive(Element element, List<Element> result)
    {
        if (element.IsFocusable && element.TabIndex >= 0)
            result.Add(element);

        // By index: foreach over the Children interface boxes an enumerator per container,
        // and this walks the whole tree on every Tab.
        var children = element.Children;
        for (int i = 0; i < children.Count; i++)
            CollectRecursive(children[i], result);
    }
}
