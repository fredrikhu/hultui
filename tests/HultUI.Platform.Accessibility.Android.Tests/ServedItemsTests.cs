using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Platform.Accessibility.Android.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// A list over 2,000 items, whose items the tree serves on demand (no node each): the items on screen
/// are the list's virtual children, as a RecyclerView's are, made from the list when TalkBack asks; a
/// finger finds them, a double tap runs them, and their ids go with the list's end. And a list that
/// stands for its pane's focus: TalkBack is told the focus is on the list's cursor.
/// </summary>
public class ServedItemsTests
{
    private const int Count = 3000;

    private static VirtualListElement<string> List(int count = Count) =>
        VirtualListElement<string>.New().ItemHeight(20).Height(200).FlexShrink(0)
            .RenderWith(t => Flex.Row().Children(Text.New(t)))
            .Items(Enumerable.Range(0, count).Select(i => $"item {i}").ToList());

    private static Harness Page(Element list) => new(Box.New().Width(400).Height(800).Children(list));

    private static AccessibilityNode NodeOf(Harness h, Element element) =>
        Find(h.Tree, element.Id) ?? throw new InvalidOperationException("not in the tree");

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    private static List<AndroidNode> Children(Harness h, Element list) =>
        h.Node(list).Children.Select(id => h.Bridge.Node(id)!).ToList();

