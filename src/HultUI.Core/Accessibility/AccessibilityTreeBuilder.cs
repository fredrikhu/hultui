using System.Text;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI.Core.Accessibility;

public static class AccessibilityTreeBuilder
{
    /// <summary>
    /// Switch point between the two ways a virtualized list exposes its items.
    /// At or below this count, every item is materialized as a child node (simple,
    /// and hit-testable straight off the snapshot tree). Above it, the list is
    /// marked <see cref="AccessibilityNode.IsVirtualItemContainer"/> and the bridge
    /// provisions items on demand by index, so a list of any size costs nothing
    /// until assistive tech walks into it.
    /// </summary>
    internal const int MaxEagerVirtualItems = 2000;

    /// <summary>
    /// The nodes built so far on this thread whose parent is still being built, deepest last.
    /// How many children a node gets is known only once its element's subtree has been walked
    /// (decorative containers pass theirs up), so they wait here and the node takes them in one
    /// array of the exact size: a tree is rebuilt in full on every change a screen reader can
    /// see, and a growing list per parent was garbage on each of them.
    /// </summary>
    [ThreadStatic] private static List<AccessibilityNode>? t_pending;

    /// <summary>
    /// Builds an accessibility tree from the element tree rooted at the given element.
    /// Decorative containers (BoxElements with no role, name, or semantic children) are
    /// flattened — their children are promoted to the parent accessibility node.
    /// When <paramref name="focusManager"/> is supplied, the focused element gets
    /// <see cref="AccessibilityNode.IsFocused"/> set.
    /// </summary>
    /// <param name="originX">Where this tree's root sits on screen, when its elements do
    /// not know. Overlay content — a context menu, a dropdown list, a dialog — is placed by
    /// the overlay system at render time, so its element boxes are relative to the overlay
    /// and every node would otherwise report bounds near the window's top-left corner:
    /// wrong for a screen reader's highlight, and wrong for anything hit-testing off the
    /// snapshot.</param>
    /// <param name="originY">The vertical half of <paramref name="originX"/>.</param>
    public static AccessibilityNode Build(Element root, FocusManager? focusManager = null,
        float originX = 0f, float originY = 0f)
    {
        // Threaded as a negative scroll: the builder already subtracts an accumulated
        // offset from every box, which is exactly this with the other sign.
        float ox = -originX, oy = -originY;
        var node = CreateNode(root, focusManager, ox, oy);
        var (cx, cy) = ChildScroll(root, ox, oy);
        // The root is the top-level visible region; descendants clipped out of it
        // (and out of any scroll view along the way) are marked offscreen.
        var childClip = ClipForChildren(root, node.BoundingRect, ox, oy);
        if (!TryBuildContent(root, node, cx, cy, childClip) && !TryBuildVirtualItems(root, node, cx, cy, childClip))
        {
            var pending = t_pending ??= [];
            int start = pending.Count;
            try
            {
                BuildElementChildren(root, node, pending, focusManager, cx, cy, childClip);
            }
            finally
            {
                // Something is left only when the walk threw: drop it rather than hold its nodes.
                if (pending.Count > start) pending.RemoveRange(start, pending.Count - start);
            }
        }
        return node;
    }

