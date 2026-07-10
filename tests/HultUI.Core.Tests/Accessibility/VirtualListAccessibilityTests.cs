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
