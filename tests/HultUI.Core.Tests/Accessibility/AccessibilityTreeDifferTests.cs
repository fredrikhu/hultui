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
}
