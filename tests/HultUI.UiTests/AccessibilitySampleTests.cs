using System.Linq;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// Drives the Accessibility sample through FlaUI (a standard UI Automation
/// client) to prove HultUI's providers work with off-the-shelf automation
/// tooling — the same interfaces a screen reader uses. Elements are located by
/// the AutomationIds the sample sets.
/// </summary>
[Collection("ui")] // never run UI automation tests in parallel
public sealed class AccessibilitySampleTests : IDisposable
{
    private readonly Application _app;
    private readonly UIA3Automation _automation;
    private readonly Window _window;

    public AccessibilitySampleTests()
    {
        _app = Application.Launch(SamplePath.Accessibility());
        _automation = new UIA3Automation();
        _window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException("sample window did not appear");
    }

    private AutomationElement Find(string automationId) =>
        _window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
        ?? throw new InvalidOperationException($"no element with AutomationId '{automationId}'");

    [UiFact]
    public void Button_HasExpectedControlTypeAndName()
    {
        var button = Find("click-me");
        Assert.Equal(ControlType.Button, button.ControlType);
        Assert.Equal("Click Me", button.Name);
    }

    [UiFact]
    public void InvokingButton_UpdatesCountText()
    {
        Assert.Equal("Clicked 0 time(s)", Find("click-count").Name);

        Find("click-me").AsButton().Invoke();

        Retry.WhileFalse(
            () => Find("click-count").Name == "Clicked 1 time(s)",
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "count text did not update after Invoke");
    }

    [UiFact]
    public void TogglingCheckbox_FlipsToggleState()
    {
        var checkbox = Find("enable-thing").AsCheckBox();
        Assert.Equal(ToggleState.Off, checkbox.Patterns.Toggle.Pattern.ToggleState.Value);

        checkbox.Patterns.Toggle.Pattern.Toggle();

        Retry.WhileFalse(
            () => Find("enable-thing").AsCheckBox().Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On,
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "checkbox did not toggle On");
    }

    [UiFact]
    public void SettingTextInputValue_TakesEffect()
    {
        var input = Find("name-input");
        input.Patterns.Value.Pattern.SetValue("Fredrik");

        Retry.WhileFalse(
            () => Find("name-input").Patterns.Value.Pattern.Value.Value == "Fredrik",
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "text input value did not update");
    }

    [UiFact]
    public void Slider_ExposesRangeValueAndSetValueWorks()
    {
        var slider = Find("volume");
        Assert.Equal(ControlType.Slider, slider.ControlType);

        var range = slider.Patterns.RangeValue.Pattern;
        Assert.Equal(0.0, range.Value.Value, 0.5);
        Assert.Equal(100.0, range.Maximum.Value, 0.5);
        Assert.Equal(0.0, range.Minimum.Value, 0.5);

        range.SetValue(75);

        Retry.WhileFalse(
            () => Math.Abs(Find("volume").Patterns.RangeValue.Pattern.Value.Value - 75) < 0.5,
            timeout: TimeSpan.FromSeconds(5), throwOnTimeout: true, timeoutMessage: "slider value did not update");
        Retry.WhileFalse(
            () => Find("volume-value").Name == "75",
            timeout: TimeSpan.FromSeconds(5), throwOnTimeout: true, timeoutMessage: "displayed value did not update");
    }

    [UiFact]
    public void Expander_ReportsAndTogglesExpandCollapseState()
    {
        var expander = Find("expander");
        Assert.Equal(ExpandCollapseState.Collapsed,
            expander.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value);

        expander.Patterns.ExpandCollapse.Pattern.Expand();
        Retry.WhileFalse(
            () => Find("expander").Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Expanded,
            timeout: TimeSpan.FromSeconds(5), throwOnTimeout: true, timeoutMessage: "did not expand");

        Find("expander").Patterns.ExpandCollapse.Pattern.Collapse();
        Retry.WhileFalse(
            () => Find("expander").Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Collapsed,
            timeout: TimeSpan.FromSeconds(5), throwOnTimeout: true, timeoutMessage: "did not collapse");
    }