    private static bool RectsIntersect(Rect a, Rect b) =>
        a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    private static Rect IntersectRects(Rect a, Rect b)
    {
        float x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        float right = Math.Min(a.Right, b.Right), bottom = Math.Min(a.Bottom, b.Bottom);
        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    /// <summary>The visible region for an element's children: its own clip, narrowed
    /// to the scroll-view viewport if this element scrolls.</summary>
    private static Rect ClipForChildren(Element element, Rect clip, float scrollX, float scrollY)
    {
        if (element is not ScrollViewElement) return clip;
        var cb = element.Layout.AbsoluteContentBox;
        return IntersectRects(clip, new Rect(cb.X - scrollX, cb.Y - scrollY, cb.Width, cb.Height));
    }

    // Element AbsoluteBorderBox is laid out ignoring scroll — a ScrollView shifts
    // its children only at render time. So the on-screen rect of a node is its
    // box minus the accumulated scroll offset of its ancestor scroll views. We
    // thread that accumulation down the element tree (which the a11y flattening
    // walks anyway) and bake scroll-adjusted rects into the nodes, so bounds and
    // hit-testing match what the user actually sees.
    private static (float x, float y) ChildScroll(Element element, float accX, float accY)
        => element is ScrollViewElement sv ? (accX + sv.ScrollOffsetX, accY + sv.ScrollOffsetY) : (accX, accY);

    private static AccessibilityNode CreateNode(Element element, FocusManager? focusManager, float scrollX, float scrollY)
    {
        var box = element.Layout.AbsoluteBorderBox;
        return CreateNode(element, focusManager, new Rect(box.X - scrollX, box.Y - scrollY, box.Width, box.Height),
            scrollX, scrollY);
    }

    /// <param name="onScreen">The node's box: the element's on screen, the scroll above it applied.</param>
    private static AccessibilityNode CreateNode(Element element, FocusManager? focusManager, Rect onScreen,
        float scrollX, float scrollY)
    {
        var textSource = element.AccessibleTextSource;
        // A source that makes its text on request gives a version, and the value comes from it
        // too: a long document's text and value are made when a reader asks, not on every rebuild.
        var versioned = textSource as IVersionedAccessibleTextSource;
        var node = new AccessibilityNode(
            element.Id,
            ResolveRole(element),
            ResolveName(element),
            element.AccessibleDescription,
            onScreen,
            isFocusable: element.IsFocusable,
            isFocused: focusManager is not null && ReferenceEquals(focusManager.FocusedElement, element),
            isEnabled: element.IsEnabledForAccessibility,
            isToggled: ResolveIsToggled(element),
            value: versioned is null ? ResolveValue(element) : null,
            automationId: element.AutomationId,
            expanded: element.AccessibleExpanded,
            isPassword: element is TextInputElement { Masked: true },
            isSelected: ResolveIsSelected(element));
        node.NativeContent = element.NativeAccessibleContent;
        if (element is IRangeValueElement range)
            node.Range = AccessibleRange.Of(range);
        // What ChildScroll subtracts from everything under it, and what UI Automation's Scroll
        // pattern answers from: it read the live scroll view through a walk of the element tree on
        // the client's thread.
        if (element is ScrollViewElement sv)
        {
            var viewport = element.Layout.AbsoluteContentBox;
            node.SetScrollView(new Point(sv.ScrollOffsetX, sv.ScrollOffsetY),
                new Rect(viewport.X - scrollX, viewport.Y - scrollY, viewport.Width, viewport.Height),
                new Size(sv.ContentWidth, sv.ContentHeight));
        }
        if (textSource is not null)
        {
            node.TextSource = textSource;
            if (versioned is not null) node.TextVersion = versioned.CurrentVersion;
            else node.Text = textSource.AccessibleText;
        }
        return node;
    }

    /// <summary>Builds <paramref name="node"/>'s children from <paramref name="element"/>'s, on top
    /// of <paramref name="pending"/>, and gives them to the node.</summary>
    private static void BuildElementChildren(Element element, AccessibilityNode node, List<AccessibilityNode> pending,
        FocusManager? focusManager, float scrollX, float scrollY, Rect clip)
    {
        int start = pending.Count;
        BuildChildren(element, pending, focusManager, scrollX, scrollY, clip);
        int count = pending.Count - start;
        if (count == 0) return;
        var children = new AccessibilityNode[count];
        pending.CopyTo(start, children, 0, count);
        pending.RemoveRange(start, count);
        node.SetChildren(children);
    }

    private static void BuildChildren(Element element, List<AccessibilityNode> pending, FocusManager? focusManager,
        float scrollX, float scrollY, Rect clip)
    {
        var children = element.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            // A sticky child is moved by its shift, subtree and all — to the builder, a negative scroll.
            var stick = child.PositionRead.Type == Layout.PositionType.Sticky ? child.StickyShift : default;
            BuildChild(child, pending, focusManager, scrollX - stick.X, scrollY - stick.Y, clip);
        }
    }

