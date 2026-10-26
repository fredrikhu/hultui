using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI.Core.Accessibility;

public static partial class AccessibilityTreeBuilder
{
    /// <summary>
    /// What a layout refresh found to change in an applied tree, kept by the window between
    /// refreshes so that finding nothing allocates nothing: the nodes that moved in their scroll
    /// views' content, the ones that went out of view or came into it, the scroll views whose offset
    /// or geometry changed, the leaves made again for a change of their own properties, and the
    /// changes a diff would have reported, in its order. Nothing is written to the tree until the
    /// whole of it is found to be what a build would make (<see cref="TryRefreshInPlace"/>).
    /// </summary>
    internal sealed class LayoutRefresh
    {
        internal readonly List<PropertyChange> Changes = [];
        internal readonly List<(AccessibilityNode Node, Rect Rect)> Moves = [];
        internal readonly List<AccessibilityNode> OffscreenFlips = [];
        internal readonly List<(AccessibilityNode Node, Point Offset, Rect Viewport, Size Content)> Scrolls = [];
        internal readonly List<(AccessibilityNode Parent, int Index, AccessibilityNode Node)> Remade = [];
        internal readonly List<(AccessibilityNode Node, AccessibleTextVersion Version)> TextVersions = [];

        /// <summary>Why the last walk did not match, for tests: the first thing it found that a
        /// build would make otherwise. Null when it matched.</summary>
        internal string? Mismatch;

        /// <summary>Records why the walk stops, and stops it.</summary>
        internal bool Fail(string why)
        {
            Mismatch ??= why;
            return false;
        }

        /// <summary>Puts what was found in the tree (UI thread). Each write is one a reader on
        /// another thread sees whole: a box (<see cref="AccessibilityNode.MoveTo"/>), a state bit, a
        /// scroll view's fields (<see cref="AccessibilityNode.ReplaceScrollView"/>), a child
        /// (<see cref="AccessibilityNode.ReplaceChild"/>).</summary>
        internal void Apply()
        {
            foreach (var (node, rect) in Moves) node.MoveTo(rect);
            foreach (var node in OffscreenFlips) node.IsOffscreen = !node.IsOffscreen;
            foreach (var (node, offset, viewport, content) in Scrolls) node.ReplaceScrollView(offset, viewport, content);
            foreach (var (node, version) in TextVersions) node.ReplaceTextVersion(version);
            foreach (var (parent, index, node) in Remade) parent.ReplaceChild(index, node);
        }

        internal void Clear()
        {
            Changes.Clear();
            Moves.Clear();
            OffscreenFlips.Clear();
            Scrolls.Clear();
            Remade.Clear();
            TextVersions.Clear();
        }
    }

    /// <summary>
    /// A layout refresh without a new tree. Walks the elements under <paramref name="root"/> and the
    /// open overlays' contents (<paramref name="overlays"/>, in the order their parts follow the
    /// root's own children in <paramref name="tree"/>) by the builder's own rules — the nodes it
    /// would make, flattened where it flattens, with the items and content it would make — against
    /// the applied tree, and finds what a build would make otherwise there: a box moved in its scroll
    /// views' content, a node gone out of view or come into it, a scroll view scrolled or resized, a
    /// leaf whose own properties changed (made again, as <see cref="RemakeLeaf"/> makes it), with the
    /// changes a diff against such a build would report, in its order, into
    /// <paramref name="refresh"/>. Nothing is written to the tree. A wheel over a list of 1,000 rows
    /// built and diffed 3,001 nodes for one scroll view's offset.
    /// <para>False, with <see cref="LayoutRefresh.Mismatch"/> saying why, when a build would make
    /// another tree: a node another element's or none, children added, gone or in another order, a
    /// list whose items changed in number, a node with children whose own properties changed, the
    /// window's root node's own, or a change no diff tells (a node focusable or not, another text
    /// source). The window builds the tree then, as it always did.</para>
    /// </summary>
    internal static bool TryRefreshInPlace(AccessibilityNode tree, Element root,
        IReadOnlyList<(Element Content, float X, float Y)> overlays, FocusManager? focusManager, LayoutRefresh refresh)
    {
        refresh.Mismatch = null;
        if (tree.ElementId != root.Id) return refresh.Fail("another root");
        // As Build: the root's node, its children, then each overlay's part under it.
        var place = new Place(-0f, -0f, -0f, -0f);
        int rootChildren = 0;
        if (!MatchNode(root, tree, parent: null, index: -1, isRoot: true, focusManager, place, clip: default,
                default, default, refresh, ref rootChildren))
            return false;
        // An overlay's part is built under the root's node with the root's new offset added back to
        // its boxes (BuildAccessibilityTreeWithOverlays): the root may scroll, and the overlay does not.
        var rootScroll = root is ScrollViewElement sv ? new Point(sv.ScrollOffsetX, sv.ScrollOffsetY) : default;
        var appliedRootScroll = tree.ScrollOffset;
        var parts = tree.Children;
        int at = rootChildren;
        for (int i = 0; i < overlays.Count; i++, at++)
        {
            var (content, x, y) = overlays[i];
            if (at >= parts.Count || parts[at].ElementId != content.Id) return refresh.Fail("an overlay's part");
            float ox = -x, oy = -y;
            var partPlace = new Place(ox, oy, ox - rootScroll.X, oy - rootScroll.Y);
            int unused = 0;
            if (!MatchNode(content, parts[at], tree, at, isRoot: true, focusManager, partPlace, clip: default,
                    new Point(0f + appliedRootScroll.X, 0f + appliedRootScroll.Y),
                    new Point(0f + rootScroll.X, 0f + rootScroll.Y), refresh, ref unused))
                return false;
        }
        return at == parts.Count || refresh.Fail("an overlay's part");
    }

