using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// Top-level fragment for a window. A UIA client reaches this through the
/// <c>WM_GETOBJECT</c> handler in <see cref="WindowsAccessibilityBridge"/>;
/// from here it can walk the tree, hit-test, and resolve focus.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaFragmentRoot(WindowsAccessibilityBridge bridge) : IRawElementProviderFragmentRoot
{
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
