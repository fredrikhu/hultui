using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class VirtualListElementTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private VirtualListElement<string> CreateList(int itemCount, float viewportHeight = 200f, float itemHeight = 40f)
    {
        var items = Enumerable.Range(0, itemCount).Select(i => $"Item {i}").ToList();
        var vl = VirtualListElement<string>.New()
            .ItemHeight(itemHeight)
            .Items(items)
            .RenderWith(item => TextElement.New(item));

        vl.Box.Width = 300;
        vl.Box.Height = viewportHeight;

        // Layout + initial sync
        _layout.PerformLayout(vl, 300, viewportHeight);
        StyleResolver.Resolve(vl);
        vl.UpdateContentSize();
        _layout.PerformLayout(vl, 300, viewportHeight);

        return vl;
    }

    [Fact]
    public void ContentHeight_ReflectsTotalVirtualHeight()
    {
        var vl = CreateList(100, viewportHeight: 200, itemHeight: 40);

        // 100 items * 40px = 4000
        Assert.Equal(4000, vl.ContentHeight);
    }

    [Fact]
    public void OnlyVisibleItemsAreMaterialized()
    {
        var vl = CreateList(1000, viewportHeight: 200, itemHeight: 40);

        // Viewport 200px / 40px per item = 5 visible + overscan
        // Children = spacer + visible items, should be much less than 1000
        Assert.True(vl.Children.Count < 15);
        Assert.True(vl.Children.Count > 1); // at least spacer + 1 item
    }

    [Fact]
    public void ScrollingUpdatesVisibleItems()
    {
        var vl = CreateList(100, viewportHeight: 200, itemHeight: 40);

        var initialChildren = vl.Children.Count;

        // Scroll down
        vl.ScrollOffsetY = 400; // scrolled to item 10
        vl.UpdateContentSize();
        _layout.PerformLayout(vl, 300, 200);

        // Should still have roughly the same number of visible items
        Assert.True(vl.Children.Count > 1);
        Assert.InRange(vl.Children.Count, initialChildren - 2, initialChildren + 2);
    }

    [Fact]
    public void ScrollOffset_ClampsToMaxScroll()
    {
        var vl = CreateList(100, viewportHeight: 200, itemHeight: 40);

        // Max scroll = 4000 - 200 = 3800
        vl.ScrollOffsetY = 9999;
        vl.UpdateContentSize();

        Assert.Equal(3800, vl.ScrollOffsetY);
    }

    [Fact]
    public void ScrollOffset_ClampsToZero()
    {
        var vl = CreateList(100, viewportHeight: 200, itemHeight: 40);

        vl.ScrollOffsetY = -100;
        Assert.Equal(0, vl.ScrollOffsetY);
    }

    [Fact]
    public void EmptyItems_NoChildren()
    {
        var vl = VirtualListElement<string>.New()
            .ItemHeight(40)
            .Items(Array.Empty<string>());
        vl.ItemTemplate = item => TextElement.New(item);

        vl.Box.Width = 300;
        vl.Box.Height = 200;

        _layout.PerformLayout(vl, 300, 200);
        vl.UpdateContentSize();

        Assert.Empty(vl.Children);
        Assert.Equal(0, vl.ContentHeight);
    }

    [Fact]
    public void NullTemplate_NoChildren()
    {
        var vl = VirtualListElement<string>.New()
            .ItemHeight(40)
            .Items(new[] { "a", "b", "c" });

        vl.Box.Width = 300;
        vl.Box.Height = 200;

        _layout.PerformLayout(vl, 300, 200);
        vl.UpdateContentSize();

        Assert.Empty(vl.Children);
    }

    [Fact]
    public void SpacerHeight_MatchesScrollPosition()
    {
        var vl = CreateList(100, viewportHeight: 200, itemHeight: 40);

        // Scroll to item 20
        vl.ScrollOffsetY = 800; // 20 * 40
        vl.UpdateContentSize();
        _layout.PerformLayout(vl, 300, 200);

        // First child is the spacer
        Assert.True(vl.Children.Count > 1);
        var spacer = vl.Children[0];
        // With 1-item overscan, first visible = max(0, 20-1) = 19
        Assert.Equal(19 * 40f, spacer.Box.Height.Resolve(0));
    }

    [Fact]
    public void ItemsFixedHeight_IsApplied()
    {
        var vl = CreateList(10, viewportHeight: 200, itemHeight: 50);

        // All non-spacer children should have height = 50
        for (int i = 1; i < vl.Children.Count; i++)
        {
            var child = vl.Children[i];
            Assert.Equal(50f, child.Box.Height.Resolve(0));
        }
    }

    [Fact]
    public void SettingNewItems_ResetsVisibleRange()
    {
        var vl = CreateList(100, viewportHeight: 200, itemHeight: 40);
        vl.ScrollOffsetY = 400;
        vl.UpdateContentSize();

        // Replace with fewer items
        vl.Items = Enumerable.Range(0, 5).Select(i => $"New {i}").ToList();
        _layout.PerformLayout(vl, 300, 200);
        vl.UpdateContentSize();

        Assert.Equal(200, vl.ContentHeight); // 5 * 40
        Assert.True(vl.Children.Count <= 7); // spacer + up to 5 items
    }

    [Fact]
    public void FluentApi_Chainable()
    {
        var items = new List<string> { "a", "b", "c" };

        var vl = VirtualListElement<string>.New()
            .ItemHeight(30)
            .Items(items)
            .RenderWith(item => TextElement.New(item))
            .Width(400)
            .Height(300)
            .ScrollSpeed(60);

        Assert.Equal(30, vl.ItemHeight);
        Assert.Same(items, vl.Items);
        Assert.NotNull(vl.ItemTemplate);
        Assert.Equal(60, vl.ScrollSpeed);
    }

    [Fact]
    public void ScrollEvent_UpdatesOffset()
    {
        var vl = CreateList(100, viewportHeight: 200, itemHeight: 40);

        var evt = new ScrollEvent
        {
            Position = new Point(150, 100),
            DeltaX = 0,
            DeltaY = -1
        };
        EventDispatcher.Dispatch(evt, vl);

        Assert.Equal(40, vl.ScrollOffsetY); // default scroll speed = 40
    }

    [Fact]
    public void FewItems_AllVisible()
    {
        // Only 3 items that fit in the viewport — all should be materialized
        var vl = CreateList(3, viewportHeight: 200, itemHeight: 40);

        // spacer + 3 items = 4 children (spacer height = 0 since start = 0)
        Assert.True(vl.Children.Count >= 4);
    }

    [Fact]
    public void PatchFrom_PreservesScrollOffset()
    {
        var vl = CreateList(100, viewportHeight: 200, itemHeight: 40);
        vl.ScrollOffsetY = 200;

        var incoming = VirtualListElement<string>.New()
            .ItemHeight(40)
            .Items(vl.Items);
        incoming.ItemTemplate = item => TextElement.New(item);

        vl.PatchFrom(incoming);

        // Scroll offset should be preserved (inherited from ScrollViewElement patch behavior)
        Assert.Equal(200, vl.ScrollOffsetY);
    }

    [Fact]
    public void Rebuild_WithSameItemsInstance_ReRealizesRows()
    {
        var vl = CreateList(100, viewportHeight: 200, itemHeight: 40);
        Assert.True(vl.Children.Count > 1);

        // Simulate a MarkDirty rebuild: patch from a freshly-built list carrying the
        // SAME items instance, then the tree differ wipes the internally-realized rows
        // because the incoming element carries none (they're created at render).
        var incoming = VirtualListElement<string>.New().ItemHeight(40).Items(vl.Items);
        incoming.ItemTemplate = i => TextElement.New(i);
        vl.PatchFrom(incoming);
        vl.ReplaceChildren();               // differ removes spacer + rows
        Assert.Empty(vl.Children);

        // The next layout/sync must re-realize the visible window. The pre-fix bug left
        // the range unchanged, so the early-out skipped rebuilding and rows stayed blank.
        _layout.PerformLayout(vl, 300, 200);
        vl.UpdateContentSize();
        _layout.PerformLayout(vl, 300, 200);

        Assert.True(vl.Children.Count > 1);
    }
}
