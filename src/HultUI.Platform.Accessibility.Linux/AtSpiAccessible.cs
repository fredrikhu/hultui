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

    /// <summary>The one state that changes after the snapshot is built: a focus move rebuilds
    /// none, so the bridge sets it on the UI thread while the bus thread may read it.</summary>
    public bool Focused
    {
        get => Volatile.Read(ref _focused);
        set => Volatile.Write(ref _focused, value);
    }
    private bool _focused;
    public bool Enabled { get; init; } = true;
    public bool Offscreen { get; init; }
    public bool Selectable { get; init; }
    public bool Selected { get; init; }
    public bool? Toggled { get; init; }
    /// <summary>Open or closed, for an object that can be either; null for one that cannot.</summary>
    public bool? Expanded { get; init; }

    /// <summary>The text a reader moves through (seam A1), as when the tree was built, and where
    /// to read it now; both null for an object with none.</summary>
    public AccessibleText? Text { get; init; }
    public IAccessibleTextSource? TextSource { get; init; }

    /// <summary>The text now: the source's, read on the bus thread, or the built snapshot.</summary>
    public AccessibleText? CurrentText => TextSource?.AccessibleText ?? Text;

    /// <summary>Device pixels per layout unit, for the text's geometry.</summary>
    public float Scale { get; init; } = 1f;

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
            // Not Value for a string value: AT-SPI's Value is a number (a slider's), and it was
            // advertised here with no handler behind it, so a client asking got UnknownMethod.
            if (Text is not null || TextSource is not null) list.Add(AtSpiConstants.TextInterface);
            return [.. list];
        }
    }

    /// <summary>Builds the object path for a node from its element id (root is well-known).</summary>
    public static string PathFor(AccessibilityNode node, bool isRoot) =>
        isRoot ? AtSpiConstants.RootPath : AtSpiConstants.AccessiblePathPrefix + node.ElementId.Value;

    /// <summary>
    /// The application's name on the bus, which a screen reader announces for it: the entry
    /// assembly's, as the CEF engine names the application's profile folder. Not the
    /// executable's, which under <c>dotnet app.dll</c> is "dotnet" for every application.
    /// </summary>
    public static readonly string ApplicationName =
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name
        ?? System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath)
        ?? "HultUI";

    public static AtSpiAccessible From(
        AccessibilityNode node, bool isRoot, string parentPath, int indexInParent, float scale)
    {
        uint role = isRoot ? AtSpiConstants.RoleApplication : RoleOf(node);
        var childPaths = new List<string>(node.Children.Count);
        foreach (var child in node.Children)
            childPaths.Add(PathFor(child, isRoot: false));

        // Bounds are in layout units; AT-SPI reports device pixels.
        var rect = node.BoundingRect;
        return new AtSpiAccessible
        {
            ElementId = node.ElementId,
            Path = PathFor(node, isRoot),
            IsRoot = isRoot,
            Role = role,
            // The root is the application, named as one; the window's content is its children.
            Name = isRoot ? ApplicationName : node.Name ?? "",
            Description = node.Description ?? "",
            Value = node.Value,
            X = (int)MathF.Round(rect.X * scale),
            Y = (int)MathF.Round(rect.Y * scale),
            Width = (int)MathF.Round(rect.Width * scale),
            Height = (int)MathF.Round(rect.Height * scale),
            Focusable = node.IsFocusable,
            Focused = node.IsFocused,
            Enabled = node.IsEnabled,
            Offscreen = node.IsOffscreen,
            Selectable = node.IsSelected is not null || node.Role is AccessibilityRole.RadioButton
                or AccessibilityRole.Tab or AccessibilityRole.ListItem or AccessibilityRole.TreeItem,
            // From the node's selected state, which a checked radio and a selected tab carry. It
            // was read from IsToggled, which neither ever had: no radio or tab read as selected.
            Selected = node.IsSelected == true,
            Toggled = node.IsToggled,
            Expanded = node.Expanded,
            Text = node.Text,
            TextSource = node.TextSource,
            Scale = scale,
            ParentPath = parentPath,
            IndexInParent = indexInParent,
            ChildPaths = childPaths,
            Action = ActionFor(node.Role),
        };
    }

    /// <summary>The role a reader is told, for anything but the root. A password is a role of
    /// its own on AT-SPI, where UIA has a flag on an edit control.</summary>
    public static uint RoleOf(AccessibilityNode node) =>
        node.IsPassword ? AtSpiConstants.RolePasswordText : AtSpiConstants.MapRole(node.Role);

    private static AtSpiActionKind? ActionFor(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Button or AccessibilityRole.Link or AccessibilityRole.ColumnHeader => AtSpiActionKind.Click,
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