    /// <summary>
    /// The node of <paramref name="element"/> (a semantic one, or a tree's root), against
    /// <paramref name="applied"/>, the node in its place in the applied tree with its id, and its
    /// children after it. As <see cref="BuildChild"/> and <see cref="Build(Element, FocusManager?, float, float, Point)"/>.
    /// </summary>
    /// <param name="parent">The node <paramref name="applied"/> is a child of, at
    /// <paramref name="index"/>; null for the window's root node.</param>
    /// <param name="isRoot">A tree's root: never offscreen, and never named by the text inside it
    /// in place of its children.</param>
    /// <param name="clip">The visible region <paramref name="element"/>'s box is clipped by.</param>
    /// <param name="prevAbove">How far the scroll views above <paramref name="applied"/> are
    /// scrolled in the applied tree, summed as a diff sums them.</param>
    /// <param name="currAbove">The same as a build would make them.</param>
    /// <param name="childrenMatched">For the window's root node: how many of its children are its
    /// element's, before the overlays' parts.</param>
    private static bool MatchNode(Element element, AccessibilityNode applied, AccessibilityNode? parent, int index,
        bool isRoot, FocusManager? focusManager, Place place, Rect clip, Point prevAbove, Point currAbove,
        LayoutRefresh refresh, ref int childrenMatched)
    {
        var box = element.Layout.AbsoluteBorderBox;
        var unscrolled = place.Unscrolled(box);
        bool offscreen = !isRoot && !RectsIntersect(clip, place.OnScreen(box));
        var scrollView = element as ScrollViewElement;
        var offset = scrollView is null ? default : new Point(scrollView.ScrollOffsetX, scrollView.ScrollOffsetY);

        switch (Compare(element, applied, focusManager))
        {
            case Sameness.Untellable:
                return refresh.Fail("a change no diff tells");
            case Sameness.TextMoved:
            {
                // The caret or the selection of text made on request moved, or what is not its text:
                // the new version goes on the node in place, as a caret move's patch puts it.
                var previous = applied.TextVersion!;
                var current = ((IVersionedAccessibleTextSource)element.AccessibleTextSource!).CurrentVersion;
                if (!current.SameSelection(previous))
                    refresh.Changes.Add(new PropertyChange(applied, AccessibilityProperties.TextSelection, applied.TextStandIn(previous)));
                refresh.TextVersions.Add((applied, current));
                break;
            }
            case Sameness.Differs:
            {
                // What a build would make, compared as a diff compares it.
                var made = CreateNode(element, focusManager, unscrolled, place.ShiftX, place.ShiftY);
                if (!isRoot) made.IsOffscreen = offscreen;
                if (element is IAccessibleItemSource { PublishesItemNodes: true } items && element is not IAccessibleContentSource)
                {
                    made.ItemSource = items;
                    if (items.ItemCount > MaxEagerVirtualItems) made.IsVirtualItemContainer = true;
                }
                int start = refresh.Changes.Count;
                AccessibilityTreeDiffer.CompareNodes(refresh.Changes, applied, made, prevAbove, currAbove);
                // A change no diff tells (a value under a text) is read from the node: only a new
                // tree carries it.
                if (refresh.Changes.Count == start) return refresh.Fail("a change no diff tells");
                // Only a leaf is made again: the children of one with children would have to move.
                if (applied.Children.Count != 0) return refresh.Fail("a node with children changed");
                if (parent is null) return refresh.Fail("the root's node changed");
                refresh.Remade.Add((parent, index, made));
                // Its children, which it must have none of, as a build would make them.
                return MatchChildren(element, made, isRoot, focusManager, ChildPlace(element, place),
                    ClipForChildren(element, isRoot ? place.OnScreen(box) : clip, place), currAbove, currAbove,
                    refresh, ref childrenMatched, mustBeLeaf: true);
            }
        }

        // The same node: what moved of it is written in place.
        PlaceInPlace(applied, unscrolled, offscreen, prevAbove, currAbove, refresh);
        if (scrollView is not null)
        {
            var viewportBox = element.Layout.AbsoluteContentBox;
            var viewport = new Rect(viewportBox.X - place.ShiftX, viewportBox.Y - place.ShiftY, viewportBox.Width, viewportBox.Height);
            var content = new Size(scrollView.ContentWidth, scrollView.ContentHeight);
            if (!applied.HasScrollView(offset, viewport, content))
            {
                if (applied.ScrollOffset != offset)
                    refresh.Changes.Add(new PropertyChange(applied, AccessibilityProperties.ScrollPosition, applied.ScrollStandIn()));
                refresh.Scrolls.Add((applied, offset, viewport, content));
            }
        }
        var appliedOffset = applied.ScrollOffset;
        return MatchChildren(element, applied, isRoot, focusManager, ChildPlace(element, place),
            ClipForChildren(element, isRoot ? place.OnScreen(box) : clip, place),
            new Point(prevAbove.X + appliedOffset.X, prevAbove.Y + appliedOffset.Y),
            new Point(currAbove.X + offset.X, currAbove.Y + offset.Y), refresh, ref childrenMatched, mustBeLeaf: false);
    }