    private static void BuildChild(Element child, List<AccessibilityNode> pending, FocusManager? focusManager,
        float scrollX, float scrollY, Rect clip)
    {
        // The child's own box sits in the parent's scroll space; its own
        // scroll (if it's a ScrollView) applies to its descendants.
        var (cx, cy) = ChildScroll(child, scrollX, scrollY);
        var childClip = ClipForChildren(child, clip, scrollX, scrollY);
        if (IsSemantic(child))
        {
            var childNode = CreateNode(child, focusManager, scrollX, scrollY);
            childNode.IsOffscreen = !RectsIntersect(clip, childNode.BoundingRect);
            pending.Add(childNode);
            // A virtualized list only materializes on-screen rows as elements;
            // expose every item to assistive tech instead. Otherwise, unless a
            // text-only button folded its text into its own name (which would
            // double-announce), walk the real children.
            if (!TryBuildContent(child, childNode, cx, cy, childClip)
                && !TryBuildVirtualItems(child, childNode, cx, cy, childClip) && !ConsumesTextContent(child))
                BuildElementChildren(child, childNode, pending, focusManager, cx, cy, childClip);
        }
        else
        {
            // Flatten: promote this element's children directly into the parent's
            BuildChildren(child, pending, focusManager, cx, cy, childClip);
        }
    }

    /// <summary>
    /// An element whose content is data (seam W11): its nodes, under the element's own, in
    /// place of its element children. Bounds come in device pixels from the element's content
    /// box and are made layout units here, scroll-adjusted and clipped like any child's.
    /// </summary>
    private static bool TryBuildContent(Element element, AccessibilityNode node, float scrollX, float scrollY, Rect clip)
    {
        if (element is not IAccessibleContentSource source) return false;
        var content = element.Layout.AbsoluteContentBox;
        float scale = source.AccessibleContentScale > 0 ? source.AccessibleContentScale : 1f;
        var roots = source.AccessibleContent;
        if (roots.Count > 0)
            node.SetChildren(BuildContent(AccessibleOwnerSlots.SlotOf(element.Id), roots,
                content.X - scrollX, content.Y - scrollY, scale, clip));
        return true;
    }

    /// <summary>One node per item of <paramref name="data"/>, each with its own under it. The
    /// owner's slot comes in looked up: it is the same for every node.</summary>
    private static AccessibilityNode[] BuildContent(uint ownerSlot, IReadOnlyList<AccessibleContentNode> data,
        float originX, float originY, float scale, Rect clip)
    {
        var nodes = new AccessibilityNode[data.Count];
        for (int i = 0; i < nodes.Length; i++)
        {
            var item = data[i];
            var b = item.Bounds;
            var rect = new Rect(originX + b.X / scale, originY + b.Y / scale, b.Width / scale, b.Height / scale);
            var node = new AccessibilityNode(
                ContentNodeId.ForSlot(ownerSlot, item.Key),
                item.Role,
                item.Name,
                item.Description,
                rect,
                isFocusable: item.IsFocusable,
                isFocused: item.IsFocused,
                isEnabled: item.IsEnabled,
                isToggled: item.IsToggled,
                value: item.Value,
                expanded: item.Expanded,
                // Described content says checked for a radio or a tab; for those, that is selected.
                isSelected: item.Role is AccessibilityRole.RadioButton or AccessibilityRole.Tab ? item.IsToggled : null)
            {
                IsOffscreen = !RectsIntersect(clip, rect),
            };
            var children = item.Children;
            if (children.Count > 0)
                node.SetChildren(BuildContent(ownerSlot, children, originX, originY, scale, clip));
            nodes[i] = node;
        }
        return nodes;
    }

