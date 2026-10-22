using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// Driving the app from UI Automation without input: press a button, toggle a checkbox or a
/// switch, set a field, open a dropdown and pick from it, open a submenu, scroll a row into view.
/// Each pattern acts through the element's own handlers on the UI thread, as a click, a key or a
/// paste would: a switch's Toggle did nothing, a radio's Select left its group with two checked,
/// Expand on a dropdown did nothing (it opens on a press, not a click), SetValue wrote a read-only
/// text area and dropped the field's undo history, and there was no ScrollItem pattern at all.
/// </summary>
[SupportedOSPlatform("windows")]
public class UiaControlPatternTests
{
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    /// <summary>A headless window with the real UIA bridge over a test HWND, and its action router.</summary>
    private sealed class Rig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public HeadlessSession Session { get; } = HeadlessSession.Create(600, 400);
        public WindowsAccessibilityBridge Bridge { get; }

        public Rig()
        {
            var window = Session.Window.Window;
            var router = new AccessibilityActionRouter(window);
            var host = new UiaHostBridge(window, router);
            window.SetUiaHostBridge(host);
            window.SetActionRouter(router);
            Bridge = new WindowsAccessibilityBridge(_hwnd.Handle, host);
            Session.Window.InstallAccessibilityBridge(new Active(Bridge));
        }

        public void Mount(Element root)
        {
            Session.Window.SetRoot(root);
            Settle();
        }

        /// <summary>Frames enough for queued actions to run and the tree to be told again.</summary>
        public void Settle()
        {
            for (int i = 0; i < 3; i++) Session.PumpFrame(TimeSpan.FromMilliseconds(150));
        }

