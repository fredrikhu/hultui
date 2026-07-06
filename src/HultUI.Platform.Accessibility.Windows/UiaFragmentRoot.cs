using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// Top-level fragment for a window. A UIA client reaches this through the
/// <c>WM_GETOBJECT</c> handler in <see cref="WindowsAccessibilityBridge"/>;
/// from here it can walk the tree, hit-test, and resolve focus.
/// </summary>
/// <remarks>
/// A fragment root must expose all three provider interfaces: UIA first
/// <c>QueryInterface</c>s the object returned from <c>WM_GETOBJECT</c> for
/// <see cref="IRawElementProviderSimple"/>, then walks it as an
/// <see cref="IRawElementProviderFragment"/>. This object is the window itself;
/// its single child is the app-root element node.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class UiaFragmentRoot(WindowsAccessibilityBridge bridge) :
    IRawElementProviderSimple,
    IRawElementProviderFragment,
    IRawElementProviderFragmentRoot
{
    // ──── IRawElementProviderSimple

    public int GetProviderOptions(out int pRetVal)
    {
        pRetVal = UiaInterop.ProviderOptions_ServerSideProvider | UiaInterop.ProviderOptions_UseComThreading;
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

    public int GetHostRawElementProvider(out IRawElementProviderSimple? pRetVal)
    {
        // The crucial link: this chains the provider tree to the HWND host
        // provider so UIA connects our fragments to the real window.
        pRetVal = bridge.HostProvider;
        return 0;
    }

    // ──── IRawElementProviderFragment

    public int Navigate(int direction, out IRawElementProviderFragment? pRetVal)
    {
        // The root's only child is the app-root element node; it has no parent
        // or siblings (the host provider supplies the chain above the window).
        pRetVal = direction switch
        {
            UiaInterop.NavigateDirection_FirstChild or UiaInterop.NavigateDirection_LastChild
                => bridge.GetFragment(bridge.RootNode?.ElementId),
            _ => null,
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
        // Empty tells UIA to fall back to the host provider's window rect.
        pRetVal = default;
        return 0;
    }

    public int GetEmbeddedFragmentRoots(out IntPtr pRetVal)
    {
        pRetVal = IntPtr.Zero;
        return 0;
    }

    public int SetFocus() => 0;

    public int GetFragmentRoot(out IRawElementProviderFragmentRoot? pRetVal)
    {
        pRetVal = this;
        return 0;
    }

    // ──── IRawElementProviderFragmentRoot

    public int ElementProviderFromPoint(double x, double y, out IRawElementProviderFragment? pRetVal)
    {
        var root = bridge.RootNode;
        if (root is null) { pRetVal = null; return 0; }

        // UIA gives us screen coords; convert back to window-local.
        var local = bridge.FromScreen(new Point((float)x, (float)y));

        // Walk deepest-first: return the innermost node whose bounds contain the point.
        var hit = HitTest(root, local);
        pRetVal = hit is null ? null : bridge.GetFragment(hit.ElementId);
        return 0;
    }

    public int GetFocus(out IRawElementProviderFragment? pRetVal)
    {
        var focusedId = bridge.FocusedNodeId;
        pRetVal = focusedId is null ? null : bridge.GetFragment(focusedId.Value);
        return 0;
    }

    private static AccessibilityNode? HitTest(AccessibilityNode node, Point localPoint)
    {
        if (!node.BoundingRect.Contains(localPoint)) return null;

        // Prefer deepest match: walk children back-to-front (later siblings paint on top).
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            var inner = HitTest(node.Children[i], localPoint);
            if (inner is not null) return inner;
        }
        return node;
    }
}
