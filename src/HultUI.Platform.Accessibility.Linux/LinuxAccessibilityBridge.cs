using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// AT-SPI2 accessibility bridge. Connects to the a11y bus discovered via
/// <c>org.a11y.Bus.GetAddress</c>, registers the application root with the
/// registry through <c>Socket.Embed</c>, and exports every accessible node
/// under <c>/org/a11y/atspi/accessible/&lt;id&gt;</c> (the root at the
/// well-known <c>…/accessible/root</c>).
/// </summary>
/// <remarks>
/// <para>
/// The exported tree is published as an immutable snapshot on every structural
/// rebuild. The D-Bus method handlers run on the bus receive thread and read
/// that snapshot lock-free; <see cref="ApplyChanges"/> swaps it, and a focus move
/// (<see cref="NotifyFocusChanged(AccessibilityNode?, AccessibilityNode?)"/>) sets the
/// focused state of its two objects in it, and a list's selection move
/// (<see cref="ApplyPatchedProperties"/>) the selected state of its two items, all on the UI
/// thread. The
/// <see cref="TreeDiff"/> drives which change signals to emit — the same delta
/// the render side already computed, reused as the AT-SPI event stream.
/// </para>
/// <para>
/// <b>Active only while assistive technology is on.</b> The bus is reachable in every GNOME and
/// KDE session, screen reader or not, and an active bridge rebuilds the tree and sends AT-SPI its
/// changes on every change a reader could see: a keystroke in a 603-node form cost 385 KB
/// against 3.2 KB with no bridge. So the bridge connects and registers, and is
/// <see cref="IsActive"/> only while <c>org.a11y.Status</c> says <c>IsEnabled</c> or
/// <c>ScreenReaderEnabled</c>, which Orca and accerciser set, as Qt does; or while
/// <see cref="AccessibilityClients.Force"/> (<c>HULTUI_ACCESSIBILITY=1</c>) says so, for clients
/// that never set the status: a plain pyatspi script, <c>samples/AtSpiVerify</c>. When the status
/// turns on, the window builds the tree afresh and the bridge publishes all of it (cache-add for
/// every object, cache-remove for any it published before that is gone now); when it turns off,
/// nothing is built or sent.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxAccessibilityBridge : IAccessibilityBridge, IAtSpiTree, IDisposable
{
    /// <summary>Immutable published view the bus-thread handlers read.</summary>
    private sealed class Snapshot
    {
        public required Dictionary<string, AtSpiAccessible> ByPath { get; init; }
        public AtSpiAccessible? Root { get; init; }
        public static readonly Snapshot Empty = new() { ByPath = [] };
    }

    private readonly AtSpiBus? _bus;
    // Where the signals go: the bus, or a test's recorder.
    private readonly IAtSpiSignals? _signals;
    private readonly IAccessibilityActionHost? _actionHost;
    private readonly IAccessibilityGeometryHost? _geometryHost;
    private readonly bool _registered;
    // Whether assistive technology is on (org.a11y.Status). Null for a test's bridge with no
    // status, which is always on. Written on the watcher's thread, read on the UI thread.
    private readonly IAtSpiStatus? _status;
    private volatile bool _statusOn;
    // Set once the watch is set up: the first status, read while the bridge is made, wakes nobody.
    private volatile bool _statusWatched;
    private volatile Snapshot _snapshot = Snapshot.Empty;
    private bool _hasBuilt;
    private string? _focusedPath;
    private int _applicationId;
    private bool _disposed;

    /// <param name="actionHost">
    /// Routes AT-triggered actions (button click, checkbox toggle) back onto the
    /// live element on the UI thread. When null, <c>DoAction</c> is a no-op.
    /// </param>
    public LinuxAccessibilityBridge(IAccessibilityActionHost? actionHost = null) : this(actionHost, connect: true) { }

    /// <summary>For tests: a bridge that does not connect, whose tree is set by <see cref="Publish"/>.</summary>
    internal LinuxAccessibilityBridge(IAccessibilityActionHost? actionHost, bool connect)
    {
        _actionHost = actionHost;
        // The windowing layer's host implements both capabilities on one object.
        _geometryHost = actionHost as IAccessibilityGeometryHost;
        if (!connect) return;
        // With no synchronization context. The bus is opened and the application registered with
        // blocking waits on D-Bus calls, and the connection takes the calling thread's context
        // for its own work: on a thread with HultUI's (a headless session's, the loop's once it
        // runs, so a window made then) that work went to the UI thread's queue, which nothing
        // drains while the thread waits, and the constructor never returned (samples/AtSpiVerify
        // hung at start). The handlers below run on the bus's receive thread, as they must.
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            // Eager connect. If no AT-SPI bus answers we stay inactive, matching the
            // "is anyone listening" contract the null bridge expresses everywhere.
            _signals = _bus = AtSpiBus.Open();
            if (_bus is null) return;

            _bus.AddMethodHandler(new AtSpiObjectHandler(this));
            _bus.AddMethodHandler(new AtSpiCacheHandler(this));

            // Seed a minimal application root *before* Embed: the registry probes
            // our root during the Embed round-trip — on the bus receive thread,
            // while this ctor thread is still blocked in the call — so the seed has
            // to be visible first, or that probe answers UnknownObject. The first
            // ApplyChanges swaps in the real tree.
            _snapshot = new Snapshot { ByPath = new() { [SeedRoot.Path] = SeedRoot }, Root = SeedRoot };

            _registered = _bus.RegisterApplication();

            // Whether anyone listens: what makes this bridge active, and what content costly to
            // describe reads (seam W11).
            WatchStatus(_status = _bus);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    /// <summary>For tests: a bridge registered with no bus, whose signals go to
    /// <paramref name="signals"/>, active whenever <paramref name="status"/> says assistive
    /// technology is on; always active with no status.</summary>
    internal LinuxAccessibilityBridge(IAtSpiSignals signals, IAccessibilityActionHost? actionHost = null,
        IAtSpiStatus? status = null)
    {
        _actionHost = actionHost;
        _geometryHost = actionHost as IAccessibilityGeometryHost;
        _signals = signals;
        _registered = true;
        if (status is not null) WatchStatus(_status = status);
    }

    // The watch subscribes first and then reads (AtSpiStatusReader.Watch). The status was read
    // here and the watch subscribed after it: a change in between was never read.
    private void WatchStatus(IAtSpiStatus status)
    {
        status.WatchStatus(on =>
        {
            _statusOn = on;
            AccessibilityClients.Report(on);
            // A change, on a thread-pool thread (AtSpiStatusReader): the window's loop, which
            // reads IsActive in its frames, is waiting for a deadline or a wake, and turning on is
            // what builds and publishes the whole tree.
            if (_statusWatched) _actionHost?.ActiveChanged();
        });
        _statusWatched = true;
    }

    /// <summary>A childless application root, valid from Embed until the first build.</summary>
    private static readonly AtSpiAccessible SeedRoot = new()
    {
        ElementId = default,
        Path = AtSpiConstants.RootPath,
        IsRoot = true,
        Role = AtSpiConstants.RoleApplication,
        Name = AtSpiAccessible.ApplicationName,
        IndexInParent = -1,
    };

    /// <summary>Registered, and assistive technology is on: <c>org.a11y.Status</c>, unless
    /// <see cref="AccessibilityClients.Force"/> says otherwise. Polled by the window every frame.</summary>
    public bool IsActive => _registered && (_status is null || (AccessibilityClients.Force ?? _statusOn));

    // Diagnostics for the verification harness — cheap counters, no bus needed.
    public int DebugApplyChangesCalls { get; private set; }
    public int DebugSignalsEmitted { get; private set; }

    // ───────────────────────────────────────────────────────── IAtSpiTree

    string IAtSpiTree.BusName => _bus?.BusName ?? "";
    (string BusName, string Path) IAtSpiTree.Desktop =>
        _bus?.Desktop ?? (AtSpiConstants.RegistryService, AtSpiConstants.RegistryRootPath);
    AtSpiAccessible? IAtSpiTree.Root => _snapshot.Root;
    AtSpiAccessible? IAtSpiTree.Find(string path) =>
        _snapshot.ByPath.TryGetValue(path, out var node) ? node : null;
    IReadOnlyCollection<AtSpiAccessible> IAtSpiTree.All => _snapshot.ByPath.Values;
    int IAtSpiTree.ApplicationId
    {
        get => _applicationId;
        set => _applicationId = value;
    }

    bool IAtSpiTree.GrabFocus(AtSpiAccessible node)
    {
        if (_actionHost is null || !node.Focusable) return false;
        _actionHost.FocusElement(node.ElementId);
        return true;
    }

    bool IAtSpiTree.SetCurrentValue(AtSpiAccessible node, double value)
    {
        if (_actionHost is null || node.Range is not { IsReadOnly: false }) return false;
        _actionHost.SetRangeValue(node.ElementId, value);
        return true;
    }

    bool IAtSpiTree.DoAction(AtSpiAccessible node)
    {
        if (_actionHost is null || node.Action is null) return false;
        // Runs on the bus receive thread; the host enqueues and the Window drains
        // it on the UI thread, so this only needs to be enqueue-safe (it is).
        switch (node.Action)
        {
            case AtSpiActionKind.Click: _actionHost.InvokeElement(node.ElementId); return true;
            case AtSpiActionKind.Toggle: _actionHost.ToggleElement(node.ElementId); return true;
            case AtSpiActionKind.Select: _actionHost.SelectElement(node.ElementId); return true;
            default: return false;
        }
    }

    // ─────────────────────────────────────────────────── IAccessibilityBridge

    public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
    {
        if (!_registered || _signals is null) return;
        DebugApplyChangesCalls++;

        var previous = _snapshot;
        _snapshot = Build(root, _geometryHost?.Scale ?? 1f);

        bool firstBuild = !_hasBuilt;
        _hasBuilt = true;

        // A tree from nothing: the first, or the first since the bridge went inactive (the
        // window drops its tree then, and builds it afresh when assistive technology is on
        // again). The cache-adds below publish all of it; what was published before and is not
        // in it any more went while nobody was told, and goes now. The object the bridge last
        // marked focused may be gone too: the window names the focused one with the next move.
        if (diff.Added.Count > 0 && diff.Added[0].Parent is null && diff.Removed.Count == 0)
        {
            foreach (var path in previous.ByPath.Keys)
            {
                if (_snapshot.ByPath.ContainsKey(path)) continue;
                _signals.EmitCacheRemove(path);
                DebugSignalsEmitted++;
            }
            _focusedPath = null;
        }

        // What a caching client (libatspi, which Orca runs on) does with these signals, seen live
        // in WSL with a pyatspi client in Atspi.event_main (plan 17, round 3, step 15): a cache-add
        // puts its object at its index in the parent's cached children, in place of whatever is
        // there, and grows the list when the index is past its end; a children-changed:add takes
        // the child out of the list and puts it back at the index it names; a children-changed:remove
        // takes it out; a cache-remove disposes the object, which leaves its parent's list. So the
        // order matters. The cache-adds came first and one children-changed:add per parent after
        // them, and an object added anywhere but at the end took a sibling's place: [A, B, C] with
        // X inserted after A read [A, X, C]; with X and Y, [A, X, Y]. Now the gone objects go
        // first, then each added object is put in its place, and each child of a reordered parent
        // that is out of place (plan 17, round 4: a reorder sent nothing, and the client kept the
        // old order), then the cache-adds fill those places.

        // Gone objects only. One the new snapshot still has moved to another parent (a box
        // that gained a name takes its children): the diff removes it from the old parent and
        // adds it under the new, and its cache-add below tells the client where it is. Removing
        // it made libatspi dispose an object it still holds — the client lost it, and saw it go
        // defunct. Its old parent learns of the move from a children-changed:remove.
        foreach (var removed in diff.Removed)
        {
            string path = PathOf(removed.Node);
            if (_snapshot.ByPath.ContainsKey(path)) continue;
            _signals.EmitCacheRemove(path);
            DebugSignalsEmitted++;
        }

        // Interior nodes (a parent that is itself in the diff) carry no structural signal: a
        // client learns of them from their parent's cache-add and child count. Skipped on the first
        // build: the client has no tree yet, and the cache-adds convey the whole initial tree.
        if (!firstBuild)
        {
            EmitRemovedChildNudges(diff.BoundaryRemoved());
            // A reordered parent's children are all put in place by its own pass, added ones with
            // them; every other parent's added children by theirs.
            HashSet<ElementId>? reordered = null;
            foreach (var parent in diff.Reordered) (reordered ??= []).Add(parent.ElementId);
            EmitAddedChildNudges(diff.BoundaryAdded(), reordered);
            foreach (var parent in diff.Reordered) EmitReorderNudges(previous, parent);
        }

        // Cache-add is genuinely per-node: it populates the AT client's object cache (the first
        // build's diff carries the whole tree) so navigation is instant and libatspi never treats a
        // later-signalled node as transient and marks it defunct. AT-SPI's Cache interface adds one
        // item at a time — unlike UIA, which the client pulls on demand — so every added node is
        // cached. Each lands in the place its children-changed:add made, or one past the end.
        foreach (var added in diff.Added)
        {
            string childPath = PathOf(added.Node);
            if (!_snapshot.ByPath.TryGetValue(childPath, out var acc)) continue;
            _signals.EmitCacheAdd(this, acc);
            DebugSignalsEmitted++;
        }

        if (!firstBuild)
            foreach (var change in diff.PropertyChanged)
                if (EmitPropertyChange(change)) DebugSignalsEmitted++;
    }

    // One children-changed:add per added child, in the order of its parent's children: each puts
    // the child at its index in a caching client's list, before its cache-add (which would put it
    // there in place of a sibling) comes. Except the run of added children that ends the parent's
    // children: one signal, for its first, and the cache-adds of the rest each land one past the
    // end, where the client grows the list. A bulk append stays one signal; a run inserted at the
    // front or in the middle is one signal per child. The gone children have left the client's
    // list already (above), so each index is the child's place among those there before it.
    // A reordered parent's added children are left to EmitReorderNudges, which puts them with the rest.
    private void EmitAddedChildNudges(List<AddedNode> boundary, HashSet<ElementId>? reordered)
    {
        if (boundary.Count == 0) return;
        var added = new HashSet<ElementId>(boundary.Count);
        foreach (var entry in boundary) added.Add(entry.Node.ElementId);
        // Per parent: the index where the run of added children that ends its children starts.
        Dictionary<ElementId, int>? runStarts = null;
        foreach (var entry in boundary)
        {
            if (entry.Parent is not { } parent || reordered?.Contains(parent.ElementId) == true) continue;
            string childPath = PathOf(entry.Node);
            if (!_snapshot.ByPath.TryGetValue(childPath, out var acc)) continue;
            runStarts ??= [];
            if (!runStarts.TryGetValue(parent.ElementId, out int runStart))
            {
                var siblings = parent.Children;
                runStart = siblings.Count;
                while (runStart > 0 && added.Contains(siblings[runStart - 1].ElementId)) runStart--;
                runStarts[parent.ElementId] = runStart;
            }
            if (acc.IndexInParent > runStart) continue;
            _signals!.EmitChildrenChanged(acc.ParentPath, childPath, acc.IndexInParent, isAdd: true);
            DebugSignalsEmitted++;
        }
    }

    /// <summary>
    /// A parent whose children changed order (added and gone ones besides, maybe): one
    /// children-changed:add per child that is not where a caching client's list has it, in the
    /// order of the parent's children, each naming its index, so the list ends in the new order. A
    /// reorder sent nothing, and libatspi kept the old order until something else changed the list
    /// (seen live in WSL: all seven reorder shapes read the old order). What the client holds is the
    /// previous snapshot's children less those that left this parent (their cache-remove or
    /// children-changed:remove went first); each signal moves its child to its index, so the
    /// children before it are in place and the rest keep their old order: a child is in place when
    /// it is the first of the old order not yet put. At most one signal per child but the last;
    /// [A, B, C] reversed is two. Added children are put the same way, except the run that ends the
    /// parent's children: one signal for its first, as in <see cref="EmitAddedChildNudges"/>, and
    /// the cache-adds of the rest land past the end.
    /// </summary>
    private void EmitReorderNudges(Snapshot previous, AccessibilityNode parent)
    {
        string parentPath = PathOf(parent);
        if (!previous.ByPath.TryGetValue(parentPath, out var before)
            || !_snapshot.ByPath.TryGetValue(parentPath, out var now)) return;
        var kids = now.ChildPaths;

        // The client's list is the old order less the children that left this parent. Those still
        // waiting to be put, as each is put the set loses it: one set, no list.
        var old = before.ChildPaths;
        var waiting = new HashSet<string>(old.Count);
        foreach (var path in old)
            if (_snapshot.ByPath.TryGetValue(path, out var child) && child.ParentPath == parentPath)
                waiting.Add(path);
        // Where the run of children new to this parent that ends its children starts.
        int runStart = kids.Count;
        while (runStart > 0 && !waiting.Contains(kids[runStart - 1])) runStart--;

        int next = 0;   // the first child of the client's list not yet put
        for (int i = 0; i < kids.Count; i++)
        {
            string path = kids[i];
            if (i >= runStart)
            {
                if (i == runStart) Emit(path, i);
                break;
            }
            while (next < old.Count && !waiting.Contains(old[next])) next++;
            if (next < old.Count && old[next] == path)
            {
                next++;   // already there
                continue;
            }
            waiting.Remove(path);
            Emit(path, i);
        }

        void Emit(string path, int index)
        {
            _signals!.EmitChildrenChanged(parentPath, path, index, isAdd: true);
            DebugSignalsEmitted++;
        }
    }

    // One children-changed:remove per surviving parent. Removed nodes are gone from
    // the new snapshot, so parent/index come from the previous tree the diff carries.
    // Except an object that moved to another parent: it gets no cache-remove, which is what
    // takes a gone object out of its parent's children in libatspi's cache, so its old parent
    // says it lost that one, each time, or the client would list it under both parents.
    private void EmitRemovedChildNudges(List<RemovedNode> boundary)
    {
        var seenParents = new HashSet<string>();
        foreach (var removed in boundary)
        {
            if (removed.Parent is null) continue;
            string parentPath = PathOf(removed.Parent);
            bool moved = _snapshot.ByPath.ContainsKey(PathOf(removed.Node));
            if (!moved && !seenParents.Add(parentPath)) continue;
            int index = IndexOf(removed.Parent, removed.Node);
            _signals!.EmitChildrenChanged(parentPath, PathOf(removed.Node), index, isAdd: false);
            DebugSignalsEmitted++;
        }
    }

    /// <summary>Focus moved; the object that had it is the one this bridge was last told of.</summary>
    public void NotifyFocusChanged(AccessibilityNode? focused) => MoveFocus(focused, previousPath: null);

    /// <summary>Focus moved, and the window marked it on its tree: <paramref name="previous"/>
    /// had it.</summary>
    public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous)
        => MoveFocus(focused, previous is null ? null : PathOf(previous));

    private void MoveFocus(AccessibilityNode? focused, string? previousPath)
    {
        if (!_registered || _signals is null) return;

        string? newPath = focused is null ? null : PathOf(focused);

        // The focused state a client reads (GetState, the cache), as of now. A focus move
        // rebuilds no snapshot, so both objects kept the state of the last rebuild, and the
        // next rebuild's diff sent state-changed:focused for both again, late.
        bool previousLost = previousPath is not null && previousPath != newPath && SetFocused(previousPath, false);
        if (_focusedPath is not null && _focusedPath != newPath) SetFocused(_focusedPath, false);
        if (newPath is not null) SetFocused(newPath, true);

        if (_focusedPath is not null && _focusedPath != newPath)
            _signals.EmitStateChanged(_focusedPath, "focused", active: false);
        // The object the tree had focused, when this bridge was not told of it (a client
        // attached after the focus last moved).
        if (previousLost && previousPath != _focusedPath)
            _signals.EmitStateChanged(previousPath!, "focused", active: false);
        if (newPath is not null && newPath != _focusedPath)
            _signals.EmitStateChanged(newPath, "focused", active: true);
        _focusedPath = newPath;
    }

    /// <summary>
    /// Properties the window marked on the applied tree in place — a list's selection move: the
    /// item that had the selection and the one that has it. Their objects in the published
    /// snapshot take the new state and the bridge sends what a diff would have
    /// (state-changed:selected), with no new snapshot. Anything else is applied as a diff.
    /// </summary>
    public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root)
    {
        if (!_registered || _signals is null) return;
        var snapshot = _snapshot;
        bool inPlace = _hasBuilt && patch.Added.Count == 0 && patch.Removed.Count == 0 && patch.Reordered.Count == 0;
        foreach (var change in patch.PropertyChanged)
            inPlace &= change.PropertyName == AccessibilityProperties.IsSelected
                && snapshot.ByPath.ContainsKey(PathOf(change.Node));
        if (!inPlace)
        {
            ApplyChanges(patch, root);
            return;
        }
        foreach (var change in patch.PropertyChanged)
        {
            string path = PathOf(change.Node);
            snapshot.ByPath[path].Selected = change.Node.IsSelected == true;
            if (EmitPropertyChange(change, path)) DebugSignalsEmitted++;
        }
    }

    /// <summary>Sets an object's focused state in the published snapshot; true when it changed.</summary>
    private bool SetFocused(string path, bool focused)
    {
        if (!_snapshot.ByPath.TryGetValue(path, out var acc) || acc.Focused == focused) return false;
        acc.Focused = focused;
        return true;
    }

    // ───────────────────────────────────────────────────────────── helpers

    private bool EmitPropertyChange(PropertyChange change) => EmitPropertyChange(change, PathOf(change.Node));

    private bool EmitPropertyChange(PropertyChange change, string path)
    {
        switch (change.PropertyName)
        {
            case AccessibilityProperties.Name:
                _signals!.EmitPropertyChange(path, AtSpiConstants.PropAccessibleName, change.Node.Name ?? "");
                return true;
            case AccessibilityProperties.Description:
                _signals!.EmitPropertyChange(path, AtSpiConstants.PropAccessibleDescription, change.Node.Description ?? "");
                return true;
            case AccessibilityProperties.Value:
                // A number field's text: its range's accessible-value says it, as a number.
                if (change.Node.Range is not null) return false;
                _signals!.EmitPropertyChange(path, AtSpiConstants.PropAccessibleValue, change.Node.Value ?? "");
                return true;
            case AccessibilityProperties.RangeValue:
                // The value; a change of the bounds or steps alone is read from the Value interface.
                if (change.Node.Range is not { } range || change.Previous?.Range?.Value == range.Value) return false;
                _signals!.EmitValueChanged(path, range.Value);
                return true;
            case AccessibilityProperties.Role:
                // A client caches the role from the cache-add: a group that became a dialog went on
                // reading "panel" in Orca's cache (seen live in WSL). The application's own object
                // keeps its role, whatever its element's.
                if (path == AtSpiConstants.RootPath) return false;
                _signals!.EmitRoleChange(path, AtSpiAccessible.RoleOf(change.Node));
                return true;
            case AccessibilityProperties.IsPassword:
                // A client caches the role from the cache-add, so a field masked or revealed
                // after it was first seen has to say its role changed.
                _signals!.EmitRoleChange(path, AtSpiAccessible.RoleOf(change.Node));
                return true;
            case AccessibilityProperties.IsToggled:
                _signals!.EmitStateChanged(path, "checked", change.Node.IsToggled == true);
                return true;
            case AccessibilityProperties.IsFocused:
                _signals!.EmitStateChanged(path, "focused", change.Node.IsFocused);
                return true;
            case AccessibilityProperties.IsExpanded:
                _signals!.EmitStateChanged(path, "expanded", change.Node.Expanded == true);
                return true;
            case AccessibilityProperties.IsEnabled:
                _signals!.EmitStateChanged(path, "enabled", change.Node.IsEnabled);
                _signals!.EmitStateChanged(path, "sensitive", change.Node.IsEnabled);
                return true;
            case AccessibilityProperties.IsSelected:
                _signals!.EmitStateChanged(path, "selected", change.Node.IsSelected == true);
                return true;
            case AccessibilityProperties.Text:
            {
                var (deleted, inserted) = AtSpiText.Difference(change.Previous?.Text?.Text ?? "", change.Node.Text?.Text ?? "");
                if (deleted is { } d) _signals!.EmitTextChanged(path, insert: false, d.Offset, d.Text);
                if (inserted is { } i) _signals!.EmitTextChanged(path, insert: true, i.Offset, i.Text);
                return deleted is not null || inserted is not null;
            }
            case AccessibilityProperties.TextSelection:
            {
                var (before, after) = (change.Previous?.Text, change.Node.Text);
                if (after is not null && after.Caret >= 0 && before?.Caret != after.Caret)
                    _signals!.EmitTextCaretMoved(path, after.Caret);
                if (before?.SelectionStart != after?.SelectionStart || before?.SelectionEnd != after?.SelectionEnd)
                    _signals!.EmitTextSelectionChanged(path);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>An AT-SPI announcement from the element's object, or the application's.</summary>
    public void Announce(AccessibilityNode? source, string text, AnnouncementPoliteness politeness)
    {
        if (!_registered || _signals is null) return;
        _signals.EmitAnnouncement(source is null ? AtSpiConstants.RootPath : PathOf(source), text,
            politeness == AnnouncementPoliteness.Assertive ? AtSpiConstants.LiveAssertive : AtSpiConstants.LivePolite);
        DebugSignalsEmitted++;
    }

    /// <summary>For tests: publishes the snapshot of <paramref name="root"/>, as a rebuild would.</summary>
    internal void Publish(AccessibilityNode root, float scale = 1f) => _snapshot = Build(root, scale);

    /// <summary>
    /// The published view of <paramref name="root"/>'s tree, rebuilt in full on every apply. What
    /// it makes per node is the object, its path and its place in its parent's list of children:
    /// the dictionary is sized for the tree (grown entry by entry to 3,000, it reallocated a dozen
    /// times), and each path is made once, in one string. A path was made twice, for the parent's
    /// list and for the node itself, in two strings each (the id's, then the path), and every
    /// parent kept its children's paths in a list: about 500 bytes a node, about 270 now.
    /// </summary>
    private static Snapshot Build(AccessibilityNode root, float scale)
    {
        var byPath = new Dictionary<string, AtSpiAccessible>(CountNodes(root));
        var rootAcc = Walk(root, AtSpiConstants.RootPath, isRoot: true, parentPath: AtSpiConstants.NullPath,
            index: -1, scale, byPath);
        return new Snapshot { ByPath = byPath, Root = rootAcc };
    }

    private static int CountNodes(AccessibilityNode node)
    {
        int count = 1;
        var children = node.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
            count += CountNodes(children[i]);
        return count;
    }

    private static AtSpiAccessible Walk(
        AccessibilityNode node, string path, bool isRoot, string parentPath, int index, float scale,
        Dictionary<string, AtSpiAccessible> byPath)
    {
        var childPaths = AtSpiAccessible.ChildPathsOf(node);
        var acc = AtSpiAccessible.From(node, path, isRoot, parentPath, index, scale, childPaths);
        byPath[path] = acc;
        var children = node.Children;
        for (int i = 0; i < childPaths.Length; i++)
            Walk(children[i], childPaths[i], isRoot: false, path, i, scale, byPath);
        return acc;
    }

    private static string PathOf(AccessibilityNode node) =>
        node.Parent is null ? AtSpiConstants.RootPath : AtSpiAccessible.PathFor(node.ElementId);

    private static int IndexOf(AccessibilityNode parent, AccessibilityNode child)
    {
        for (int i = 0; i < parent.Children.Count; i++)
            if (parent.Children[i].ElementId == child.ElementId)
                return i;
        return -1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bus?.Dispose();
    }
}
