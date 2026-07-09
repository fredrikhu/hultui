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
    : INativeSimple, INativeFragment, INativeFragmentRoot
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
        pRetVal = hit is null ? IntPtr.Zero : bridge.NativeFragment(hit.ElementId);
        return 0;
    }

    public int GetFocus(out IntPtr pRetVal)
    {
        var focusedId = bridge.FocusedNodeId;
        pRetVal = focusedId is null ? IntPtr.Zero : bridge.NativeFragment(focusedId.Value);
        return 0;
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
