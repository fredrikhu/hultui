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
    /// Builds an accessibility tree from the element tree rooted at the given element.
    /// Decorative containers (BoxElements with no role, name, or semantic children) are
    /// flattened — their children are promoted to the parent accessibility node.
    /// When <paramref name="focusManager"/> is supplied, the focused element gets
    /// <see cref="AccessibilityNode.IsFocused"/> set.
    /// </summary>
    public static AccessibilityNode Build(Element root, FocusManager? focusManager = null)
    {
        var node = CreateNode(root, focusManager, 0f, 0f);
        var (cx, cy) = ChildScroll(root, 0f, 0f);
        // The root is the top-level visible region; descendants clipped out of it
        // (and out of any scroll view along the way) are marked offscreen.
        var childClip = ClipForChildren(root, node.BoundingRect, 0f, 0f);
        if (!TryBuildVirtualItems(root, node, cx, cy, childClip))
            BuildChildren(root, node, focusManager, cx, cy, childClip);
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
        var onScreen = new Rect(box.X - scrollX, box.Y - scrollY, box.Width, box.Height);
        return new AccessibilityNode(
            element.Id,
            ResolveRole(element),
            ResolveName(element),
            element.AccessibleDescription,
            onScreen,
            isFocusable: element.IsFocusable,
            isFocused: focusManager is not null && ReferenceEquals(focusManager.FocusedElement, element),
            isEnabled: element.IsEnabledForAccessibility,
            isToggled: ResolveIsToggled(element),
            value: ResolveValue(element),
            automationId: element.AutomationId,
            expanded: element.AccessibleExpanded);
    }

    private static void BuildChildren(Element element, AccessibilityNode parent, FocusManager? focusManager,
        float scrollX, float scrollY, Rect clip)
    {
        foreach (var child in element.Children)
        {
            // The child's own box sits in the parent's scroll space; its own
            // scroll (if it's a ScrollView) applies to its descendants.
            var (cx, cy) = ChildScroll(child, scrollX, scrollY);
            var childClip = ClipForChildren(child, clip, scrollX, scrollY);
            if (IsSemantic(child))
            {
                var childNode = CreateNode(child, focusManager, scrollX, scrollY);
                childNode.IsOffscreen = !RectsIntersect(clip, childNode.BoundingRect);
                parent.AddChild(childNode);
                // A virtualized list only materializes on-screen rows as elements;
                // expose every item to assistive tech instead. Otherwise, unless a
                // text-only button folded its text into its own name (which would
                // double-announce), walk the real children.
                if (!TryBuildVirtualItems(child, childNode, cx, cy, childClip) && !ConsumesTextContent(child))
                    BuildChildren(child, childNode, focusManager, cx, cy, childClip);
            }
            else
            {
                // Flatten: promote this element's children directly into parent
                BuildChildren(child, parent, focusManager, cx, cy, childClip);
            }
        }
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
        for (int i = 0; i < count; i++)
        {
            float top = content.Y + i * itemHeight;
            var rect = new Rect(content.X - scrollX, top - scrollY, content.Width, itemHeight);
            listNode.AddChild(new AccessibilityNode(
                VirtualItemId.For(list.Id, i),
                AccessibilityRole.ListItem,
                source.GetItemLabel(i),
                description: null,
                rect,
                isFocusable: false,
                isFocused: false,
                isEnabled: true,
                isToggled: null,
                value: null)
            {
                IsOffscreen = !RectsIntersect(clip, rect),
            });
        }
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
    /// "name from content" rule). Currently buttons.
    /// </summary>
    private static bool IsNameFromContentRole(AccessibilityRole role)
        => role == AccessibilityRole.Button;

    /// <summary>
    /// Concatenates the text of an element's descendant <see cref="TextElement"/>s
    /// in document order, or <c>null</c> if there is none.
    /// </summary>
    internal static string? GetContentText(Element element)
    {
        var sb = new StringBuilder();
        AppendContentText(element, sb);
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static void AppendContentText(Element element, StringBuilder sb)
    {
        foreach (var child in element.Children)
        {
            if (child is TextElement text && !string.IsNullOrEmpty(text.Content))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(text.Content);
            }
            AppendContentText(child, sb);
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
        foreach (var child in element.Children)
        {
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
        _ => null
    };

    internal static string? ResolveValue(Element element) => element switch
    {
        TextInputElement ti => ti.Text,
        TextAreaElement ta => ta.Text,
        NumericUpDownElement n => n.DisplayText,
        // The pickers carry a value a reader needs and a name that describes the control,
        // so they cannot fold the two together the way a button does.
        DatePickerElement dp => dp.Value is { } d ? dp.DisplayText : "",
        TimePickerElement tp => tp.Value is null ? "" : tp.DisplayText,
        CalendarElement cal => cal.AccessibleValueText,
        _ => null
    };
}