    [UiFact]
    public void MultiSelectList_ExposesContainerAndPerItemSelection()
    {
        var list = Find("toppings");
        var selection = list.Patterns.Selection.Pattern;
        Assert.True(selection.CanSelectMultiple.Value);
        Assert.Empty(selection.Selection.Value);

        Find("toppings").FindFirstChild(cf => cf.ByName("Cheese"))!
            .Patterns.SelectionItem.Pattern.AddToSelection();
        Find("toppings").FindFirstChild(cf => cf.ByName("Olives"))!
            .Patterns.SelectionItem.Pattern.AddToSelection();

        Retry.WhileFalse(
            () => Find("toppings").Patterns.Selection.Pattern.Selection.Value.Length == 2,
            timeout: TimeSpan.FromSeconds(5), throwOnTimeout: true,
            timeoutMessage: "container selection did not reflect two selected items");

        var names = Find("toppings").Patterns.Selection.Pattern.Selection.Value
            .Select(e => e.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Cheese", "Olives" }, names);

        // Removing one drops it from the container selection too.
        Find("toppings").FindFirstChild(cf => cf.ByName("Cheese"))!
            .Patterns.SelectionItem.Pattern.RemoveFromSelection();

        Retry.WhileFalse(
            () =>
            {
                var sel = Find("toppings").Patterns.Selection.Pattern.Selection.Value;
                return sel.Length == 1 && sel[0].Name == "Olives";
            },
            timeout: TimeSpan.FromSeconds(5), throwOnTimeout: true,
            timeoutMessage: "removing an item did not update the container selection");
    }

    [UiFact]
    public void ResizingWindow_UpdatesReportedBounds()
    {
        // Wait until the sample's deferred row load has fired (header flips to
        // "Commits"). That load is a structural change that rebuilds the whole
        // a11y tree; doing it first ensures the *only* thing that can refresh
        // bounds during the resize below is the layout-change path under test —
        // otherwise a coincidental structural rebuild could mask a regression.
        Retry.WhileNull(
            () => _window.FindFirstDescendant(cf => cf.ByName("Commits")),
            timeout: TimeSpan.FromSeconds(10), throwOnTimeout: true,
            timeoutMessage: "deferred commit load did not complete");

        // The close button is right-aligned in the title bar, so its screen X
        // tracks the window's right edge. Its UIA bounding rect must follow a
        // resize — a resize only marks layout (not structure) dirty, so this
        // guards the accessibility tree being refreshed on layout changes too.
        double originalX = Find("close-button").BoundingRectangle.X;

        var hwnd = _window.Properties.NativeWindowHandle.ValueOrDefault;
        Assert.NotEqual(IntPtr.Zero, hwnd);
        Assert.True(GetWindowRect(hwnd, out var rect), "GetWindowRect failed");
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        // Shrink the width by 200px from the right (SWP_NOMOVE keeps the top-left
        // corner fixed). The custom-frame window exposes no Transform pattern, so
        // drive the resize through Win32 directly.
        Assert.True(
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width - 200, height, SWP_NOMOVE | SWP_NOZORDER),
            "SetWindowPos failed");

        Retry.WhileFalse(
            () => Find("close-button").BoundingRectangle.X < originalX - 100,
            timeout: TimeSpan.FromSeconds(5), throwOnTimeout: true,
            timeoutMessage: "close button's reported bounds did not follow the window resize");
    }

    public void Dispose()
    {
        _automation.Dispose();
        try { _app.Close(); } catch { /* best effort */ }
        _app.Dispose();
    }

    // ── Win32 window resize (the custom frame has no UIA Transform pattern) ──

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}

[CollectionDefinition("ui", DisableParallelization = true)]
public sealed class UiCollection { }
