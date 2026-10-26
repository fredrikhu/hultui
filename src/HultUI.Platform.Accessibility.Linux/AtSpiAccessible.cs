using System.Globalization;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// A snapshot of one accessible object as it is exposed on the a11y
/// bus. One is built per <see cref="AccessibilityNode"/> on each structural
/// rebuild; the object <see cref="Path"/> is derived from the stable
/// <see cref="ElementId"/> so a node keeps its D-Bus identity across rebuilds.
/// What changes without a rebuild is set in place on the UI thread: the focused and
/// selected states, and a leaf's name, description, value, range, toggle and expanded
/// state (the window's patch). Its box and whether it is out of view are read from its node
/// when a client asks (<see cref="Node"/>), as they are now.
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

    /// <summary>The name. Changes after the snapshot is built, as <see cref="Description"/>,
    /// <see cref="Value"/>, <see cref="Range"/>, <see cref="Toggled"/> and <see cref="Expanded"/> do:
    /// a leaf's change of its own properties rebuilds no snapshot (plan 17, round 4), so the bridge
    /// sets the new value on the UI thread, one write each, while the bus thread may read it.</summary>
    public string Name
    {
        get => Volatile.Read(ref _name);
        set => Volatile.Write(ref _name, value);
    }
    private string _name = "";

    public string Description
    {
        get => Volatile.Read(ref _description);
        set => Volatile.Write(ref _description, value);
    }
    private string _description = "";

    /// <summary>The node's string value: a text field's text (a password's bullets).</summary>
    public string? Value
    {
        get
        {
            var value = Volatile.Read(ref _value);   // once: the UI thread may replace it meanwhile
            return value is RangeBox box ? box.Value : (string?)value;
        }
        set => Volatile.Write(ref _value, _value is RangeBox box ? box with { Value = value } : value);
    }

    /// <summary>
    /// The node this object stands for in the window's applied tree; null for the seed root. Its box
    /// and whether it is out of view are read from it on the bus thread (<see cref="Box"/>,
    /// <see cref="Offscreen"/>): a layout refresh writes them in place in the tree — a scroll is one
    /// scroll view's offset, a move one node's box — and sends nothing for them, as a diff sends
    /// nothing for a scroll, so the object follows with no snapshot made again. The objects were made
    /// again for every node on every refresh to copy them: 772 KB a wheel turn over 1,000 rows. A
    /// leaf's node made again (the window's patch) is pointed at here, on the UI thread.
    /// </summary>
    public AccessibilityNode? Node
    {
        get => Volatile.Read(ref _node);
        set => Volatile.Write(ref _node, value);
    }
    private AccessibilityNode? _node;

    /// <summary>The bounding box in window-relative device pixels: the node's box on screen, as it is
    /// now, times <see cref="Scale"/>, rounded. Read once: each read walks the node's parents.</summary>
    public (int X, int Y, int Width, int Height) Box
    {
        get
        {
            if (Node is not { } node) return default;
            var rect = node.BoundingRect;
            return ((int)MathF.Round(rect.X * Scale), (int)MathF.Round(rect.Y * Scale),
                (int)MathF.Round(rect.Width * Scale), (int)MathF.Round(rect.Height * Scale));
        }
    }

    public int X => Box.X;
    public int Y => Box.Y;
    public int Width => Box.Width;
    public int Height => Box.Height;

    public bool Focusable { get; init; }

    /// <summary>Changes after the snapshot is built (with a list item's <see cref="Selected"/>): a
    /// focus move rebuilds none, so the bridge sets it on the UI thread while the bus thread may
    /// read it.</summary>
    public bool Focused
    {
        get => Volatile.Read(ref _focused);
        set => Volatile.Write(ref _focused, value);
    }
    private bool _focused;
    public bool Enabled { get; init; } = true;

    /// <summary>Whether the node is scrolled or clipped out of view, as it is now (<see cref="Node"/>).</summary>
    public bool Offscreen => Node?.IsOffscreen ?? false;
    public bool Selectable { get; init; }

    /// <summary>Changes after the snapshot is built too, for a list's items: a move of the list's
    /// selection rebuilds none, so the bridge sets it on the UI thread while the bus thread may
    /// read it.</summary>
    public bool Selected
    {
        get => Volatile.Read(ref _selected);
        set => Volatile.Write(ref _selected, value);
    }
    private bool _selected;
    /// <summary>Checked or not, for a toggle; null for anything else.</summary>
    public bool? Toggled
    {
        get => FromTriState(Volatile.Read(ref _toggled));
        set => Volatile.Write(ref _toggled, ToTriState(value));
    }
    private byte _toggled;

    /// <summary>Open or closed, for an object that can be either; null for one that cannot.</summary>
    public bool? Expanded
    {
        get => FromTriState(Volatile.Read(ref _expanded));
        set => Volatile.Write(ref _expanded, ToTriState(value));
    }
    private byte _expanded;

    // A nullable state in one byte, written and read whole: none, false, true.
    private static byte ToTriState(bool? value) => value is { } v ? (byte)(v ? 2 : 1) : (byte)0;
    private static bool? FromTriState(byte state) => state == 0 ? null : state == 2;

    /// <summary>A value in a range, for the Value interface: a slider's, a progress bar's, a number
    /// field's; null for anything else. Kept with the string value in one field, in an object of
    /// its own that only those nodes get: a snapshot makes an object per node on every apply, and
    /// the range inline would have been 56 bytes more in each, a fifth of a node's cost on AT-SPI.</summary>
    public AccessibleRange? Range
    {
        get => (Volatile.Read(ref _value) as RangeBox)?.Range;
        set => Volatile.Write(ref _value, value is { } range ? new RangeBox(range, Value) : Value);
    }

    // The string value, or a RangeBox holding a range and the string value. Replaced whole when either
    // changes, so a reader on the bus thread reads both as they were or both as they are.
    private object? _value;

    private sealed record RangeBox(AccessibleRange Range, string? Value);

    /// <summary>The text a reader moves through (seam A1): where to read it now, or, for an object
    /// with no source, the text as the tree was built with it; both null for an object with none.</summary>
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
            // A number in a range, as Chromium and GTK advertise it for a slider, a progress bar and
            // a spin button.
            if (Range is not null) list.Add(AtSpiConstants.ValueInterface);
            return [.. list];
        }
    }

    /// <summary>Builds the object path for a node from its element id (root is well-known).</summary>
    public static string PathFor(AccessibilityNode node, bool isRoot) =>
        isRoot ? AtSpiConstants.RootPath : PathFor(node.ElementId);

    /// <summary>The object path of an element that is not the root: the id written straight into
    /// the path's one string. Adding the id to the prefix made the id's own string first, then
    /// the path, and the snapshot makes a path for every node on every rebuild. (Not an
    /// interpolated string: until the JIT optimizes it, its generic append boxes the id.)</summary>
    public static string PathFor(ElementId id)
    {
        Span<char> digits = stackalloc char[20];   // a ulong has at most 20 digits
        id.Value.TryFormat(digits, out int length, provider: CultureInfo.InvariantCulture);
        return string.Concat(AtSpiConstants.AccessiblePathPrefix.AsSpan(), digits[..length]);
    }

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
        => From(node, PathFor(node, isRoot), isRoot, parentPath, indexInParent, scale, ChildPathsOf(node));

    /// <summary>The paths of a node's children, in order, in an array of the exact length.</summary>
    public static string[] ChildPathsOf(AccessibilityNode node)
    {
        var children = node.Children;   // by index: foreach over the interface boxes an enumerator
        if (children.Count == 0) return [];
        var paths = new string[children.Count];
        for (int i = 0; i < paths.Length; i++)
            paths[i] = PathFor(children[i].ElementId);
        return paths;
    }

    /// <summary>
    /// The object for <paramref name="node"/>, published at <paramref name="path"/>, listing its
    /// children at <paramref name="childPaths"/>. The bridge's walk makes each path once: it lists
    /// a child under its parent and publishes the child at the same string.
    /// </summary>
    public static AtSpiAccessible From(
        AccessibilityNode node, string path, bool isRoot, string parentPath, int indexInParent, float scale,
        string[] childPaths)
    {
        uint role = isRoot ? AtSpiConstants.RoleApplication : RoleOf(node);

        return new AtSpiAccessible
        {
            ElementId = node.ElementId,
            Path = path,
            IsRoot = isRoot,
            Role = role,
            // The root is the application, named as one; the window's content is its children.
            Name = isRoot ? ApplicationName : node.Name ?? "",
            Description = node.Description ?? "",
            // A node that offers text is read through its source when asked: its text and value
            // are made only then (a long document's are 1.5 MB), and its changes are text changes.
            Value = node.TextSource is null ? node.Value : null,
            // Its box and offscreen state, read from it; AT-SPI reports device pixels (Scale).
            Node = node,
            Focusable = node.IsFocusable,
            Focused = node.IsFocused,
            Enabled = node.IsEnabled,
            Selectable = node.IsSelected is not null || node.Role is AccessibilityRole.RadioButton
                or AccessibilityRole.Tab or AccessibilityRole.ListItem or AccessibilityRole.TreeItem,
            // From the node's selected state, which a checked radio and a selected tab carry. It
            // was read from IsToggled, which neither ever had: no radio or tab read as selected.
            Selected = node.IsSelected == true,
            Toggled = node.IsToggled,
            Expanded = node.Expanded,
            Range = node.Range,
            Text = node.TextSource is null ? node.Text : null,
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
