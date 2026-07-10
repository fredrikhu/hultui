using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// UIAutomation bridge. Built by <c>HultUI.AccessibilityBridgeFactory</c> when
/// running on Windows. Owns a per-window fragment root and a cache of per-node
/// <see cref="UiaFragment"/> logic objects keyed by <see cref="ElementId"/>.
/// </summary>
/// <remarks>
/// <para>
/// Providers are exposed to UIA as hand-built native COM objects (see
/// <see cref="NativeProvider"/>) rather than <c>ComWrappers</c> CCWs, which
/// UIAutomationCore access-violates on released .NET 10.
/// </para>
/// <para>
/// <c>WM_GETOBJECT</c> is a <em>sent</em> message, so it is serviced from a
/// window-procedure subclass installed here — not SDL's message-pump hook,
/// which only runs for queued messages and so never delivered the provider.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed unsafe class WindowsAccessibilityBridge : IAccessibilityBridge, IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly IUiaHostBridge _host;
    private readonly UiaFragmentRoot _fragmentRoot;
    // UIA calls provider methods (Navigate, GetFocus, …) from worker threads
    // while the UI thread mutates this in ApplyChanges — must be concurrent.
    private readonly ConcurrentDictionary<ElementId, UiaFragment> _wrappers = new();
    private IntPtr _hostProviderPtr;
    private AccessibilityNode? _rootNode;
    // ElementId? (nullable ulong) isn't torn-read-safe across threads; store the
    // focused id as a raw long (0 = none) accessed via Volatile.
    private long _focusedIdRaw;
    private bool _disposed;

    // Cached native COM objects — one per element (and one for the root), so
    // repeated Navigate/GetFragmentRoot/etc. reuse a stable object instead of
    // allocating a fresh one per call. The bridge holds one reference to each;
    // clients get AddRef'd tear-offs. Released when the element leaves the tree.
    private readonly object _nativeLock = new();
    private IntPtr _rootNative;
    private readonly ConcurrentDictionary<ElementId, IntPtr> _nativeByElement = new();

    // WndProc subclassing state.
    private IntPtr _originalWndProc;
    private static readonly ConcurrentDictionary<IntPtr, WindowsAccessibilityBridge> _byHwnd = new();

    public WindowsAccessibilityBridge(IntPtr hwnd, IUiaHostBridge host)
    {
        _hwnd = hwnd;
        _host = host;
        _fragmentRoot = new UiaFragmentRoot(this);

        UiaInterop.UiaHostProviderFromHwnd(hwnd, out _hostProviderPtr);

        // Serve WM_GETOBJECT from the real window procedure.
        _byHwnd[hwnd] = this;
        delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr> proc = &WndProc;
        _originalWndProc = UiaInterop.SetWindowLongPtr(hwnd, UiaInterop.GWLP_WNDPROC, (IntPtr)proc);
    }

    public bool IsActive => UiaInterop.UiaClientsAreListening();

    // Raise UIA events (structure/property/focus changed) so an assistive tech
    // refreshes live as the tree changes. This used to fast-fail on the
    // ComWrappers path; with native provider objects it works. Opt out with
    // HULTUI_UIA_EVENTS=0 if a specific screen reader misbehaves.
    private static readonly bool RaiseEvents =
        Environment.GetEnvironmentVariable("HULTUI_UIA_EVENTS") != "0";

    internal AccessibilityNode? RootNode => Volatile.Read(ref _rootNode);

    internal ElementId? FocusedNodeId
    {
        get
        {
            long raw = Volatile.Read(ref _focusedIdRaw);
            return raw == 0 ? null : new ElementId((ulong)raw);
        }
    }

    // ── Native-object factory (all return AddRef'd pointers UIA will Release).
    //    Each call names the interface the COM method returns, so the caller
    //    gets the matching tear-off (see NativeProvider.As). ──

    /// <summary>The cached native root object (bridge-owned reference).</summary>
    private IntPtr RootNativePrimary()
    {
        IntPtr p = Volatile.Read(ref _rootNative);
        if (p != IntPtr.Zero) return p;
        lock (_nativeLock)
        {
            if (_rootNative == IntPtr.Zero)
                _rootNative = NativeProvider.Create(_fragmentRoot); // refcount 1 = bridge's ref
            return _rootNative;
        }
    }

    /// <summary>The cached native object for an element, or Zero if it has no wrapper.</summary>
    private IntPtr ElementNativePrimary(ElementId id)
    {
        if (_nativeByElement.TryGetValue(id, out var p)) return p;
        if (!_wrappers.TryGetValue(id, out var wrapper)) return IntPtr.Zero;
        lock (_nativeLock)
        {
            if (_nativeByElement.TryGetValue(id, out p)) return p;
            p = NativeProvider.Create(wrapper); // refcount 1 = bridge's ref
            _nativeByElement[id] = p;
            return p;
        }
    }

    /// <summary>The root as IRawElementProviderSimple* (WM_GETOBJECT, events).</summary>
    internal IntPtr NativeRootSimple() => NativeProvider.Tearoff(RootNativePrimary(), NativeProvider.As.Simple);

    /// <summary>The root as IRawElementProviderFragment* (a fragment's parent).</summary>
    internal IntPtr NativeRootFragment() => NativeProvider.Tearoff(RootNativePrimary(), NativeProvider.As.Fragment);

    /// <summary>The root as IRawElementProviderFragmentRoot* (GetFragmentRoot).</summary>
    internal IntPtr NativeRootFragmentRoot() => NativeProvider.Tearoff(RootNativePrimary(), NativeProvider.As.FragmentRoot);

    /// <summary>A cached fragment as IRawElementProviderFragment* (Navigate, hit-test, focus), or null.</summary>
    internal IntPtr NativeFragment(ElementId? id)
        => id is null ? IntPtr.Zero : NativeProvider.Tearoff(ElementNativePrimary(id.Value), NativeProvider.As.Fragment);

    /// <summary>A cached fragment as IRawElementProviderSimple* (events, selection container), or null.</summary>
    internal IntPtr NativeFragmentSimple(ElementId? id)
        => id is null ? IntPtr.Zero : NativeProvider.Tearoff(ElementNativePrimary(id.Value), NativeProvider.As.Simple);

    /// <summary>The HWND host provider, AddRef'd (chains our tree to the window's non-client tree).</summary>
    internal IntPtr HostProviderRef()
    {
        if (_hostProviderPtr == IntPtr.Zero) return IntPtr.Zero;
        Marshal.AddRef(_hostProviderPtr);
        return _hostProviderPtr;
    }

    // ── On-demand items for large virtualized lists (IsVirtualItemContainer) ──

    /// <summary>
    /// A freshly built IRawElementProviderFragment* for a virtualized list item.
    /// Not cached: there may be millions, and UIA tracks identity by the item's
    /// stable runtime id, so a new object per navigation result is fine. The
    /// returned pointer carries the single reference UIA will release.
    /// </summary>
    internal IntPtr NativeVirtualItem(ElementId listId, int index)
    {
        IntPtr primary = NativeProvider.Create(new UiaVirtualItem(this, listId, index));
        if (primary == IntPtr.Zero) return IntPtr.Zero;
        IntPtr fragment = NativeProvider.Tearoff(primary, NativeProvider.As.Fragment);
        Marshal.Release(primary); // hand the caller the fragment's single reference
        return fragment;
    }

    internal int GetVirtualItemCount(ElementId listId) => _host.GetItemCount(listId);

    internal string? GetVirtualItemLabel(ElementId listId, int index) => _host.GetItemLabel(listId, index);

    internal bool GetVirtualItemSelected(ElementId listId, int index) => _host.GetItemSelected(listId, index);

    /// <summary>Snapshot expand state of a node (null = not expandable), for ExpandCollapse.</summary>
    internal bool? GetExpanded(ElementId id) => _wrappers.TryGetValue(id, out var w) ? w.Expanded : null;

    internal bool TryGetVirtualItemScreenRect(ElementId listId, int index, out Rect screen)
    {
        if (_host.TryGetItemLayout(listId, index, out var layout))
        {
            screen = ToScreen(layout);
            return true;
        }
        screen = default;
        return false;
    }

    /// <summary>The item index under a point (in layout space) of a virtual list, or -1.</summary>
    internal int VirtualItemIndexAt(ElementId listId, Point local)
    {
        if (!_host.TryGetItemLayout(listId, 0, out var first) || first.Height <= 0) return -1;
        if (local.X < first.X || local.X > first.X + first.Width) return -1;
        int count = _host.GetItemCount(listId);
        int i = (int)Math.Floor((local.Y - first.Y) / first.Height);
        return i >= 0 && i < count ? i : -1;
    }

    // ── Accessibility tree upkeep ──

    public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
    {
        // Always track the current full tree — ElementProviderFromPoint hit-tests
        // against it, and it must reflect deep changes even when the root node's
        // own properties didn't change.
        Volatile.Write(ref _rootNode, root);

        // Index the new tree by id. Parent wrappers must be refreshed from *this*
        // (authoritative, current) snapshot: RemovedNode.Parent references the
        // previous tree, so refreshing a parent from it would leave the cached
        // wrapper pointing at stale children — including just-removed ones whose
        // wrappers we drop below. Navigate would then hand UIA a child ElementId
        // with no wrapper (NativeFragment → Zero) and the whole subtree vanishes.
        var currentById = new Dictionary<ElementId, AccessibilityNode>();
        IndexTree(root, currentById);

        // 1) Materialise/refresh wrappers for added nodes (no parent refresh yet).
        foreach (var added in diff.Added)
        {
            // Keep the wrapper stable per ElementId so the cached native object
            // (which wraps it) stays valid; only create one the first time.
            if (_wrappers.TryGetValue(added.Node.ElementId, out var existing))
                existing.UpdateFrom(added.Node);
            else
                _wrappers[added.Node.ElementId] = new UiaFragment(this, added.Node);
        }

        // 2) Drop wrappers (and cached native objects) for removed nodes.
        foreach (var removed in diff.Removed)
        {
            _wrappers.TryRemove(removed.Node.ElementId, out _);
            // Drop the bridge's reference to the element's cached native object;
            // it frees once any client references are also released.
            if (_nativeByElement.TryRemove(removed.Node.ElementId, out var native))
                Marshal.Release(native);
        }

        // 3) Apply property changes to surviving wrappers.
        foreach (var change in diff.PropertyChanged)
            if (_wrappers.TryGetValue(change.Node.ElementId, out var wrapper))
                wrapper.UpdateFrom(change.Node);

        // 4) Now that every wrapper exists (adds) or is gone (removes), refresh
        //    each affected parent from the new tree so its child list is current,
        //    and raise the matching UIA events.
        foreach (var added in diff.Added)
        {
            RefreshParent(added.Parent?.ElementId, currentById);
            RaiseStructureChanged(added.Parent?.ElementId, UiaInterop.StructureChangeType_ChildrenBulkAdded);
        }
        foreach (var removed in diff.Removed)
        {
            RefreshParent(removed.Parent?.ElementId, currentById);
            RaiseStructureChanged(removed.Parent?.ElementId, UiaInterop.StructureChangeType_ChildrenBulkRemoved);
        }
        foreach (var reorderedParent in diff.Reordered)
        {
            RefreshParent(reorderedParent.ElementId, currentById);
            RaiseStructureChanged(reorderedParent.ElementId, UiaInterop.StructureChangeType_ChildrenReordered);
        }
        foreach (var change in diff.PropertyChanged)
            if (_wrappers.TryGetValue(change.Node.ElementId, out var wrapper))
                RaisePropertyChanged(wrapper, change.PropertyName);
    }

    private static void IndexTree(AccessibilityNode node, Dictionary<ElementId, AccessibilityNode> map)
    {
        map[node.ElementId] = node;
        foreach (var child in node.Children)
            IndexTree(child, map);
    }

    /// <summary>
    /// Point a parent's cached wrapper at its node in the current tree so its
    /// child list is up to date. Looked up in <paramref name="currentById"/> (the
    /// new tree) — never a diff-supplied parent, which may be a previous-tree
    /// snapshot. Skips parents no longer in the tree (their wrapper is gone too).
    /// </summary>
    private void RefreshParent(ElementId? parentId, Dictionary<ElementId, AccessibilityNode> currentById)
    {
        if (parentId is null) return;
        if (currentById.TryGetValue(parentId.Value, out var node)
            && _wrappers.TryGetValue(parentId.Value, out var wrapper))
            wrapper.UpdateFrom(node);
    }

    public void NotifyFocusChanged(AccessibilityNode? focused)
    {
        Volatile.Write(ref _focusedIdRaw, focused is null ? 0L : unchecked((long)focused.ElementId.Value));
        if (!RaiseEvents || focused is null) return;
        IntPtr prov = NativeFragmentSimple(focused.ElementId);
        if (prov == IntPtr.Zero) return;
        try { UiaInterop.UiaRaiseAutomationEvent(prov, UiaInterop.UIA_AutomationFocusChangedEventId); }
        finally { Marshal.Release(prov); }
    }

    private void RaiseStructureChanged(ElementId? parentId, int changeType)
    {
        if (!RaiseEvents) return;
        IntPtr prov = parentId is null ? NativeRootSimple() : NativeFragmentSimple(parentId.Value);
        if (prov == IntPtr.Zero) return;
        try { UiaInterop.UiaRaiseStructureChangedEvent(prov, changeType, IntPtr.Zero, 0); }
        finally { Marshal.Release(prov); }
    }

    private void RaisePropertyChanged(UiaFragment wrapper, string propertyName)
    {
        if (!RaiseEvents) return;
        int propertyId = propertyName switch
        {
            AccessibilityProperties.Name => UiaInterop.UIA_NamePropertyId,
            AccessibilityProperties.Description => UiaInterop.UIA_HelpTextPropertyId,
            AccessibilityProperties.Value => UiaInterop.UIA_ValueValuePropertyId,
            AccessibilityProperties.IsToggled => UiaInterop.UIA_ToggleToggleStatePropertyId,
            AccessibilityProperties.IsEnabled => UiaInterop.UIA_IsEnabledPropertyId,
            AccessibilityProperties.IsFocused => UiaInterop.UIA_HasKeyboardFocusPropertyId,
            AccessibilityProperties.BoundingRect => UiaInterop.UIA_BoundingRectanglePropertyId,
            _ => 0,
        };
        if (propertyId == 0) return;

        wrapper.GetPropertyValue(propertyId, out var newValue);
        var oldValue = UiaVariant.Empty;
        IntPtr prov = NativeFragmentSimple(wrapper.ElementId);
        if (prov == IntPtr.Zero) { newValue.Free(); return; }
        try { UiaInterop.UiaRaiseAutomationPropertyChangedEvent(prov, propertyId, oldValue, newValue); }
        finally { newValue.Free(); Marshal.Release(prov); }
    }

    // ── Window procedure: serve WM_GETOBJECT with the native root provider ──

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (!_byHwnd.TryGetValue(hwnd, out var self))
                return UiaInterop.DefWindowProc(hwnd, msg, wParam, lParam);

            if (msg == UiaInterop.WM_GETOBJECT && (int)lParam == UiaInterop.UiaRootObjectId)
            {
                IntPtr root = self.NativeRootSimple(); // UiaReturnRawElementProvider wants IRawElementProviderSimple*
                IntPtr lresult = UiaInterop.UiaReturnRawElementProvider(hwnd, wParam, lParam, root);
                Marshal.Release(root); // UIA took its own reference
                return lresult;
            }

            return UiaInterop.CallWindowProc(self._originalWndProc, hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HultUI] accessibility WndProc failed: {ex}");
            return UiaInterop.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }

    // ── Coordinate conversion ──

    // Element bounds are in density-independent layout units; UIA works in
    // physical screen pixels. Scale by the display scale (DPI) before/after the
    // client<->screen origin shift, which ClientToScreen does in physical pixels.
    internal Rect ToScreen(Rect layoutRect)
    {
        float s = _host.ContentScale;
        var origin = new UiaInterop.PointNative { X = (int)(layoutRect.X * s), Y = (int)(layoutRect.Y * s) };
        UiaInterop.ClientToScreen(_hwnd, ref origin);
        return new Rect(origin.X, origin.Y, layoutRect.Width * s, layoutRect.Height * s);
    }

    internal Point FromScreen(Point screen)
    {
        float s = _host.ContentScale;
        var origin = new UiaInterop.PointNative { X = 0, Y = 0 };
        UiaInterop.ClientToScreen(_hwnd, ref origin);
        if (s <= 0f) s = 1f;
        return new Point((screen.X - origin.X) / s, (screen.Y - origin.Y) / s);
    }

    // ── Actions routed back to the app ──

    internal void InvokeElement(ElementId id) => _host.InvokeElement(id);
    internal void ToggleElement(ElementId id) => _host.ToggleElement(id);
    internal void SelectElement(ElementId id) => _host.SelectElement(id);
    internal void SetValue(ElementId id, string value) => _host.SetValue(id, value);
    internal string? GetValue(ElementId id) => _host.GetValue(id);
    internal bool? GetToggleState(ElementId id) => _host.GetToggleState(id);
    internal void RequestFocus(ElementId id) => _host.RequestFocus(id);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_originalWndProc != IntPtr.Zero)
            UiaInterop.SetWindowLongPtr(_hwnd, UiaInterop.GWLP_WNDPROC, _originalWndProc);
        _byHwnd.TryRemove(_hwnd, out _);

        _wrappers.Clear();

        // Drop the bridge's references to cached native objects.
        lock (_nativeLock)
        {
            foreach (var native in _nativeByElement.Values)
                Marshal.Release(native);
            _nativeByElement.Clear();
            if (_rootNative != IntPtr.Zero) { Marshal.Release(_rootNative); _rootNative = IntPtr.Zero; }
        }

        if (_hostProviderPtr != IntPtr.Zero)
        {
            Marshal.Release(_hostProviderPtr);
            _hostProviderPtr = IntPtr.Zero;
        }
    }
}
