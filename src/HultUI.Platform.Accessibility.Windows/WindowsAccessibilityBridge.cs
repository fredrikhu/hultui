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

    public bool IsActive
    {
        get
        {
            bool listening = UiaInterop.UiaClientsAreListening();
            // Content that describes itself only while something listens (a terminal's screen
            // notices, a web page's tree) reads AccessibilityClients; the Linux bridge reports
            // there from the bus, and this one from UIA. Polled every frame, told on change.
            if (listening != _reportedListening)
            {
                _reportedListening = listening;
                AccessibilityClients.Report(listening);
            }
            return listening;
        }
    }

    private bool _reportedListening;

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

    /// <summary>
    /// A freshly built IRawElementProviderSimple* for a virtualized list item —
    /// the Simple-tear-off sibling of <see cref="NativeVirtualItem"/>, used where
    /// UIA wants an item as a plain provider (selection container membership,
    /// ItemContainer results). The returned pointer carries one reference.
    /// </summary>
    internal IntPtr NativeVirtualItemSimple(ElementId listId, int index)
    {
        IntPtr primary = NativeProvider.Create(new UiaVirtualItem(this, listId, index));
        if (primary == IntPtr.Zero) return IntPtr.Zero;
        IntPtr simple = NativeProvider.Tearoff(primary, NativeProvider.As.Simple);
        Marshal.Release(primary); // hand the caller the simple tear-off's single reference
        return simple;
    }

    /// <summary>
    /// The IRawElementProviderSimple* (AddRef'd) for item <paramref name="index"/>
    /// of a list — the same provider UIA navigation would yield, so its runtime id
    /// matches. A lazy list returns an on-demand virtual item; an eager list
    /// returns its real child node (returning a virtual item there would mint a
    /// different runtime id and break UIA identity). Null when out of range.
    /// </summary>
    internal IntPtr ItemSimpleProvider(ElementId listId, int index)
    {
        if (index < 0 || !_wrappers.TryGetValue(listId, out var w)) return IntPtr.Zero;
        var node = w.Node; // immutable per-frame snapshot
        if (node.IsVirtualItemContainer)
            return index < _host.GetItemCount(listId) ? NativeVirtualItemSimple(listId, index) : IntPtr.Zero;
        return index < node.Children.Count ? NativeFragmentSimple(node.Children[index].ElementId) : IntPtr.Zero;
    }

    /// <summary>The list's selected item index (-1 for none), for the Selection pattern.</summary>
    internal int GetSelectedIndex(ElementId listId) => _host.GetSelectedIndex(listId);

    /// <summary>All selected item indices of a list, ascending, for container Selection.</summary>
    internal IReadOnlyList<int> GetSelectedIndices(ElementId listId) => _host.GetSelectedIndices(listId);

    /// <summary>Whether a list allows multi-selection, for container Selection.</summary>
    internal bool GetCanSelectMultiple(ElementId listId) => _host.GetCanSelectMultiple(listId);

    /// <summary>
    /// Resolves an item provider's element id to its owning list and index — for
    /// both virtualized items (encoded ids) and eager list-item nodes (real nodes
    /// whose parent is a List). Returns false for anything that isn't a list item
    /// (e.g. a radio button, which shares the SelectionItem provider).
    /// </summary>
    private bool TryResolveItem(ElementId itemId, out ElementId listId, out int index)
    {
        // A virtual item of a list that has unmounted since decodes to no list: it is gone.
        if (VirtualItemId.TryDecode(itemId, out listId, out index))
            return listId != default;
        if (_wrappers.TryGetValue(itemId, out var w) && w.Node.Parent is { } parent
            && parent.Role == AccessibilityRole.List)
        {
            var kids = parent.Children;
            for (int i = 0; i < kids.Count; i++)
                if (kids[i].ElementId == itemId) { listId = parent.ElementId; index = i; return true; }
        }
        listId = default;
        index = -1;
        return false;
    }

    /// <summary>SelectionItem.Select on a list item — sole selection. False if not a list item.</summary>
    internal bool TrySelectItemOnly(ElementId itemId)
    {
        if (!TryResolveItem(itemId, out var listId, out var index)) return false;
        _host.SelectItemOnly(listId, index);
        return true;
    }

    /// <summary>SelectionItem.AddToSelection/RemoveFromSelection on a list item. False if not one.</summary>
    internal bool TrySetItemSelected(ElementId itemId, bool selected)
    {
        if (!TryResolveItem(itemId, out var listId, out var index)) return false;
        _host.SetItemSelected(listId, index, selected);
        return true;
    }

    /// <summary>Whether a list item is selected; false (not handled) if it isn't a list item.</summary>
    internal bool TryGetItemSelected(ElementId itemId, out bool selected)
    {
        if (TryResolveItem(itemId, out var listId, out var index))
        {
            selected = _host.GetItemSelected(listId, index);
            return true;
        }
        selected = false;
        return false;
    }

    /// <summary>The selection container (owning list, as Simple) of a list item, or null.</summary>
    internal IntPtr ItemSelectionContainer(ElementId itemId)
        => TryResolveItem(itemId, out var listId, out _) ? NativeFragmentSimple(listId) : IntPtr.Zero;

    /// <summary>
    /// Resolves an <c>ItemContainer.FindItemByProperty</c> <c>pStartAfter</c>
    /// provider back to its item index within <paramref name="listId"/>, or -1
    /// (search from the start) for null or an unrecognized provider.
    /// </summary>
    internal int DecodeItemIndex(ElementId listId, IntPtr startAfter)
    {
        switch (NativeProvider.LogicOf(startAfter))
        {
            case UiaVirtualItem vi when vi.ListId == listId:
                return vi.Index;
            case UiaFragment f:
                if (_wrappers.TryGetValue(listId, out var w))
                {
                    var kids = w.Node.Children;
                    for (int i = 0; i < kids.Count; i++)
                        if (kids[i].ElementId == f.ElementId) return i;
                }
                return -1;
            default:
                return -1;
        }
    }

    /// <summary>
    /// Finds the first item after <paramref name="startIndex"/> matching a UIA
    /// property (Name / IsSelected, or 0 for any) and returns its provider
    /// (AddRef'd Simple), or null. The lookup itself runs in the item source (via
    /// the host), so a source with an index can skip the linear scan — either way
    /// only the matched item is realized as a provider, never the whole list.
    /// </summary>
    internal IntPtr FindItemByProperty(ElementId listId, int startIndex, int propertyId, string? value)
    {
        AccessibleItemQuery query;
        switch (propertyId)
        {
            case 0:
                query = new AccessibleItemQuery(AccessibleItemProperty.Any);
                break;
            case UiaInterop.UIA_NamePropertyId:
                if (value is null) return IntPtr.Zero;
                query = new AccessibleItemQuery(AccessibleItemProperty.Name, value);
                break;
            case UiaInterop.UIA_SelectionItemIsSelectedPropertyId:
                query = new AccessibleItemQuery(AccessibleItemProperty.Selected);
                break;
            default:
                return IntPtr.Zero; // unsupported property — no match
        }
        int idx = _host.FindItemIndex(listId, startIndex, query);
        return idx >= 0 ? ItemSimpleProvider(listId, idx) : IntPtr.Zero;
    }

    internal int GetVirtualItemCount(ElementId listId) => _host.GetItemCount(listId);

    internal string? GetVirtualItemLabel(ElementId listId, int index) => _host.GetItemLabel(listId, index);


    internal bool GetVirtualItemOffscreen(ElementId listId, int index) => _host.GetItemOffscreen(listId, index);

    internal bool TryGetScrollInfo(ElementId id, out ScrollInfo info) => _host.TryGetScrollInfo(id, out info);
    internal void SetScrollPercent(ElementId id, double h, double v) => _host.SetScrollPercent(id, h, v);
    internal void ScrollByAmount(ElementId id, int h, int v) => _host.ScrollByAmount(id, h, v);

    internal bool TryGetRangeValue(ElementId id, out RangeValueInfo info) => _host.TryGetRangeValue(id, out info);
    internal void SetRangeValue(ElementId id, double value) => _host.SetRangeValue(id, value);

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
        //    and raise the matching UIA events — once per topmost changed parent.
        //    A parent that is itself part of the diff needs neither: its wrapper
        //    was already created from (or dropped with) the current snapshot,
        //    and UIA structure events are re-query hints on a parent, so the
        //    topmost boundary's single event covers the whole subtree. Without
        //    this, a bulk insert of N rows raised N identical events on the
        //    container plus one per grandchild — ~70k cross-process calls for a
        //    10k-row create, which profiled as half the operation.
        foreach (var parentId in diff.CoalescedAddedParents())
        {
            RefreshParent(parentId, currentById);
            RaiseStructureChanged(parentId, UiaInterop.StructureChangeType_ChildrenBulkAdded);
        }
        foreach (var parentId in diff.CoalescedRemovedParents())
        {
            RefreshParent(parentId, currentById);
            RaiseStructureChanged(parentId, UiaInterop.StructureChangeType_ChildrenBulkRemoved);
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

    /// <summary>
    /// A UIA notification (seam A2): the reader speaks the text without moving. From the
    /// announcing element's node, or the window's root when it has none. Polite queues behind
    /// what the reader is saying; assertive is "important", spoken at once.
    /// </summary>
    public void Announce(AccessibilityNode? source, string text, AnnouncementPoliteness politeness)
    {
        if (!RaiseEvents) return;
        IntPtr provider = source is not null ? NativeFragmentSimple(source.ElementId) : IntPtr.Zero;
        if (provider == IntPtr.Zero) provider = NativeRootSimple();
        if (provider == IntPtr.Zero) return;
        try
        {
            UiaInterop.RaiseNotification(provider, UiaInterop.NotificationKind_Other,
                politeness == AnnouncementPoliteness.Assertive
                    ? UiaInterop.NotificationProcessing_ImportantAll
                    : UiaInterop.NotificationProcessing_All,
                text, NotificationActivity);
        }
        finally { Marshal.Release(provider); }
    }

    /// <summary>What a reader is told the announcements are: one kind, so it can treat a run of
    /// them alike.</summary>
    private const string NotificationActivity = "HultUI.Announcement";

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
        if (propertyName is AccessibilityProperties.Text or AccessibilityProperties.TextSelection)
        {
            // Events, not property changes: a reader re-reads the text, or reads at the caret.
            IntPtr source = NativeFragmentSimple(wrapper.ElementId);
            if (source == IntPtr.Zero) return;
            try
            {
                UiaInterop.UiaRaiseAutomationEvent(source, propertyName == AccessibilityProperties.Text
                    ? UiaInterop.UIA_Text_TextChangedEventId : UiaInterop.UIA_Text_TextSelectionChangedEventId);
            }
            finally { Marshal.Release(source); }
            return;
        }
        if (propertyName == AccessibilityProperties.NativeContent)
        {
            // A foreign tree came or went. Its window moves between the window root and the
            // node, so both have different children now.
            RaiseStructureChanged(wrapper.ElementId, UiaInterop.StructureChangeType_ChildrenInvalidated);
            RaiseStructureChanged(null, UiaInterop.StructureChangeType_ChildrenInvalidated);
            return;
        }
        int propertyId = propertyName switch
        {
            AccessibilityProperties.Name => UiaInterop.UIA_NamePropertyId,
            AccessibilityProperties.Description => UiaInterop.UIA_HelpTextPropertyId,
            AccessibilityProperties.Value => UiaInterop.UIA_ValueValuePropertyId,
            AccessibilityProperties.IsToggled => UiaInterop.UIA_ToggleToggleStatePropertyId,
            AccessibilityProperties.IsExpanded => UiaInterop.UIA_ExpandCollapseExpandCollapseStatePropertyId,
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

    /// <summary>A node's text as it is now (seam A1), or null when it has none.</summary>
    internal AccessibleText? GetText(ElementId id) => _wrappers.TryGetValue(id, out var w) ? w.Text : null;

    /// <summary>A node's box in layout units, scroll applied.</summary>
    internal bool TryGetBounds(ElementId id, out Rect bounds)
    {
        bool found = _wrappers.TryGetValue(id, out var w);
        bounds = found ? w!.Bounds : default;
        return found;
    }

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
