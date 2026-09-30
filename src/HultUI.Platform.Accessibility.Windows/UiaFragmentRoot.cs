using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// Provider logic for the window/root. A UIA client reaches this through the
/// <c>WM_GETOBJECT</c> handler in <see cref="WindowsAccessibilityBridge"/>; from
/// here it can walk the tree, hit-test, and resolve focus. Exposed to UIA as a
/// native object implementing IRawElementProviderSimple / Fragment /
/// FragmentRoot (see <see cref="NativeProvider"/>). Methods that return another
/// provider hand back a native IUnknown* the caller owns.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaFragmentRoot(WindowsAccessibilityBridge bridge)
    : INativeSimple, INativeFragment, INativeFragmentRoot, INativeHwndOverride
{
    // ── IRawElementProviderSimple ──

    public int GetProviderOptions(out int pRetVal)
    {
        pRetVal = UiaInterop.ProviderOptions_ServerSideProvider;
        return 0;
    }

    public int GetPatternProvider(int patternId, out IntPtr pPatternProvider)
    {
        pPatternProvider = IntPtr.Zero; // the window root exposes no control patterns
        return 0;
    }

    public int GetPropertyValue(int propertyId, out UiaVariant pRetVal)
    {
        pRetVal = propertyId == UiaInterop.UIA_ControlTypePropertyId
            ? UiaVariant.FromInt(UiaInterop.UIA_WindowControlTypeId)
            : UiaVariant.Empty;
        return 0;
    }

    public int GetHostRawElementProvider(out IntPtr pRetVal)
    {
        // The crucial link: chains our tree to the HWND host provider so UIA
        // connects the fragments to the real window.
        pRetVal = bridge.HostProviderRef();
        return 0;
    }

    // ── IRawElementProviderFragment ──

    public int Navigate(int direction, out IntPtr pRetVal)
    {
        // The root's only child is the app-root element node; it has no parent
        // or siblings (the host provider supplies the chain above the window).
        pRetVal = direction switch
        {
            UiaInterop.NavigateDirection_FirstChild or UiaInterop.NavigateDirection_LastChild
                => bridge.NativeFragment(bridge.RootNode?.ElementId),
            _ => IntPtr.Zero,
        };
        return 0;
    }

    public int GetRuntimeId(out IntPtr pRetVal)
    {
        // The fragment root is identified by its HWND via host chaining, so a
        // null runtime id is the documented, correct value here.
        pRetVal = IntPtr.Zero;
        return 0;
    }

    public int GetBoundingRectangle(out UiaRect pRetVal)
    {
        pRetVal = default; // empty -> UIA falls back to the host provider's window rect
        return 0;
    }

    public int GetEmbeddedFragmentRoots(out IntPtr pRetVal)
    {
        pRetVal = IntPtr.Zero;
        return 0;
    }

    public int SetFocus() => 0;

    public int GetFragmentRoot(out IntPtr pRetVal)
    {
        pRetVal = bridge.NativeRootFragmentRoot();
        return 0;
    }

    // ── IRawElementProviderFragmentRoot ──

    public int ElementProviderFromPoint(double x, double y, out IntPtr pRetVal)
    {
        var root = bridge.RootNode;
        if (root is null) { pRetVal = IntPtr.Zero; return 0; }

        var local = bridge.FromScreen(new Point((float)x, (float)y));
        var hit = HitTest(root, local);
        if (hit is not null && hit.NativeContent.HitTestWindow != IntPtr.Zero)
        {
            // Over a foreign tree: name the window that can resolve the point, and UIA asks
            // its providers — the answer is the page's button, not our box around the page.
            // The window is not on screen where the page is drawn, so UIA cannot find it by
            // position itself; a provider that only stands for it is how we point there.
            var standIn = NativeProvider.Create(new UiaWindowStandIn(hit.NativeContent.HitTestWindow));
            pRetVal = NativeProvider.Tearoff(standIn, NativeProvider.As.Fragment);
            Marshal.Release(standIn); // hand the caller the tear-off's single reference
            return 0;
        }
        if (hit is not null && hit.IsVirtualItemContainer)
        {
            // The container has no child nodes; resolve the point to an item index.
            int idx = bridge.VirtualItemIndexAt(hit.ElementId, local);
            pRetVal = idx >= 0 ? bridge.NativeVirtualItem(hit.ElementId, idx) : bridge.NativeFragment(hit.ElementId);
            return 0;
        }
        pRetVal = hit is null ? IntPtr.Zero : bridge.NativeFragment(hit.ElementId);
        return 0;
    }

    public int GetFocus(out IntPtr pRetVal)
    {
        var focusedId = bridge.FocusedNodeId;
        pRetVal = focusedId is null ? IntPtr.Zero : bridge.NativeFragment(focusedId.Value);
        return 0;
    }

    // ── IRawElementProviderHwndOverride ──

    /// <summary>
    /// UI Automation reached a child window of ours and asks who stands for it. When it is a
    /// node's <see cref="AccessibilityNode.NativeContent"/>, that node does: UIA then joins
    /// the node with the window's own providers, so the window's tree hangs under the node
    /// instead of under the window root (provider repositioning). Otherwise nobody, and UIA's
    /// default provider for the window stays. Any thread.
    /// </summary>
    public int GetOverrideProviderForHwnd(IntPtr hwnd, out IntPtr pRetVal)
    {
        var node = bridge.RootNode is { } root ? HostOf(root, hwnd) : null;
        pRetVal = node is null ? IntPtr.Zero : bridge.NativeFragmentSimple(node.ElementId);
        return 0;
    }

    /// <summary>The node that stands for child window <paramref name="hwnd"/>, if any.</summary>
    private static AccessibilityNode? HostOf(AccessibilityNode node, IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && node.NativeContent.Window == hwnd) return node;
        foreach (var child in node.Children)
            if (HostOf(child, hwnd) is { } found) return found;
        return null;
    }

    private static AccessibilityNode? HitTest(AccessibilityNode node, Point localPoint)
    {
        if (!node.BoundingRect.Contains(localPoint)) return null;
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            var inner = HitTest(node.Children[i], localPoint);
            if (inner is not null) return inner;
        }
        return node;
    }
}
