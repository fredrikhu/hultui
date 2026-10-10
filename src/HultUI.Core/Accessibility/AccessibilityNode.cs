using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Core.Accessibility;

public sealed class AccessibilityNode
{
    public ElementId ElementId { get; }
    public AccessibilityRole Role { get; }
    public string? Name { get; }
    public string? Description => _extras?.Description;

    /// <summary>The node's box on screen, in layout units: every scroll container above it
    /// subtracted (<see cref="ScrollOffset"/>).</summary>
    public Rect BoundingRect { get; }

    /// <summary>
    /// How far this node's content is scrolled, in layout units: a scroll view's (a virtual
    /// list's) offset; zero for anything that does not scroll. Its descendants' bounds have it
    /// subtracted, so a scroll moves them on screen and leaves them where they were within it.
    /// </summary>
    public Point ScrollOffset
    {
        get => _extras?.ScrollOffset ?? default;
        internal set
        {
            if (_extras is null && value == default) return;
            Extras.ScrollOffset = value;
        }
    }

    public bool IsFocusable => (_flags & FocusableBit) != 0;

    /// <summary>
    /// Whether the node's element has the keyboard focus. The one property that changes after
    /// the tree is built: a focus move rebuilds no tree, so the window marks the move on the
    /// applied tree's two nodes in place (UI thread), and the next diff does not report it again.
    /// </summary>
    public bool IsFocused
    {
        get => (_flags & FocusedBit) != 0;
        internal set => SetFlag(FocusedBit, value);
    }

    public bool IsEnabled => (_flags & EnabledBit) != 0;

    /// <summary>
    /// Toggle state for checkboxes and switch-like elements. <c>null</c> for
    /// elements that don't have a toggle concept (mapped to UIA
    /// <c>ToggleState.Indeterminate</c> or "pattern not supported" depending on
    /// role).
    /// </summary>
    public bool? IsToggled => GetTriState(ToggledShift);

    /// <summary>
    /// String value for elements that expose UIA's ValueProvider — text inputs
    /// and (later) sliders. <c>null</c> for elements without a value concept.
    /// </summary>
    public string? Value => _extras?.Value;

    /// <summary>A password field: its <see cref="Value"/> is the bullets on screen, never the
    /// text, and a reader says what it is (UIA's IsPassword, AT-SPI's password-text role).</summary>
    public bool IsPassword => (_flags & PasswordBit) != 0;

    /// <summary>Stable automation identifier (UIA AutomationId), for test selectors.</summary>
    public string? AutomationId => _extras?.AutomationId;

    /// <summary>Expanded state for an expandable node, or null if not expandable.</summary>
    public bool? Expanded => GetTriState(ExpandedShift);

    /// <summary>Selected state for one of a set — a radio button, a tab, an item of a list —
    /// or null for a node that is not selectable that way. A list's item nodes take it from
    /// their list (<see cref="IAccessibleItemSource.IsItemSelected"/>).</summary>
    public bool? IsSelected => GetTriState(SelectedShift);

    /// <summary>True when the node is scrolled/clipped out of the visible viewport.</summary>
    public bool IsOffscreen
    {
        get => (_flags & OffscreenBit) != 0;
        internal set => SetFlag(OffscreenBit, value);
    }

    public AccessibilityNode? Parent { get; private set; }

    /// <summary>The node's children, in order. Never changes once the tree is built.</summary>
    public IReadOnlyList<AccessibilityNode> Children => _children;

    /// <summary>
    /// True when this node is a virtualized list whose items are provided on
    /// demand rather than as child nodes (used for very large lists). The bridge
    /// synthesizes item providers by index instead of walking <see cref="Children"/>.
    /// </summary>
    public bool IsVirtualItemContainer
    {
        get => (_flags & VirtualItemsBit) != 0;
        internal set => SetFlag(VirtualItemsBit, value);
    }

    /// <summary>
    /// Content with an accessibility tree of its own, in windows of its own — a web page's.
    /// The bridge makes that tree this node's children and sends hit tests inside the node
    /// there. Empty for everything else.
    /// </summary>
    public NativeAccessibleContent NativeContent
    {
        get => _extras?.NativeContent ?? default;
        internal set
        {
            if (_extras is null && value.Equals(default(NativeAccessibleContent))) return;
            Extras.NativeContent = value;
        }
    }

    /// <summary>
    /// The text a reader can move through (UIA's text pattern, AT-SPI's Text), as it was when
    /// this tree was built; null for an element that offers none. The differ compares it to
    /// tell bridges the text, caret or selection changed.
    /// </summary>
    public AccessibleText? Text
    {
        get => _extras?.Text;
        internal set
        {
            if (_extras is null && value is null) return;
            Extras.Text = value;
        }
    }

