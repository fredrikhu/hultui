using HultUI.Core.Elements;
using HultUI.Core.Events;

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
        var node = CreateNode(root, focusManager);
        BuildChildren(root, node, focusManager);
        return node;
    }

    private static AccessibilityNode CreateNode(Element element, FocusManager? focusManager)
    {
        return new AccessibilityNode(
            element.Id,
            ResolveRole(element),
            ResolveName(element),
            element.AccessibleDescription,
            element.Layout.AbsoluteBorderBox,
            isFocusable: element.IsFocusable,
            isFocused: focusManager is not null && ReferenceEquals(focusManager.FocusedElement, element),
            isEnabled: true,
            isToggled: ResolveIsToggled(element),
            value: ResolveValue(element));
    }

    private static void BuildChildren(Element element, AccessibilityNode parent, FocusManager? focusManager)
    {
        foreach (var child in element.Children)
        {
            if (IsSemantic(child))
            {
                var childNode = CreateNode(child, focusManager);
                parent.AddChild(childNode);
                BuildChildren(child, childNode, focusManager);
            }
            else
            {
                // Flatten: promote this element's children directly into parent
                BuildChildren(child, parent, focusManager);
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

        return element.Name;
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
