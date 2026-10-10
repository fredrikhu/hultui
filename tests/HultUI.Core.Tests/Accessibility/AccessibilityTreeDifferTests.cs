using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

public class AccessibilityTreeDifferTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private void Layout(Element root, float w = 400, float h = 400) =>
        _layout.PerformLayout(root, w, h);

    private static BoxElement AppRoot()
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;
        return root;
    }

    [Fact]
    public void Diff_BothNull_ReturnsEmptyDiff()
    {
        var diff = AccessibilityTreeDiffer.Diff(null, null);
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Diff_PreviousNull_AddsEverything()
    {
        var root = AppRoot();
        root.AddChild(TextElement.New("Hello"));
        Layout(root);
        var tree = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(null, tree);

        Assert.Equal(2, diff.Added.Count); // app root + label
        Assert.Empty(diff.Removed);
        Assert.Empty(diff.PropertyChanged);
        Assert.Null(diff.Added[0].Parent); // root has no parent
        Assert.Same(tree, diff.Added[0].Node);
        Assert.Same(tree, diff.Added[1].Parent);
    }

    [Fact]
    public void Diff_CurrentNull_RemovesEverything()
    {
        var root = AppRoot();
        root.AddChild(TextElement.New("Hello"));
        Layout(root);
        var tree = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(tree, null);

        Assert.Equal(2, diff.Removed.Count);
        // Deepest-first: label first, then app root
        Assert.Equal(AccessibilityRole.Label, diff.Removed[0].Node.Role);
        Assert.Equal(AccessibilityRole.Application, diff.Removed[1].Node.Role);
        Assert.Empty(diff.Added);
    }

    [Fact]
    public void Diff_UnchangedTree_IsEmpty()
    {
        var root = AppRoot();
        var text = TextElement.New("Hello");
        root.AddChild(text);
        Layout(root);

        var a = AccessibilityTreeBuilder.Build(root);
        var b = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(a, b);

        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Diff_ChildAdded_ReportsSingleAddedNode()
    {
        var root = AppRoot();
        Layout(root);
        var before = AccessibilityTreeBuilder.Build(root);

        root.AddChild(TextElement.New("Later"));
        Layout(root);
        var after = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);

        var added = Assert.Single(diff.Added);
        Assert.Equal(AccessibilityRole.Label, added.Node.Role);
        Assert.Equal("Later", added.Node.Name);
        Assert.NotNull(added.Parent);
        Assert.Equal(AccessibilityRole.Application, added.Parent!.Role);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void Diff_ChildRemoved_ReportsSingleRemovedNode()
    {
        var root = AppRoot();
        var a = TextElement.New("A");
        var b = TextElement.New("B");
        root.AddChild(a);
        root.AddChild(b);
        Layout(root);
        var before = AccessibilityTreeBuilder.Build(root);

        root.RemoveChild(a);
        Layout(root);
        var after = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);

        var removed = Assert.Single(diff.Removed);
        Assert.Equal("A", removed.Node.Name);
        Assert.Empty(diff.Added);
    }

    [Fact]
    public void Diff_NestedSubtreeRemoved_RemovesLeavesFirst()
    {
        var root = AppRoot();
        var container = BoxElement.New();
        container.Role = AccessibilityRole.Group;
        container.AccessibleName = "Box";
        container.AddChild(TextElement.New("inside"));
        root.AddChild(container);
        Layout(root);
        var before = AccessibilityTreeBuilder.Build(root);

        root.RemoveChild(container);
        Layout(root);
        var after = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);

        Assert.Equal(2, diff.Removed.Count);
        Assert.Equal(AccessibilityRole.Label, diff.Removed[0].Node.Role);
        Assert.Equal(AccessibilityRole.Group, diff.Removed[1].Node.Role);
    }

    [Fact]
    public void Diff_NameChanged_EmitsNamePropertyChange()
    {
        var root = AppRoot();
        var text = TextElement.New("Before");
        root.AddChild(text);
        Layout(root);
        var before = AccessibilityTreeBuilder.Build(root);

        text.Content = "After";
        Layout(root);
        var after = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);

        var change = Assert.Single(diff.PropertyChanged);
        Assert.Equal(AccessibilityProperties.Name, change.PropertyName);
        Assert.Equal("After", change.Node.Name);
    }

    [Fact]
    public void Diff_ToggleStateChanged_EmitsIsToggledChange()
    {
        var root = AppRoot();
        var checkbox = new CheckboxElement { Label = "Remember me" };
        root.AddChild(checkbox);
        Layout(root);
        var before = AccessibilityTreeBuilder.Build(root);

        checkbox.IsChecked = true;
        Layout(root);
        var after = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);

        var change = Assert.Single(diff.PropertyChanged);
        Assert.Equal(AccessibilityProperties.IsToggled, change.PropertyName);
        Assert.True(change.Node.IsToggled);
    }

    [Fact]
    public void Diff_ExpandedStateChanged_EmitsIsExpandedChange()
    {
        var root = AppRoot();
        var header = new BoxElement { Role = AccessibilityRole.Button, AccessibleName = "Details", AccessibleExpanded = false };
        root.AddChild(header);
        Layout(root);
        var before = AccessibilityTreeBuilder.Build(root);

        header.AccessibleExpanded = true;
        Layout(root);
        var after = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);

        var change = Assert.Single(diff.PropertyChanged);
        Assert.Equal(AccessibilityProperties.IsExpanded, change.PropertyName);
        Assert.Equal(true, change.Node.Expanded);
    }

    [Fact]
    public void ARangesValue_IsInItsNode_AndAMoveIsARangeValueChange_WithThePreviousNode()
    {
        // A slider's, a progress bar's, a number field's: the node carried none, so a move told
        // nothing (the bridges read the value live and were never told it changed).
        var root = AppRoot();
        var slider = SliderElement.New(20, 0, 50);
        var bar = ProgressBarElement.New();
        var number = NumericUpDownElement.New(3, 0, 10);
        root.AddChild(slider);
        root.AddChild(bar);
        root.AddChild(number);
        Layout(root);
        var before = AccessibilityTreeBuilder.Build(root);
        Assert.Equal(new AccessibleRange(20, 0, 50, 1, 10, false), before.Children[0].Range);
        Assert.Equal(new AccessibleRange(0, 0, 1, 0, 0, true), before.Children[1].Range);
        Assert.Equal(3, before.Children[2].Range!.Value.Value);
        Assert.Empty(AccessibilityTreeDiffer.Diff(before, AccessibilityTreeBuilder.Build(root)).PropertyChanged);

        slider.Value = 21;
        bar.Maximum = 2;
        var after = AccessibilityTreeBuilder.Build(root);
        var diff = AccessibilityTreeDiffer.Diff(before, after);
        Assert.Equal(2, diff.PropertyChanged.Count);
        Assert.All(diff.PropertyChanged, c => Assert.Equal(AccessibilityProperties.RangeValue, c.PropertyName));
        var moved = diff.PropertyChanged.Single(c => c.Node.ElementId == slider.Id);
        Assert.Equal(20, moved.Previous!.Range!.Value.Value);
        Assert.Equal(21, moved.Node.Range!.Value.Value);
        var widened = diff.PropertyChanged.Single(c => c.Node.ElementId == bar.Id);
        Assert.Equal(2, widened.Node.Range!.Value.Maximum);
    }

    [Fact]
    public void Diff_TextInputValueChanged_EmitsValueChange()
    {
        var root = AppRoot();
        var input = new TextInputElement { Text = "old" };
        input.AccessibleName = "field";
        root.AddChild(input);
        Layout(root);
        var before = AccessibilityTreeBuilder.Build(root);

        input.Text = "new";
        Layout(root);
        var after = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);

        var change = Assert.Single(diff.PropertyChanged);
        Assert.Equal(AccessibilityProperties.Value, change.PropertyName);
        Assert.Equal("new", change.Node.Value);
    }

    [Fact]
    public void Diff_SiblingsReordered_ReportsReorderOnParent()
    {
        var root = AppRoot();
        var a = TextElement.New("A");
        var b = TextElement.New("B");
        var c = TextElement.New("C");
        root.AddChild(a);
        root.AddChild(b);
        root.AddChild(c);
        Layout(root);
        var before = AccessibilityTreeBuilder.Build(root);

        // Reorder to c, a, b
        root.ReplaceChildren(c, a, b);
        Layout(root);
        var after = AccessibilityTreeBuilder.Build(root);

        var diff = AccessibilityTreeDiffer.Diff(before, after);

        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
        var reorderedParent = Assert.Single(diff.Reordered);
        Assert.Equal(AccessibilityRole.Application, reorderedParent.Role);
    }

    [Fact]
    public void Diff_KeyedByElementId_PersistsAcrossRebuilds()
    {
        // Same tree built twice → same ElementIds → no diff at all. Proves the
        // differ actually keys on ElementId (not identity), which is what bridges
        // rely on to reuse wrapper objects across frames.
        var root = AppRoot();
        var text = TextElement.New("Hello");
        root.AddChild(text);
        Layout(root);

        var a = AccessibilityTreeBuilder.Build(root);
        var b = AccessibilityTreeBuilder.Build(root);

        Assert.NotSame(a, b);
        Assert.NotSame(a.Children[0], b.Children[0]);
        Assert.Equal(a.Children[0].ElementId, b.Children[0].ElementId);

        var diff = AccessibilityTreeDiffer.Diff(a, b);
        Assert.True(diff.IsEmpty);
    }

    [Fact]
    public void Build_PopulatesFocusedFromFocusManager()
    {
        var root = AppRoot();
        var button = BoxElement.New();
        button.AccessibleName = "OK";
        button.On<ClickEvent>(_ => { });
        button.IsFocusable = true;
        root.AddChild(button);
        Layout(root);

        var focusManager = new FocusManager();
        focusManager.SetFocus(button);

        var tree = AccessibilityTreeBuilder.Build(root, focusManager);

        Assert.False(tree.IsFocused);
        Assert.True(tree.Children[0].IsFocused);
        Assert.True(tree.Children[0].IsFocusable);
    }

    [Fact]
    public void Build_PopulatesCheckboxToggleState()
    {
        var root = AppRoot();
        var cb = new CheckboxElement { Label = "agree", IsChecked = true };
        root.AddChild(cb);
        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        Assert.Single(tree.Children);
        Assert.Equal(AccessibilityRole.Checkbox, tree.Children[0].Role);
        Assert.Equal(true, tree.Children[0].IsToggled);
        Assert.Equal("agree", tree.Children[0].Name);
    }

    [Fact]
    public void Build_PopulatesTextInputValue()
    {
        var root = AppRoot();
        var input = new TextInputElement { Text = "hello" };
        input.AccessibleName = "greeting";
        root.AddChild(input);
        Layout(root);

        var tree = AccessibilityTreeBuilder.Build(root);

        Assert.Equal("hello", tree.Children[0].Value);
        Assert.Equal("greeting", tree.Children[0].Name);
    }

    [Fact]
    public void NotifyAccessibilityPropertyChanged_WalksToObserverOnRoot()
    {
        var root = AppRoot();
        var cb = new CheckboxElement { Label = "x" };
        root.AddChild(cb);

        var observer = new RecordingObserver();
        // Assign via the same internal setter the Window uses.
        typeof(Element).GetProperty("AccessibilityObserver",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(root, observer);

        cb.IsChecked = true;

        var (el, prop) = Assert.Single(observer.Events);
        Assert.Same(cb, el);
        Assert.Equal(AccessibilityProperties.IsToggled, prop);
    }

    private sealed class RecordingObserver : IElementPropertyObserver
    {
        public List<(Element Element, string Property)> Events { get; } = [];
        public void OnElementPropertyChanged(Element element, string propertyName)
            => Events.Add((element, propertyName));
    }

    // ── Off-screen bounds are not diffed (perf: virtualized lists) ──

    private static readonly ElementId RootId = ElementId.New();
    private static readonly ElementId OnscreenId = ElementId.New();
    private static readonly ElementId OffscreenId = ElementId.New();

    private static AccessibilityNode Item(ElementId id, Rect rect, bool offscreen)
        => new(id, AccessibilityRole.ListItem, "item", null, rect) { IsOffscreen = offscreen };

    private static AccessibilityNode TreeWith(params AccessibilityNode[] items)
    {
        var root = new AccessibilityNode(RootId, AccessibilityRole.Application, null, null, new Rect(0, 0, 400, 400));
        foreach (var i in items) root.AddChild(i);
        return root;
    }

    [Fact]
    public void Diff_OffscreenNodeBoundsChange_DoesNotEmitPropertyChange()
    {
        // Both items shift in X (as when a virtualized list is resized). Only the
        // on-screen item's bounds change should be reported; the off-screen one is
        // skipped so assistive tech isn't flooded with events it can't use.
        var prev = TreeWith(
            Item(OnscreenId, new Rect(0, 0, 100, 20), offscreen: false),
            Item(OffscreenId, new Rect(0, 5000, 100, 20), offscreen: true));
        var curr = TreeWith(
            Item(OnscreenId, new Rect(50, 0, 100, 20), offscreen: false),
            Item(OffscreenId, new Rect(50, 5000, 100, 20), offscreen: true));

        var diff = AccessibilityTreeDiffer.Diff(prev, curr);

        Assert.Contains(diff.PropertyChanged,
            pc => pc.Node.ElementId == OnscreenId && pc.PropertyName == AccessibilityProperties.BoundingRect);
        Assert.DoesNotContain(diff.PropertyChanged, pc => pc.Node.ElementId == OffscreenId);
    }

    [Fact]
    public void Diff_ItemScrollsIntoView_EmitsBoundsChange()
    {
        // Off-screen → on-screen: the transition must still emit so the freshly
        // visible item gets current bounds.
        var prev = TreeWith(Item(OnscreenId, new Rect(0, 5000, 100, 20), offscreen: true));
        var curr = TreeWith(Item(OnscreenId, new Rect(0, 10, 100, 20), offscreen: false));

        var diff = AccessibilityTreeDiffer.Diff(prev, curr);

        Assert.Contains(diff.PropertyChanged,
            pc => pc.Node.ElementId == OnscreenId && pc.PropertyName == AccessibilityProperties.BoundingRect);
    }
}
