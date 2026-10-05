using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Windows;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// What UI Automation is told a tab is. UIA's names run the other way from ARIA's: its
/// <c>Tab</c> control type is the strip that holds the tabs, and one tab is a <c>TabItem</c>
/// (W3C Core-AAM 1.1; Chromium maps them the same). A single tab was reported as <c>Tab</c>,
/// so Narrator read each one as a whole tab control.
/// </summary>
[SupportedOSPlatform("windows")]
public class TabRolesUiaTests
{
    private const int ControlType = 30003;

    [WindowsTheory]
    [InlineData(AccessibilityRole.Tab, UiaInterop.UIA_TabItemControlTypeId)]
    [InlineData(AccessibilityRole.TabPanel, UiaInterop.UIA_PaneControlTypeId)]
    [InlineData(AccessibilityRole.TabList, UiaInterop.UIA_TabControlTypeId)]
    public void ATabIsATabItem_AndItsPanelAPane(AccessibilityRole role, int controlType)
    {
        using var hwnd = TestHwnd.Create();
        using var host = HandleWindowHost.Create(hwnd.Handle);
        var bridge = Assert.IsType<WindowsAccessibilityBridge>(AccessibilityBridgeFactory.Create(host.Application.PrimaryWindow));
        try
        {
            var app = new AccessibilityNode(ElementId.New(), AccessibilityRole.Application, null, null, new Rect(0, 0, 200, 100));
            var node = new AccessibilityNode(ElementId.New(), role, "General", null, new Rect(0, 0, 80, 24));
            app.AddChild(node);
            bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(null, app), app);

            var native = bridge.NativeFragment(node.ElementId);
            UiaFragment fragment;
            try { fragment = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
            finally { Marshal.Release(native); }

            Assert.Equal(0, fragment.GetPropertyValue(ControlType, out var type));
            Assert.Equal(controlType, type.IntValue);
        }
        finally { bridge.Dispose(); }
    }
}