    /// <summary>Where a bridge reads the text as it is now, from its own thread, between
    /// rebuilds; null with <see cref="Text"/>.</summary>
    public IAccessibleTextSource? TextSource
    {
        get => _extras?.TextSource;
        internal set
        {
            if (_extras is null && value is null) return;
            Extras.TextSource = value;
        }
    }

    /// <summary>The text now: the source's, or the snapshot this tree was built with.</summary>
    public AccessibleText? CurrentText => _extras is { } x ? x.TextSource?.AccessibleText ?? x.Text : null;

    /// <summary>Whether the node offers text a reader moves through (UIA's text pattern, AT-SPI's
    /// Text): a change of it is told as a text change, never as a change of its
    /// <see cref="Value"/>.</summary>
    internal bool OffersText => _extras is { } x && (x.TextSource is not null || x.Text is not null);

    // An array of the exact length: a tree is rebuilt in full on every change an assistive
    // technology can see, and most nodes are leaves, which share the one empty array.
    private AccessibilityNode[] _children = [];

    /// <summary>
    /// The fields most nodes leave empty, in one object that only the nodes that set one of them
    /// get: a description, a value, an automation id, a scroll offset, native content, text. A
    /// tree is rebuilt in full on every change a reader can see, and these were 64 bytes of every
    /// node (136 → 80) for the few that use them: a text field, a scroll view, a described control.
    /// Written while the tree is built, on the UI thread, never after it is out.
    /// </summary>
    private sealed class RareFields
    {
        public string? Description;
        public string? Value;
        public string? AutomationId;
        public Point ScrollOffset;
        public NativeAccessibleContent NativeContent;
        public AccessibleText? Text;
        public IAccessibleTextSource? TextSource;
    }

    private RareFields? _extras;

    private RareFields Extras => _extras ??= new RareFields();

    /// <summary>Whether this node or <paramref name="other"/> carries any of the rarely set
    /// fields; when neither does, the differ has none of them to compare.</summary>
    internal bool EitherHasRareFields(AccessibilityNode other) => _extras is not null || other._extras is not null;

    // The node's states in one field, for the same reason: six bools and three nullable bools
    // were 12 bytes of every node, and ScrollOffset took their place. A nullable state is two
    // bits, whether it has a value and the value. Written on the UI thread only; IsFocused is
    // the one written once the tree is out, and a reader on another thread sees every other
    // bit unchanged either side of that write.
    private ushort _flags;
    private const int FocusableBit = 1 << 0, FocusedBit = 1 << 1, EnabledBit = 1 << 2, PasswordBit = 1 << 3,
        OffscreenBit = 1 << 4, VirtualItemsBit = 1 << 5;
    private const int ToggledShift = 6, ExpandedShift = 8, SelectedShift = 10;

    /// <summary>Every state at once, for the differ: equal when none of them differs.</summary>
    internal int States => _flags;

    private void SetFlag(int bit, bool on) =>
        _flags = (ushort)(on ? _flags | bit : _flags & ~bit);

    private bool? GetTriState(int shift) =>
        ((_flags >> shift) & 1) == 0 ? null : ((_flags >> shift) & 2) != 0;

    private static int TriState(bool? value, int shift) =>
        value is { } v ? (v ? 3 : 1) << shift : 0;

    internal AccessibilityNode(
        ElementId elementId,
        AccessibilityRole role,
        string? name,
        string? description,
        Rect boundingRect,
        bool isFocusable = false,
        bool isFocused = false,
        bool isEnabled = true,
        bool? isToggled = null,
        string? value = null,
        string? automationId = null,
        bool? expanded = null,
        bool isPassword = false,
        bool? isSelected = null)
    {
        ElementId = elementId;
        Role = role;
        Name = name;
        BoundingRect = boundingRect;
        if (description is not null || value is not null || automationId is not null)
            _extras = new RareFields { Description = description, Value = value, AutomationId = automationId };
        _flags = (ushort)((isFocusable ? FocusableBit : 0) | (isFocused ? FocusedBit : 0)
            | (isEnabled ? EnabledBit : 0) | (isPassword ? PasswordBit : 0)
            | TriState(isToggled, ToggledShift) | TriState(expanded, ExpandedShift)
            | TriState(isSelected, SelectedShift));
    }

    /// <summary>Makes <paramref name="children"/> this node's children, in place of any it had.
    /// The node keeps the array: the caller must not change it afterwards.</summary>
    internal void SetChildren(AccessibilityNode[] children)
    {
        foreach (var child in children)
            child.Parent = this;
        _children = children;
    }

    /// <summary>Adds one child after the others. Copies the children each time: for the odd
    /// node added after a build (an overlay's tree under the window's root), not for building.</summary>
    internal void AddChild(AccessibilityNode child)
    {
        var children = new AccessibilityNode[_children.Length + 1];
        _children.CopyTo(children, 0);
        children[^1] = child;
        child.Parent = this;
        _children = children;
    }
}
