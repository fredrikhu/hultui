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
/// thread; a caret move in text made on request is told through the same call, with nothing in
/// the snapshot to change. The
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
        /// <summary>The lists whose items are served on demand, by the list's element: their items
        /// are found here, not in <see cref="ByPath"/>. Null when there are none.</summary>
        public Dictionary<ElementId, AtSpiServedItems>? Served { get; init; }
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

    AtSpiAccessible? IAtSpiTree.Find(string path)
    {
        var snapshot = _snapshot;   // once: the UI thread may publish another meanwhile
        return snapshot.ByPath.TryGetValue(path, out var node) ? node : ServedItem(snapshot, path);
    }

    /// <summary>The object of the item of a list served on demand at <paramref name="path"/>, made the
    /// first time a client asks; null when the path is no such item, or the item is gone.</summary>
    private static AtSpiAccessible? ServedItem(Snapshot snapshot, string path)
    {
        if (snapshot.Served is not { } served
            || !path.StartsWith(AtSpiConstants.AccessiblePathPrefix, StringComparison.Ordinal)
            || !ulong.TryParse(path.AsSpan(AtSpiConstants.AccessiblePathPrefix.Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out ulong value)
            || !VirtualItemId.TryDecode(new ElementId(value), out var listId, out int index)
            || !served.TryGetValue(listId, out var items))
            return null;
        return items.Item(index);
    }

    /// <summary>Whether <paramref name="id"/> is an item <paramref name="snapshot"/> serves on demand: a
    /// node of an item of a list that went past 2,000 items is gone from the tree, and its object stays.</summary>
    private static bool Serves(Snapshot snapshot, ElementId id) =>
        snapshot.Served is { } served && VirtualItemId.TryDecode(id, out var listId, out int index)
        && served.TryGetValue(listId, out var items) && (uint)index < (uint)items.Count;
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
        _snapshot = Build(root, _geometryHost?.Scale ?? 1f, previous);

        bool firstBuild = !_hasBuilt;
        _hasBuilt = true;

        // A tree from nothing: the first, or the first since the bridge went inactive (the
        // window drops its tree then, and builds it afresh when assistive technology is on
        // again). The cache-adds below publish all of it; what was published before and is not
        // in it any more went while nobody was told, and goes now. The object the bridge last
        // marked focused may be gone too: the window names the focused one with the next move.
        if (diff.Added.Count > 0 && diff.Added[0].Parent is null && diff.Removed.Count == 0)
        {
            foreach (var (path, acc) in previous.ByPath)
            {
                if (_snapshot.ByPath.ContainsKey(path) || Serves(_snapshot, acc.ElementId)) continue;
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
        // An item node of a list that went past 2,000 items is not gone either: the list serves it on
        // demand now, at the same path, and the object a client holds stays its object.
        foreach (var removed in diff.Removed)
        {
            string path = PathOf(removed.Node);
            if (_snapshot.ByPath.ContainsKey(path) || Serves(_snapshot, removed.Node.ElementId)) continue;
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
            // them; every other parent's added children by theirs. A list that served its items on
            // demand and has item nodes now is put right as a whole below (TellServedLists): the
            // client's list of its children is as long as it had items.
            HashSet<ElementId>? reordered = null;
            foreach (var parent in diff.Reordered) (reordered ??= []).Add(parent.ElementId);
            if (previous.Served is { } wasServed)
                foreach (var listId in wasServed.Keys) (reordered ??= []).Add(listId);
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
        {
            TellServedLists(previous);
            foreach (var change in diff.PropertyChanged)
            {
                string path = PathOf(change.Node);
                if (change.PropertyName == AccessibilityProperties.ItemCount)
                {
                    // Told from the snapshots above, with every other list whose items' number changed;
                    // a patch applied as a diff here has the same tree on both sides, and tells it from
                    // the change.
                    if (!_servedTold.Contains(change.Node.ElementId) && _snapshot.ByPath.TryGetValue(path, out var list)
                        && list.Served is { } items)
                        TellServedCount(list, items, change.Node.Served.Count, change.Previous?.Served.Count ?? 0, retire: false);
                    continue;
                }
                // An expanded state is told from the object as it was published: whether it was
                // expandable at all is a state of its own.
                var expandedBefore = change.PropertyName == AccessibilityProperties.IsExpanded
                    && previous.ByPath.TryGetValue(path, out var before) ? (true, before.Expanded) : default;
                if (EmitPropertyChange(change, path, expandedBefore)) DebugSignalsEmitted++;
            }
        }
    }

    /// <summary>
    /// The lists that serve their items on demand in <paramref name="previous"/> or in the snapshot now,
    /// whose children changed in number: a list served on demand with another number of items, one
    /// that went past 2,000 (its item nodes are gone and the list serves them now, at the same paths)
    /// or came back under it (it has item nodes again), and one that is gone with its items. Each is
    /// told as <see cref="TellServedCount"/> tells it; a list gone, by a cache-remove of each item a
    /// client may hold (the list's own went with the diff's).
    /// </summary>
    private void TellServedLists(Snapshot previous)
    {
        _servedTold.Clear();
        if (previous.Served is { } was)
            foreach (var (listId, items) in was)
            {
                var path = AtSpiAccessible.PathFor(listId);
                var before = previous.ByPath[path];
                if (!_snapshot.ByPath.TryGetValue(path, out var now))
                {
                    // The list is gone: so are its items.
                    TellServedCount(null, items, count: 0, ChildrenOf(before), retire: true);
                    continue;
                }
                if (now.Served == items) continue;   // served now too: below
                // It has item nodes again, whose cache-adds went out above.
                TellServedCount(now, items, now.ChildCount, ChildrenOf(before), retire: true);
            }
        if (_snapshot.Served is { } served)
            foreach (var (listId, items) in served)
            {
                int count = items.Count;
                int before = previous.ByPath.TryGetValue(AtSpiAccessible.PathFor(listId), out var old) ? ChildrenOf(old) : count;
                if (before == count) continue;
                TellServedCount(items.List, items, count, before, retire: false);
                _servedTold.Add(listId);
            }
    }

    // The lists TellServedLists told of in this apply. UI thread.
    private readonly HashSet<ElementId> _servedTold = [];

    /// <summary>How many children an object of a previous snapshot had, as the client was told: a list
    /// served on demand's items as its node carried them (its items are the next snapshot's by then).</summary>
    private static int ChildrenOf(AtSpiAccessible acc) =>
        acc.Node is { IsVirtualItemContainer: true } node ? node.Served.Count : acc.ChildPaths.Count;

    /// <summary>
    /// A list's children went from <paramref name="before"/> to <paramref name="count"/>, and some or all
    /// of them are items it serves on demand (<paramref name="items"/>), which have no nodes for a diff to
    /// add or remove. What a caching client (libatspi, Orca's) must hear for its list of the list's
    /// children to be right: each item it may hold past the new end disposed (cache-remove); one
    /// children-changed for the first item that went or came, as a diff's run of items removed from or
    /// added to a list's end is told; and the list's object published again (cache-add), which sizes the
    /// client's list of its children to the number now, keeping what it holds below it (plan 17, round 3,
    /// step 15, seen live in WSL). With <paramref name="retire"/>, the list serves its items no more.
    /// </summary>
    /// <param name="list">The list's object now; null when the list is gone.</param>
    private void TellServedCount(AtSpiAccessible? list, AtSpiServedItems items, int count, int before, bool retire)
    {
        var gone = _gone;
        items.TakeGone(retire && list is null ? 0 : count, before, gone);
        if (retire && list is not null)
        {
            // Items below the end have nodes now: their objects are the snapshot's, at the same paths.
            for (int i = gone.Count - 1; i >= 0; i--)
                if (_snapshot.ByPath.ContainsKey(gone[i])) gone.RemoveAt(i);
        }
        foreach (var path in gone)
        {
            _signals!.EmitCacheRemove(path);
            DebugSignalsEmitted++;
        }
        gone.Clear();
        if (list is null || count == before) return;
        if (count < before)
            _signals!.EmitChildrenChanged(list.Path, items.PathOf(count), count, isAdd: false);
        else
            _signals!.EmitChildrenChanged(list.Path, items.PathOf(before), before, isAdd: true);
        _signals.EmitCacheAdd(this, list);
        DebugSignalsEmitted += 2;
    }

    private readonly List<string> _gone = [];

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
            // Still its list's item, at its place, served on demand now: the list's own signals
            // tell its children changed (TellServedLists).
            if (removed.Parent is null || Serves(_snapshot, removed.Node.ElementId)) continue;
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
    /// item that had the selection and the one that has it; a caret or selection move in text made
    /// on request (the rich text editor's); a leaf's change of its own name, description, value,
    /// range, toggle or expanded state, whose node the window made again and put in its place; and
    /// a layout refresh made in place: boxes moved, nodes gone out of view or come into it, scroll
    /// views scrolled, with such leaves among them. Their objects in the published snapshot take the
    /// new state and the new node (a text's object reads its text through its source, so it has none
    /// to take; every object reads its box and offscreen state from its node) and the bridge sends
    /// what a diff would have (state-changed:selected; text-caret-moved and text-selection-changed;
    /// accessible-name, accessible-description, accessible-value, state-changed:checked or expanded;
    /// nothing for a move or a scroll), with no new snapshot. A list whose items are served on demand
    /// tells a move of its selection on its items (state-changed:selected, <see cref="EmitServedSelection"/>)
    /// and another number of items as its children's (<see cref="TellServedCount"/>). Anything else is
    /// applied as a diff.
    /// </summary>
    public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root)
    {
        if (!_registered || _signals is null) return;
        var snapshot = _snapshot;
        bool inPlace = _hasBuilt && patch.Added.Count == 0 && patch.Removed.Count == 0 && patch.Reordered.Count == 0;
        foreach (var change in patch.PropertyChanged)
        {
            inPlace &= snapshot.ByPath.ContainsKey(PathOf(change.Node))
                && change.PropertyName switch
                {
                    AccessibilityProperties.IsSelected => true,
                    AccessibilityProperties.TextSelection => change.Node.TextSource is not null,
                    // Not the application's own object, which is named as the application.
                    AccessibilityProperties.Name or AccessibilityProperties.Description or AccessibilityProperties.Value
                        or AccessibilityProperties.RangeValue or AccessibilityProperties.IsToggled
                        or AccessibilityProperties.IsExpanded => change.Node.Parent is not null && change.Node.TextSource is null,
                    // Read from the node: nothing to set, and nothing a diff sends.
                    AccessibilityProperties.BoundingRect or AccessibilityProperties.ScrollPosition => true,
                    // A list served on demand: its items read their number and selection from its node.
                    AccessibilityProperties.ItemCount or AccessibilityProperties.ItemSelection
                        => snapshot.Served?.ContainsKey(change.Node.ElementId) == true,
                    _ => false,
                };
        }
        if (!inPlace)
        {
            ApplyChanges(patch, root);
            return;
        }
        foreach (var change in patch.PropertyChanged)
        {
            string path = PathOf(change.Node);
            var acc = snapshot.ByPath[path];
            var node = change.Node;
            // A leaf's node made again is the change's: the object reads its box from it now.
            acc.Node = node;
            if (change.PropertyName == AccessibilityProperties.ItemCount)
            {
                TellServedCount(acc, acc.Served!, node.Served.Count, change.Previous?.Served.Count ?? 0, retire: false);
                continue;
            }
            (bool, bool?) expandedBefore = (true, acc.Expanded);
            switch (change.PropertyName)
            {
                case AccessibilityProperties.IsSelected: acc.Selected = node.IsSelected == true; break;
                case AccessibilityProperties.Name: acc.Name = node.Name ?? ""; break;
                case AccessibilityProperties.Description: acc.Description = node.Description ?? ""; break;
                // As the snapshot takes them (AtSpiAccessible.From): a password's bullets, never its text.
                case AccessibilityProperties.Value: acc.Value = node.Value; break;
                case AccessibilityProperties.RangeValue: acc.Range = node.Range; break;
                case AccessibilityProperties.IsToggled: acc.Toggled = node.IsToggled; break;
                case AccessibilityProperties.IsExpanded: acc.Expanded = node.Expanded; break;
            }
            if (EmitPropertyChange(change, path, expandedBefore)) DebugSignalsEmitted++;
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

    /// <param name="expandedBefore">For an expanded state's change: the object's expanded state as
    /// it was published, when known.</param>
    private bool EmitPropertyChange(PropertyChange change, string path, (bool Known, bool? Value) expandedBefore = default)
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
            {
                // Expandable (there is something to open) is a state of its own beside expanded. A
                // node that stops being expandable, or starts, says so: a caching client (Orca's
                // libatspi) went on reading "collapsed" for one that had nothing to open any more
                // (seen live in WSL), as only expanded was ever sent.
                bool? now = change.Node.Expanded;
                if (expandedBefore.Known && (expandedBefore.Value is null) != (now is null))
                {
                    if (now is not null) _signals!.EmitStateChanged(path, "expandable", true);
                    if ((expandedBefore.Value == true) != (now == true))
                        _signals!.EmitStateChanged(path, "expanded", now == true);
                    if (now is null) _signals!.EmitStateChanged(path, "expandable", false);
                    return true;
                }
                _signals!.EmitStateChanged(path, "expanded", now == true);
                return true;
            }
            case AccessibilityProperties.IsEnabled:
                _signals!.EmitStateChanged(path, "enabled", change.Node.IsEnabled);
                _signals!.EmitStateChanged(path, "sensitive", change.Node.IsEnabled);
                return true;
            case AccessibilityProperties.IsSelected:
                _signals!.EmitStateChanged(path, "selected", change.Node.IsSelected == true);
                return true;
            case AccessibilityProperties.ItemSelection:
                return EmitServedSelection(change.Previous, change.Node);
            case AccessibilityProperties.Text:
            {
                // From the versions' characters where the text is made on request: a text area's
                // version hands over its string, where making the text made the tables of its lines
                // and paragraphs on every keystroke (the rich text editor's makes its text still).
                var (deleted, inserted) = AtSpiText.Difference(CharactersOf(change.Previous) ?? "", CharactersOf(change.Node) ?? "");
                if (deleted is { } d) _signals!.EmitTextChanged(path, insert: false, d.Offset, d.Text);
                if (inserted is { } i) _signals!.EmitTextChanged(path, insert: true, i.Offset, i.Text);
                return deleted is not null || inserted is not null;
            }
            case AccessibilityProperties.TextSelection:
            {
                var (before, after) = (SelectionOf(change.Previous), SelectionOf(change.Node));
                if (after is { Caret: >= 0 } now && before?.Caret != now.Caret)
                    _signals!.EmitTextCaretMoved(path, now.Caret);
                if (before?.Start != after?.Start || before?.End != after?.End)
                    _signals!.EmitTextSelectionChanged(path);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// A move of the selection of a list served on demand: state-changed:selected on each item whose
    /// state changed — the one that had the selection, the one that has it — in item order, with its
    /// state now, as a list of item nodes sends for the same move. On the item's path, whether or not a
    /// client has asked for its object (it is made when one does); a client may hold it from now on.
    /// O(1) for a move of one selection. An item that went with the list's end sends nothing, as an
    /// item node removed does not. False when nothing was sent.
    /// </summary>
    private bool EmitServedSelection(AccessibilityNode? previous, AccessibilityNode node)
    {
        if (_snapshot.Served?.GetValueOrDefault(node.ElementId) is not { } items) return false;
        var now = node.Served;
        int sent = 0;
        foreach (var (index, selected) in ServedItems.Changes(previous?.Served ?? default, now))
        {
            if (index >= now.Count) continue;
            _signals!.EmitStateChanged(items.PathOf(index), "selected", selected);
            items.Named(index);
            sent++;
        }
        DebugSignalsEmitted += Math.Max(0, sent - 1);   // the caller counts one
        return sent > 0;
    }

    /// <summary>
    /// A node's caret and selection as its text has them. From the version of a text made on
    /// request (the rich text editor's), which holds the offsets: making the text to read them made
    /// the whole document's text, its lines and its boxes on every caret move (475 KB at 5,000
    /// blocks), for two numbers. Null for a node with no text.
    /// </summary>
    private static (int Caret, int Start, int End)? SelectionOf(AccessibilityNode? node) =>
        node?.TextVersion is { } version ? (version.Caret, version.SelectionStart, version.SelectionEnd)
        : node?.Text is { } text ? (text.Caret, text.SelectionStart, text.SelectionEnd)
        : null;

    private static string? CharactersOf(AccessibilityNode? node) =>
        node?.TextVersion is { } version ? version.Characters : node?.Text?.Text;

    /// <summary>An AT-SPI announcement from the element's object, or the application's.</summary>
    public void Announce(AccessibilityNode? source, string text, AnnouncementPoliteness politeness)
    {
        if (!_registered || _signals is null) return;
        _signals.EmitAnnouncement(source is null ? AtSpiConstants.RootPath : PathOf(source), text,
            politeness == AnnouncementPoliteness.Assertive ? AtSpiConstants.LiveAssertive : AtSpiConstants.LivePolite);
        DebugSignalsEmitted++;
    }

    /// <summary>For tests: publishes the snapshot of <paramref name="root"/>, as a rebuild would.</summary>
    internal void Publish(AccessibilityNode root, float scale = 1f) => _snapshot = Build(root, scale, _snapshot);

    /// <summary>
    /// The published view of <paramref name="root"/>'s tree, rebuilt in full on every apply. What
    /// it makes per node is the object, its path and its place in its parent's list of children:
    /// the dictionary is sized for the tree (grown entry by entry to 3,000, it reallocated a dozen
    /// times), and each path is made once, in one string. A path was made twice, for the parent's
    /// list and for the node itself, in two strings each (the id's, then the path), and every
    /// parent kept its children's paths in a list: about 500 bytes a node, about 270 now. A list
    /// whose items are served on demand keeps its items (<see cref="AtSpiServedItems"/>) from
    /// <paramref name="previous"/> while it serves them, so the item objects a client holds stay.
    /// </summary>
    private static Snapshot Build(AccessibilityNode root, float scale, Snapshot previous)
    {
        var byPath = new Dictionary<string, AtSpiAccessible>(CountNodes(root));
        List<AtSpiAccessible>? lists = null;
        var rootAcc = Walk(root, AtSpiConstants.RootPath, isRoot: true, parentPath: AtSpiConstants.NullPath,
            index: -1, scale, byPath, ref lists);
        Dictionary<ElementId, AtSpiServedItems>? served = null;
        if (lists is not null)
            foreach (var acc in lists)
            {
                var id = acc.ElementId;
                var items = previous.Served?.GetValueOrDefault(id) ?? new AtSpiServedItems(id, AccessibleOwnerSlots.SlotOf(id));
                items.List = acc;
                acc.Served = items;
                (served ??= [])[id] = items;
            }
        return new Snapshot { ByPath = byPath, Root = rootAcc, Served = served };
    }

    private static int CountNodes(AccessibilityNode node)
    {
        int count = 1;
        var children = node.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
            count += CountNodes(children[i]);
        return count;
    }

    /// <param name="lists">The objects of lists whose items are served on demand, added as found.</param>
    private static AtSpiAccessible Walk(
        AccessibilityNode node, string path, bool isRoot, string parentPath, int index, float scale,
        Dictionary<string, AtSpiAccessible> byPath, ref List<AtSpiAccessible>? lists)
    {
        var childPaths = AtSpiAccessible.ChildPathsOf(node);
        var acc = AtSpiAccessible.From(node, path, isRoot, parentPath, index, scale, childPaths);
        byPath[path] = acc;
        if (node.IsVirtualItemContainer && !isRoot) (lists ??= []).Add(acc);
        var children = node.Children;
        for (int i = 0; i < childPaths.Length; i++)
            Walk(children[i], childPaths[i], isRoot: false, path, i, scale, byPath, ref lists);
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
