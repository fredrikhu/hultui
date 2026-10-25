#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// What a UI Automation client reads through a pattern — a text input's value, a toggle, a range, a
/// list item's selected state, a list's selection, a scroll view's percents, a large list's items —
/// answers from the element's node in the tree the bridge applied (plan 17, round 4). Each read
/// went to the host, which found the element with a walk of the live element tree on the client's
/// thread, unsynchronized with the UI thread that changes it: on a 1,533-element page a check
/// box's toggle visited 1,532 elements. And a pattern read the element as it is now while the same
/// property read the node, so the RangeValue pattern and its Value property disagreed until the
/// range's coalesced refresh, and a switch's Toggle pattern said indeterminate.
/// </summary>
[SupportedOSPlatform("windows")]
public class UiaReadsFromNodeTests
{
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    /// <summary>The window's host, counting the reads the bridge asks of it: each one found its
    /// element with a walk of the live element tree (<c>Window.FindElement</c>).</summary>
    private sealed class CountingHost(IUiaHostBridge inner) : IUiaHostBridge
    {
        private int _reads;
        public int Reads => Volatile.Read(ref _reads);
        private T Read<T>(T value) { Interlocked.Increment(ref _reads); return value; }

        public void InvokeElement(ElementId id) => inner.InvokeElement(id);
        public void ToggleElement(ElementId id) => inner.ToggleElement(id);
        public void SelectElement(ElementId id) => inner.SelectElement(id);
        public void SetValue(ElementId id, string value) => inner.SetValue(id, value);
        public void RequestFocus(ElementId id) => inner.RequestFocus(id);
        public float ContentScale => inner.ContentScale;
        public void RootRequested() => inner.RootRequested();
        public void SelectItemOnly(ElementId listId, int index) => inner.SelectItemOnly(listId, index);
        public void SetItemSelected(ElementId listId, int index, bool selected) => inner.SetItemSelected(listId, index, selected);
        public void SetScrollPercent(ElementId id, double h, double v) => inner.SetScrollPercent(id, h, v);
        public void ScrollByAmount(ElementId id, int h, int v) => inner.ScrollByAmount(id, h, v);
        public void SetRangeValue(ElementId id, double value) => inner.SetRangeValue(id, value);

        public string? GetValue(ElementId id) => Read(inner.GetValue(id));
        public bool? GetToggleState(ElementId id) => Read(inner.GetToggleState(id));
        public int GetItemCount(ElementId listId) => Read(inner.GetItemCount(listId));
        public string? GetItemLabel(ElementId listId, int index) => Read(inner.GetItemLabel(listId, index));
        public bool GetItemSelected(ElementId listId, int index) => Read(inner.GetItemSelected(listId, index));
        public int GetSelectedIndex(ElementId listId) => Read(inner.GetSelectedIndex(listId));
        public bool GetCanSelectMultiple(ElementId listId) => Read(inner.GetCanSelectMultiple(listId));
        public IReadOnlyList<int> GetSelectedIndices(ElementId listId) => Read(inner.GetSelectedIndices(listId));
        public int FindItemIndex(ElementId listId, int startAfter, AccessibleItemQuery query) => Read(inner.FindItemIndex(listId, startAfter, query));
        public bool GetItemOffscreen(ElementId listId, int index) => Read(inner.GetItemOffscreen(listId, index));
        public bool TryGetItemLayout(ElementId listId, int index, out Rect layoutRect) => Read(inner.TryGetItemLayout(listId, index, out layoutRect));
        public bool TryGetScrollInfo(ElementId id, out ScrollInfo info) => Read(inner.TryGetScrollInfo(id, out info));
        public bool TryGetRangeValue(ElementId id, out RangeValueInfo info) => Read(inner.TryGetRangeValue(id, out info));
    }

    private sealed class Rig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public HeadlessSession Session { get; } = HeadlessSession.Create(800, 900);
        public WindowsAccessibilityBridge Bridge { get; }
        public UiaHostBridge Host { get; }
        public CountingHost Counting { get; }

        public Rig()
        {
            var window = Session.Window.Window;
            var router = new AccessibilityActionRouter(window);
            Host = new UiaHostBridge(window, router);
            window.SetUiaHostBridge(Host);
            window.SetActionRouter(router);
            Counting = new CountingHost(Host);
            Bridge = new WindowsAccessibilityBridge(_hwnd.Handle, Counting);
            Session.Window.InstallAccessibilityBridge(new Active(Bridge));
        }

