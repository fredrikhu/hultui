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

    // Raising UIA events proactively is opt-in: the ComWrappers path used to
    // fast-fail, and the native-object path here hasn't been validated against a
    // real screen reader yet. The passive tree (served on WM_GETOBJECT) already
    // lets an assistive tech navigate/read on demand. HULTUI_UIA_EVENTS=1 to try.
    private static readonly bool RaiseEvents =
        Environment.GetEnvironmentVariable("HULTUI_UIA_EVENTS") == "1";

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

    /// <summary>The root as IRawElementProviderSimple* (WM_GETOBJECT, events).</summary>
    internal IntPtr NativeRootSimple() => NativeProvider.Create(_fragmentRoot, NativeProvider.As.Simple);

    /// <summary>The root as IRawElementProviderFragment* (a fragment's parent).</summary>
    internal IntPtr NativeRootFragment() => NativeProvider.Create(_fragmentRoot, NativeProvider.As.Fragment);

    /// <summary>The root as IRawElementProviderFragmentRoot* (GetFragmentRoot).</summary>
    internal IntPtr NativeRootFragmentRoot() => NativeProvider.Create(_fragmentRoot, NativeProvider.As.FragmentRoot);

    /// <summary>A cached fragment as IRawElementProviderFragment* (Navigate, hit-test, focus), or null.</summary>
    internal IntPtr NativeFragment(ElementId? id)
    {
        if (id is null || !_wrappers.TryGetValue(id.Value, out var wrapper)) return IntPtr.Zero;
        return NativeProvider.Create(wrapper, NativeProvider.As.Fragment);
    }

    /// <summary>A cached fragment as IRawElementProviderSimple* (events), or null.</summary>
    private IntPtr NativeFragmentSimple(ElementId? id)
    {
        if (id is null || !_wrappers.TryGetValue(id.Value, out var wrapper)) return IntPtr.Zero;
        return NativeProvider.Create(wrapper, NativeProvider.As.Simple);
    }

    /// <summary>The HWND host provider, AddRef'd (chains our tree to the window's non-client tree).</summary>
    internal IntPtr HostProviderRef()
    {
        if (_hostProviderPtr == IntPtr.Zero) return IntPtr.Zero;
        Marshal.AddRef(_hostProviderPtr);
        return _hostProviderPtr;
    }

    // ── Accessibility tree upkeep ──

    public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
    {
        // Always track the current full tree — ElementProviderFromPoint hit-tests
        // against it, and it must reflect deep changes even when the root node's
        // own properties didn't change.
        Volatile.Write(ref _rootNode, root);

        foreach (var added in diff.Added)
        {
            _wrappers[added.Node.ElementId] = new UiaFragment(this, added.Node);
            // The parent's child set changed: refresh its snapshot so Navigate
            // returns the new children (a persistent parent isn't in
            // PropertyChanged when only its children changed).
            RefreshParent(added.Parent);
            RaiseStructureChanged(added.Parent?.ElementId, UiaInterop.StructureChangeType_ChildrenBulkAdded);
        }

        foreach (var removed in diff.Removed)
        {
            _wrappers.TryRemove(removed.Node.ElementId, out _);
            RefreshParent(removed.Parent);
            RaiseStructureChanged(removed.Parent?.ElementId, UiaInterop.StructureChangeType_ChildrenBulkRemoved);
        }

        foreach (var reorderedParent in diff.Reordered)
        {
            RefreshParent(reorderedParent);
            RaiseStructureChanged(reorderedParent.ElementId, UiaInterop.StructureChangeType_ChildrenReordered);
        }

        foreach (var change in diff.PropertyChanged)
        {
            if (!_wrappers.TryGetValue(change.Node.ElementId, out var wrapper)) continue;
            wrapper.UpdateFrom(change.Node);
            RaisePropertyChanged(wrapper, change.PropertyName);
        }
    }

    /// <summary>Point a parent's cached wrapper at its new node so its child list is current.</summary>
    private void RefreshParent(AccessibilityNode? parent)
    {
        if (parent is not null && _wrappers.TryGetValue(parent.ElementId, out var wrapper))
            wrapper.UpdateFrom(parent);
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

    public void NotifyPropertyChanged(AccessibilityNode node, string propertyName)
    {
        if (!_wrappers.TryGetValue(node.ElementId, out var wrapper)) return;
        wrapper.UpdateFrom(node);
        RaisePropertyChanged(wrapper, propertyName);
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

        if (_hostProviderPtr != IntPtr.Zero)
        {
            Marshal.Release(_hostProviderPtr);
            _hostProviderPtr = IntPtr.Zero;
        }
    }
}