    /// <summary>The children of <paramref name="node"/>, as <see cref="BuildChild"/> makes them for
    /// <paramref name="element"/>: its content, its items, none when its name is the text inside it,
    /// or its element children's nodes.</summary>
    /// <param name="mustBeLeaf">The node is one made again: a build would make no children for it.</param>
    private static bool MatchChildren(Element element, AccessibilityNode node, bool isRoot, FocusManager? focusManager,
        Place place, Rect clip, Point prevAbove, Point currAbove, LayoutRefresh refresh, ref int childrenMatched,
        bool mustBeLeaf)
    {
        if (element is IAccessibleContentSource content)
        {
            if (node.ItemSource is not null) return refresh.Fail("a list's items");
            var box = element.Layout.AbsoluteContentBox;
            float scale = content.AccessibleContentScale > 0 ? content.AccessibleContentScale : 1f;
            var roots = content.AccessibleContent;
            if (roots.Count == 0) return node.Children.Count == 0 || refresh.Fail("content");
            if (mustBeLeaf) return refresh.Fail("content");
            return MatchContent(AccessibleOwnerSlots.SlotOf(element.Id), roots, node, place.Unscrolled(box),
                place.OnScreen(box), scale, clip, prevAbove, currAbove, refresh);
        }
        if (element is IAccessibleItemSource { PublishesItemNodes: true } items)
        {
            if (!ReferenceEquals(node.ItemSource, items)) return refresh.Fail("a list's items");
            if (items.ItemCount > MaxEagerVirtualItems)
                return (node.IsVirtualItemContainer && node.Children.Count == 0) || refresh.Fail("a list's items");
            if (node.IsVirtualItemContainer) return refresh.Fail("a list's items");
            if (mustBeLeaf && items.ItemCount != 0) return refresh.Fail("a list's items");
            return MatchItems(element, items, node, place, clip, prevAbove, currAbove, refresh);
        }
        if (node.ItemSource is not null || node.IsVirtualItemContainer) return refresh.Fail("a list's items");
        if (!isRoot && ConsumesTextContent(element)) return node.Children.Count == 0 || refresh.Fail("children");
        int matched = 0;
        if (!MatchElementChildren(element, node, ref matched, focusManager, place, clip, prevAbove, currAbove, refresh))
            return false;
        // The window's root node has the overlays' parts after its element's children.
        if (node.Parent is null && !mustBeLeaf)
        {
            childrenMatched = matched;
            return true;
        }
        return matched == node.Children.Count || refresh.Fail("children");
    }

