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
    private readonly bool _registered;
    private volatile Snapshot _snapshot = Snapshot.Empty;
    private bool _hasBuilt;
    private string? _focusedPath;
    private int _applicationId;
    private bool _disposed;

    public LinuxAccessibilityBridge()
    {
        // Eager connect. If no AT-SPI bus answers we stay inactive, matching the
        // "is anyone listening" contract the null bridge expresses everywhere.
        _bus = AtSpiBus.Open();
        if (_bus is null) return;

        _bus.AddMethodHandler(new AtSpiObjectHandler(this));
        _bus.AddMethodHandler(new AtSpiCacheHandler(this));
        _registered = _bus.RegisterApplication();
    }

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

    // ─────────────────────────────────────────────────── IAccessibilityBridge

    public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
    {
        if (!_registered || _bus is null) return;
        DebugApplyChangesCalls++;

        _snapshot = Build(root);

        // The very first build hands the whole tree to the client via GetItems;
        // per-node children-changed on top of that is just noise. Emit deltas
        // only for subsequent structural changes.
        bool firstBuild = !_hasBuilt;
        _hasBuilt = true;
        if (firstBuild) return;

        foreach (var added in diff.Added)
        {
            if (added.Parent is null) continue; // root — no parent to notify
            string childPath = PathOf(added.Node);
            if (_snapshot.ByPath.TryGetValue(childPath, out var acc))
            {
                _bus.EmitChildrenChanged(acc.ParentPath, childPath, acc.IndexInParent, isAdd: true);
                DebugSignalsEmitted++;
            }
        }

        foreach (var removed in diff.Removed)
        {
            if (removed.Parent is null) continue;
            string parentPath = PathOf(removed.Parent);
            int index = IndexOf(removed.Parent, removed.Node);
            _bus.EmitChildrenChanged(parentPath, PathOf(removed.Node), index, isAdd: false);
            DebugSignalsEmitted++;
        }

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

    private static Snapshot Build(AccessibilityNode root)
    {
        var byPath = new Dictionary<string, AtSpiAccessible>();
        var rootAcc = Walk(root, isRoot: true, parentPath: AtSpiConstants.NullPath, index: -1, byPath);
        return new Snapshot { ByPath = byPath, Root = rootAcc };
    }

    private static AtSpiAccessible Walk(
        AccessibilityNode node, bool isRoot, string parentPath, int index,
        Dictionary<string, AtSpiAccessible> byPath)
    {
        var acc = AtSpiAccessible.From(node, isRoot, parentPath, index);
        byPath[acc.Path] = acc;
        for (int i = 0; i < node.Children.Count; i++)
            Walk(node.Children[i], isRoot: false, acc.Path, i, byPath);
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