    [Fact]
    public void AListServedOnDemand_HasTheItemsOnScreenAsItsChildren()
    {
        var list = List();
        using var h = Page(list);
        Assert.True(NodeOf(h, list).IsVirtualItemContainer);

        var node = h.Node(list);
        Assert.Equal(Count, node.Collection!.Value.Rows);
        var items = Children(h, list);
        // 200 high, 20 an item: ten on screen, and no more.
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"item {i}"), items.Select(n => n.Text));
        Assert.All(items, n => Assert.Equal(AndroidClasses.View, n.ClassName));
        Assert.Equal([0, 1, 2], items.Take(3).Select(n => n.CollectionItem!.Value.Row));
        Assert.All(items, n => Assert.Equal(h.Id(list), n.ParentId));
        Assert.All(items, n => Assert.True(n.Has(AndroidNodeFlags.VisibleToUser | AndroidNodeFlags.Clickable)));
        Assert.Equal(new PixelBox(0, 40, 400, 60), items[2].BoundsInScreen);
    }

    [Fact]
    public void AfterAScroll_TheChildrenAreTheItemsNowOnScreen()
    {
        var list = List();
        using var h = Page(list);
        list.ScrollOffsetY = 20 * 1500 + 10;   // half of item 1500 above the top
        h.Apply();

        var items = Children(h, list);
        Assert.Equal("item 1500", items[0].Text);
        Assert.Equal(1500, items[0].CollectionItem!.Value.Row);
        // The half that shows, clipped to the list.
        Assert.Equal(new PixelBox(0, 0, 400, 10), items[0].BoundsInScreen);
        Assert.Equal("item 1510", items[^1].Text);
        Assert.Equal(11, items.Count);
    }

    [Fact]
    public void AFingerOnAServedItem_FindsIt_AndADoubleTapRunsIt()
    {
        var list = List();
        using var h = Page(list);

        int id = h.Bridge.HitTest(100, 45);
        var item = h.Bridge.Node(id)!;
        Assert.Equal("item 2", item.Text);

        Assert.True(h.Bridge.Perform(id, AndroidActions.Click));
        Assert.True(h.Bridge.Perform(id, AndroidActions.ShowOnScreen));
        ulong element = VirtualItemId.For(list.Id, 2).Value;
        Assert.Equal([$"invoke {element}", $"reveal {element}"], h.Host.Calls);
    }

    [Fact]
    public void TalkBacksFocus_StaysOnAServedItem_ItWasMovedTo()
    {
        var list = List();
        using var h = Page(list);
        int id = Children(h, list)[3].Id;

        Assert.True(h.Bridge.Perform(id, AndroidActions.AccessibilityFocus));
        Assert.True(h.Bridge.Node(id)!.Has(AndroidNodeFlags.AccessibilityFocused));
    }

    [Fact]
    public void TheSelectedItem_IsSelected_AndTheOthersOfferToBe()
    {
        var list = List().SelectedIndex(4);
        using var h = Page(list);

        var items = Children(h, list);
        Assert.True(items[4].Has(AndroidNodeFlags.Selected));
        Assert.True(items[4].CollectionItem!.Value.Selected);
        Assert.False(items[4].Can(AndroidActions.Select));
        Assert.False(items[3].Has(AndroidNodeFlags.Selected));
        Assert.True(items[3].Can(AndroidActions.Select));
        Assert.Equal(AndroidSelectionMode.Single, h.Node(list).Collection!.Value.SelectionMode);
    }

    [Fact]
    public void TheIdsOfItemsPastTheListsNewEnd_AreLetGo_AndTheOthersKept()
    {
        var items = Enumerable.Range(0, 2600).Select(i => $"item {i}").ToList();
        var list = List().Items(items);
        using var h = Page(list);
        int kept = Children(h, list)[1].Id;
        list.ScrollOffsetY = 20 * 2500;
        h.Apply();
        int last = Children(h, list)[0].Id;
        Assert.Equal("item 2500", h.Bridge.Node(last)!.Text);

        list.Items(items.Take(2100).ToList());
        h.Apply();
        Assert.Equal(AndroidIds.None, h.Bridge.KnownId(VirtualItemId.For(list.Id, 2500)));
        Assert.Null(h.Bridge.Node(last));
        Assert.Equal(kept, h.Bridge.KnownId(VirtualItemId.For(list.Id, 1)));
    }

    [Fact]
    public void TheIdsOfAServedListsItems_AreLetGoWithTheList()
    {
        var list = List();
        var page = Box.New().Width(400).Height(800).Children(list);
        using var h = new Harness(page);
        Children(h, list);
        Assert.NotEqual(AndroidIds.None, h.Bridge.KnownId(VirtualItemId.For(list.Id, 0)));
        var itemOf = VirtualItemId.For(list.Id, 0);

        page.Children(Button.New("Back"));
        h.Apply();
        Assert.Equal(AndroidIds.None, h.Bridge.KnownId(itemOf));
    }

    // ── A list that stands for its pane's focus (AccessibilityNode.FocusHolder) ──

    /// <summary>A focusable pane with a header and a list inside, as Vesper's pane holds the focus and
    /// walks its file list's cursor.</summary>
    private static (BoxElement Pane, VirtualListElement<string> List) Pane(bool takesFocus, int count = 30)
    {
        var list = List(count).SelectedIndex(2).TakesAncestorFocus(takesFocus);
        list.IsFocusable = false;
        var pane = Flex.Column().Width(400).Height(400).Children(Text.New("Name  Size"), list);
        pane.IsFocusable = true;
        pane.Role = AccessibilityRole.Group;
        pane.AccessibleName = "Left pane";
        return (pane, list);
    }

    [Theory]
    [InlineData(30)]      // items as nodes
    [InlineData(Count)]   // served on demand
    public void APaneWhoseListStandsForItsFocus_IsToldFocusedOnTheListsCursor(int count)
    {
        var (pane, list) = Pane(takesFocus: true, count);
        using var h = new Harness(Box.New().Width(400).Height(800).Children(pane));
        list.CurrentIndex = 5;
        h.Apply();

        h.Bridge.NotifyFocusChanged(NodeOf(h, pane));
        var focused = Assert.Single(h.Sink.Events);
        Assert.Equal(AndroidEventType.ViewFocused, focused.Type);
        Assert.Equal("item 5", h.Bridge.Node(focused.Source)!.Text);

        // The cursor moves with no focus change: told once the frame's changes are.
        h.Sink.Clear();
        list.CurrentIndex = 6;
        h.Apply();
        h.Bridge.NotifyChangesApplied(h.Tree);
        focused = Assert.Single(h.Sink.Events, e => e.Type == AndroidEventType.ViewFocused);
        Assert.Equal("item 6", h.Bridge.Node(focused.Source)!.Text);
        Assert.Equal(focused.Source, h.Bridge.InputFocusedId);
    }

    [Fact]
    public void APaneWhoseListDoesNotStandForItsFocus_IsToldFocusedItself()
    {
        var (pane, list) = Pane(takesFocus: false);
        using var h = new Harness(Box.New().Width(400).Height(800).Children(pane));
        list.CurrentIndex = 5;
        h.Apply();

        h.Bridge.NotifyFocusChanged(NodeOf(h, pane));
        var focused = Assert.Single(h.Sink.Events);
        Assert.Equal(h.Id(pane), focused.Source);
    }
}