        /// <summary>The logic of pattern <paramref name="patternId"/> of the element's provider.</summary>
        public T Pattern<T>(ElementId id, int patternId) where T : class
        {
            var native = Bridge.NativeFragment(id);
            Assert.NotEqual(IntPtr.Zero, native);
            try
            {
                var wrapper = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native));
                wrapper.GetPatternProvider(patternId, out var pattern);
                Assert.NotEqual(IntPtr.Zero, pattern);
                try { return Assert.IsType<T>(NativeProvider.LogicOf(pattern)); }
                finally { Marshal.Release(pattern); }
            }
            finally { Marshal.Release(native); }
        }

        /// <summary>What the element's provider answers for property <paramref name="propertyId"/>.</summary>
        public object? Property(ElementId id, int propertyId)
        {
            var native = Bridge.NativeFragment(id);
            Assert.NotEqual(IntPtr.Zero, native);
            try
            {
                var wrapper = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native));
                wrapper.GetPropertyValue(propertyId, out var v);
                try
                {
                    return v.Vt switch
                    {
                        UiaVariant.VT_BSTR => v.AsString(),
                        UiaVariant.VT_R8 => v.DoubleValue,
                        UiaVariant.VT_I4 => v.IntValue,
                        UiaVariant.VT_BOOL => v.ShortValue != 0,
                        _ => null,
                    };
                }
                finally { v.Free(); }
            }
            finally { Marshal.Release(native); }
        }

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            _hwnd.Dispose();
        }
    }

    private static List<string> Items(int count, string stem) => [.. Enumerable.Range(0, count).Select(i => $"{stem} {i}")];

    private static VirtualListElement<string> List(List<string> items, float height) =>
        VirtualListElement<string>.New().ItemHeight(24)
            .RenderWith(t => Flex.Row().Padding(new Thickness(2, 8)).Children(TextB.New(t).FontSize(12f)))
            .Items(items).Height(Dimension.Px(height));

    /// <summary>The node of <paramref name="id"/> in the tree the bridge applied.</summary>
    private static AccessibilityNode NodeOf(Rig rig, ElementId id)
    {
        static AccessibilityNode? Find(AccessibilityNode n, ElementId id)
        {
            if (n.ElementId == id) return n;
            foreach (var c in n.Children) if (Find(c, id) is { } f) return f;
            return null;
        }
        return Find(rig.Bridge.RootNode!, id) ?? throw new InvalidOperationException("no node");
    }

    [WindowsFact]
    public void EveryPatternRead_AnswersFromTheNode_WithNoWalkOfTheLiveTree_OnAnyThread()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var rig = new Rig();
            var rows = new Element[400];
            for (int i = 0; i < rows.Length; i++) rows[i] = TextB.New($"row {i}").FontSize(12f).Height(Dimension.Px(18));
            var scroll = ScrollView.New().Height(Dimension.Px(200)).Children(Flex.Column().FlexShrink(0).Children(rows));
            var input = TextInput.New("seed").Width(Dimension.Px(200));
            var check = Checkbox.New(true, "Check");
            var toggle = ToggleSwitch.New(true, "Switch").Width(Dimension.Px(160));
            var slider = Slider.New(30, 0, 100).Width(Dimension.Px(200));
            var small = List(Items(20, "item"), 120);
            var big = List(Items(3000, "commit"), 120);
            rig.Session.Window.SetRoot(Flex.Column(gap: 4).Children(scroll, input, check, toggle, slider, small, big));
            small.SelectedIndex = 3;
            big.SelectedIndex = 7;
            scroll.ScrollOffsetY = 300;
            big.ScrollOffsetY = 24 * 100;
            for (int f = 0; f < 4; f++) rig.Session.PumpFrame(TimeSpan.FromMilliseconds(300));

            var smallNode = NodeOf(rig, small.Id);
            Assert.False(smallNode.IsVirtualItemContainer);
            Assert.True(NodeOf(rig, big.Id).IsVirtualItemContainer);
            var item3 = smallNode.Children[3].ElementId;
            var value = rig.Pattern<UiaValueProvider>(input.Id, UiaInterop.UIA_ValuePatternId);
            var checkToggle = rig.Pattern<UiaToggleProvider>(check.Id, UiaInterop.UIA_TogglePatternId);
            var switchToggle = rig.Pattern<UiaToggleProvider>(toggle.Id, UiaInterop.UIA_TogglePatternId);
            var range = rig.Pattern<UiaRangeValueProvider>(slider.Id, UiaInterop.UIA_RangeValuePatternId);
            var item = rig.Pattern<UiaSelectionItemProvider>(item3, UiaInterop.UIA_SelectionItemPatternId);
            var scrolls = rig.Pattern<UiaScrollProvider>(scroll.Id, UiaInterop.UIA_ScrollPatternId);
            var bigScroll = rig.Pattern<UiaScrollProvider>(big.Id, UiaInterop.UIA_ScrollPatternId);
            int reads = rig.Counting.Reads;

            // On another thread, as a client's calls come.
            static string D(double d) => Math.Round(d, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var answers = Task.Run(() =>
            {
                var a = new List<string>();
                value.GetValue(out var bstr);
                a.Add(Marshal.PtrToStringBSTR(bstr));
                Marshal.FreeBSTR(bstr);
                checkToggle.GetToggleState(out int c);
                switchToggle.GetToggleState(out int t);
                a.Add($"toggles {c} {t}");
                range.GetValue(out double rv); range.GetMinimum(out double rmin); range.GetMaximum(out double rmax);
                range.GetSmallChange(out double sc); range.GetLargeChange(out double lc); range.GetIsReadOnly(out int ro);
                a.Add($"range {D(rv)} {D(rmin)} {D(rmax)} {D(sc)} {D(lc)} {ro}");
                item.GetIsSelected(out int sel);
                a.Add($"item 3 selected {sel}");
                a.Add($"selection {string.Join(',', rig.Bridge.GetSelectedIndices(small.Id))} / {string.Join(',', rig.Bridge.GetSelectedIndices(big.Id))}, multiple {rig.Bridge.GetCanSelectMultiple(big.Id)}");
                rig.Bridge.TryGetItemSelected(VirtualItemId.For(big.Id, 7), out bool vsel);
                a.Add($"on demand: {rig.Bridge.GetVirtualItemCount(big.Id)} items, 42 {rig.Bridge.GetVirtualItemLabel(big.Id, 42)}, 7 selected {vsel}, "
                    + $"offscreen 0 {rig.Bridge.GetVirtualItemOffscreen(big.Id, 0)} 101 {rig.Bridge.GetVirtualItemOffscreen(big.Id, 101)}");
                scrolls.GetVerticalScrollPercent(out double vp); scrolls.GetVerticalViewSize(out double vv);
                scrolls.GetVerticallyScrollable(out int vs); scrolls.GetHorizontallyScrollable(out int hs);
                bigScroll.GetVerticalScrollPercent(out double bp);
                a.Add($"scroll {D(vp)} {D(vv)} {vs} {hs} / {D(bp)}");
                var found = rig.Bridge.FindItemByProperty(small.Id, -1, UiaInterop.UIA_NamePropertyId, "item 5");
                var selected = rig.Bridge.FindItemByProperty(small.Id, -1, UiaInterop.UIA_SelectionItemIsSelectedPropertyId, null);
                var inBig = rig.Bridge.FindItemByProperty(big.Id, -1, UiaInterop.UIA_NamePropertyId, "commit 42");
                a.Add($"found {(NativeProvider.LogicOf(found) as UiaFragment)?.Node.Name}, {(NativeProvider.LogicOf(selected) as UiaFragment)?.Node.Name}, "
                    + $"{(NativeProvider.LogicOf(inBig) as UiaVirtualItem)?.Index}");
                Marshal.Release(found);
                Marshal.Release(selected);
                Marshal.Release(inBig);
                return a;
            }).Result;

            Assert.Equal(0, rig.Counting.Reads - reads);
            // And the answers are what the elements say, the frames having told everything.
            var r = (IRangeValueElement)slider;
            rig.Host.TryGetScrollInfo(scroll.Id, out var live);
            rig.Host.TryGetScrollInfo(big.Id, out var liveBig);
            Assert.Equal(
            [
                "seed",
                $"toggles {UiaInterop.ToggleState_On} {UiaInterop.ToggleState_On}",
                $"range 30 0 100 {D(r.SmallChange)} {D(r.LargeChange)} 0",
                "item 3 selected -1",
                "selection 3 / 7, multiple False",
                "on demand: 3000 items, 42 commit 42, 7 selected True, offscreen 0 True 101 False",
                $"scroll {D(live.VerticalPercent)} {D(live.VerticalViewSize)} -1 0 / {D(liveBig.VerticalPercent)}",
                "found item 5, item 3, 42",
            ], answers);
        }
        finally { AccessibilityClients.Force = null; }
    }

    [WindowsFact]
    public void AnOnDemandItem_IsWhereTheHostPutIt_FromTheListsNode()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var rig = new Rig();
            var big = List(Items(3000, "commit"), 160);
            var spacer = Box.New().Height(Dimension.Px(37));
            rig.Session.Window.SetRoot(Flex.Column().Padding(new Thickness(5, 11)).Children(spacer, big));
            big.ScrollOffsetY = 24 * 50 + 7;
            for (int f = 0; f < 4; f++) rig.Session.PumpFrame(TimeSpan.FromMilliseconds(300));

            int reads = rig.Counting.Reads;
            foreach (int i in new[] { 0, 49, 50, 51, 55, 56, 57, 2999 })
            {
                Assert.True(rig.Host.TryGetItemLayout(big.Id, i, out var live));
                Assert.True(rig.Bridge.TryGetVirtualItemScreenRect(big.Id, i, out var screen));
                Assert.Equal(rig.Bridge.ToScreen(live), screen);
                Assert.Equal(rig.Host.GetItemOffscreen(big.Id, i), rig.Bridge.GetVirtualItemOffscreen(big.Id, i));
            }
            Assert.False(rig.Bridge.TryGetVirtualItemScreenRect(big.Id, 3000, out _));
            Assert.True(rig.Bridge.GetVirtualItemOffscreen(big.Id, 3000));
            // The host's reads above are the test's own; the bridge asked it for none.
            Assert.Equal(0, rig.Counting.Reads - reads);
        }
        finally { AccessibilityClients.Force = null; }
    }

    [WindowsFact]
    public void TheRangeValuePattern_AnswersAsItsProperty_InsideTheCoalescingWindow()
    {
        // A range is told on the leading edge, then at most every 250 ms: between, the property
        // answers the value told and the pattern answered the slider as it is now.
        AccessibilityClients.Force = true;
        try
        {
            using var rig = new Rig();
            var slider = Slider.New(30, 0, 100).Width(Dimension.Px(200));
            rig.Session.Window.SetRoot(Flex.Column().Padding(8).Children(slider));
            rig.Session.PumpFrames(2);
            rig.Session.PumpFrame(TimeSpan.FromMilliseconds(400));
            var range = rig.Pattern<UiaRangeValueProvider>(slider.Id, UiaInterop.UIA_RangeValuePatternId);
            double Pattern() { range.GetValue(out double v); return v; }
            double Property() => (double)rig.Property(slider.Id, UiaInterop.UIA_RangeValueValuePropertyId)!;

            slider.Value = 50;
            rig.Session.PumpFrame();            // the leading edge: told
            Assert.Equal((50.0, 50.0), (Property(), Pattern()));
            slider.Value = 70;
            rig.Session.PumpFrame();            // inside the window: not told yet
            Assert.Equal((50.0, 50.0), (Property(), Pattern()));
            rig.Session.PumpFrame(TimeSpan.FromMilliseconds(300));
            Assert.Equal((70.0, 70.0), (Property(), Pattern()));
        }
        finally { AccessibilityClients.Force = null; }
    }

    [WindowsFact]
    public void TheValueAndTogglePatterns_AnswerAsTheirProperties_BeforeTheFrameTellsAChange()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var rig = new Rig();
            var input = TextInput.New("seed").Width(Dimension.Px(200));
            var check = Checkbox.New(false, "Check");
            var toggle = ToggleSwitch.New(false, "Switch").Width(Dimension.Px(160));
            rig.Session.Window.SetRoot(Flex.Column(gap: 4).Padding(8).Children(input, check, toggle));
            rig.Session.PumpFrames(3);
            var value = rig.Pattern<UiaValueProvider>(input.Id, UiaInterop.UIA_ValuePatternId);
            var checkToggle = rig.Pattern<UiaToggleProvider>(check.Id, UiaInterop.UIA_TogglePatternId);
            var switchToggle = rig.Pattern<UiaToggleProvider>(toggle.Id, UiaInterop.UIA_TogglePatternId);
            string? Value() { value.GetValue(out var b); try { return Marshal.PtrToStringBSTR(b); } finally { Marshal.FreeBSTR(b); } }
            int Toggle(UiaToggleProvider p) { p.GetToggleState(out int s); return s; }

            // A switch's pattern says what its property says: it read the element, and only a check
            // box or a radio button was one, so a switch was indeterminate.
            Assert.Equal(rig.Property(toggle.Id, UiaInterop.UIA_ToggleToggleStatePropertyId), Toggle(switchToggle));
            Assert.Equal(UiaInterop.ToggleState_Off, Toggle(switchToggle));

            // Changed, and no frame yet to tell it: the patterns answer what was told, as the properties do.
            rig.Session.Window.Click(input);
            rig.Session.PumpFrame();
            rig.Session.Window.TypeText("x");
            check.IsChecked = true;
            toggle.IsOn = true;
            Assert.Equal(rig.Property(input.Id, UiaInterop.UIA_ValueValuePropertyId), Value());
            Assert.Equal(rig.Property(check.Id, UiaInterop.UIA_ToggleToggleStatePropertyId), Toggle(checkToggle));
            Assert.Equal(rig.Property(toggle.Id, UiaInterop.UIA_ToggleToggleStatePropertyId), Toggle(switchToggle));
            Assert.Equal((UiaInterop.ToggleState_Off, UiaInterop.ToggleState_Off), (Toggle(checkToggle), Toggle(switchToggle)));

            rig.Session.PumpFrame();
            Assert.Equal(input.Text, Value());
            Assert.Equal((UiaInterop.ToggleState_On, UiaInterop.ToggleState_On), (Toggle(checkToggle), Toggle(switchToggle)));
        }
        finally { AccessibilityClients.Force = null; }
    }

    [WindowsFact]
    public void AScrollRefresh_RaisesThePercentFromTheNode_WithNoWalkOfTheLiveTree()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var rig = new Rig();
            var raised = new List<(UiaRaised Raised, double Value)>();
            var rows = new Element[200];
            for (int i = 0; i < rows.Length; i++) rows[i] = TextB.New($"row {i}").FontSize(12f).Height(Dimension.Px(18));
            var scroll = ScrollView.New().Height(Dimension.Px(200)).Children(Flex.Column().FlexShrink(0).Children(rows));
            rig.Session.Window.SetRoot(Flex.Column().Children(TextB.New("top"), scroll));
            for (int f = 0; f < 3; f++) rig.Session.PumpFrame(TimeSpan.FromMilliseconds(150));
            rig.Bridge.Raised = r =>
            {
                double v = double.NaN;
                if (r.Kind == UiaRaisedKind.Property && r.Id == UiaInterop.UIA_ScrollVerticalScrollPercentPropertyId)
                {
                    rig.Host.TryGetScrollInfo(scroll.Id, out var live);
                    v = live.VerticalPercent;
                }
                raised.Add((r, v));
            };
            int reads = rig.Counting.Reads;

            scroll.ScrollOffsetY = 500;
            scroll.MarkPaintDirty();
            rig.Session.PumpFrame(TimeSpan.FromMilliseconds(150));
            rig.Session.PumpFrame(TimeSpan.FromMilliseconds(150));

            var percent = Assert.Single(raised, r => r.Raised.Id == UiaInterop.UIA_ScrollVerticalScrollPercentPropertyId);
            Assert.Equal(scroll.Id, percent.Raised.Element);
            Assert.Equal(0, rig.Counting.Reads - reads);
            rig.Bridge.TryGetScrollInfo(scroll.Id, out var fromNode);
            Assert.Equal(percent.Value, fromNode.VerticalPercent, 6);
        }
        finally { AccessibilityClients.Force = null; }
    }
}
