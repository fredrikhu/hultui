using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// UIAutomation bridge. Built by
/// <c>HultUI.AccessibilityBridgeFactory</c> when running on Windows and an
/// assistive tech is probing. Owns a per-window fragment root and a cache of
/// per-node <see cref="UiaFragment"/> wrappers keyed by
/// <see cref="ElementId"/>.
/// </summary>
/// <remarks>
/// <para>
/// Client/host hooks — installing an SDL message hook for <c>WM_GETOBJECT</c>,
/// routing element mutations back to the UI thread — are provided by the host
/// via the constructor. The bridge is transport-agnostic so it can later be
/// shared with a non-SDL host without changes.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsAccessibilityBridge : IAccessibilityBridge, IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly IUiaHostBridge _host;
    private readonly UiaFragmentRoot _fragmentRoot;
    private readonly Dictionary<ElementId, UiaFragment> _wrappers = [];
    private IRawElementProviderSimple? _hostProvider;
    private AccessibilityNode? _rootNode;
    private ElementId? _focusedNodeId;
    private bool _disposed;

    public WindowsAccessibilityBridge(IntPtr hwnd, IUiaHostBridge host)
    {
        _hwnd = hwnd;
        _host = host;
        _fragmentRoot = new UiaFragmentRoot(this);

        UiaInterop.UiaHostProviderFromHwnd(hwnd, out var hostProviderPtr);
        if (hostProviderPtr != IntPtr.Zero)
        {
            _hostProvider = (IRawElementProviderSimple)UiaComWrappers.GetObject(hostProviderPtr);
            Marshal.Release(hostProviderPtr);
        }

        _host.InstallMessageHook(OnWindowMessage);
    }

    public bool IsActive => UiaInterop.UiaClientsAreListening();

    internal IRawElementProviderSimple? HostProvider => _hostProvider;
    internal UiaFragmentRoot FragmentRoot => _fragmentRoot;
    internal AccessibilityNode? RootNode => _rootNode;
    internal ElementId? FocusedNodeId => _focusedNodeId;

    internal UiaFragment? GetFragment(ElementId? id)
    {
        if (id is null) return null;
        return _wrappers.TryGetValue(id.Value, out var f) ? f : null;
    }

    public void ApplyChanges(TreeDiff diff)
    {
        // Track root separately — it's the only fragment with a null parent and
        // the one that chains to the host provider.
        foreach (var added in diff.Added)
        {
            var wrapper = new UiaFragment(this, added.Node);
            _wrappers[added.Node.ElementId] = wrapper;
            if (added.Parent is null)
                _rootNode = added.Node;

            // UIA wants a ChildAdded event raised on the PARENT provider.
            RaiseStructureChanged(added.Parent?.ElementId, UiaInterop.StructureChangeType_ChildAdded);
        }

        foreach (var removed in diff.Removed)
        {
            if (_wrappers.TryGetValue(removed.Node.ElementId, out var wrapper))
            {
                IntPtr punk = UiaComWrappers.GetIUnknown(wrapper);
                try { UiaInterop.UiaDisconnectProvider(punk); }
                finally { Marshal.Release(punk); }
                _wrappers.Remove(removed.Node.ElementId);
            }
            RaiseStructureChanged(removed.Parent?.ElementId, UiaInterop.StructureChangeType_ChildRemoved);
        }

        foreach (var reorderedParent in diff.Reordered)
            RaiseStructureChanged(reorderedParent.ElementId, UiaInterop.StructureChangeType_ChildrenReordered);

        foreach (var change in diff.PropertyChanged)
        {
            if (!_wrappers.TryGetValue(change.Node.ElementId, out var wrapper)) continue;
            wrapper.UpdateFrom(change.Node);
            RaisePropertyChanged(wrapper, change.PropertyName);
        }
    }

    public void NotifyFocusChanged(AccessibilityNode? focused)
    {
        _focusedNodeId = focused?.ElementId;
        if (focused is null) return;
        if (!_wrappers.TryGetValue(focused.ElementId, out var wrapper)) return;

        IntPtr punk = UiaComWrappers.GetIUnknown(wrapper);
        try { UiaInterop.UiaRaiseAutomationEvent(punk, UiaInterop.UIA_AutomationFocusChangedEventId); }
        finally { Marshal.Release(punk); }
    }

    public void NotifyPropertyChanged(AccessibilityNode node, string propertyName)
    {
        if (!_wrappers.TryGetValue(node.ElementId, out var wrapper)) return;
        wrapper.UpdateFrom(node);
        RaisePropertyChanged(wrapper, propertyName);
    }

    private void RaiseStructureChanged(ElementId? parentId, int changeType)
    {
        var provider = parentId is null
            ? (object)_fragmentRoot
            : GetFragment(parentId.Value);
        if (provider is null) return;

        IntPtr punk = UiaComWrappers.GetIUnknown(provider);
        try { UiaInterop.UiaRaiseStructureChangedEvent(punk, changeType, IntPtr.Zero, 0); }
        finally { Marshal.Release(punk); }
    }

    private static void RaisePropertyChanged(UiaFragment wrapper, string propertyName)
    {
        // Map the neutral property name onto a UIA property ID. Unmapped
        // properties are silently ignored (fine for this release — we add
        // them as we wire more).
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

        IntPtr punk = UiaComWrappers.GetIUnknown(wrapper);
        try { UiaInterop.UiaRaiseAutomationPropertyChangedEvent(punk, propertyId, IntPtr.Zero, IntPtr.Zero); }
        finally { Marshal.Release(punk); }
    }

    /// <summary>
    /// Invoked by the host's SDL message hook when a <c>WM_GETOBJECT</c>
    /// arrives on our HWND. Returning true tells the host the message was
    /// handled.
    /// </summary>
    private bool OnWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (hwnd != _hwnd) return false;
        if (message != 0x003D /* WM_GETOBJECT */) return false;
        if (lParam.ToInt32() != UiaInterop.UiaRootObjectId) return false;

        IntPtr rootPunk = UiaComWrappers.GetIUnknown(_fragmentRoot);
        try { UiaInterop.UiaReturnRawElementProvider(hwnd, wParam, lParam, rootPunk); }
        finally { Marshal.Release(rootPunk); }
        return true;
    }

    // ──── Coordinate conversion

    internal Rect ToScreen(Rect windowLocal)
    {
        var origin = new UiaInterop.PointNative { X = (int)windowLocal.X, Y = (int)windowLocal.Y };
        UiaInterop.ClientToScreen(_hwnd, ref origin);
        return new Rect(origin.X, origin.Y, windowLocal.Width, windowLocal.Height);
    }

    internal Point FromScreen(Point screen)
    {
        // Reverse of ToScreen. Client-to-screen offset is additive, so compute
        // the origin in screen coords once and subtract.
        var origin = new UiaInterop.PointNative { X = 0, Y = 0 };
        UiaInterop.ClientToScreen(_hwnd, ref origin);
        return new Point(screen.X - origin.X, screen.Y - origin.Y);
    }

    // ──── Actions routed back to the app

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
        _host.UninstallMessageHook();

        foreach (var wrapper in _wrappers.Values)
        {
            IntPtr punk = UiaComWrappers.GetIUnknown(wrapper);
            try { UiaInterop.UiaDisconnectProvider(punk); }
            finally { Marshal.Release(punk); }
        }
        _wrappers.Clear();

        if (_hostProvider is not null)
        {
            IntPtr punk = UiaComWrappers.GetIUnknown(_hostProvider);
            try { UiaInterop.UiaDisconnectProvider(punk); }
            finally { Marshal.Release(punk); }
            _hostProvider = null;
        }
    }
}