    /// <summary>As <see cref="BuildChildren"/>: each element child's node, or its children's when it
    /// has none, at <paramref name="matched"/> and on among <paramref name="node"/>'s children.</summary>
    private static bool MatchElementChildren(Element element, AccessibilityNode node, ref int matched,
        FocusManager? focusManager, Place place, Rect clip, Point prevAbove, Point currAbove, LayoutRefresh refresh)
    {
        var children = element.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var childPlace = child.PositionRead.Type == Layout.PositionType.Sticky ? place.Shifted(child.StickyShift) : place;
            // As BuildChild.
            if (IsSemantic(child))
            {
                var nodes = node.Children;
                if (matched >= nodes.Count || nodes[matched].ElementId != child.Id) return refresh.Fail("children");
                int index = matched++;
                int unused = 0;
                if (!MatchNode(child, nodes[index], node, index, isRoot: false, focusManager, childPlace, clip,
                        prevAbove, currAbove, refresh, ref unused))
                    return false;
            }
            else if (!MatchElementChildren(child, node, ref matched, focusManager, ChildPlace(child, childPlace),
                         ClipForChildren(child, clip, childPlace), prevAbove, currAbove, refresh))
                return false;
        }
        return true;
    }

    /// <summary>As <see cref="BuildContent"/>: one node per item of <paramref name="data"/>, each
    /// with its own under it.</summary>
    private static bool MatchContent(uint ownerSlot, IReadOnlyList<AccessibleContentNode> data, AccessibilityNode parent,
        Rect unscrolled, Rect onScreen, float scale, Rect clip, Point prevAbove, Point currAbove, LayoutRefresh refresh)
    {
        var nodes = parent.Children;
        if (nodes.Count != data.Count) return refresh.Fail("content");
        for (int i = 0; i < nodes.Count; i++)
        {
            var item = data[i];
            var node = nodes[i];
            if (node.ElementId != ContentNodeId.ForSlot(ownerSlot, item.Key)) return refresh.Fail("content");
            var b = item.Bounds;
            float x = b.X / scale, y = b.Y / scale, width = b.Width / scale, height = b.Height / scale;
            var rect = new Rect(unscrolled.X + x, unscrolled.Y + y, width, height);
            bool offscreen = !RectsIntersect(clip, new Rect(onScreen.X + x, onScreen.Y + y, width, height));
            if (node.Role != item.Role || node.Name != item.Name || node.Description != item.Description
                || node.IsFocusable != item.IsFocusable || node.IsFocused != item.IsFocused
                || node.IsEnabled != item.IsEnabled || node.IsToggled != item.IsToggled
                || node.BuiltValue != item.Value || node.Expanded != item.Expanded
                || node.IsSelected != ContentSelected(item))
            {
                if (node.IsFocusable != item.IsFocusable) return refresh.Fail("a change no diff tells");
                if (node.Children.Count != 0 || item.Children.Count != 0) return refresh.Fail("a node with children changed");
                var made = MakeContentNode(ownerSlot, item, rect, offscreen);
                AccessibilityTreeDiffer.CompareNodes(refresh.Changes, node, made, prevAbove, currAbove);
                refresh.Remade.Add((parent, i, made));
                continue;
            }
            PlaceInPlace(node, rect, offscreen, prevAbove, currAbove, refresh);
            if (item.Children.Count != 0 || node.Children.Count != 0)
            {
                // Content nodes do not scroll: their children are in the same content.
                if (!MatchContent(ownerSlot, item.Children, node, unscrolled, onScreen, scale, clip, prevAbove, currAbove, refresh))
                    return false;
            }
        }
        return true;
    }

    /// <summary>As <see cref="BuildVirtualItems"/>: one node per item of the list, which has as many
    /// as the list has items.</summary>
    private static bool MatchItems(Element list, IAccessibleItemSource source, AccessibilityNode listNode, Place place,
        Rect clip, Point prevAbove, Point currAbove, LayoutRefresh refresh)
    {
        int count = source.ItemCount;
        var nodes = listNode.Children;
        if (nodes.Count != count) return refresh.Fail("a list's items");
        if (count == 0) return true;
        float itemHeight = source.ItemHeight;
        var content = list.Layout.AbsoluteContentBox;
        uint slot = AccessibleOwnerSlots.SlotOf(list.Id);
        for (int i = 0; i < count; i++)
        {
            var node = nodes[i];
            if (node.ElementId != VirtualItemId.ForSlot(slot, i)) return refresh.Fail("a list's items");
            var (localTop, height) = source.ItemBounds(i) ?? (i * itemHeight, itemHeight);
            var box = new Rect(content.X, content.Y + localTop, content.Width, height);
            var unscrolled = place.Unscrolled(box);
            bool offscreen = !RectsIntersect(clip, place.OnScreen(box));
            // Kept labels on item rows are the same string; a label made on each ask is compared.
            string? label = source.GetItemLabel(i);
            bool? toggled = source.ItemIsToggled(i);
            bool selected = source.IsItemSelected(i);
            if (node.Name != label || node.IsToggled != toggled || node.IsSelected != selected)
            {
                var made = MakeItemNode(slot, i, label, unscrolled, toggled, selected, offscreen);
                AccessibilityTreeDiffer.CompareNodes(refresh.Changes, node, made, prevAbove, currAbove);
                refresh.Remade.Add((listNode, i, made));
                continue;
            }
            PlaceInPlace(node, unscrolled, offscreen, prevAbove, currAbove, refresh);
        }
        return true;
    }

    /// <summary>A node a build would make the same but for where it is: its box and whether it is
    /// out of view, written in place, and its move told as a diff tells it.</summary>
    private static void PlaceInPlace(AccessibilityNode node, Rect unscrolled, bool offscreen, Point prevAbove, Point currAbove,
        LayoutRefresh refresh)
    {
        var old = node.UnscrolledRect;
        if (old != unscrolled)
        {
            refresh.Moves.Add((node, unscrolled));
            if (!(node.IsOffscreen && offscreen) && AccessibilityTreeDiffer.Moved(old, unscrolled, prevAbove, currAbove))
                refresh.Changes.Add(new PropertyChange(node, AccessibilityProperties.BoundingRect));
        }
        if (offscreen != node.IsOffscreen) refresh.OffscreenFlips.Add(node);
    }

    private enum Sameness : byte { Same, Differs, TextMoved, Untellable }

    /// <summary>
    /// Whether <paramref name="node"/> is what <see cref="CreateNode(Element, FocusManager?, Rect, float, float)"/>
    /// would make of <paramref name="element"/> now, but for its box and its scroll view's fields:
    /// <see cref="Sameness.Differs"/> when something a diff compares is not,
    /// <see cref="Sameness.TextMoved"/> when only the version of its text made on request is (the
    /// same text, a caret or a selection or a layout moved), and <see cref="Sameness.Untellable"/>
    /// when something it does not compare is not (whether it is focusable, its text source), which
    /// no diff tells and only a new tree carries.
    /// </summary>
    private static Sameness Compare(Element element, AccessibilityNode node, FocusManager? focusManager)
    {
        if (node.IsFocusable != element.IsFocusable) return Sameness.Untellable;
        var textSource = element.AccessibleTextSource;
        if (!ReferenceEquals(node.TextSource, textSource)) return Sameness.Untellable;
        var versioned = textSource as IVersionedAccessibleTextSource;
        if (node.Role != ResolveRole(element)
            || node.Name != ResolveName(element)
            || node.Description != element.AccessibleDescription
            || node.IsFocused != (focusManager is not null && ReferenceEquals(focusManager.FocusedElement, element))
            || node.IsEnabled != element.IsEnabledForAccessibility
            || node.IsToggled != ResolveIsToggled(element)
            || node.BuiltValue != (versioned is null ? ResolveValue(element) : null)
            || node.AutomationId != element.AutomationId
            || node.Expanded != element.AccessibleExpanded
            || node.IsPassword != (element is TextInputElement { Masked: true })
            || node.IsSelected != ResolveIsSelected(element)
            || node.NativeContent != element.NativeAccessibleContent
            || node.Range != (element is IRangeValueElement range ? AccessibleRange.Of(range) : null))
            return Sameness.Differs;
        if (versioned is not null)
        {
            var (was, now) = (node.TextVersion, versioned.CurrentVersion);
            if (ReferenceEquals(was, now)) return Sameness.Same;
            return was is not null && was.GetType() == now.GetType() && now.SameText(was) ? Sameness.TextMoved : Sameness.Differs;
        }
        if (textSource is not null)
        {
            // Made on every build, compared as a diff compares it: the same text, caret and selection
            // is nothing to tell, whichever object holds them.
            var (was, now) = (node.BuiltText, textSource.AccessibleText);
            if (!ReferenceEquals(was, now) && (was?.Text != now?.Text || was?.Caret != now?.Caret
                || was?.SelectionStart != now?.SelectionStart || was?.SelectionEnd != now?.SelectionEnd))
                return Sameness.Differs;
        }
        return Sameness.Same;
    }
}
