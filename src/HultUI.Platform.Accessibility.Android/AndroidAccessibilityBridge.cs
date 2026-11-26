using System.Collections.Concurrent;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Android;

/// <summary>
/// The TalkBack bridge (docs/design/TALKBACK.md), platform-neutral: HultUI's accessibility tree as
/// Android's virtual views on SDL's surface. The Android layer (<c>Android/</c>) asks it for nodes,
/// hit tests and actions from its <c>AccessibilityNodeProvider</c> on Android's main thread, and
/// sends the events it is handed; the window calls the <see cref="IAccessibilityBridge"/> side on
/// HultUI's UI thread.
/// </summary>
/// <remarks>
/// <para><b>The window's side is cheap.</b> A tree applied is kept and counted
/// (<see cref="TakeTree"/>); what TalkBack is told is worked out from the diff, for the nodes it has
/// been shown only. The map from element to node that the provider needs is made on Android's main
/// thread, the first time it asks after a tree came, never on the UI thread.</para>
/// <para><b>Ids.</b> A node gets a small int the first time TalkBack is shown it, kept while its
/// element is in the tree, and never given to another: TalkBack may still hold one for a node that
/// went. The host view (<see cref="AndroidIds.Host"/>), SDL's surface, has one child: the window's
/// root node, which keeps what it is (a root scroll view scrolls).</para>
/// <para><b>Bounds</b> are the node's box on screen in layout units (scrolled), clipped by the scroll
/// views above it and the window, times the display scale, plus the surface's place on screen.</para>
/// </remarks>
internal sealed class AndroidAccessibilityBridge : IAccessibilityBridge, ITreeDiffBorrower
{
    /// <summary>More content changes than this in one call are told as one change of the whole tree:
    /// TalkBack reads it again either way, and a hundred events cost more than one.</summary>
    internal const int MaxContentChanges = 32;

    private readonly IAccessibilityActionHost? _host;
    private readonly IAccessibilityEditActions? _edits;
    private readonly IAccessibilityGeometryHost? _geometry;
    private volatile IAndroidEventSink? _sink;

    // Whether an accessibility service listens (AccessibilityManager.isEnabled): written on Android's
    // main thread, read on the UI thread.
    private volatile bool _enabled;
    // The tree last applied, and the count of trees taken, the newer bumped after the root is set.
    private volatile AccessibilityNode? _root;
    private int _generation;
    // The first tree since turning on is told as a change of everything. Cleared on Android's main
    // thread as a service starts listening, read and set on the UI thread.
    private volatile bool _toldAll;

    // Virtual ids, both ways. Written on both threads; an id is never given twice.
    private readonly ConcurrentDictionary<ElementId, int> _ids = new();
    private readonly ConcurrentDictionary<int, ElementId> _elements = new();
    private int _lastId = AndroidIds.Announcer;

    // The provider's map from element to node, for the tree of one generation: made on Android's
    // main thread when it first asks for a node of a newer tree.
    private sealed record Index(int Generation, AccessibilityNode Root, Dictionary<ElementId, AccessibilityNode> Nodes);
    private volatile Index? _index;
    private readonly Lock _indexLock = new();

    // Android's main thread: where TalkBack's focus is, and the node a finger is over.
    private int _accessibilityFocused = AndroidIds.None;
    private int _hovered = AndroidIds.None;

    // UI thread: the node that holds the focus, as the window told it, and the one TalkBack was told.
    private ElementId _holderId;
    private bool _hasHolder, _holderTracksItems, _retellFocus;
    private ElementId _toldFocus;
    private bool _hasToldFocus;
    private volatile int _inputFocused = AndroidIds.None;

    // The last announcement, which the announcer node carries (AndroidIds.Announcer).
    private sealed record Announcement(string Text, AnnouncementPoliteness Politeness, ElementId? Source);
    private volatile Announcement? _announcement;

    // UI thread, reused: one call's events, and its content changes by node.
    private readonly List<AndroidEvent> _events = [];
    private readonly Dictionary<int, AndroidContentChange> _contentChanges = [];

