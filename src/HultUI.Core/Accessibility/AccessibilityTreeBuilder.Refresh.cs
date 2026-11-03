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
    /// or geometry changed, the leaves made again for a change of their own properties, the lists
    /// served on demand whose items changed in number or selection, the nodes whose children are
    /// others now, with their new children, and the changes a diff would have reported, in its
    /// order: the nodes added and removed, the parents whose children are in another order, the
    /// properties changed. Nothing is written to the tree until the whole of it is found to be what a
    /// build would make (<see cref="TryRefreshInPlace"/>).
    /// </summary>
    internal sealed class LayoutRefresh
    {
        internal readonly List<PropertyChange> Changes = [];
        internal readonly List<(AccessibilityNode Node, Rect Rect)> Moves = [];
        internal readonly List<AccessibilityNode> OffscreenFlips = [];
        internal readonly List<(AccessibilityNode Node, Point Offset, Rect Viewport, Size Content)> Scrolls = [];
        internal readonly List<(AccessibilityNode Parent, int Index, AccessibilityNode Node)> Remade = [];
        internal readonly List<(AccessibilityNode Node, AccessibleTextVersion Version)> TextVersions = [];
        internal readonly List<(AccessibilityNode Node, int Count, int[] Selected)> Served = [];

        /// <summary>The nodes a build makes that the applied tree does not have, with their parents
        /// (the applied ones), each subtree in its order: as a diff lists them.</summary>
        internal readonly List<AddedNode> Added = [];

        /// <summary>The applied tree's nodes a build no longer makes, deepest first, the top one's
        /// parent a stand-in with the children it had (<see cref="AccessibilityNode.ChildrenStandIn"/>):
        /// as a diff lists them.</summary>
        internal readonly List<RemovedNode> Removed = [];

        /// <summary>The nodes whose children that stay are in another order.</summary>
        internal readonly List<AccessibilityNode> Reordered = [];

        /// <summary>The nodes whose children a build makes otherwise, each with its new children: the
        /// ones it keeps, the same nodes, and the new ones.</summary>
        internal readonly List<(AccessibilityNode Node, AccessibilityNode[] Children)> Restructured = [];

        /// <summary>While the walk runs: the children found so far of the nodes whose children
        /// change, deepest last, as a build keeps the children of the nodes it makes.</summary>
        internal readonly List<AccessibilityNode> Pending = [];

        /// <summary>While the walk runs: the applied children not found yet of the nodes whose
        /// children change, each with its index among its parent's. An id is one node's in a tree.</summary>
        internal readonly Dictionary<ElementId, int> Unfound = [];

        /// <summary>Whether a build would make other nodes, which the refresh makes in their place.</summary>
        internal bool Structural => Added.Count > 0 || Removed.Count > 0 || Reordered.Count > 0;

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
        /// scroll view's fields (<see cref="AccessibilityNode.ReplaceScrollView"/>), a node's children
        /// (<see cref="AccessibilityNode.ReplaceChildren"/>), a child
        /// (<see cref="AccessibilityNode.ReplaceChild"/>), a list's items served on demand
        /// (<see cref="AccessibilityNode.ReplaceServedItems"/>). A node's new children before the
        /// leaves made again: a leaf's place is its index among its parent's children as a build
        /// makes them.</summary>
        internal void Apply()
        {
            foreach (var (node, rect) in Moves) node.MoveTo(rect);
            foreach (var node in OffscreenFlips) node.IsOffscreen = !node.IsOffscreen;
            foreach (var (node, offset, viewport, content) in Scrolls) node.ReplaceScrollView(offset, viewport, content);
            foreach (var (node, version) in TextVersions) node.ReplaceTextVersion(version);
            foreach (var (node, children) in Restructured) node.ReplaceChildren(children);
            foreach (var (parent, index, node) in Remade) parent.ReplaceChild(index, node);
            foreach (var (node, count, selected) in Served) node.ReplaceServedItems(count, selected);
        }

        internal void Clear()
        {
            Changes.Clear();
            Moves.Clear();
            OffscreenFlips.Clear();
            Scrolls.Clear();
            Remade.Clear();
            TextVersions.Clear();
            Served.Clear();
            Added.Clear();
            Removed.Clear();
            Reordered.Clear();
            Restructured.Clear();
            Pending.Clear();
            // Empty after a walk that matched: each id it put in, it took out.
            if (Unfound.Count > 0) Unfound.Clear();
        }
    }

    /// <summary>
    /// A layout refresh without a new tree. Walks the elements under <paramref name="root"/> and the
    /// open overlays' contents (<paramref name="overlays"/>, in the order their parts follow the
    /// root's own children in <paramref name="tree"/>) by the builder's own rules — the nodes it
    /// would make, flattened where it flattens, with the items and content it would make — against
    /// the applied tree, and finds what a build would make otherwise there: a box moved in its scroll
    /// views' content, a node gone out of view or come into it, a scroll view scrolled or resized, a
    /// leaf whose own properties changed (made again, as <see cref="RemakeLeaf"/> makes it), children
    /// added, gone or in another order (the new ones built as a build builds them, the rest kept),
    /// with the changes a diff against such a build would report, in its order, into
    /// <paramref name="refresh"/>. Nothing is written to the tree. A wheel over a list of 1,000 rows
    /// built and diffed 3,001 nodes for one scroll view's offset, and a row a key made again in a
    /// composite list built and diffed every node of the window.
    /// <para>False, with <see cref="LayoutRefresh.Mismatch"/> saying why, when a build would make
    /// another tree than one made that way: a node another element's (with the same id), described
    /// content in another number, a list whose items cross the number they are served on demand
    /// from, a node with children whose own properties changed, the window's root node's own, an
    /// overlay opened or closed, or a change no diff tells (a node focusable or not, another text
    /// source). The window builds the tree then, as it always did.</para>
    /// </summary>
    internal static bool TryRefreshInPlace(AccessibilityNode tree, Element root,
        IReadOnlyList<(Element Content, float X, float Y)> overlays, FocusManager? focusManager, LayoutRefresh refresh)
    {
        refresh.Mismatch = null;
        if (tree.ElementId != root.Id) return refresh.Fail("another root");
        // The root's node has its element's children's nodes, then one part per open overlay; the
        // same overlays, each checked below.
        var parts = tree.Children;
        int own = parts.Count - overlays.Count;
        // A root whose children are its content or its items has them all, and no part after them, in
        // the walk's eyes.
        if (own < 0 || (overlays.Count > 0 && root is IAccessibleContentSource or IAccessibleItemSource { PublishesItemNodes: true }))
            return refresh.Fail("an overlay's part");
        // As Build: the root's node, its children, then each overlay's part under it.
        var place = new Place(-0f, -0f, -0f, -0f);
        int rootChildren = own;
        if (!MatchNode(root, tree, parent: null, index: -1, isRoot: true, focusManager, place, clip: default,
                default, default, refresh, ref rootChildren))
            return false;
        // An overlay's part is built under the root's node with the root's new offset added back to
        // its boxes (BuildAccessibilityTreeWithOverlays): the root may scroll, and the overlay does not.
        var rootScroll = root is ScrollViewElement sv ? new Point(sv.ScrollOffsetX, sv.ScrollOffsetY) : default;
        var appliedRootScroll = tree.ScrollOffset;
        for (int i = 0; i < overlays.Count; i++)
        {
            var (content, x, y) = overlays[i];
            int at = own + i;
            if (parts[at].ElementId != content.Id) return refresh.Fail("an overlay's part");
            float ox = -x, oy = -y;
            var partPlace = new Place(ox, oy, ox - rootScroll.X, oy - rootScroll.Y);
            int unused = 0;
            // Its place among the root node's children as a build makes them: after the element's.
            if (!MatchNode(content, parts[at], tree, rootChildren + i, isRoot: true, focusManager, partPlace, clip: default,
                    new Point(0f + appliedRootScroll.X, 0f + appliedRootScroll.Y),
                    new Point(0f + rootScroll.X, 0f + rootScroll.Y), refresh, ref unused))
                return false;
        }
        return true;
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
    /// <param name="childrenMatched">For the window's root node: how many of its applied children are
    /// its element's, before the overlays' parts; then how many a build makes.</param>
    private static bool MatchNode(Element element, AccessibilityNode applied, AccessibilityNode? parent, int index,
        bool isRoot, FocusManager? focusManager, Place place, Rect clip, Point prevAbove, Point currAbove,
        LayoutRefresh refresh, ref int childrenMatched)
    {
        var box = element.Layout.AbsoluteBorderBox;
        var unscrolled = place.Unscrolled(box);
        bool offscreen = !isRoot && !RectsIntersect(clip, place.OnScreen(box));
        var scrollView = element as ScrollViewElement;
        // A list has a node, so it comes through here, and only a scroll view pays for the ask.
        scrollView?.RealizeForAccessibility();
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
                    int count = items.ItemCount;
                    if (count > MaxEagerVirtualItems && OpenRow(element, items, focusManager).Element is null)
                        ServeOnDemand(made, items, count);
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
            var open = OpenRow(element, items, focusManager);
            if (items.ItemCount > MaxEagerVirtualItems && open.Element is null)
            {
                if (!node.IsVirtualItemContainer || node.Children.Count != 0) return refresh.Fail("a list's items");
                // Its items' number and selection are the node's own: told as a diff tells them, and
                // put in place. A node made again carries them as they are.
                if (!mustBeLeaf) ServedItemChanges(node, items, refresh.Changes, refresh.Served);
                return true;
            }
            if (node.IsVirtualItemContainer) return refresh.Fail("a list's items");
            if (mustBeLeaf && items.ItemCount != 0) return refresh.Fail("a list's items");
            return MatchItems(element, items, node, open, focusManager, place, clip, prevAbove, currAbove, refresh);
        }
        if (node.ItemSource is not null || node.IsVirtualItemContainer) return refresh.Fail("a list's items");
        if (!isRoot && ConsumesTextContent(element)) return node.Children.Count == 0 || refresh.Fail("children");
        // The window's root node has the overlays' parts after its element's children.
        bool windowRoot = node.Parent is null && !mustBeLeaf;
        var kids = new ChildWalk(node, windowRoot ? childrenMatched : node.Children.Count, mayChange: !mustBeLeaf);
        if (!MatchElementChildren(element, ref kids, focusManager, place, clip, prevAbove, currAbove, refresh)
            || !EndChildren(ref kids, refresh))
            return false;
        if (windowRoot) childrenMatched = kids.Made;
        return true;
    }

    /// <summary>
    /// Where a walk is among one node's children: the next of its applied children it expects while a
    /// build makes the same ones in the same order, and, from the first it makes otherwise, where the
    /// node's children as a build makes them start in <see cref="LayoutRefresh.Pending"/>. Its applied
    /// children not found yet are in <see cref="LayoutRefresh.Unfound"/> then, and each one found is
    /// looked for there, as a diff pairs a node's children by their ids from the first difference on.
    /// </summary>
    private struct ChildWalk(AccessibilityNode node, int count, bool mayChange)
    {
        /// <summary>The node whose children these are.</summary>
        public readonly AccessibilityNode Node = node;

        /// <summary>How many of its applied children are its element's: all but the overlays' parts
        /// under the window's root node.</summary>
        public readonly int Count = count;

        /// <summary>False for a node made again for a change of its own: a build makes no children
        /// for one, and the walk finds none in place.</summary>
        public readonly bool MayChange = mayChange;

        /// <summary>The applied child expected next, while the children are the applied ones in order.</summary>
        public int Next;

        /// <summary>Where its children as a build makes them start in <see cref="LayoutRefresh.Pending"/>,
        /// once they are not the applied ones in order; -1 before.</summary>
        public int Start = -1;

        /// <summary>The applied children before <see cref="Start"/> was set, found in order.</summary>
        public int First;

        /// <summary>The index of the applied child found last, as a diff keeps it.</summary>
        public int Last;

        /// <summary>A child found before one found earlier: the children that stay are in another order.</summary>
        public bool Reordered;

        /// <summary>How many children a build makes, once the walk is through them.</summary>
        public int Made;
    }

    /// <summary>As <see cref="BuildChildren"/>: each element child's node, or its children's when it
    /// has none, among <paramref name="kids"/>.</summary>
    private static bool MatchElementChildren(Element element, ref ChildWalk kids,
        FocusManager? focusManager, Place place, Rect clip, Point prevAbove, Point currAbove, LayoutRefresh refresh)
    {
        var children = element.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
        {
            if (!MatchChild(children[i], ref kids, focusManager, place, clip, prevAbove, currAbove, refresh))
                return false;
        }
        return true;
    }

    /// <summary>As <see cref="BuildChild"/>: <paramref name="child"/>'s node, or its children's when
    /// it has none, among <paramref name="kids"/>: the applied node with its id, walked in its place,
    /// or, when the applied children have none, one built as a build builds it.</summary>
    private static bool MatchChild(Element child, ref ChildWalk kids,
        FocusManager? focusManager, Place place, Rect clip, Point prevAbove, Point currAbove, LayoutRefresh refresh)
    {
        var childPlace = Placed(child, place);
        if (!IsSemantic(child))
            return MatchElementChildren(child, ref kids, focusManager, ChildPlace(child, childPlace),
                ClipForChildren(child, clip, childPlace), prevAbove, currAbove, refresh);
        var node = kids.Node;
        var nodes = node.Children;
        int unused = 0;
        if (kids.Start < 0)
        {
            if (kids.Next < kids.Count && nodes[kids.Next].ElementId == child.Id)
            {
                int index = kids.Next++;
                return MatchNode(child, nodes[index], node, index, isRoot: false, focusManager, childPlace, clip,
                    prevAbove, currAbove, refresh, ref unused);
            }
            if (!BeginChange(ref kids, refresh)) return false;
        }
        var pending = refresh.Pending;
        int at = pending.Count - kids.Start;
        int found = FindApplied(ref kids, child.Id, refresh);
        if (found < 0)
        {
            // New to the tree: built here as a build builds it, which adds its one node.
            BuildChild(child, pending, focusManager, childPlace, clip);
            AddRecursive(refresh.Added, pending[^1], node);
            return true;
        }
        var applied = nodes[found];
        pending.Add(applied);
        return MatchNode(child, applied, node, at, isRoot: false, focusManager, childPlace, clip,
            prevAbove, currAbove, refresh, ref unused);
    }

    /// <summary>The children of <paramref name="kids"/>' node are not the applied ones in order from
    /// its next on: those before it are its first children as a build makes them, and the rest are
    /// each looked for by id. False when the node may have no other children.</summary>
    private static bool BeginChange(ref ChildWalk kids, LayoutRefresh refresh)
    {
        if (!kids.MayChange) return refresh.Fail("children");
        var nodes = kids.Node.Children;
        var pending = refresh.Pending;
        kids.Start = pending.Count;
        kids.First = kids.Next;
        kids.Last = kids.Next - 1;
        for (int i = 0; i < kids.Next; i++) pending.Add(nodes[i]);
        var unfound = refresh.Unfound;
        for (int i = kids.Next; i < kids.Count; i++)
            if (!unfound.TryAdd(nodes[i].ElementId, i)) return refresh.Fail("children");
        return true;
    }

    /// <summary>The index of the applied child of <paramref name="kids"/>' node with
    /// <paramref name="id"/>, not found before, taken out of those not found; -1 when there is none
    /// (the child is new). As a diff pairs them: one found before the one found last is out of order.</summary>
    private static int FindApplied(ref ChildWalk kids, ElementId id, LayoutRefresh refresh)
    {
        // The id may be another node's not found yet, one the element left: its index is in another
        // node's children.
        var unfound = refresh.Unfound;
        if (!unfound.TryGetValue(id, out int index) || index < kids.First || index >= kids.Count
            || kids.Node.Children[index].ElementId != id)
            return -1;
        unfound.Remove(id);
        if (index < kids.Last) kids.Reordered = true;
        kids.Last = index;
        return index;
    }

    /// <summary>
    /// The end of <paramref name="kids"/>' node's children. When a build makes others, or the same in
    /// another order: the applied ones not found are removed, in their order, each subtree deepest
    /// first, with a stand-in for the node as it was for their parent (a diff's previous node), the
    /// node is reordered when the ones that stay are in another order, and its new children, those of
    /// the walk and then the overlays' parts under the window's root node, are kept for
    /// <see cref="LayoutRefresh.Apply"/>.
    /// </summary>
    private static bool EndChildren(ref ChildWalk kids, LayoutRefresh refresh)
    {
        if (kids.Start < 0)
        {
            if (kids.Next == kids.Count)
            {
                kids.Made = kids.Count;
                return true;
            }
            // The last ones are gone.
            if (!BeginChange(ref kids, refresh)) return false;
        }
        var node = kids.Node;
        var nodes = node.Children;
        var unfound = refresh.Unfound;
        AccessibilityNode? was = null;
        for (int i = kids.First; i < kids.Count; i++)
        {
            var gone = nodes[i];
            if (!unfound.Remove(gone.ElementId)) continue;   // found
            was ??= node.ChildrenStandIn();
            RemoveRecursive(refresh.Removed, gone, was);
        }
        if (kids.Reordered) refresh.Reordered.Add(node);
        var pending = refresh.Pending;
        int made = pending.Count - kids.Start, parts = nodes.Count - kids.Count;
        AccessibilityNode[] children = made + parts == 0 ? [] : new AccessibilityNode[made + parts];
        pending.CopyTo(kids.Start, children, 0, made);
        pending.RemoveRange(kids.Start, made);
        for (int i = 0; i < parts; i++) children[made + i] = nodes[kids.Count + i];
        refresh.Restructured.Add((node, children));
        kids.Made = made;
        return true;
    }

    /// <summary>As a diff adds <paramref name="node"/>: it, then each of its children's subtrees.</summary>
    private static void AddRecursive(List<AddedNode> added, AccessibilityNode node, AccessibilityNode parent)
    {
        added.Add(new AddedNode(node, parent));
        var children = node.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
            AddRecursive(added, children[i], node);
    }

    /// <summary>As a diff removes <paramref name="node"/>: its children's subtrees, then it.</summary>
    private static void RemoveRecursive(List<RemovedNode> removed, AccessibilityNode node, AccessibilityNode parent)
    {
        var children = node.Children;
        for (int i = 0; i < children.Count; i++)
            RemoveRecursive(removed, children[i], node);
        removed.Add(new RemovedNode(node, parent));
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

    /// <summary>As <see cref="BuildVirtualItems"/>: one node per item of the list, with nothing under
    /// them but what the row <paramref name="open"/> holds. An item's id is its place: items added to
    /// the list's end are new nodes, made as a build makes them, and those gone from it are removed,
    /// as a diff tells them.</summary>
    private static bool MatchItems(Element list, IAccessibleItemSource source, AccessibilityNode listNode, ItemRow open,
        FocusManager? focusManager, Place place, Rect clip, Point prevAbove, Point currAbove, LayoutRefresh refresh)
    {
        int count = source.ItemCount;
        var nodes = listNode.Children;
        if (count == 0 && nodes.Count == 0) return true;
        int kept = Math.Min(count, nodes.Count);
        float itemHeight = source.ItemHeight;
        var content = list.Layout.AbsoluteContentBox;
        uint slot = AccessibleOwnerSlots.SlotOf(list.Id);
        for (int i = 0; i < kept; i++)
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
            // Only an item with nothing under it, in either tree, is one node to make again.
            bool holdsRow = i == open.Item || node.Children.Count != 0;
            if (node.Name != label || node.IsToggled != toggled || node.IsSelected != selected)
            {
                if (holdsRow) return refresh.Fail("a node with children changed");
                var made = MakeItemNode(slot, i, label, unscrolled, toggled, selected, offscreen);
                AccessibilityTreeDiffer.CompareNodes(refresh.Changes, node, made, prevAbove, currAbove);
                refresh.Remade.Add((listNode, i, made));
                continue;
            }
            PlaceInPlace(node, unscrolled, offscreen, prevAbove, currAbove, refresh);
            if (!holdsRow) continue;
            // The row under it as a build makes it: the one open, none when it closed.
            var kids = new ChildWalk(node, node.Children.Count, mayChange: true);
            if (i == open.Item && !MatchChild(open.Element!, ref kids, focusManager, place, clip, prevAbove, currAbove, refresh))
                return false;
            if (!EndChildren(ref kids, refresh)) return false;
        }
        if (kept == count && kept == nodes.Count) return true;

        // Items added at the list's end, or gone from it: after the kept ones, as a diff pairs them.
        var pending = refresh.Pending;
        int start = pending.Count;
        for (int i = 0; i < kept; i++) pending.Add(nodes[i]);
        for (int i = kept; i < count; i++)
        {
            var (localTop, height) = source.ItemBounds(i) ?? (i * itemHeight, itemHeight);
            var box = new Rect(content.X, content.Y + localTop, content.Width, height);
            var item = MakeItemNode(slot, i, source.GetItemLabel(i), place.Unscrolled(box), source.ItemIsToggled(i),
                source.IsItemSelected(i), !RectsIntersect(clip, place.OnScreen(box)));
            if (i == open.Item)
            {
                int row = pending.Count;
                BuildChild(open.Element!, pending, focusManager, Placed(open.Element!, place), clip);
                TakePending(item, pending, row);
            }
            pending.Add(item);
            AddRecursive(refresh.Added, item, listNode);
        }
        AccessibilityNode? was = null;
        for (int i = kept; i < nodes.Count; i++)
        {
            was ??= listNode.ChildrenStandIn();
            RemoveRecursive(refresh.Removed, nodes[i], was);
        }
        AccessibilityNode[] items = count == 0 ? [] : new AccessibilityNode[count];
        pending.CopyTo(start, items, 0, count);
        pending.RemoveRange(start, count);
        refresh.Restructured.Add((listNode, items));
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
