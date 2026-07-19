using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// An immutable snapshot of one accessible object as it is exposed on the a11y
/// bus. One is built per <see cref="AccessibilityNode"/> on each structural
/// rebuild; the object <see cref="Path"/> is derived from the stable
/// <see cref="ElementId"/> so a node keeps its D-Bus identity across rebuilds.
/// </summary>
/// <remarks>
/// The snapshot carries everything the D-Bus method handlers need to answer a
/// query without touching the live element tree — which matters because those
/// handlers run on the bus receive thread, not the UI thread.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class AtSpiAccessible
{
    public required ElementId ElementId { get; init; }
    public required string Path { get; init; }
    public required bool IsRoot { get; init; }

    public uint Role { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Value { get; init; }

    // Bounding box in window-relative layout coordinates.
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public bool Focusable { get; init; }
    public bool Focused { get; init; }
    public bool Enabled { get; init; } = true;
    public bool Offscreen { get; init; }
    public bool Selectable { get; init; }
    public bool Selected { get; init; }
    public bool? Toggled { get; init; }

    public string ParentPath { get; init; } = AtSpiConstants.NullPath;
    public int IndexInParent { get; init; } = -1;
    public IReadOnlyList<string> ChildPaths { get; init; } = [];

    /// <summary>The action this object exposes to assistive tech, or null if none.</summary>
    public AtSpiActionKind? Action { get; init; }

    public int ChildCount => ChildPaths.Count;
    public bool HasAction => Action is not null;

    /// <summary>The AT-SPI action name reported via the Action interface.</summary>
    public string ActionName => Action switch
    {
        AtSpiActionKind.Click => "click",
        AtSpiActionKind.Toggle => "toggle",
        AtSpiActionKind.Select => "select",
        _ => "",
    };

    /// <summary>The AT-SPI interface names this object advertises via GetInterfaces.</summary>
    public string[] Interfaces
    {
        get
        {
            var list = new List<string>(5)
            {
                AtSpiConstants.AccessibleInterface,
                AtSpiConstants.ComponentInterface,
            };
            if (IsRoot) list.Add(AtSpiConstants.ApplicationInterface);
            if (HasAction) list.Add(AtSpiConstants.ActionInterface);
            if (Value is not null) list.Add(AtSpiConstants.ValueInterface);
            return [.. list];
        }
    }

    /// <summary>Builds the object path for a node from its element id (root is well-known).</summary>
    public static string PathFor(AccessibilityNode node, bool isRoot) =>
        isRoot ? AtSpiConstants.RootPath : AtSpiConstants.AccessiblePathPrefix + node.ElementId.Value;

    public static AtSpiAccessible From(AccessibilityNode node, bool isRoot, string parentPath, int indexInParent)
    {
        uint role = isRoot ? AtSpiConstants.RoleApplication : AtSpiConstants.MapRole(node.Role);
        var childPaths = new List<string>(node.Children.Count);
        foreach (var child in node.Children)
            childPaths.Add(PathFor(child, isRoot: false));

        return new AtSpiAccessible
        {
            ElementId = node.ElementId,
            Path = PathFor(node, isRoot),
            IsRoot = isRoot,
            Role = role,
            Name = node.Name ?? "",
            Description = node.Description ?? "",
            Value = node.Value,
            X = (int)MathF.Round(node.BoundingRect.X),
            Y = (int)MathF.Round(node.BoundingRect.Y),
            Width = (int)MathF.Round(node.BoundingRect.Width),
            Height = (int)MathF.Round(node.BoundingRect.Height),
            Focusable = node.IsFocusable,
            Focused = node.IsFocused,
            Enabled = node.IsEnabled,
            Offscreen = node.IsOffscreen,
            Selectable = node.Role is AccessibilityRole.RadioButton or AccessibilityRole.Tab
                or AccessibilityRole.ListItem or AccessibilityRole.TreeItem,
            Selected = node.IsToggled == true &&
                node.Role is AccessibilityRole.RadioButton or AccessibilityRole.Tab,
            Toggled = node.IsToggled,
            ParentPath = parentPath,
            IndexInParent = indexInParent,
            ChildPaths = childPaths,
            Action = ActionFor(node.Role),
        };
    }

    private static AtSpiActionKind? ActionFor(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Button or AccessibilityRole.Link => AtSpiActionKind.Click,
        AccessibilityRole.Checkbox or AccessibilityRole.Switch => AtSpiActionKind.Toggle,
        AccessibilityRole.RadioButton => AtSpiActionKind.Select,
        AccessibilityRole.Tab or AccessibilityRole.ListItem or AccessibilityRole.TreeItem => AtSpiActionKind.Click,
        _ => null,
    };
}

/// <summary>The kind of activation an accessible exposes through the Action interface.</summary>
[SupportedOSPlatform("linux")]
internal enum AtSpiActionKind
{
    /// <summary>Dispatch a click (buttons, links, list items).</summary>
    Click,
    /// <summary>Flip a checkbox / switch.</summary>
    Toggle,
    /// <summary>Select a radio button.</summary>
    Select,
}