    /// <summary>
    /// If the element is a virtualized list small enough to expand eagerly, emits
    /// its item nodes and returns true; otherwise returns false so the caller
    /// falls back to walking realized children.
    /// </summary>
    private static bool TryBuildVirtualItems(Element element, AccessibilityNode node, float scrollX, float scrollY, Rect clip)
    {
        if (element is not IAccessibleItemSource source) return false;
        // A container whose rows are composite (a grid row of cards) publishes nothing of
        // its own: its realized rows carry the real controls, and one node per row would
        // bury them.
        if (!source.PublishesItemNodes) return false;
        // What a bridge asks for the items it serves on demand, without finding the list first.
        node.ItemSource = source;
        if (source.ItemCount <= MaxEagerVirtualItems)
            // Small enough: materialize every item as a child node (fast, and
            // supports hit-testing directly off the snapshot tree).
            BuildVirtualItems(element, source, node, scrollX, scrollY, clip);
        else
            // Too large to expand eagerly: mark it so the bridge provisions items
            // on demand by index instead of building/diffing them all.
            node.IsVirtualItemContainer = true;
        return true;
    }

    /// <summary>
    /// Emits one <see cref="AccessibilityRole.ListItem"/> node per item of a
    /// virtualized list — including the off-screen items the list hasn't realized
    /// as elements. Item positions are derived from the fixed item height and the
    /// list's content box, scroll-adjusted like any other node so bounds and
    /// hit-testing stay correct as the list scrolls.
    /// </summary>
    private static void BuildVirtualItems(Element list, IAccessibleItemSource source,
        AccessibilityNode listNode, float scrollX, float scrollY, Rect clip)
    {
        int count = source.ItemCount;
        if (count == 0) return;

        float itemHeight = source.ItemHeight;
        var content = list.Layout.AbsoluteContentBox;
        // Every item's id packs the list's slot: look it up once, not under a lock per item.
        uint slot = AccessibleOwnerSlots.SlotOf(list.Id);
        var items = new AccessibilityNode[count];
        for (int i = 0; i < count; i++)
        {
            // A container with uneven items (a menu with separators in it) places them
            // itself; everything else stacks at one height.
            var (localTop, height) = source.ItemBounds(i) ?? (i * itemHeight, itemHeight);
            float top = content.Y + localTop;
            var rect = new Rect(content.X - scrollX, top - scrollY, content.Width, height);
            items[i] = new AccessibilityNode(
                VirtualItemId.ForSlot(slot, i),
                AccessibilityRole.ListItem,
                source.GetItemLabel(i),
                description: null,
                rect,
                isFocusable: false,
                isFocused: false,
                isEnabled: true,
                isToggled: source.ItemIsToggled(i),
                value: null,
                expanded: source.ItemIsExpanded(i),
                // In the node, so the differ sees a move of the selection: without it a move that
                // scrolled nothing changed no node, and no reader heard which item was selected.
                isSelected: source.IsItemSelected(i))
            {
                IsOffscreen = !RectsIntersect(clip, rect),
            };
        }
        listNode.SetChildren(items);
    }

