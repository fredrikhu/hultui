using System.Text;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI.Core.Accessibility;

public static class AccessibilityTreeBuilder
{
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
        BuildChildren(root, node, focusManager, cx, cy);
        return node;
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
            isEnabled: true,
            isToggled: ResolveIsToggled(element),
            value: ResolveValue(element));
    }

    private static void BuildChildren(Element element, AccessibilityNode parent, FocusManager? focusManager,
        float scrollX, float scrollY)
    {
        foreach (var child in element.Children)
        {
            // The child's own box sits in the parent's scroll space; its own
            // scroll (if it's a ScrollView) applies to its descendants.
            var (cx, cy) = ChildScroll(child, scrollX, scrollY);
            if (IsSemantic(child))
            {
                var childNode = CreateNode(child, focusManager, scrollX, scrollY);
                parent.AddChild(childNode);
                // A text-only button (or other name-from-content role) folds its
                // text into its own accessible name; don't also expose that text
                // as child nodes — that matches native controls and stops a
                // screen reader announcing the label twice.
                if (!ConsumesTextContent(child))
                    BuildChildren(child, childNode, focusManager, cx, cy);
            }
            else
            {
                // Flatten: promote this element's children directly into parent
                BuildChildren(child, parent, focusManager, cx, cy);
            }
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
        CheckboxElement => AccessibilityRole.Checkbox,
        RadioButtonElement => AccessibilityRole.RadioButton,
        TextElement => AccessibilityRole.Label,
        ImageElement => AccessibilityRole.Image,
        ScrollViewElement => AccessibilityRole.ScrollView,
        CanvasElement => AccessibilityRole.Group,
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
        _ => null
    };

    internal static string? ResolveValue(Element element) => element switch
    {
        TextInputElement ti => ti.Text,
        TextAreaElement ta => ta.Text,
        _ => null
    };
}
