namespace HultUI.Core.Elements;

/// <summary>
/// Drives a selection that spans several <see cref="TextElement"/> labels sharing a
/// <see cref="TextElement.SelectionGroup"/> — e.g. the per-line code labels of a diff.
/// The window supplies the group's labels in document (top-to-bottom) order and the
/// anchor/focus of the drag; this sets each label's own selection range so the highlight
/// and copy behave as one continuous selection. Pure logic, no rendering or events.
/// </summary>
internal static class TextGroupSelection
{
    /// <summary>Clears the selection on every label in the group.</summary>
    public static void Clear(IReadOnlyList<TextElement> ordered)
    {
        foreach (var el in ordered)
            el.SetSelectionRange(0, 0);
    }

    /// <summary>
    /// Selects from <paramref name="anchorEl"/>@<paramref name="anchorOff"/> to
    /// <paramref name="focusEl"/>@<paramref name="focusOff"/> across the ordered labels:
    /// the two endpoints are partially selected, every label between them fully, and all
    /// others cleared. No-op if an endpoint isn't in the list (e.g. recycled off-screen).
    /// </summary>
    public static void Apply(IReadOnlyList<TextElement> ordered,
        TextElement anchorEl, int anchorOff, TextElement focusEl, int focusOff)
    {
        int ai = IndexOf(ordered, anchorEl);
        int fi = IndexOf(ordered, focusEl);
        if (ai < 0 || fi < 0) return;

        // Normalize so (lo) precedes (hi) in document order.
        int loIdx, loOff, hiIdx, hiOff;
        if (ai < fi || (ai == fi && anchorOff <= focusOff))
        {
            loIdx = ai; loOff = anchorOff; hiIdx = fi; hiOff = focusOff;
        }
        else
        {
            loIdx = fi; loOff = focusOff; hiIdx = ai; hiOff = anchorOff;
        }

        for (int j = 0; j < ordered.Count; j++)
        {
            var m = ordered[j];
            if (j < loIdx || j > hiIdx)
                m.SetSelectionRange(0, 0);                       // outside the span
            else if (loIdx == hiIdx)
                m.SetSelectionRange(loOff, hiOff);               // single label
            else if (j == loIdx)
                m.SetSelectionRange(loOff, m.ContentLength);     // first: to end
            else if (j == hiIdx)
                m.SetSelectionRange(0, hiOff);                   // last: from start
            else
                m.SetSelectionRange(0, m.ContentLength);         // middle: whole line
        }
    }

    /// <summary>
    /// The selected text across the group, each selected label on its own line
    /// (joined by '\n'), in document order.
    /// </summary>
    public static string GatherText(IReadOnlyList<TextElement> ordered)
    {
        var parts = new List<string>();
        foreach (var el in ordered)
            if (el.HasSelection)
                parts.Add(el.SelectedText);
        return string.Join("\n", parts);
    }

    private static int IndexOf(IReadOnlyList<TextElement> list, TextElement target)
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], target)) return i;
        return -1;
    }
}
