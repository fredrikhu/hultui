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
    /// <remarks>Worked out from the node's place in its scroll views' content
    /// (<see cref="UnscrolledRect"/>) less the offsets of the scroll views above it, read up its
    /// <see cref="Parent"/>s: a scroll changes one node's offset, and every box under it reads
    /// the move from there.</remarks>
    public Rect BoundingRect
    {
        get
        {
            var rect = _rect;
            var above = ScrollAbove();
            return new Rect(rect.X - above.X, rect.Y - above.Y, rect.Width, rect.Height);
        }
    }

    /// <summary>
    /// The node's box where it sits in the content of the scroll views above it, in layout units:
    /// <see cref="BoundingRect"/> with their offsets added back. What the builder makes and the
    /// differ compares: a scroll moves a node on screen and leaves this as it was.
    /// </summary>
    internal Rect UnscrolledRect => _rect;

    // The node's box in its scroll views' content (UnscrolledRect).
    private readonly Rect _rect;

    /// <summary>
    /// How far the scroll views above this node are scrolled, summed: the offsets of its
    /// <see cref="Parent"/>s, added from the root down, in the order the builder adds them while it
    /// walks down, so <see cref="BoundingRect"/> comes out as the builder's subtraction did.
    /// </summary>
    internal Point ScrollAbove()
    {
        var parent = Parent;
        if (parent is null) return default;
        var above = parent.ScrollAbove();
        var offset = parent.ScrollOffset;
        return new Point(above.X + offset.X, above.Y + offset.Y);
    }

    /// <summary>
    /// How far this node's content is scrolled, in layout units: a scroll view's (a virtual
    /// list's) offset; zero for anything that does not scroll. Its descendants' bounds have it
    /// subtracted (<see cref="BoundingRect"/>), so a scroll moves them on screen and leaves them
    /// where they were within it (<see cref="UnscrolledRect"/>).
    /// </summary>
    public Point ScrollOffset
    {
        get => _extras?.Scroll?.Offset ?? default;
        internal set
        {
            if (_extras?.Scroll is null && value == default) return;
            Scroll.Offset = value;
        }
    }

    /// <summary>
    /// A scroll view's (a virtual list's) box its content scrolls in, on screen in layout units —
    /// the scroll of the views above it applied, not its own — and the size of that content, as
    /// this tree was built: what UI Automation's Scroll pattern answers, on the client's thread.
    /// Null for anything that does not scroll.
    /// </summary>
    public AccessibleScrollExtent? ScrollExtent
    {
        get
        {
            if (_extras?.Scroll is not { IsScrollView: true } s) return null;
            // Kept where it sits in the content of the views above, as the node's box is.
            var above = ScrollAbove();
            var viewport = s.Viewport;
            return new AccessibleScrollExtent(
                new Rect(viewport.X - above.X, viewport.Y - above.Y, viewport.Width, viewport.Height), s.Content);
        }
    }

    /// <summary>
    /// The list a node of a list stands for (<see cref="IAccessibleItemSource"/>): its items' nodes
    /// are this node's children, or, for a list over <c>MaxEagerVirtualItems</c>
    /// (<see cref="IsVirtualItemContainer"/>), a bridge asks it for them by index when a reader
    /// does. Null for anything else. It is the live list, read on the bridge's thread: only what is
    /// served on demand is read from it, never what the tree carries.
    /// </summary>
    public IAccessibleItemSource? ItemSource
    {
        get => _extras?.Scroll?.Items;
        internal set
        {
            if (_extras?.Scroll is null && value is null) return;
            Scroll.Items = value;
        }
    }

    /// <summary>Gives the node its scroll view's geometry (<see cref="ScrollExtent"/>) and offset. The
    /// viewport is where it sits in the content of the scroll views above, as the node's own box
    /// (<see cref="UnscrolledRect"/>).</summary>
    internal void SetScrollView(Point offset, Rect viewport, Size content)
    {
        var scroll = Scroll;
        scroll.Offset = offset;
        scroll.Viewport = viewport;
        scroll.Content = content;
        scroll.IsScrollView = true;
    }

    public bool IsFocusable => (_flags & FocusableBit) != 0;

    /// <summary>
    /// Whether the node's element has the keyboard focus. One of the two properties that change
    /// after the tree is built (with a list item's <see cref="IsSelected"/>): a focus move
    /// rebuilds no tree, so the window marks the move on the applied tree's two nodes in place
    /// (UI thread), and the next diff does not report it again.
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
    /// An element whose text is made on request (<see cref="IVersionedAccessibleTextSource"/>, a
    /// long document's) makes its value on the first ask too.
    /// </summary>
    public string? Value => _extras is { } x ? x.Value ?? x.TextVersion?.Value : null;

    /// <summary>The value in a range of a slider, a progress bar or a number field
    /// (<see cref="IRangeValueElement"/>), as this tree was built; null for anything else.</summary>
    public AccessibleRange? Range
    {
        get => _extras?.Range;
        internal set
        {
            if (_extras is null && value is null) return;
            Extras.Range = value;
        }
    }

    /// <summary>A password field: its <see cref="Value"/> is the bullets on screen, never the
    /// text, and a reader says what it is (UIA's IsPassword, AT-SPI's password-text role).</summary>
    public bool IsPassword => (_flags & PasswordBit) != 0;

    /// <summary>Stable automation identifier (UIA AutomationId), for test selectors.</summary>
    public string? AutomationId => _extras?.AutomationId;

    /// <summary>Expanded state for an expandable node, or null if not expandable.</summary>
    public bool? Expanded => GetTriState(ExpandedShift);

    /// <summary>Selected state for one of a set — a radio button, a tab, an item of a list —
    /// or null for a node that is not selectable that way. A list's item nodes take it from
    /// their list (<see cref="IAccessibleItemSource.IsItemSelected"/>), and a move of the list's
    /// selection rebuilds no tree: the window marks it on the applied tree's item nodes in place
    /// (UI thread), as it marks a focus move, and the next diff does not report it again.</summary>
    public bool? IsSelected
    {
        get => GetTriState(SelectedShift);
        // One store of both bits: a reader on another thread sees the old state or the new.
        internal set => _flags = (ushort)((_flags & ~(3 << SelectedShift)) | TriState(value, SelectedShift));
    }

    /// <summary>True when the node is scrolled/clipped out of the visible viewport.</summary>
    public bool IsOffscreen
    {
        get => (_flags & OffscreenBit) != 0;
        internal set => SetFlag(OffscreenBit, value);
    }

    public AccessibilityNode? Parent { get; private set; }

    /// <summary>The node's children, in order. Never changes once the tree is built, but for a
    /// leaf whose own properties changed: the window makes its node again and puts it in its
    /// place in the applied tree (<see cref="ReplaceChild"/>), one reference write, so a reader on
    /// another thread finds the leaf's old node there or its new one, each whole, with the same id
    /// and parent.</summary>
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
    /// tell bridges the text, caret or selection changed. An element whose text is costly to make
    /// on every rebuild gives the tree a <see cref="TextVersion"/> instead, and the text is made
    /// from it on the first ask.
    /// </summary>
    public AccessibleText? Text
    {
        get => _extras is { } x ? x.Text ?? x.TextVersion?.Text : null;
        internal set
        {
            if (_extras is null && value is null) return;
            Extras.Text = value;
        }
    }

    /// <summary>The text as this tree was built with it, not yet made (see <see cref="Text"/>);
    /// null for a node whose text was made with the tree, or that has none. One of the properties
    /// that change after the tree is built (with <see cref="IsFocused"/> and a list item's
    /// <see cref="IsSelected"/>): a move of the caret or the selection rebuilds no tree, so the
    /// window puts the text's new version on the applied tree's node in place
    /// (<see cref="ReplaceTextVersion"/>), and the next diff does not report the move again.</summary>
    internal AccessibleTextVersion? TextVersion
    {
        get => _extras?.TextVersion;
        set
        {
            if (_extras is null && value is null) return;
            Extras.TextVersion = value;
        }
    }

    /// <summary>Puts a newer version of the same text on a node of a tree that is out (UI thread),
    /// one reference write: a bridge's thread reading the node meanwhile reads the old version or
    /// the new, each whole.</summary>
    internal void ReplaceTextVersion(AccessibleTextVersion version) =>
        Volatile.Write(ref Extras.TextVersion, version);

    /// <summary>
    /// A node of no tree standing for this one as it was with <paramref name="previous"/>, an
    /// earlier version of its text: what a patched caret move hands a bridge as the change's
    /// previous node, which a diff takes from the previous tree. It carries what a bridge reads of
    /// a previous node for a text change — the text and where to read it — and the id and role.
    /// </summary>
    internal AccessibilityNode TextStandIn(AccessibleTextVersion previous) =>
        new(ElementId, Role, Name, description: null, BoundingRect)
        {
            TextSource = TextSource,
            TextVersion = previous,
        };

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
    internal bool OffersText => _extras is { } x && (x.TextSource is not null || x.Text is not null || x.TextVersion is not null);

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
        public ScrollFields? Scroll;
        public NativeAccessibleContent NativeContent;
        public AccessibleText? Text;
        public IAccessibleTextSource? TextSource;
        public AccessibleTextVersion? TextVersion;
        public AccessibleRange? Range;
    }

    /// <summary>
    /// A scroll view's or a list's fields, in an object of their own that only those nodes get: the
    /// offset was the one of them a rare field had, and RareFields stays the size it was for every
    /// other node that has one (a text field, a described control).
    /// </summary>
    private sealed class ScrollFields
    {
        public Point Offset;
        public Rect Viewport;
        public Size Content;
        public bool IsScrollView;
        public IAccessibleItemSource? Items;
    }

    private RareFields? _extras;

    private RareFields Extras => _extras ??= new RareFields();

    private ScrollFields Scroll => Extras.Scroll ??= new ScrollFields();

    /// <summary>Whether this node or <paramref name="other"/> carries any of the rarely set
    /// fields; when neither does, the differ has none of them to compare.</summary>
    internal bool EitherHasRareFields(AccessibilityNode other) => _extras is not null || other._extras is not null;

    // The node's states in one field, for the same reason: six bools and three nullable bools
    // were 12 bytes of every node, and ScrollOffset took their place. A nullable state is two
    // bits, whether it has a value and the value. Written on the UI thread only; IsFocused and
    // a list item's IsSelected are the ones written once the tree is out, and a reader on another
    // thread sees every other bit unchanged either side of such a write.
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

    /// <param name="unscrolledRect">The node's box in the content of the scroll views above it
    /// (<see cref="UnscrolledRect"/>); a node that is no tree's, or the root's, is on screen there.</param>
    internal AccessibilityNode(
        ElementId elementId,
        AccessibilityRole role,
        string? name,
        string? description,
        Rect unscrolledRect,
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
        _rect = unscrolledRect;
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

    /// <summary>Puts <paramref name="child"/> in place of the child at <paramref name="index"/>, in
    /// a tree that is out (UI thread): a leaf's node made again for a change of its own properties
    /// (<see cref="AccessibilityTreeBuilder.RemakeLeaf"/>), with the id of the one it replaces and
    /// no children; or an open overlay's part of the tree built again, under the root node. One
    /// reference write into the array readers on other threads walk.</summary>
    internal void ReplaceChild(int index, AccessibilityNode child)
    {
        child.Parent = this;
        Volatile.Write(ref _children[index], child);
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

/// <summary>A scroll view as an accessibility tree was built with it
/// (<see cref="AccessibilityNode.ScrollExtent"/>).</summary>
/// <param name="Viewport">The box its content scrolls in, on screen in layout units: the scroll of
/// the views above it applied, not its own (<see cref="AccessibilityNode.ScrollOffset"/>).</param>
/// <param name="Content">How big the content that scrolls in it is.</param>
public readonly record struct AccessibleScrollExtent(Rect Viewport, Size Content);
