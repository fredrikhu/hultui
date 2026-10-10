using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

public class VirtualListAccessibilityTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private VirtualListElement<string> MakeList(int count, float itemHeight = 20f)
    {
        var list = VirtualListElement<string>.New();
        list.Box.Width = 200;
        list.Box.Height = 100; // viewport shows only ~5 of the items
        list.ItemHeight = itemHeight;
        var items = new string[count];
        for (int i = 0; i < count; i++) items[i] = $"row {i}";
        list.Items = items;
        list.ItemTemplate = s => TextElement.New(s);
        return list;
    }

    private AccessibilityNode BuildLaidOut(VirtualListElement<string> list)
    {
        _layout.PerformLayout(list, 200, 100);
        list.UpdateContentSize();
        return AccessibilityTreeBuilder.Build(list);
    }

    [Fact]
    public void VirtualList_HasListRole()
    {
        var tree = BuildLaidOut(MakeList(50));
        Assert.Equal(AccessibilityRole.List, tree.Role);
    }

    [Fact]
    public void VirtualList_ExposesEveryItem_NotJustRealized()
    {
        // 100 items but the 100px viewport only materializes a handful of rows.
        var tree = BuildLaidOut(MakeList(100));
        Assert.Equal(100, tree.Children.Count);
        Assert.All(tree.Children, c => Assert.Equal(AccessibilityRole.ListItem, c.Role));
    }

    [Fact]
    public void CompositeRows_PublishTheirOwnControlsInsteadOfOneNodePerRow()
    {
        // A wrap grid driven through the list: one item is a row of buttons, and each
        // button has to reach assistive tech with its own name and automation id.
        var list = VirtualListElement<int>.New();
        list.Box.Width = 200;
        list.Box.Height = 100;
        list.ItemHeight = 20f;
        list.RowsAreComposite = true;
        list.Items = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        list.ItemTemplate = row =>
        {
            var line = BoxElement.New();
            line.Flex.Direction = FlexDirection.Row;
            for (int card = 0; card < 2; card++)
            {
                var button = BoxElement.New();
                button.Role = AccessibilityRole.Button;
                button.AccessibleName = $"card {row}.{card}";
                button.AutomationId = $"card-{row}-{card}";
                line.AddChild(button);
            }
            return line;
        };

        var tree = BuildLaidOut2(list);
        var names = Walk(tree).Where(n => n.Role == AccessibilityRole.Button).Select(n => n.Name).ToList();

        Assert.Contains("card 0.0", names);
        Assert.Contains("card 0.1", names);
        // And nothing is published as a bare list item named after the whole row.
        Assert.DoesNotContain(Walk(tree), n => n.Role == AccessibilityRole.ListItem);
    }

    [Fact]
    public void CompositeRowsOff_KeepsTheOneNodePerItemModel()
    {
        var tree = BuildLaidOut(MakeList(10));
        Assert.Equal(10, tree.Children.Count);
        Assert.All(tree.Children, c => Assert.Equal(AccessibilityRole.ListItem, c.Role));
    }

    private AccessibilityNode BuildLaidOut2(VirtualListElement<int> list)
    {
        _layout.PerformLayout(list, 200, 100);
        list.UpdateContentSize();
        return AccessibilityTreeBuilder.Build(list);
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    [Fact]
    public void VirtualList_ItemLabel_UsesAccessibleLabel()
    {
        var list = MakeList(10);
        list.AccessibleLabel = s => $"commit: {s}";
        var tree = BuildLaidOut(list);
        Assert.Equal("commit: row 0", tree.Children[0].Name);
        Assert.Equal("commit: row 9", tree.Children[9].Name);
    }

    [Fact]
    public void VirtualList_ItemLabel_FallsBackToToString()
    {
        var tree = BuildLaidOut(MakeList(3));
        Assert.Equal("row 0", tree.Children[0].Name);
    }

    [Fact]
    public void VirtualList_ItemPositions_StackByItemHeight()
    {
        var tree = BuildLaidOut(MakeList(20, itemHeight: 30f));
        var y0 = tree.Children[0].BoundingRect.Y;
        var y1 = tree.Children[1].BoundingRect.Y;
        var y5 = tree.Children[5].BoundingRect.Y;
        Assert.Equal(30, y1 - y0, 3);
        Assert.Equal(150, y5 - y0, 3);
        Assert.Equal(30, tree.Children[0].BoundingRect.Height, 3);
    }

    [Fact]
    public void VirtualList_ItemsBeyondViewport_AreOffscreen()
    {
        // viewport 100, itemHeight 20 → ~5 visible; the rest are offscreen.
        var tree = BuildLaidOut(MakeList(100));
        Assert.False(tree.Children[0].IsOffscreen);
        Assert.True(tree.Children[50].IsOffscreen);
    }

    [Fact]
    public void VirtualList_Scroll_ShiftsItemBounds()
    {
        var list = MakeList(100);
        var before = BuildLaidOut(list).Children[0].BoundingRect.Y;
        list.ScrollOffsetY = 40;
        var after = AccessibilityTreeBuilder.Build(list).Children[0].BoundingRect.Y;
        Assert.Equal(before - 40, after, 3);
    }

    [Fact]
    public void VirtualList_ItemIds_AreStableAcrossRebuilds()
    {
        var list = MakeList(10);
        var id1 = BuildLaidOut(list).Children[5].ElementId;
        var id2 = AccessibilityTreeBuilder.Build(list).Children[5].ElementId;
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void VirtualList_ItemIds_DoNotCollideWithRealElements()
    {
        // Real ids come from a small incrementing counter; synthetic item ids set
        // the top bit, so they never collide with a real element's id.
        var list = MakeList(5);
        var tree = BuildLaidOut(list);
        Assert.NotEqual(list.Id.Value, tree.Children[0].ElementId.Value);
        Assert.All(tree.Children, c => Assert.True(c.ElementId.Value >= (1UL << 63)));
    }

    [Fact]
    public void VirtualList_Empty_HasNoItems()
    {
        var tree = BuildLaidOut(MakeList(0));
        Assert.Equal(AccessibilityRole.List, tree.Role);
        Assert.Empty(tree.Children);
    }

    [Fact]
    public void VirtualItemId_RoundTrips()
    {
        var listId = new ElementId(12345);
        var id = VirtualItemId.For(listId, 678);
        Assert.True(VirtualItemId.TryDecode(id, out var decodedList, out var index));
        Assert.Equal(listId, decodedList);
        Assert.Equal(678, index);
    }

    [Fact]
    public void VirtualItemId_RealElementIdIsNotVirtual()
    {
        Assert.False(VirtualItemId.TryDecode(new ElementId(42), out _, out _));
    }

    [Fact]
    public void BuilderItemId_DecodesToOwningListAndIndex()
    {
        var list = MakeList(10);
        var tree = BuildLaidOut(list);
        Assert.True(VirtualItemId.TryDecode(tree.Children[7].ElementId, out var listId, out var index));
        Assert.Equal(list.Id, listId);
        Assert.Equal(7, index);
    }

    [Fact]
    public void VirtualList_SelectedIndex_DefaultsToNoneAndRoundTrips()
    {
        var list = MakeList(10);
        Assert.Equal(-1, ((IAccessibleItemSource)list).SelectedIndex);
        list.SelectedIndex = 4;
        Assert.Equal(4, ((IAccessibleItemSource)list).SelectedIndex);
    }

    private sealed class RecordingObserver : IElementPropertyObserver
    {
        public List<string> Changes { get; } = [];
        public void OnElementPropertyChanged(Element element, string propertyName) => Changes.Add(propertyName);
    }

    [Fact]
    public void VirtualList_ItemNodes_CarryTheirSelectedState()
    {
        // Without it a move of the selection that scrolled nothing changed no node, and the
        // differ had nothing to tell a reader.
        var list = MakeList(10);
        list.SelectedIndex = 3;
        var before = BuildLaidOut(list);
        Assert.All(before.Children, (item, i) => Assert.Equal(i == 3, item.IsSelected));

        list.SelectedIndex = 4;
        var after = BuildLaidOut(list);
        var diff = AccessibilityTreeDiffer.Diff(before, after);
        Assert.Equal(
            [(before.Children[3].ElementId, AccessibilityProperties.IsSelected), (after.Children[4].ElementId, AccessibilityProperties.IsSelected)],
            diff.PropertyChanged.Select(c => (c.Node.ElementId, c.PropertyName)));
    }

    [Fact]
    public void VirtualList_SelectionChanges_AskForARebuild_OncePerChange()
    {
        var list = MakeList(10);
        var observer = new RecordingObserver();
        list.AccessibilityObserver = observer;

        list.SelectedIndex = 2;
        list.SelectedIndex = 2;
        list.SelectedIndex = -1;
        Assert.Equal([AccessibilityProperties.IsSelected, AccessibilityProperties.IsSelected], observer.Changes);

        // A patch carries the selection of the list built anew; it is the same change.
        observer.Changes.Clear();
        var rebuilt = MakeList(10);
        rebuilt.SelectedIndex = 5;
        list.PatchFrom(rebuilt);
        Assert.Equal([AccessibilityProperties.IsSelected], observer.Changes);

        // And a multi-selection changed by assistive technology.
        observer.Changes.Clear();
        list.CanSelectMultiple = true;
        ((IAccessibleItemSource)list).SetItemSelected(1, true);
        Assert.Equal([AccessibilityProperties.IsSelected], observer.Changes);
    }

    [Fact]
    public void VirtualList_PatchFrom_CarriesAccessibilitySelectionAndWiring()
    {
        // Simulates a declarative re-render: the mounted list is patched from a
        // freshly built one that recomputed its selection/activation each frame.
        var mounted = MakeList(10);
        Assert.Equal(-1, ((IAccessibleItemSource)mounted).SelectedIndex);

        var rebuilt = MakeList(10);
        rebuilt.SelectedIndex = 7;
        string? activated = null;
        rebuilt.OnItemActivate = s => activated = s;

        mounted.PatchFrom(rebuilt);

        Assert.Equal(7, ((IAccessibleItemSource)mounted).SelectedIndex);
        ((IAccessibleItemSource)mounted).ActivateItem(2);
        Assert.Equal("row 2", activated);
    }

    [Fact]
    public void VirtualList_MultiSelect_AddsRemovesAndReportsSelection()
    {
        var list = MakeList(10);
        list.CanSelectMultiple = true;
        var src = (IAccessibleItemSource)list;
        Assert.True(src.CanSelectMultiple);

        src.SetItemSelected(2, true);
        src.SetItemSelected(5, true);
        Assert.Equal(new[] { 2, 5 }, src.SelectedIndices);
        Assert.True(src.IsItemSelected(2));
        Assert.False(src.IsItemSelected(3));

        src.SetItemSelected(2, false);
        Assert.Equal(new[] { 5 }, src.SelectedIndices);

        // SelectOnly replaces the whole set.
        src.SelectOnly(8);
        Assert.Equal(new[] { 8 }, src.SelectedIndices);
    }

    [Fact]
    public void VirtualList_MultiSelect_FiresOnSelectionChanged()
    {
        var list = MakeList(10);
        list.CanSelectMultiple = true;
        IReadOnlyList<int>? last = null;
        list.OnSelectionChanged = s => last = s;

        ((IAccessibleItemSource)list).SetItemSelected(3, true);

        Assert.Equal(new[] { 3 }, last);
    }

    [Fact]
    public void VirtualList_SingleSelect_SelectOnlyActivates()
    {
        // Without multi-select, SelectionItem.Select routes to activation — the
        // app owns the selection via its activate handler.
        var list = MakeList(10);
        string? activated = null;
        list.OnItemActivate = s => activated = s;

        ((IAccessibleItemSource)list).SelectOnly(4);

        Assert.Equal("row 4", activated);
        Assert.False(((IAccessibleItemSource)list).CanSelectMultiple);
    }

    [Fact]
    public void VirtualList_PatchFrom_CarriesMultiSelectMode()
    {
        var mounted = MakeList(10);
        mounted.CanSelectMultiple = true;
        ((IAccessibleItemSource)mounted).SetItemSelected(1, true);

        // A re-render with no live selection must not wipe the mounted set.
        var rebuilt = MakeList(10);
        rebuilt.CanSelectMultiple = true;
        mounted.PatchFrom(rebuilt);

        Assert.True(((IAccessibleItemSource)mounted).CanSelectMultiple);
        Assert.Equal(new[] { 1 }, ((IAccessibleItemSource)mounted).SelectedIndices);
    }

    [Fact]
    public void VirtualList_FindItem_MatchesByNameAndSelection()
    {
        var list = MakeList(10);
        var src = (IAccessibleItemSource)list;

        Assert.Equal(3, src.FindItem(-1, new AccessibleItemQuery(AccessibleItemProperty.Name, "row 3")));
        Assert.Equal(-1, src.FindItem(3, new AccessibleItemQuery(AccessibleItemProperty.Name, "row 3"))); // exclusive start
        Assert.Equal(0, src.FindItem(-1, new AccessibleItemQuery(AccessibleItemProperty.Any)));

        list.SelectedIndex = 6;
        Assert.Equal(6, src.FindItem(-1, new AccessibleItemQuery(AccessibleItemProperty.Selected)));
    }

    [Fact]
    public void VirtualList_ActivateItem_InvokesHandlerWithItem()
    {
        var list = MakeList(10);
        string? activated = null;
        list.OnItemActivate = s => activated = s;
        ((IAccessibleItemSource)list).ActivateItem(3);
        Assert.Equal("row 3", activated);
    }

    [Fact]
    public void VirtualList_ActivateItem_OutOfRange_DoesNotInvoke()
    {
        var list = MakeList(3);
        int calls = 0;
        list.OnItemActivate = _ => calls++;
        ((IAccessibleItemSource)list).ActivateItem(99);
        ((IAccessibleItemSource)list).ActivateItem(-1);
        Assert.Equal(0, calls);
    }

    // Minimal source that leaves every optional member on its default — exercises
    // the interface's default single-select + linear-scan implementations.
    private sealed class StubSource(int count, int selected = -1) : IAccessibleItemSource
    {
        public int ItemCount => count;
        public float ItemHeight => 10f;
        public int SelectedIndex => selected;
        public string? GetItemLabel(int index) => $"item {index}";
        public void ActivateItem(int index) { }
    }

    // Source with its own index — overrides FindItem to skip the scan entirely.
    private sealed class IndexedSource(int count, int findResult) : IAccessibleItemSource
    {
        public int ItemCount => count;
        public float ItemHeight => 10f;
        public int SelectedIndex => -1;
        public string? GetItemLabel(int index) => $"item {index}";
        public void ActivateItem(int index) { }
        public int FindItem(int startAfter, AccessibleItemQuery query) => findResult;
    }

    [Fact]
    public void IAccessibleItemSource_Defaults_AreSingleSelect()
    {
        IAccessibleItemSource src = new StubSource(5, selected: 2);
        Assert.False(src.CanSelectMultiple);
        Assert.Equal(new[] { 2 }, src.SelectedIndices);
        Assert.True(src.IsItemSelected(2));
        Assert.False(src.IsItemSelected(0));
        Assert.Equal(2, src.FindItem(-1, new AccessibleItemQuery(AccessibleItemProperty.Selected)));
        Assert.Equal(3, src.FindItem(-1, new AccessibleItemQuery(AccessibleItemProperty.Name, "item 3")));
    }

    [Fact]
    public void IAccessibleItemSource_NoSelection_ReportsEmpty()
    {
        IAccessibleItemSource src = new StubSource(5);
        Assert.Empty(src.SelectedIndices);
        Assert.Equal(-1, src.FindItem(-1, new AccessibleItemQuery(AccessibleItemProperty.Selected)));
    }

    [Fact]
    public void IAccessibleItemSource_FindItem_OverrideIsHonored()
    {
        // A source with its own index can answer without the linear scan.
        IAccessibleItemSource src = new IndexedSource(1_000_000, findResult: 999_999);
        Assert.Equal(999_999,
            src.FindItem(-1, new AccessibleItemQuery(AccessibleItemProperty.Name, "item 999999")));
    }

    [Fact]
    public void VirtualList_AtOrBelowCap_IsNotAVirtualItemContainer()
    {
        var tree = BuildLaidOut(MakeList(AccessibilityTreeBuilder.MaxEagerVirtualItems));
        Assert.False(tree.IsVirtualItemContainer);
        Assert.Equal(AccessibilityTreeBuilder.MaxEagerVirtualItems, tree.Children.Count);
    }

    [Fact]
    public void VirtualList_OverCap_BecomesLazyContainerWithNoChildNodes()
    {
        // Above the eager cap the list must NOT expand every item (that wouldn't
        // scale); it is marked for on-demand provisioning and emits no children.
        var tree = BuildLaidOut(MakeList(AccessibilityTreeBuilder.MaxEagerVirtualItems + 1));
        Assert.Equal(AccessibilityRole.List, tree.Role);
        Assert.True(tree.IsVirtualItemContainer);
        Assert.Empty(tree.Children);
    }
}
