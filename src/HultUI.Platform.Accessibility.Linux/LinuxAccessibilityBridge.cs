using System.Runtime.Versioning;
using HultUI.Core.Accessibility;

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
/// that snapshot lock-free; <see cref="ApplyChanges"/> and
/// <see cref="NotifyFocusChanged"/> run on the UI thread and swap it. The
/// <see cref="TreeDiff"/> drives which change signals to emit — the same delta
/// the render side already computed, reused as the AT-SPI event stream.
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
    private readonly IAccessibilityActionHost? _actionHost;
    private readonly IAccessibilityGeometryHost? _geometryHost;
    private readonly bool _registered;
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
        // Eager connect. If no AT-SPI bus answers we stay inactive, matching the
        // "is anyone listening" contract the null bridge expresses everywhere.
        _bus = AtSpiBus.Open();
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

        // Whether anyone listens, for content costly to describe (seam W11).
        AccessibilityClients.Report(_bus.ReadStatus());
        _bus.WatchStatus(AccessibilityClients.Report);
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

    public bool IsActive => _registered;

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
        if (!_registered || _bus is null) return;
        DebugApplyChangesCalls++;

        _snapshot = Build(root, _geometryHost?.Scale ?? 1f);

        bool firstBuild = !_hasBuilt;
        _hasBuilt = true;

        // Cache-add is genuinely per-node: it populates the AT client's object
        // cache (the first build's diff carries the whole tree) so navigation is
        // instant and libatspi never treats a later-signalled node as transient
        // and marks it defunct. AT-SPI's Cache interface adds one item at a time —
        // unlike UIA, which the client pulls on demand — so every added node is
        // cached. That work stays; only the redundant structural signals below go.
        foreach (var added in diff.Added)
        {
            string childPath = PathOf(added.Node);
            if (!_snapshot.ByPath.TryGetValue(childPath, out var acc)) continue;
            _bus.EmitCacheAdd(this, acc);
            DebugSignalsEmitted++;
        }

        foreach (var removed in diff.Removed)
        {
            _bus.EmitCacheRemove(PathOf(removed.Node));
            DebugSignalsEmitted++;
        }

        // children-changed is a structural re-query hint on a *parent*, not a
        // per-child record: the per-node Cache.Add/RemoveAccessible signals above
        // already carry the exact node set to the client's cache, so one nudge per
        // affected parent is enough — the client re-reads that parent's children
        // (served lock-free from the fresh snapshot; verified with a live pyatspi
        // client, which even observes reorders with no signal at all). So we
        // coalesce two ways:
        //   • interior nodes (parent is itself in the diff) carry no information —
        //     the client discovers them by walking their freshly added/removed
        //     parent — so BoundaryAdded/Removed drops them; and
        //   • the remaining boundary children under one surviving parent collapse
        //     to a single signal on that parent (naming the first affected child).
        // Together these turn a bulk insert/clear of N rows under one container
        // from N children-changed signals into one. (Skipped on the first build:
        // the client has no prior tree to diff, and the cache-add burst conveys
        // the whole initial tree.)
        if (!firstBuild)
        {
            EmitAddedChildNudges(diff.BoundaryAdded());
            EmitRemovedChildNudges(diff.BoundaryRemoved());
        }

        // diff.Reordered (same children, new order) needs no signal: every
        // Accessible/Cache query is served from the freshly rebuilt snapshot, so
        // GetChildren / GetChildAtIndex already return the new order. Verified —
        // a persistent pyatspi client observes each reorder with no reorder signal
        // emitted at all. A high-level AT that caches child order and only
        // refreshes on events (Orca) would want an object:children-changed here;
        // add it when an Orca test shows it's needed and confirms the exact form.

        if (!firstBuild)
            foreach (var change in diff.PropertyChanged)
                if (EmitPropertyChange(change)) DebugSignalsEmitted++;
    }

    // One children-changed:add per surviving parent. The boundary list has already
    // dropped interior nodes; here we keep only the first added child under each
    // distinct parent, since the signal is a re-query nudge and the per-node
    // cache-adds already conveyed the full set.
    private void EmitAddedChildNudges(List<AddedNode> boundary)
    {
        var seenParents = new HashSet<string>();
        foreach (var added in boundary)
        {
            if (added.Parent is null) continue;
            string childPath = PathOf(added.Node);
            if (!_snapshot.ByPath.TryGetValue(childPath, out var acc)) continue;
            if (!seenParents.Add(acc.ParentPath)) continue;
            _bus!.EmitChildrenChanged(acc.ParentPath, childPath, acc.IndexInParent, isAdd: true);
            DebugSignalsEmitted++;
        }
    }

    // One children-changed:remove per surviving parent. Removed nodes are gone from
    // the new snapshot, so parent/index come from the previous tree the diff carries.
    private void EmitRemovedChildNudges(List<RemovedNode> boundary)
    {
        var seenParents = new HashSet<string>();
        foreach (var removed in boundary)
        {
            if (removed.Parent is null) continue;
            string parentPath = PathOf(removed.Parent);
            if (!seenParents.Add(parentPath)) continue;
            int index = IndexOf(removed.Parent, removed.Node);
            _bus!.EmitChildrenChanged(parentPath, PathOf(removed.Node), index, isAdd: false);
            DebugSignalsEmitted++;
        }
    }

    public void NotifyFocusChanged(AccessibilityNode? focused)
    {
        if (!_registered || _bus is null) return;

        string? newPath = focused is null ? null : PathOf(focused);
        if (newPath == _focusedPath) return;

        if (_focusedPath is not null)
            _bus.EmitStateChanged(_focusedPath, "focused", active: false);
        if (newPath is not null)
            _bus.EmitStateChanged(newPath, "focused", active: true);
        _focusedPath = newPath;
    }

    // ───────────────────────────────────────────────────────────── helpers

    private bool EmitPropertyChange(PropertyChange change)
    {
        string path = PathOf(change.Node);
        switch (change.PropertyName)
        {
            case AccessibilityProperties.Name:
                _bus!.EmitPropertyChange(path, AtSpiConstants.PropAccessibleName, change.Node.Name ?? "");
                return true;
            case AccessibilityProperties.Description:
                _bus!.EmitPropertyChange(path, AtSpiConstants.PropAccessibleDescription, change.Node.Description ?? "");
                return true;
            case AccessibilityProperties.Value:
                _bus!.EmitPropertyChange(path, AtSpiConstants.PropAccessibleValue, change.Node.Value ?? "");
                return true;
            case AccessibilityProperties.IsToggled:
                _bus!.EmitStateChanged(path, "checked", change.Node.IsToggled == true);
                return true;
            case AccessibilityProperties.IsFocused:
                _bus!.EmitStateChanged(path, "focused", change.Node.IsFocused);
                return true;
            case AccessibilityProperties.IsEnabled:
                _bus!.EmitStateChanged(path, "enabled", change.Node.IsEnabled);
                _bus!.EmitStateChanged(path, "sensitive", change.Node.IsEnabled);
                return true;
            case AccessibilityProperties.IsSelected:
                _bus!.EmitStateChanged(path, "selected", change.Node.IsToggled == true);
                return true;
            default:
                return false;
        }
    }

    /// <summary>For tests: publishes the snapshot of <paramref name="root"/>, as a rebuild would.</summary>
    internal void Publish(AccessibilityNode root, float scale = 1f) => _snapshot = Build(root, scale);

    private static Snapshot Build(AccessibilityNode root, float scale)
    {
        var byPath = new Dictionary<string, AtSpiAccessible>();
        var rootAcc = Walk(root, isRoot: true, parentPath: AtSpiConstants.NullPath, index: -1, scale, byPath);
        return new Snapshot { ByPath = byPath, Root = rootAcc };
    }

    private static AtSpiAccessible Walk(
        AccessibilityNode node, bool isRoot, string parentPath, int index, float scale,
        Dictionary<string, AtSpiAccessible> byPath)
    {
        var acc = AtSpiAccessible.From(node, isRoot, parentPath, index, scale);
        byPath[acc.Path] = acc;
        for (int i = 0; i < node.Children.Count; i++)
            Walk(node.Children[i], isRoot: false, acc.Path, i, scale, byPath);
        return acc;
    }

    private static string PathOf(AccessibilityNode node) =>
        node.Parent is null
            ? AtSpiConstants.RootPath
            : AtSpiConstants.AccessiblePathPrefix + node.ElementId.Value;

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
