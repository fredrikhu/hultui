using HultUI.Core.Primitives;

namespace HultUI.Core.Accessibility;

/// <summary>What assistive technology can ask of a node of described content.</summary>
public enum AccessibleContentAction
{
    /// <summary>Activate it, as a click would: a button, a link.</summary>
    Invoke,
    /// <summary>Flip it: a check box, a switch.</summary>
    Toggle,
    /// <summary>Choose it: a radio button, an option.</summary>
    Select,
    /// <summary>Give it the keyboard.</summary>
    Focus,
    /// <summary>Replace its text with the value given.</summary>
    SetValue,
}

/// <summary>
/// One node of content an element describes with data rather than with elements: a web
/// page's, from its engine. Immutable; a source publishes a new tree as a whole.
/// </summary>
/// <param name="Key">The source's own id for the node, stable while the node lives. Together
/// with the element's id it names the node to the platform (<see cref="ContentNodeId"/>), and
/// it is what an action is addressed to.</param>
/// <param name="Role">What the node is.</param>
/// <param name="Name">What it is called, or null.</param>
/// <param name="Bounds">Where it is, in device pixels from the element's content box (see
/// <see cref="IAccessibleContentSource.AccessibleContentScale"/>).</param>
public sealed record AccessibleContentNode(int Key, AccessibilityRole Role, string? Name, Rect Bounds)
{
    public string? Description { get; init; }

    /// <summary>A value to read out and, for text, to set: a field's text.</summary>
    public string? Value { get; init; }

    public bool IsFocusable { get; init; }
    public bool IsFocused { get; init; }
    public bool IsEnabled { get; init; } = true;

    /// <summary>On or off, for a check box or a switch; null for nodes without the state.</summary>
    public bool? IsToggled { get; init; }

    /// <summary>Expanded or collapsed, for a node that is either; null otherwise.</summary>
    public bool? Expanded { get; init; }

    public IReadOnlyList<AccessibleContentNode> Children { get; init; } = [];
}

/// <summary>
/// Implemented by an element whose content is described by data, not by elements: the
/// tree builder makes nodes of the data, under the element's own node, and the platform
/// bridges serve them like any others. Seam W11 of <c>docs/design/WEBVIEW.md</c>.
/// </summary>
/// <remarks>
/// The tree is read on the UI thread, whenever the accessibility tree is rebuilt; a source
/// whose content changed says so with <c>NotifyAccessibilityPropertyChanged</c>
/// (<see cref="AccessibilityProperties.Content"/>). A source costly to describe keeps its
/// content empty while <see cref="AccessibilityClients.Active"/> is false.
/// </remarks>
public interface IAccessibleContentSource
{
    /// <summary>The roots of the content's tree; empty when there is none, or none described.</summary>
    IReadOnlyList<AccessibleContentNode> AccessibleContent { get; }

    /// <summary>Device pixels per layout unit, which the content's bounds are measured in.</summary>
    float AccessibleContentScale { get; }

    /// <summary>
    /// Assistive technology asked something of the node <paramref name="key"/> names. UI
    /// thread. <paramref name="value"/> is the text for <see cref="AccessibleContentAction.SetValue"/>.
    /// </summary>
    void PerformAccessibleAction(int key, AccessibleContentAction action, string? value = null);
}
