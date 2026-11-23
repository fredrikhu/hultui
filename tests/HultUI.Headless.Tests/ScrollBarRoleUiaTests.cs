using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Windows;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A scrollbar standing on its own (<see cref="ScrollbarElement"/>) is a UIA ScrollBar with the
/// RangeValue pattern, as a native one is: a reader hears its kind and its position, and can set it.
/// </summary>
[SupportedOSPlatform("windows")]
public class ScrollBarRoleUiaTests
{
    private const int ControlType = 30003;

    [WindowsFact]
    public void AScrollBar_IsAScrollBarControl_WithARangeValueProvider()
    {
        using var hwnd = TestHwnd.Create();
        using var host = HandleWindowHost.Create(hwnd.Handle);
        var bridge = Assert.IsType<WindowsAccessibilityBridge>(AccessibilityBridgeFactory.Create(host.Application.PrimaryWindow));
        try
        {
            var app = new AccessibilityNode(ElementId.New(), AccessibilityRole.Application, null, null, new Rect(0, 0, 200, 300));
            var node = new AccessibilityNode(ElementId.New(), AccessibilityRole.ScrollBar, null, null, new Rect(188, 0, 12, 300))
            {
                Range = new AccessibleRange(25, 0, 900, 1, 100, false),
            };
            app.AddChild(node);
            bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(null, app), app);

            var native = bridge.NativeFragment(node.ElementId);
            try
            {
                var fragment = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native));
                Assert.Equal(0, fragment.GetPropertyValue(ControlType, out var type));
                Assert.Equal(UiaInterop.UIA_ScrollBarControlTypeId, type.IntValue);

                fragment.GetPatternProvider(UiaInterop.UIA_RangeValuePatternId, out var pattern);
                Assert.NotEqual(IntPtr.Zero, pattern);
                try { Assert.IsType<UiaRangeValueProvider>(NativeProvider.LogicOf(pattern)); }
                finally { Marshal.Release(pattern); }
            }
            finally { Marshal.Release(native); }
        }
        finally { bridge.Dispose(); }
    }
}