    /// <param name="host">Routes TalkBack's actions onto the UI thread: the window's router, which
    /// also gives the display scale (<see cref="IAccessibilityGeometryHost"/>) and a field's value and
    /// a reader's scroll (<see cref="IAccessibilityEditActions"/>).</param>
    public AndroidAccessibilityBridge(IAccessibilityActionHost? host)
    {
        _host = host;
        _edits = host as IAccessibilityEditActions;
        _geometry = host as IAccessibilityGeometryHost;
    }

    /// <summary>Device pixels per layout unit.</summary>
    public float Scale => _geometry?.Scale is > 0f and var scale ? scale : 1f;

    /// <summary>Where events go: the Android layer, once it has the surface.</summary>
    public void SetEventSink(IAndroidEventSink sink)
    {
        _sink = sink;
        // A tree applied before there was anywhere to tell it.
        if (_enabled && _root is not null) sink.Send([new AndroidEvent(AndroidEventType.WindowContentChanged, AndroidIds.Host) { Changes = AndroidContentChange.Subtree }]);
    }

    /// <summary>
    /// An accessibility service started or stopped listening (Android's main thread). The window looks
    /// again, and the first tree after it is told whole.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        if (enabled) _toldAll = false;
        _enabled = enabled;
        AccessibilityClients.Report(enabled);
        _host?.ActiveChanged();
    }

    // ──── The window's side (HultUI's UI thread) ────

    public bool IsActive => _enabled;

    public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
    {
        TakeTree(root);
        Forget(diff);
        TellChanges(diff);
    }

    public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root)
    {
        TakeTree(root);
        TellChanges(patch);
    }

    private void TellChanges(TreeDiff diff)
    {
        if (!_enabled) return;
        Begin();
        if (!_toldAll)
        {
            _toldAll = true;
            Content(AndroidIds.Host, AndroidContentChange.Subtree);
        }
        else Tell(diff);
        Flush();
    }

    public void NotifyFocusChanged(AccessibilityNode? focused) => NotifyFocusChanged(focused, null);

    public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous)
    {
        _hasHolder = focused is not null;
        _holderId = focused?.ElementId ?? default;
        _holderTracksItems = focused?.TracksCurrentItem ?? false;
        TellFocus(focused);
    }

    public void NotifyChangesApplied(AccessibilityNode root)
    {
        // A list's or a menu's current item moves the reader's focus with no focus change.
        if (!_hasHolder || !(_holderTracksItems || _retellFocus)) return;
        _retellFocus = false;
        if (Find(root, _holderId) is { } holder) TellFocus(holder);
    }

    public void NotifyWindowActivated()
    {
        _hasToldFocus = false;
        _retellFocus = true;
    }

    /// <summary>
    /// An announcement, as a live region (docs/design/TALKBACK.md §6, decision 2): the announcer node
    /// takes the text and tells TalkBack its text changed, and TalkBack speaks a live region's change,
    /// politely or cutting in. <c>announceForAccessibility</c>, which recent Android discourages, is
    /// not used.
    /// </summary>
    public void Announce(AccessibilityNode? source, string text, AnnouncementPoliteness politeness)
    {
        if (!_enabled || string.IsNullOrEmpty(text)) return;
        _announcement = new Announcement(text, politeness, source?.ElementId);
        Send([new AndroidEvent(AndroidEventType.WindowContentChanged, AndroidIds.Announcer) { Changes = AndroidContentChange.Text }]);
    }

    /// <summary>Keeps the tree, and counts it: the provider makes its map of the tree again.</summary>
    private void TakeTree(AccessibilityNode root)
    {
        _root = root;
        Interlocked.Increment(ref _generation);
    }

    /// <summary>Lets go of the ids of nodes the diff removed. An id is never given again.</summary>
    private void Forget(TreeDiff diff)
    {
        foreach (var removed in diff.Removed)
            if (_ids.TryRemove(removed.Node.ElementId, out int id)) _elements.TryRemove(id, out _);
    }

    private void Begin()
    {
        _events.Clear();
        _contentChanges.Clear();
    }

    /// <summary>What TalkBack is told of a diff: for nodes it has been shown only, as it knows of no
    /// other (a node is shown in its parent's children, which gives it an id).</summary>
    private void Tell(TreeDiff diff)
    {
        // Each change of children on the nearest shown node (a removed one's parent is the previous
        // tree's, as is what is shown above it); one content change per node, whatever came or went.
        List<AddedNode>? boundaryAdded = diff.Added.Count > 0 ? diff.BoundaryAdded() : null;
        if (boundaryAdded is not null)
            foreach (var added in boundaryAdded) Structure(added.Parent);
        if (diff.Removed.Count > 0)
            foreach (var removed in diff.BoundaryRemoved()) Structure(removed.Parent);
        foreach (var node in diff.Reordered) Structure(node);
        if (boundaryAdded is not null)
        {
            foreach (var added in boundaryAdded)
            {
                // A dialog opened: TalkBack speaks its title. An alert is a live region that came.
                if (added.Node.Role == AccessibilityRole.Dialog)
                    _events.Add(new AndroidEvent(AndroidEventType.WindowStateChanged, IdOf(added.Node.ElementId))
                    { Changes = AndroidContentChange.PaneAppeared });
                else if (added.Node.Role == AccessibilityRole.Alert)
                    Content(IdOf(added.Node.ElementId), AndroidContentChange.Text);
            }
        }
        foreach (var change in diff.PropertyChanged) Property(change);
    }

    private void Structure(ElementId? parent)
    {
        // No parent: the root itself came or went, the host's one child.
        int id = parent is { } p ? KnownId(p) : AndroidIds.Host;
        if (id != AndroidIds.None) Content(id, AndroidContentChange.Subtree);
    }

    /// <summary>A child came or went under <paramref name="parent"/>, which may be hidden (a group inside a
    /// named list item): told on the nearest shown node, whose virtual children changed.</summary>
    private void Structure(AccessibilityNode? parent)
    {
        while (parent is not null && !AndroidNodeMapper.IsExposed(parent)) parent = parent.Parent;
        Structure(parent?.ElementId);
    }

    private void Property(PropertyChange change)
    {
        var node = change.Node;
        int id = KnownId(node.ElementId);
        if (id == AndroidIds.None || !AndroidNodeMapper.IsExposed(node)) return;
        switch (change.PropertyName)
        {
            case AccessibilityProperties.Name:
                // A field's name is its hint.
                Content(id, node.Role == AccessibilityRole.TextInput ? AndroidContentChange.Undefined : NameChange(node));
                break;
            case AccessibilityProperties.Value:
                Content(id, NameChange(node));
                // A field with no text pattern (TextInput) tells its edits as its value.
                if (node.Role == AccessibilityRole.TextInput && !node.IsPassword) TextChanged(id, change.Previous, node);
                break;
            case AccessibilityProperties.Content:
                Content(id, NameChange(node));
                break;
            case AccessibilityProperties.Text:
                Content(id, AndroidContentChange.Text);
                if (node.Role == AccessibilityRole.TextInput && !node.IsPassword) TextChanged(id, change.Previous, node);
                break;
            case AccessibilityProperties.TextSelection:
                if (node.Text is { } text)
                {
                    (int from, int to) = text.HasSelection ? (text.SelectionStart, text.SelectionEnd) : (text.Caret, text.Caret);
                    _events.Add(new AndroidEvent(AndroidEventType.ViewTextSelectionChanged, id)
                    { FromIndex = from, ToIndex = to, ItemCount = text.Length });
                }
                break;
            case AccessibilityProperties.IsToggled:
                Content(id, AndroidContentChange.StateDescription | AndroidContentChange.Checked);
                break;
            case AccessibilityProperties.IsExpanded:
                Content(id, AndroidContentChange.StateDescription | AndroidContentChange.Expanded);
                break;
            case AccessibilityProperties.IsSelected:
                if (node.Role == AccessibilityRole.RadioButton)
                    Content(id, AndroidContentChange.StateDescription | AndroidContentChange.Checked);
                else if (node.IsSelected == true)
                    _events.Add(new AndroidEvent(AndroidEventType.ViewSelected, id));
                else Content(id, AndroidContentChange.Undefined);
                break;
            case AccessibilityProperties.IsEnabled:
                Content(id, AndroidContentChange.Enabled);
                break;
            case AccessibilityProperties.RangeValue:
                Content(id, AndroidContentChange.StateDescription);
                break;
            case AccessibilityProperties.ScrollPosition:
                Scrolled(id, node, change.Previous);
                break;
            case AccessibilityProperties.BoundingRect:
                // Read again when asked; only TalkBack's highlight, drawn where the focused node was, needs telling.
                if (id == Volatile.Read(ref _accessibilityFocused)) Content(id, AndroidContentChange.Undefined);
                break;
            case AccessibilityProperties.ItemCount:
            case AccessibilityProperties.ItemSelection:
            case AccessibilityProperties.NativeContent:
                Content(id, AndroidContentChange.Subtree);
                break;
            case AccessibilityProperties.Role:
            case AccessibilityProperties.IsPassword:
            case AccessibilityProperties.Description:
                Content(id, AndroidContentChange.Undefined);
                break;
        }
    }

    /// <summary>Which of the node's words a change of its name or value changes (<see cref="AndroidNodeMapper"/>'s
    /// split): an image's content description, a dialog's pane title, a container's title, else its text.</summary>
    private static AndroidContentChange NameChange(AccessibilityNode node) => node.Role switch
    {
        AccessibilityRole.Image or AccessibilityRole.Slider or AccessibilityRole.ProgressBar
            or AccessibilityRole.SpinButton => AndroidContentChange.ContentDescription | AndroidContentChange.StateDescription,
        AccessibilityRole.Dialog => AndroidContentChange.PaneTitle,
        AccessibilityRole.List or AccessibilityRole.Tree or AccessibilityRole.Menu or AccessibilityRole.MenuBar
            or AccessibilityRole.TabList or AccessibilityRole.TabPanel or AccessibilityRole.Group
            or AccessibilityRole.ScrollView or AccessibilityRole.Document or AccessibilityRole.Application => AndroidContentChange.Undefined,
        _ => AndroidContentChange.Text,
    };

    /// <summary>A field's text changed: where, and how much went and came, from the text the two trees
    /// were built with (the live text is the new one already).</summary>
    private void TextChanged(int id, AccessibilityNode? previous, AccessibilityNode node)
    {
        string before = previous?.Text?.Text ?? previous?.Value ?? "";
        string after = node.Text?.Text ?? node.Value ?? "";
        if (before == after) return;
        int prefix = 0, max = Math.Min(before.Length, after.Length);
        while (prefix < max && before[prefix] == after[prefix]) prefix++;
        int suffix = 0;
        while (suffix < max - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        _events.Add(new AndroidEvent(AndroidEventType.ViewTextChanged, id)
        {
            BeforeText = before,
            FromIndex = prefix,
            RemovedCount = before.Length - prefix - suffix,
            AddedCount = after.Length - prefix - suffix,
        });
    }

    private void Scrolled(int id, AccessibilityNode node, AccessibilityNode? previous)
    {
        float scale = Scale;
        var offset = node.ScrollOffset;
        var before = previous?.ScrollOffset ?? offset;
        var extent = node.ScrollExtent;
        float maxX = extent is { } e ? Math.Max(0f, e.Content.Width - e.Viewport.Width) : 0f;
        float maxY = extent is { } f ? Math.Max(0f, f.Content.Height - f.Viewport.Height) : 0f;
        _events.Add(new AndroidEvent(AndroidEventType.ViewScrolled, id)
        {
            ScrollX = Px(offset.X, scale),
            ScrollY = Px(offset.Y, scale),
            MaxScrollX = Px(maxX, scale),
            MaxScrollY = Px(maxY, scale),
            ScrollDeltaX = Px(offset.X - before.X, scale),
            ScrollDeltaY = Px(offset.Y - before.Y, scale),
        });
    }

    private static int Px(float layout, float scale) => (int)MathF.Round(layout * scale);

    private void Content(int id, AndroidContentChange change) =>
        _contentChanges[id] = _contentChanges.TryGetValue(id, out var was) ? was | change : change;

    /// <summary>Sends the call's events: its content changes first, one per node, or one of the whole
    /// tree when there are many; then the rest in order.</summary>
    private void Flush()
    {
        if (_contentChanges.Count == 0 && _events.Count == 0) return;
        var all = new List<AndroidEvent>(_contentChanges.Count + _events.Count);
        if (_contentChanges.Count > MaxContentChanges)
            all.Add(new AndroidEvent(AndroidEventType.WindowContentChanged, AndroidIds.Host) { Changes = AndroidContentChange.Subtree });
        else
            foreach (var (id, change) in _contentChanges)
                all.Add(new AndroidEvent(AndroidEventType.WindowContentChanged, id) { Changes = change });
        all.AddRange(_events);
        Begin();
        Send(all);
    }

    private void Send(IReadOnlyList<AndroidEvent> events)
    {
        if (events.Count > 0) _sink?.Send(events);
    }

    /// <summary>Tells TalkBack where the input focus is: on the reader's focus target of the node that
    /// holds it (a list's current item, <see cref="AccessibilityFocus"/>).</summary>
    private void TellFocus(AccessibilityNode? holder)
    {
        if (holder is null)
        {
            _inputFocused = AndroidIds.None;
            _hasToldFocus = false;
            return;
        }
        var target = AccessibilityFocus.TargetOf(holder, follower: null);
        if (_hasToldFocus && target == _toldFocus) return;
        _toldFocus = target;
        _hasToldFocus = true;
        int id = IdOf(target);
        _inputFocused = id;
        if (_enabled) Send([new AndroidEvent(AndroidEventType.ViewFocused, id)]);
    }

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    // ──── Ids ────

    /// <summary>The element's virtual id, given now if it has none.</summary>
    internal int IdOf(ElementId element)
    {
        int id = _ids.GetOrAdd(element, static (_, bridge) => Interlocked.Increment(ref bridge._lastId), this);
        _elements.TryAdd(id, element);
        return id;
    }

    /// <summary>The element's virtual id, or <see cref="AndroidIds.None"/> when TalkBack has not been
    /// shown it.</summary>
    internal int KnownId(ElementId element) => _ids.TryGetValue(element, out int id) ? id : AndroidIds.None;

    // ──── The provider's side (Android's main thread) ────

    /// <summary>The node a finger exploring is over, or <see cref="AndroidIds.None"/>.</summary>
    public int HoveredId => _hovered;

    /// <summary>Where TalkBack's focus is, or <see cref="AndroidIds.None"/>.</summary>
    public int AccessibilityFocusedId => Volatile.Read(ref _accessibilityFocused);

    /// <summary>The node that has the input focus, or <see cref="AndroidIds.None"/>.</summary>
    public int InputFocusedId => _inputFocused;

    /// <summary>The map from element to node for the tree last applied, made now if that tree is newer
    /// than the map. Null before any tree.</summary>
    private Index? IndexNow()
    {
        // The count before the root: a root newer than its count makes a map that is made again next time.
        int generation = Volatile.Read(ref _generation);
        var root = _root;
        if (root is null) return null;
        var index = _index;
        if (index is not null && index.Generation == generation && ReferenceEquals(index.Root, root)) return index;
        lock (_indexLock)
        {
            index = _index;
            if (index is not null && index.Generation == generation && ReferenceEquals(index.Root, root)) return index;
            var nodes = new Dictionary<ElementId, AccessibilityNode>();
            AddAll(root, nodes);
            index = new Index(generation, root, nodes);
            _index = index;
            return index;
        }
    }

    private static void AddAll(AccessibilityNode node, Dictionary<ElementId, AccessibilityNode> nodes)
    {
        nodes[node.ElementId] = node;
        foreach (var child in node.Children) AddAll(child, nodes);
    }

    private AccessibilityNode? Lookup(int id, out Index? index)
    {
        index = IndexNow();
        if (index is null) return null;
        if (id == AndroidIds.Host) return index.Root;   // the host's record is made from the root's
        return _elements.TryGetValue(id, out var element) && index.Nodes.TryGetValue(element, out var node) ? node : null;
    }

    /// <summary>
    /// The virtual view <paramref name="id"/> as TalkBack reads it, or null for one that is gone.
    /// <paramref name="originX"/>, <paramref name="originY"/>: the surface's place on screen, in device
    /// pixels (<c>getLocationOnScreen</c>).
    /// </summary>
    public AndroidNode? Node(int id, int originX = 0, int originY = 0)
    {
        if (id == AndroidIds.Announcer) return AnnouncerNode(originX, originY);
        var node = Lookup(id, out var index);
        if (node is null || index is null) return null;
        var bounds = ScreenBounds(node, originX, originY);
        if (id == AndroidIds.Host)
            return new AndroidNode { Id = AndroidIds.Host, ParentId = AndroidIds.None, Children = [IdOf(node.ElementId)], BoundsInScreen = bounds };
        return AndroidNodeMapper.Map(node, id, ParentIdOf(node), ChildIds(node), bounds, id == AccessibilityFocusedId);
    }

    /// <summary>The virtual parent's id: the nearest shown node above (<see cref="AndroidNodeMapper.IsExposed"/>),
    /// the host for the root.</summary>
    private int ParentIdOf(AccessibilityNode node) =>
        ShownAncestor(node) is { } parent ? IdOf(parent.ElementId) : AndroidIds.Host;

    /// <summary>The nearest node above <paramref name="node"/> that is a virtual view; null above the root.</summary>
    private static AccessibilityNode? ShownAncestor(AccessibilityNode node)
    {
        for (var above = node.Parent; above is not null; above = above.Parent)
            if (AndroidNodeMapper.IsExposed(above)) return above;
        return null;
    }

    /// <summary>The virtual children's ids: the shown children, with a hidden child's shown descendants in
    /// its place.</summary>
    private int[] ChildIds(AccessibilityNode node)
    {
        if (node.Children.Count == 0) return [];
        var ids = new List<int>(node.Children.Count);
        AddShown(node, ids);
        return [.. ids];
    }

    private void AddShown(AccessibilityNode node, List<int> ids)
    {
        foreach (var child in node.Children)
        {
            if (AndroidNodeMapper.IsExposed(child)) ids.Add(IdOf(child.ElementId));
            else AddShown(child, ids);
        }
    }

    /// <summary>
    /// The part of the node on screen, in layout units: its box clipped by every scroll view above it
    /// and by the window (the root's box). Empty when none of it shows.
    /// </summary>
    internal static Rect VisibleBox(AccessibilityNode node)
    {
        var box = node.BoundingRect;
        for (var above = node.Parent; above is not null; above = above.Parent)
        {
            if (above.ScrollExtent is { } extent) box = box.Intersect(extent.Viewport);
            if (above.Parent is null) box = box.Intersect(above.BoundingRect);
            if (box.Width <= 0f || box.Height <= 0f) return Rect.Zero;
        }
        return box.Width > 0f && box.Height > 0f ? box : Rect.Zero;
    }

    private PixelBox ScreenBounds(AccessibilityNode node, int originX, int originY)
    {
        var box = VisibleBox(node);
        if (box.Width <= 0f || box.Height <= 0f) return default;
        float scale = Scale;
        return new PixelBox(
            originX + (int)MathF.Floor(box.X * scale),
            originY + (int)MathF.Floor(box.Y * scale),
            originX + (int)MathF.Ceiling(box.Right * scale),
            originY + (int)MathF.Ceiling(box.Bottom * scale));
    }

    /// <summary>
    /// The virtual view under a finger exploring the screen, at <paramref name="x"/>, <paramref name="y"/>
    /// device pixels in the surface: the deepest node there, in the topmost of overlapping siblings (an
    /// open dialog over the page). TalkBack goes up from it to the node it focuses (a button, not its
    /// label). <see cref="AndroidIds.None"/> outside the window.
    /// </summary>
    public int HitTest(float x, float y)
    {
        var index = IndexNow();
        if (index is null) return AndroidIds.None;
        float scale = Scale;
        var point = new Point(x / scale, y / scale);
        var root = index.Root;
        var hit = Deepest(root, point) ?? (VisibleBox(root).Contains(point) ? root : null);
        // A hidden node (a named button's label) is found as the shown node above it.
        if (hit is not null && !AndroidNodeMapper.IsExposed(hit)) hit = ShownAncestor(hit);
        return hit is null ? AndroidIds.None : IdOf(hit.ElementId);
    }

    private static AccessibilityNode? Deepest(AccessibilityNode node, Point point)
    {
        var children = node.Children;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];
            if (child.Role == AccessibilityRole.ScrollBar || child.IsOffscreen) continue;
            if (!VisibleBox(child).Contains(point)) continue;
            return Deepest(child, point) ?? child;
        }
        return null;
    }

    /// <summary>
    /// A finger exploring moved to <paramref name="x"/>, <paramref name="y"/> (device pixels in the
    /// surface), or left it (null): TalkBack is told it entered the node under it and left the one
    /// before, as ExploreByTouchHelper tells it, and focuses what it entered. True when a virtual view
    /// took the hover, or the finger left one; false leaves it to the host view.
    /// </summary>
    public bool HoverTo(float? x, float? y)
    {
        int id = x is { } hx && y is { } hy ? HitTest(hx, hy) : AndroidIds.None;
        if (id == _hovered) return id != AndroidIds.None;
        int before = _hovered;
        _hovered = id;
        var events = new List<AndroidEvent>(2);
        if (id != AndroidIds.None) events.Add(new AndroidEvent(AndroidEventType.ViewHoverEnter, id));
        if (before != AndroidIds.None) events.Add(new AndroidEvent(AndroidEventType.ViewHoverExit, before));
        Send(events);
        return id != AndroidIds.None || before != AndroidIds.None;
    }

    /// <summary>
    /// TalkBack performs <paramref name="action"/> on virtual view <paramref name="id"/>. Its focus is
    /// kept here; anything else the node offers goes to the window's router, which runs it on the UI
    /// thread in the next frame. False for an action the node does not offer, or a node that is gone.
    /// </summary>
    public bool Perform(int id, AndroidActions action, AndroidActionArgs args = default)
    {
        switch (action)
        {
            case AndroidActions.AccessibilityFocus:
            {
                int before = _accessibilityFocused;
                if (before == id) return false;
                if (id != AndroidIds.Host && Lookup(id, out _) is null) return false;
                _accessibilityFocused = id;
                var events = new List<AndroidEvent>(2);
                if (before != AndroidIds.None) events.Add(new AndroidEvent(AndroidEventType.ViewAccessibilityFocusCleared, before));
                events.Add(new AndroidEvent(AndroidEventType.ViewAccessibilityFocused, id));
                Send(events);
                return true;
            }
            case AndroidActions.ClearAccessibilityFocus:
                if (_accessibilityFocused != id) return false;
                _accessibilityFocused = AndroidIds.None;
                Send([new AndroidEvent(AndroidEventType.ViewAccessibilityFocusCleared, id)]);
                return true;
        }
        if (id == AndroidIds.Host || _host is null) return false;
        var node = Lookup(id, out var index);
        if (node is null || index is null) return false;
        var offered = AndroidNodeMapper.Map(node, id, AndroidIds.Host, [], default, id == _accessibilityFocused).Actions;
        if ((offered & action) != action) return false;
        var element = node.ElementId;
        switch (action)
        {
            case AndroidActions.Click:
                Click(node);
                return true;
            case AndroidActions.Focus:
                _host.FocusElement(element);
                return true;
            case AndroidActions.Select:
                _host.SelectElement(element);
                return true;
            case AndroidActions.Expand:
            case AndroidActions.Collapse:
                _host.ExpandCollapseElement(element, action == AndroidActions.Expand);
                return true;
            case AndroidActions.SetProgress:
                if (args.Progress is not { } progress) return false;
                _host.SetRangeValue(element, progress);
                return true;
            case AndroidActions.SetText:
                if (_edits is null) return false;
                _edits.SetValue(element, args.Text ?? "");
                return true;
            case AndroidActions.ShowOnScreen:
                _host.ScrollElementIntoView(element);
                return true;
            case AndroidActions.ScrollForward:
            case AndroidActions.ScrollBackward:
                return Step(node, action == AndroidActions.ScrollForward);
            case AndroidActions.ScrollUp:
            case AndroidActions.ScrollDown:
            case AndroidActions.ScrollLeft:
            case AndroidActions.ScrollRight:
                return Page(node, action);
            default:
                return false;
        }
    }

    /// <summary>A double tap: a check box or a switch toggles, a radio button is chosen, a dropdown
    /// opens or closes, anything else is invoked.</summary>
    private void Click(AccessibilityNode node)
    {
        var element = node.ElementId;
        switch (node.Role)
        {
            case AccessibilityRole.Checkbox or AccessibilityRole.Switch:
            case AccessibilityRole.Button when node.IsToggled is not null:
                _host!.ToggleElement(element);
                break;
            case AccessibilityRole.RadioButton:
                _host!.SelectElement(element);
                break;
            case AccessibilityRole.ComboBox:
                _host!.ExpandCollapseElement(element, !(node.Expanded ?? false));
                break;
            default:
                _host!.InvokeElement(element);
                break;
        }
    }

    /// <summary>Forward or back: a slider by a step, a scroll view by a page, down before sideways.</summary>
    private bool Step(AccessibilityNode node, bool forward)
    {
        if (node.Range is { IsReadOnly: false } range)
        {
            double step = range.SmallChange > 0 ? range.SmallChange : (range.Maximum - range.Minimum) / 20;
            _host!.SetRangeValue(node.ElementId, Math.Clamp(range.Value + (forward ? step : -step), range.Minimum, range.Maximum));
            return true;
        }
        if (node.ScrollExtent is not { } extent) return false;
        var (up, down, left, right) = AndroidNodeMapper.ScrollRoom(node, extent);
        var action = forward ? (down ? AndroidActions.ScrollDown : right ? AndroidActions.ScrollRight : AndroidActions.None)
            : (up ? AndroidActions.ScrollUp : left ? AndroidActions.ScrollLeft : AndroidActions.None);
        return action != AndroidActions.None && Page(node, action);
    }

    /// <summary>A page in a direction: the viewport's height or width, as Android's own scroll views move.</summary>
    private bool Page(AccessibilityNode node, AndroidActions direction)
    {
        if (_edits is null || node.ScrollExtent is not { } extent) return false;
        float height = extent.Viewport.Height, width = extent.Viewport.Width;
        var (dx, dy) = direction switch
        {
            AndroidActions.ScrollUp => (0f, -height),
            AndroidActions.ScrollDown => (0f, height),
            AndroidActions.ScrollLeft => (-width, 0f),
            AndroidActions.ScrollRight => (width, 0f),
            _ => (0f, 0f),
        };
        if (dx == 0f && dy == 0f) return false;
        _edits.ScrollBy(node.ElementId, dx, dy);
        return true;
    }

    /// <summary>The announcer: the last announcement as a live region, over the node it came from (or
    /// the window). In no node's children, so it is read when it changes and never swiped to.</summary>
    private AndroidNode? AnnouncerNode(int originX, int originY)
    {
        if (_announcement is not { } announcement) return null;
        var index = IndexNow();
        var over = announcement.Source is { } source && index is not null && index.Nodes.TryGetValue(source, out var node)
            ? node : index?.Root;
        var bounds = over is null ? default : ScreenBounds(over, originX, originY);
        if (bounds.IsEmpty && index?.Root is { } root) bounds = ScreenBounds(root, originX, originY);
        return new AndroidNode
        {
            Id = AndroidIds.Announcer,
            ParentId = AndroidIds.Host,
            Text = announcement.Text,
            Flags = AndroidNodeFlags.Enabled | AndroidNodeFlags.VisibleToUser,
            BoundsInScreen = bounds,
            LiveRegion = announcement.Politeness == AnnouncementPoliteness.Assertive ? AndroidLiveRegion.Assertive : AndroidLiveRegion.Polite,
        };
    }
}
