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
    public LinuxAccessibilityBridge(IAccessibilityActionHost? actionHost = null)
    {
        _actionHost = actionHost;
        // The windowing layer's host implements both capabilities on one object.
        _geometryHost = actionHost as IAccessibilityGeometryHost;
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
    }

    /// <summary>A childless application root, valid from Embed until the first build.</summary>
    private static readonly AtSpiAccessible SeedRoot = new()
    {
        ElementId = default,
        Path = AtSpiConstants.RootPath,
        IsRoot = true,
        Role = AtSpiConstants.RoleApplication,
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

        // Populate the client's cache for every added node (the first build's
        // diff carries the whole tree). This is what keeps libatspi from
        // treating a later-signalled node as transient and marking it defunct.
        // The structural children-changed / property-change *deltas* are only
        // meaningful after the first build — before that the client has nothing
        // to diff against, so cache-add alone conveys the initial tree.
        bool firstBuild = !_hasBuilt;
        _hasBuilt = true;

        foreach (var added in diff.Added)
        {
            string childPath = PathOf(added.Node);
            if (!_snapshot.ByPath.TryGetValue(childPath, out var acc)) continue;
            _bus.EmitCacheAdd(this, acc);
            DebugSignalsEmitted++;
            if (!firstBuild && added.Parent is not null)
            {
                _bus.EmitChildrenChanged(acc.ParentPath, childPath, acc.IndexInParent, isAdd: true);
                DebugSignalsEmitted++;
            }
        }

        foreach (var removed in diff.Removed)
        {
            string childPath = PathOf(removed.Node);
            _bus.EmitCacheRemove(childPath);
            DebugSignalsEmitted++;
            if (!firstBuild && removed.Parent is not null)
            {
                int index = IndexOf(removed.Parent, removed.Node);
                _bus.EmitChildrenChanged(PathOf(removed.Parent), childPath, index, isAdd: false);
                DebugSignalsEmitted++;
            }
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