        /// <summary>The pattern a UIA client gets for <paramref name="id"/>, or null when it is not offered.</summary>
        public T? Pattern<T>(ElementId id, int patternId) where T : class
        {
            var native = Bridge.NativeFragment(id);
            Assert.NotEqual(IntPtr.Zero, native);
            try
            {
                var fragment = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native));
                fragment.GetPatternProvider(patternId, out var provider);
                if (provider == IntPtr.Zero) return null;
                try { return Assert.IsAssignableFrom<T>(NativeProvider.LogicOf(provider)); }
                finally { Marshal.Release(provider); }
            }
            finally { Marshal.Release(native); }
        }

        public AccessibilityNode Node(Func<AccessibilityNode, bool> match) =>
            Walk(Session.Window.AccessibilityTree).First(match);

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            _hwnd.Dispose();
        }
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var d in Walk(child))
                yield return d;
    }

    private static int ToggleState(INativeToggle toggle)
    {
        toggle.GetToggleState(out int state);
        return state;
    }

    [WindowsFact]
    public void Invoke_PressesAButton_AndADisabledOneRefusesIt()
    {
        using var rig = new Rig();
        int pressed = 0, disabledPressed = 0;
        var button = Button.New("Save").OnClick(_ => pressed++);
        var disabled = Button.New("Delete").OnClick(_ => disabledPressed++);
        disabled.IsEnabled = false;
        rig.Mount(Flex.Column().Children(button, disabled));

        rig.Pattern<INativeInvoke>(button.Id, UiaInterop.UIA_InvokePatternId)!.Invoke();
        rig.Pattern<INativeInvoke>(disabled.Id, UiaInterop.UIA_InvokePatternId)!.Invoke();
        Assert.Equal(0, pressed);   // queued for the UI thread, not run on the client's
        rig.Settle();

        Assert.Equal(1, pressed);
        Assert.Equal(0, disabledPressed);
    }

    [WindowsFact]
    public void Toggle_GoesThroughTheClick_ForACheckboxAndASwitch()
    {
        using var rig = new Rig();
        var changes = new List<string>();
        int clicks = 0;
        var checkbox = Checkbox.New(false, "Urgent");
        checkbox.OnChanged = on => changes.Add($"checkbox {on}");
        checkbox.On<ClickEvent>(_ => clicks++);   // what the app hung on the click runs too
        var toggle = ToggleSwitch.New(false, "Dark");
        toggle.OnChanged = on => changes.Add($"switch {on}");
        rig.Mount(Flex.Column().Children(checkbox, toggle));

        var switchPattern = rig.Pattern<INativeToggle>(toggle.Id, UiaInterop.UIA_TogglePatternId)!;
        Assert.Equal(UiaInterop.ToggleState_Off, ToggleState(switchPattern));   // was indeterminate

        rig.Pattern<INativeToggle>(checkbox.Id, UiaInterop.UIA_TogglePatternId)!.Toggle();
        switchPattern.Toggle();
        rig.Settle();

        Assert.Equal(["checkbox True", "switch True"], changes);
        Assert.Equal(1, clicks);
        Assert.True(checkbox.IsChecked);
        Assert.True(toggle.IsOn);
        Assert.Equal(UiaInterop.ToggleState_On, ToggleState(switchPattern));
    }

    [WindowsFact]
    public void SelectionItem_SelectsARadio_AndClearsTheRestOfItsGroup()
    {
        using var rig = new Rig();
        var first = RadioButton.New("size", true, "Small");
        var second = RadioButton.New("size", false, "Large");
        var told = new List<string>();
        first.OnChanged = on => told.Add($"small {on}");
        second.OnChanged = on => told.Add($"large {on}");
        rig.Mount(Flex.Column().Children(first, second));

        rig.Pattern<INativeSelectionItem>(second.Id, UiaInterop.UIA_SelectionItemPatternId)!.Select();
        rig.Settle();

        Assert.False(first.IsChecked);
        Assert.True(second.IsChecked);
        Assert.Equal(["small False", "large True"], told);
    }

    [WindowsFact]
    public void Value_SetsAField_AsOneEditCtrlZTakesBack()
    {
        using var rig = new Rig();
        var input = TextInput.New("draft");
        var told = new List<string>();
        input.TextChanged = told.Add;
        rig.Mount(Flex.Column().Children(input));

        var value = rig.Pattern<INativeValue>(input.Id, UiaInterop.UIA_ValuePatternId)!;
        value.GetIsReadOnly(out int readOnly);
        Assert.Equal(0, readOnly);
        var text = Marshal.StringToHGlobalUni("final");
        try { Assert.Equal(0, value.SetValue(text)); }
        finally { Marshal.FreeHGlobal(text); }
        rig.Settle();

        Assert.Equal("final", input.Text);
        Assert.Equal(["final"], told);
        Assert.True(input.Undo());
        Assert.Equal("draft", input.Text);
    }

    [WindowsFact]
    public void Value_IsRefused_ByAReadOnlyTextArea()
    {
        using var rig = new Rig();
        var area = TextArea.New("signed report");
        area.ReadOnly = true;
        rig.Mount(Flex.Column().Children(area));

        var value = rig.Pattern<INativeValue>(area.Id, UiaInterop.UIA_ValuePatternId)!;
        value.GetIsReadOnly(out int readOnly);
        Assert.Equal(1, readOnly);
        var text = Marshal.StringToHGlobalUni("changed");
        try { Assert.Equal(unchecked((int)0x80040200), value.SetValue(text)); }   // UIA_E_ELEMENTNOTENABLED
        finally { Marshal.FreeHGlobal(text); }
        rig.Settle();

        Assert.Equal("signed report", area.Text);
    }

    [WindowsFact]
    public void ExpandCollapse_OpensADropdown_AndSelectionItemPicksFromItsList()
    {
        using var rig = new Rig();
        var dropdown = Dropdown.New(["Alpha", "Beta", "Gamma"], 0);
        int fired = -1;
        dropdown.Changed(i => fired = i);
        rig.Mount(Box.New().Padding(new Thickness(40)).Children(dropdown));

        var expand = rig.Pattern<INativeExpandCollapse>(dropdown.Id, UiaInterop.UIA_ExpandCollapsePatternId)!;
        expand.GetExpandCollapseState(out int state);
        Assert.Equal(UiaInterop.ExpandCollapseState_Collapsed, state);

        expand.Expand();
        rig.Settle();
        Assert.True(dropdown.IsOpen);
        expand.GetExpandCollapseState(out state);
        Assert.Equal(UiaInterop.ExpandCollapseState_Expanded, state);

        // Its rows are list items: Select commits one, as a click on the row does.
        var beta = rig.Node(n => n.Role == AccessibilityRole.ListItem && n.Name == "Beta");
        rig.Pattern<INativeSelectionItem>(beta.ElementId, UiaInterop.UIA_SelectionItemPatternId)!.Select();
        rig.Settle();
        Assert.False(dropdown.IsOpen);
        Assert.Equal(1, dropdown.SelectedIndex);
        Assert.Equal(1, fired);

        // And Collapse closes it again without choosing.
        expand.Expand();
        rig.Settle();
        Assert.True(dropdown.IsOpen);
        expand.Collapse();
        rig.Settle();
        Assert.False(dropdown.IsOpen);
        Assert.Equal(1, dropdown.SelectedIndex);
    }

    [WindowsFact]
    public void ExpandCollapse_OpensAndClosesASubmenu()
    {
        using var rig = new Rig();
        bool ran = false;
        var target = Box.New().Width(120).Height(40).AccessibleName("Target");
        rig.Mount(Box.New().FlexGrow(1).Children(target));
        rig.Session.Window.Window.OpenContextMenu(
        [
            new ContextMenuItem { Label = "Copy" },
            new ContextMenuItem
            {
                Label = "Export",
                Children = [new ContextMenuItem { Label = "As PDF", Action = () => ran = true }],
            },
        ], new Point(60, 20));
        // A menu opened from code is told on the next frame: showing an overlay marked only paint
        // dirty, and with nothing else changing the menu never reached UI Automation.
        rig.Session.PumpFrame();

        var copy = rig.Node(n => n.Name == "Copy");
        Assert.Null(rig.Pattern<INativeExpandCollapse>(copy.ElementId, UiaInterop.UIA_ExpandCollapsePatternId));
        var export = rig.Node(n => n.Name == "Export");
        var expand = rig.Pattern<INativeExpandCollapse>(export.ElementId, UiaInterop.UIA_ExpandCollapsePatternId)!;
        expand.GetExpandCollapseState(out int state);
        Assert.Equal(UiaInterop.ExpandCollapseState_Collapsed, state);

        expand.Expand();
        rig.Settle();
        var pdf = rig.Node(n => n.Name == "As PDF");
        Assert.Equal(true, rig.Node(n => n.Name == "Export").Expanded);

        expand.Collapse();
        rig.Settle();
        Assert.DoesNotContain(Walk(rig.Session.Window.AccessibilityTree), n => n.Name == "As PDF");

        // Open again and invoke the item inside: it runs, as a click on it does.
        expand.Expand();
        rig.Settle();
        pdf = rig.Node(n => n.Name == "As PDF");
        rig.Pattern<INativeInvoke>(pdf.ElementId, UiaInterop.UIA_InvokePatternId)!.Invoke();
        rig.Settle();
        Assert.True(ran);
    }

    [WindowsFact]
    public void ScrollItem_ScrollsARowOfAScrollView_AndAListItem_IntoView()
    {
        using var rig = new Rig();
        var rows = Flex.Column();
        for (int i = 0; i < 60; i++) rows.AddChild(Button.New($"row {i}").Height(30));
        var scroll = ScrollView.New().Height(200).Children(rows);
        var list = VirtualListElement<string>.New().ItemHeight(24).Height(120)
            .RenderWith(t => TextB.New(t))
            .Items(Enumerable.Range(0, 100).Select(i => $"item {i}").ToList());
        rig.Mount(Flex.Row().Children(scroll.Width(200), list.Width(200)));

        var row = rows.Children[40];
        var scrollItem = rig.Pattern<INativeScrollItem>(row.Id, UiaInterop.UIA_ScrollItemPatternId)!;
        Assert.Equal(0f, scroll.ScrollOffsetY);
        scrollItem.ScrollIntoView();
        rig.Settle();
        var viewTop = scroll.Layout.AbsoluteContentBox.Y + scroll.ScrollOffsetY;
        Assert.InRange(row.Layout.AbsoluteBorderBox.Y, viewTop, viewTop + 200 - 30 + 0.5f);

        var item = VirtualItemId.For(list.Id, 80);
        rig.Pattern<INativeScrollItem>(item, UiaInterop.UIA_ScrollItemPatternId)!.ScrollIntoView();
        rig.Settle();
        Assert.InRange(list.ScrollOffsetY, 80 * 24 + 24 - 120 - 0.5f, 80 * 24 + 0.5f);

        // Something in no scroll view has nothing to scroll.
        Assert.Null(rig.Pattern<INativeScrollItem>(scroll.Id, UiaInterop.UIA_ScrollItemPatternId));
    }

    [WindowsFact]
    public void TheNativeObject_AnswersTheScrollItemInterface()
    {
        // What a real client does: QueryInterface the pattern object for IScrollItemProvider.
        var native = NativeProvider.Create(new UiaScrollItemProviderProbe());
        try
        {
            var iid = new Guid("2360c714-4bf1-4b26-ba65-9b21316127eb");
            Assert.Equal(0, Marshal.QueryInterface(native, in iid, out var scrollItem));
            Marshal.Release(scrollItem);
        }
        finally { Marshal.Release(native); }
    }

    private sealed class UiaScrollItemProviderProbe : INativeScrollItem
    {
        public int ScrollIntoView() => 0;
    }
}
