using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Events;

/// <summary>A D-pad press: which way the focus moves (docs/design/DPAD-FOCUS.md).</summary>
internal enum FocusDirection : byte { Up, Down, Left, Right }

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

    /// <summary>
    /// The focusable element a D-pad press in <paramref name="direction"/> goes to from
    /// <paramref name="current"/>: Android's <c>FocusFinder</c> rule over where each is in the window
    /// (<see cref="Element.WindowBorderBox"/>, scroll applied). Null when nothing lies that way.
    /// Candidates are Tab's (focusable, <see cref="Element.TabIndex"/> 0 or more), with a box, and
    /// neither inside <paramref name="current"/> nor around it. docs/design/DPAD-FOCUS.md §2.
    /// </summary>
    public static Element? FindInDirection(Element root, Element current, FocusDirection direction)
    {
        var scratch = RentList();
        try
        {
            CollectReachable(root, scratch);
            var from = current.WindowBorderBox;
            Element? best = null;
            Rect bestBox = default;
            for (int i = 0; i < scratch.Count; i++)
            {
                var candidate = scratch[i];
                if (ReferenceEquals(candidate, current) || Encloses(candidate, current) || Encloses(current, candidate))
                    continue;
                var box = candidate.WindowBorderBox;
                if (box.Width <= 0 || box.Height <= 0 || !IsToward(direction, from, box)) continue;
                if (HiddenFrom(candidate, box, current)) continue;
                if (best is null || IsBetter(direction, from, box, bestBox))
                {
                    best = candidate;
                    bestBox = box;
                }
            }
            return best;
        }
        finally { ReturnList(scratch); }
    }

    /// <summary>
    /// The focusable element nearest <paramref name="where"/> (centre to centre), for the focus to go
    /// to when the element that had it at that place left the tree. Null when nothing is focusable.
    /// </summary>
    public static Element? FindNearest(Element root, Rect where)
    {
        var scratch = RentList();
        try
        {
            CollectReachable(root, scratch);
            Element? best = null;
            float bestDistance = float.MaxValue;
            float cx = where.X + where.Width / 2f, cy = where.Y + where.Height / 2f;
            for (int i = 0; i < scratch.Count; i++)
            {
                var box = scratch[i].WindowBorderBox;
                if (box.Width <= 0 || box.Height <= 0) continue;
                float dx = box.X + box.Width / 2f - cx, dy = box.Y + box.Height / 2f - cy;
                float distance = dx * dx + dy * dy;
                if (distance < bestDistance)
                {
                    best = scratch[i];
                    bestDistance = distance;
                }
            }
            return best;
        }
        finally { ReturnList(scratch); }
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> lies outside the viewport of a scroll view the focus is not
    /// in. Moving inside a list walks on into its hidden part, which then scrolls in; from outside it,
    /// the hidden part is not there: on a Shield, Down from the rail's last door went to a card below the
    /// bottom of Home's scrolled page.
    /// </summary>
    private static bool HiddenFrom(Element candidate, Rect box, Element current)
    {
        for (var e = candidate.Parent; e is not null; e = e.Parent)
        {
            if (e is not ScrollViewElement view || Encloses(view, current)) continue;
            var shown = view.WindowContentBox;
            if (box.Right <= shown.X || box.X >= shown.Right || box.Bottom <= shown.Y || box.Y >= shown.Bottom)
                return true;
        }
        return false;
    }

    /// <summary>The scope's first stop the D-pad can reach (<see cref="CollectReachable"/>), for the
    /// window's own choice of focus; null when there is none.</summary>
    public static Element? FindFirstReachable(Element root)
    {
        var scratch = RentList();
        try
        {
            CollectReachable(root, scratch);
            if (scratch.Count == 0) return null;
            // Tab's order: a positive TabIndex first, in order, then document order.
            Element? best = null;
            foreach (var e in scratch)
                if (best is null || Order(e) < Order(best)) best = e;
            return best;
        }
        finally { ReturnList(scratch); }

        static int Order(Element e) => e.TabIndex == 0 ? int.MaxValue : e.TabIndex;
    }

    /// <summary>
    /// Tab's stops, less those in a subtree marked <see cref="Element.AccessibilityHidden"/>: what is
    /// covered (a page under a player's sheet) is no part of what a reader meets, nor of what a D-pad
    /// reaches. On a Shield, Right along the player's bar went to a cast card of the film's page under
    /// the player, and Back from there left the film.
    /// </summary>
    private static void CollectReachable(Element element, List<Element> result)
    {
        if (element.AccessibilityHidden) return;
        if (element.IsFocusable && element.TabIndex >= 0)
            result.Add(element);
        var children = element.Children;
        for (int i = 0; i < children.Count; i++)
            CollectReachable(children[i], result);
    }

    private static bool Encloses(Element outer, Element inner)
    {
        for (var e = inner.Parent; e is not null; e = e.Parent)
            if (ReferenceEquals(e, outer)) return true;
        return false;
    }

    // The rest is Android's FocusFinder (frameworks/base, core/java/android/view/FocusFinder.java),
    // as every TV app's D-pad moves.

    /// <summary>Whether <paramref name="to"/> lies in <paramref name="direction"/> from
    /// <paramref name="from"/>: Android's test (its far edge past the source's, and not starting
    /// behind it), and its centre past the source's leading edge, as the web's spatial navigation has
    /// it. Android's alone took what is mostly beside the source: on a Shield, Up from a top-row
    /// poster went to the rail's second door, beside the poster and a few pixels higher.</summary>
    private static bool IsToward(FocusDirection direction, Rect from, Rect to) => direction switch
    {
        FocusDirection.Left => (from.Right > to.Right || from.X >= to.Right) && from.X > to.X
            && to.X + to.Width / 2f < from.X,
        FocusDirection.Right => (from.X < to.X || from.Right <= to.X) && from.Right < to.Right
            && to.X + to.Width / 2f > from.Right,
        FocusDirection.Up => (from.Bottom > to.Bottom || from.Y >= to.Bottom) && from.Y > to.Y
            && to.Y + to.Height / 2f < from.Y,
        _ => (from.Y < to.Y || from.Bottom <= to.Y) && from.Bottom < to.Bottom
            && to.Y + to.Height / 2f > from.Bottom,
    };

    /// <summary>Whether <paramref name="to"/> overlaps the source across the direction: in the
    /// band the source would sweep moving that way.</summary>
    private static bool InBeam(FocusDirection direction, Rect from, Rect to) =>
        direction is FocusDirection.Left or FocusDirection.Right
            ? to.Bottom > from.Y && to.Y < from.Bottom
            : to.Right > from.X && to.X < from.Right;

    /// <summary>The gap from the source's leading edge to the near edge of <paramref name="to"/>.</summary>
    private static float MajorDistance(FocusDirection direction, Rect from, Rect to) => Math.Max(0f, direction switch
    {
        FocusDirection.Left => from.X - to.Right,
        FocusDirection.Right => to.X - from.Right,
        FocusDirection.Up => from.Y - to.Bottom,
        _ => to.Y - from.Bottom,
    });

    /// <summary>The gap from the source's leading edge to the far edge of <paramref name="to"/>.</summary>
    private static float MajorDistanceToFarEdge(FocusDirection direction, Rect from, Rect to) => Math.Max(1f, direction switch
    {
        FocusDirection.Left => from.X - to.X,
        FocusDirection.Right => to.Right - from.Right,
        FocusDirection.Up => from.Y - to.Y,
        _ => to.Bottom - from.Bottom,
    });

    /// <summary>How far the centres are apart across the direction.</summary>
    private static float MinorDistance(FocusDirection direction, Rect from, Rect to) =>
        direction is FocusDirection.Left or FocusDirection.Right
            ? Math.Abs(from.Y + from.Height / 2f - (to.Y + to.Height / 2f))
            : Math.Abs(from.X + from.Width / 2f - (to.X + to.Width / 2f));

    /// <summary>Whether <paramref name="a"/> is a better move than <paramref name="b"/>; both lie in
    /// the direction. One in the beam wins (across always; along, unless the other is nearer than its
    /// far edge), then the weighted distance: 13 times the gap squared plus the offset squared.</summary>
    private static bool IsBetter(FocusDirection direction, Rect from, Rect a, Rect b)
    {
        if (BeamBeats(direction, from, a, b)) return true;
        if (BeamBeats(direction, from, b, a)) return false;
        return Weighted(direction, from, a) < Weighted(direction, from, b);
    }

    private static bool BeamBeats(FocusDirection direction, Rect from, Rect a, Rect b)
    {
        if (!InBeam(direction, from, a) || InBeam(direction, from, b)) return false;
        if (direction is FocusDirection.Left or FocusDirection.Right) return true;
        return MajorDistance(direction, from, a) < MajorDistanceToFarEdge(direction, from, b);
    }

    private static float Weighted(FocusDirection direction, Rect from, Rect to)
    {
        float major = MajorDistance(direction, from, to), minor = MinorDistance(direction, from, to);
        return 13f * major * major + minor * minor;
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