    /// <summary>
    /// What a move of a list's selection changes in an applied tree's node of the list, without
    /// building the tree again: an <see cref="AccessibilityProperties.IsSelected"/> change for each
    /// item node whose state is not the list's now, in item order, as the differ would report
    /// them, added to <paramref name="changes"/>. The nodes are not touched. False, adding nothing,
    /// when the node's items cannot be patched and the tree has to be built again: the list does
    /// not publish item nodes (its rows are composite), or its items are not the node's (their
    /// number changed, or crossed <see cref="MaxEagerVirtualItems"/>). A list whose items are
    /// served on demand has no item nodes and tells no selection: true, adding nothing.
    /// </summary>
    /// <remarks>Only the selected state: an item whose label changed with the move is told by the
    /// next rebuild, when the list's layout refresh comes due, as a change of items always was.</remarks>
    internal static bool ItemSelectionChanges(AccessibilityNode listNode, IAccessibleItemSource source,
        List<PropertyChange> changes)
    {
        if (!source.PublishesItemNodes) return false;
        int count = source.ItemCount;
        if (count > MaxEagerVirtualItems) return listNode.IsVirtualItemContainer;
        if (listNode.IsVirtualItemContainer) return false;
        var items = listNode.Children;
        if (items.Count != count) return false;
        int start = changes.Count;
        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            if (item.IsSelected is not { } selected)
            {
                // Not an item node: the list's children are something else since the tree was built.
                changes.RemoveRange(start, changes.Count - start);
                return false;
            }
            if (selected != source.IsItemSelected(i))
                changes.Add(new PropertyChange(item, AccessibilityProperties.IsSelected));
        }
        return true;
    }

    /// <summary>
    /// What a move of the caret or the selection changes in an applied tree's node of text that is
    /// made on request (<see cref="IVersionedAccessibleTextSource"/>: the rich text editor's),
    /// without building the tree again: the version the source has now, in <paramref name="next"/>
    /// (null when it is the node's own), and the <see cref="AccessibilityProperties.TextSelection"/>
    /// change the differ would report when its caret or selection moved, added to
    /// <paramref name="changes"/> with a stand-in for the node as it was
    /// (<see cref="AccessibilityNode.TextStandIn"/>). A version that differs in what is not the text
    /// (its layout, its scroll, read-only) is put in place with nothing to tell. The node is not
    /// touched. False, adding nothing, when the tree has to be built again: the text itself
    /// changed, or the node carries no version of this source's kind.
    /// </summary>
    internal static bool TextSelectionChange(AccessibilityNode node, IVersionedAccessibleTextSource source,
        List<PropertyChange> changes, out AccessibleTextVersion? next)
    {
        next = null;
        if (node.TextVersion is not { } previous) return false;
        var current = source.CurrentVersion;
        if (ReferenceEquals(current, previous)) return true;
        if (current.GetType() != previous.GetType() || !current.SameText(previous)) return false;
        next = current;
        if (!current.SameSelection(previous))
            changes.Add(new PropertyChange(node, AccessibilityProperties.TextSelection, node.TextStandIn(previous)));
        return true;
    }

    /// <summary>
    /// What a change of a leaf's own properties changes in an applied tree's node of it, without
    /// building the tree again: the node made again from the element now, in
    /// <paramref name="remade"/> (null when nothing a reader is told changed), and the changes the
    /// differ reports between the two, added to <paramref name="changes"/> in its order, each on the
    /// remade node (a range's with the applied node as the previous one). The applied node is not
    /// touched. A leaf is an element with no children whose node has none: its node's properties are
    /// its own, and no other node takes anything from them but an ancestor named from its content (a
    /// button's, a tab's name is the text inside), so a change of a name, a description or an
    /// expanded state under one is not patched. The remade node keeps the applied node's box and
    /// offscreen state: whatever moved marked layout, and the coalesced layout refresh tells it, as it
    /// tells every move.
    /// <para>False, adding nothing, when the tree has to be built again: the element is not a leaf,
    /// offers text, content, items or a scroll of its own, is not a node any more (no longer
    /// semantic), is the tree's root, changed anything but its name, description, value, range, toggle
    /// or expanded state, or (the first three) has an ancestor named from its content.</para>
    /// </summary>
    internal static bool RemakeLeaf(Element element, AccessibilityNode applied, FocusManager? focusManager,
        List<PropertyChange> changes, out AccessibilityNode? remade)
    {
        remade = null;
        if (applied.Parent is null || applied.Children.Count != 0 || element.Children.Count != 0
            || applied.OffersText || applied.ScrollExtent is not null || applied.ItemSource is not null
            || applied.IsVirtualItemContainer || !applied.NativeContent.IsEmpty
            || element is ScrollViewElement or IAccessibleContentSource or IAccessibleItemSource
            || element.AccessibleTextSource is not null || !element.NativeAccessibleContent.IsEmpty
            || !IsSemantic(element))
            return false;
        var node = CreateNode(element, focusManager, applied.BoundingRect, 0f, 0f);
        node.IsOffscreen = applied.IsOffscreen;
        // Not compared by the differ, so not told; a bridge keeps it from the node it was made from.
        if (node.IsFocusable != applied.IsFocusable) return false;
        int start = changes.Count;
        AccessibilityTreeDiffer.CompareNodes(changes, applied, node);
        bool named = false;
        for (int i = start; i < changes.Count; i++)
        {
            switch (changes[i].PropertyName)
            {
                case AccessibilityProperties.Name or AccessibilityProperties.Description or AccessibilityProperties.IsExpanded:
                    named = true;
                    break;
                case AccessibilityProperties.Value or AccessibilityProperties.RangeValue or AccessibilityProperties.IsToggled:
                    break;
                default:
                    changes.RemoveRange(start, changes.Count - start);
                    return false;
            }
        }
        if (named && NamedFromContentAbove(element))
        {
            changes.RemoveRange(start, changes.Count - start);
            return false;
        }
        if (changes.Count > start) remade = node;
        return true;
    }

    /// <summary>Whether an ancestor of <paramref name="element"/> takes its name from the text inside it
    /// (<see cref="GetContentText"/>): a button or a tab with no name of its own.</summary>
    private static bool NamedFromContentAbove(Element element)
    {
        for (var ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.AccessibleName is null && IsNameFromContentRole(ResolveRole(ancestor)))
                return true;
        return false;
    }


    /// <summary>
    /// Determines whether an element is semantically meaningful in the accessibility tree.
    /// Elements with explicit roles, accessible names, or inferred roles are semantic.
    /// Plain BoxElements acting as layout containers are not.
    /// </summary>
    internal static bool IsSemantic(Element element)
    {
        if (element.Role != AccessibilityRole.None)
            return true;
        if (element.AccessibleName is not null)
            return true;
        if (element.AccessibleDescription is not null)
            return true;

        return InferRole(element) != AccessibilityRole.None;
    }

    /// <summary>
    /// Resolves the final accessibility role for an element.
    /// Explicit Role takes priority over inference.
    /// </summary>
    public static AccessibilityRole ResolveRole(Element element)
    {
        if (element.Role != AccessibilityRole.None)
            return element.Role;

        return InferRole(element);
    }

    /// <summary>
    /// Infers a role from the element's type and behavior.
    /// </summary>
    internal static AccessibilityRole InferRole(Element element) => element switch
    {
        TextInputElement => AccessibilityRole.TextInput,
        TextAreaElement => AccessibilityRole.TextInput,
        SliderElement => AccessibilityRole.Slider,
        CheckboxElement => AccessibilityRole.Checkbox,
        ToggleSwitchElement => AccessibilityRole.Switch,
        ProgressBarElement => AccessibilityRole.ProgressBar,
        RadioButtonElement => AccessibilityRole.RadioButton,
        TextElement => AccessibilityRole.Label,
        ImageElement => AccessibilityRole.Image,
        // Checked before ScrollViewElement: a virtualized list derives from it but
        // is semantically a list of items.
        IAccessibleItemSource => AccessibilityRole.List,
        ScrollViewElement => AccessibilityRole.ScrollView,
        CanvasElement => AccessibilityRole.Group,
        // A button is a button whether or not anything has subscribed to its click yet.
        ButtonElement => AccessibilityRole.Button,
        BoxElement box when box.HasEventHandler<ClickEvent>() => AccessibilityRole.Button,
        _ => AccessibilityRole.None
    };

    /// <summary>
    /// Resolves the accessible name. Explicit AccessibleName wins.
    /// Falls back to text content for TextElements, or element Name for others.
    /// </summary>
    internal static string? ResolveName(Element element)
    {
        if (element.AccessibleName is not null)
            return element.AccessibleName;

        if (element is TextElement text && !string.IsNullOrEmpty(text.Content))
            return text.Content;

        // Labels on checkboxes/radios serve as the accessible name when no
        // explicit AccessibleName is set.
        if (element is CheckboxElement checkbox && !string.IsNullOrEmpty(checkbox.Label))
            return checkbox.Label;
        if (element is RadioButtonElement radio && !string.IsNullOrEmpty(radio.Label))
            return radio.Label;
        if (element is ToggleSwitchElement toggle && !string.IsNullOrEmpty(toggle.Label))
            return toggle.Label;

        // Name from content: a button with no explicit name takes the
        // concatenated text of its descendants (e.g. a Box wrapping Text "Save").
        if (IsNameFromContentRole(ResolveRole(element)) && GetContentText(element) is { } content)
            return content;

        return element.Name;
    }

    /// <summary>
    /// Roles whose accessible name is derived from descendant text when no
    /// explicit <see cref="Element.AccessibleName"/> is set (as in the UIA/HTML
    /// "name from content" rule). Currently buttons and tabs, which ARIA names from content too.
    /// </summary>
    private static bool IsNameFromContentRole(AccessibilityRole role)
        => role is AccessibilityRole.Button or AccessibilityRole.Tab;

    /// <summary>
    /// Concatenates the text of an element's descendant <see cref="TextElement"/>s
    /// in document order, or <c>null</c> if there is none.
    /// </summary>
    internal static string? GetContentText(Element element)
    {
        // Mostly one text names the control: then its string is the name, and the tree, rebuilt
        // on every change a reader can see, builds no string per button to say so.
        string? first = null;
        StringBuilder? joined = null;
        AppendContentText(element, ref first, ref joined);
        return joined?.ToString() ?? first;
    }

    private static void AppendContentText(Element element, ref string? first, ref StringBuilder? joined)
    {
        var children = element.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child is TextElement text && !string.IsNullOrEmpty(text.Content))
            {
                if (first is null) first = text.Content;
                else (joined ??= new StringBuilder(first)).Append(' ').Append(text.Content);
            }
            AppendContentText(child, ref first, ref joined);
        }
    }

    /// <summary>
    /// True when a node's descendant text is fully captured by its own accessible
    /// name, so the text children can be dropped from the tree. Requires a
    /// name-from-content role, no explicit name (else the name may differ from the
    /// text), and only plain-text semantic descendants (no nested controls/images
    /// to preserve).
    /// </summary>
    private static bool ConsumesTextContent(Element element)
    {
        if (element.AccessibleName is not null)
            return false;
        if (!IsNameFromContentRole(ResolveRole(element)))
            return false;
        return HasOnlyTextDescendants(element);
    }

    private static bool HasOnlyTextDescendants(Element element)
    {
        var children = element.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (IsSemantic(child) && ResolveRole(child) != AccessibilityRole.Label)
                return false;
            if (!HasOnlyTextDescendants(child))
                return false;
        }
        return true;
    }

    internal static bool? ResolveIsToggled(Element element) => element switch
    {
        CheckboxElement cb => cb.IsChecked,
        ToggleSwitchElement ts => ts.IsOn,
        _ => element.AccessibleToggled,
    };

    /// <summary>Selected, for one of a set: a radio button is when it is checked; anything
    /// else says so with <see cref="Element.AccessibleSelected"/>.</summary>
    internal static bool? ResolveIsSelected(Element element) => element switch
    {
        RadioButtonElement rb => rb.IsChecked,
        _ => element.AccessibleSelected,
    };

    internal static string? ResolveValue(Element element) => element switch
    {
        TextInputElement ti => ti.ShownText,
        TextAreaElement ta => ta.Text,
        NumericUpDownElement n => n.DisplayText,
        // The pickers carry a value a reader needs and a name that describes the control,
        // so they cannot fold the two together the way a button does.
        DatePickerElement dp => dp.Value is { } d ? dp.DisplayText : "",
        TimePickerElement tp => tp.Value is null ? "" : tp.DisplayText,
        CalendarElement cal => cal.AccessibleValueText,
        _ => element.AccessibleValue
    };
}
