using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Testing;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class ZOrderHelperTests
{
    [Fact]
    public void NoZIndex_ReturnsOriginalList()
    {
        var parent = BoxElement.New();
        var a = BoxElement.New();
        var b = BoxElement.New();
        var c = BoxElement.New();
        parent.ReplaceChildren(a, b, c);

        var result = ZOrderHelper.GetChildrenInZOrder(parent);

        // Should return the original list reference (zero allocation)
        Assert.Same(parent.Children, result);
    }

    /// <summary>The renderer and the hit tester ask for every element of every frame, so the
    /// common case allocates nothing at all. The sort's comparison captured the child list, and
    /// C# allocated that closure on the method's first line — 24 bytes per element per frame
    /// with no z-index or sticky child in sight.</summary>
    [Fact]
    public void NoZIndex_AllocatesNothing()
    {
        var parent = BoxElement.New();
        parent.ReplaceChildren(BoxElement.New(), BoxElement.New(), BoxElement.New());
        var leaf = BoxElement.New();
        ZOrderHelper.GetChildrenInZOrder(parent);
        ZOrderHelper.GetChildrenInZOrder(leaf);

        Assert.Equal(0, Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                ZOrderHelper.GetChildrenInZOrder(parent);
                ZOrderHelper.GetChildrenInZOrder(leaf);
            }
        }));
    }

    [Fact]
    public void SingleChild_ReturnsOriginalList()
    {
        var parent = BoxElement.New();
        var child = BoxElement.New();
        parent.AddChild(child);

        var result = ZOrderHelper.GetChildrenInZOrder(parent);

        Assert.Same(parent.Children, result);
    }

    [Fact]
    public void WithZIndex_SortsAscending()
    {
        var parent = BoxElement.New();
        var back = BoxElement.New();
        back.Position.ZIndex = -1;
        var mid = BoxElement.New();
        mid.Position.ZIndex = 0;
        var front = BoxElement.New();
        front.Position.ZIndex = 1;

        // Add in wrong order
        parent.ReplaceChildren(front, back, mid);

        var result = ZOrderHelper.GetChildrenInZOrder(parent);

        Assert.Equal(3, result.Count);
        Assert.Same(back, result[0]);
        Assert.Same(mid, result[1]);
        Assert.Same(front, result[2]);
    }

    [Fact]
    public void EqualZIndex_PreservesInsertionOrder()
    {
        var parent = BoxElement.New();
        var a = BoxElement.New();
        a.Position.ZIndex = 1;
        var b = BoxElement.New();
        b.Position.ZIndex = 1;
        var c = BoxElement.New();
        c.Position.ZIndex = 1;

        parent.ReplaceChildren(a, b, c);

        var result = ZOrderHelper.GetChildrenInZOrder(parent);

        Assert.Same(a, result[0]);
        Assert.Same(b, result[1]);
        Assert.Same(c, result[2]);
    }

    [Fact]
    public void NoChildren_ReturnsEmptyList()
    {
        var parent = BoxElement.New();

        var result = ZOrderHelper.GetChildrenInZOrder(parent);

        Assert.Empty(result);
    }

    /// <summary>Children with the same z-index keep their order in a long list too. The sort
    /// claimed to be stable and was not: List.Sort only behaved so while it insertion-sorted
    /// sixteen or fewer, which is all <see cref="EqualZIndex_PreservesInsertionOrder"/> tried.</summary>
    [Fact]
    public void EqualZIndexesKeepTheirOrder_InALongList()
    {
        var parent = BoxElement.New();
        var raised = BoxElement.New();
        raised.Position.ZIndex = 1;
        parent.AddChild(raised);
        var rest = Enumerable.Range(0, 40).Select(_ => BoxElement.New()).ToArray();
        foreach (var child in rest) parent.AddChild(child);

        var order = ZOrderHelper.GetChildrenInZOrder(parent);
        Assert.Equal([.. rest, raised], order);
    }

    [Fact]
    public void AStickyChildIsDrawnAfterItsSiblings_AtItsZIndex()
    {
        var parent = BoxElement.New();
        var header = BoxElement.New();
        header.Position.Type = PositionType.Sticky;
        var a = BoxElement.New();
        var b = BoxElement.New();
        var above = BoxElement.New();
        above.Position.ZIndex = 2;
        parent.ReplaceChildren(header, a, above, b);

        Assert.Equal([a, b, header, above], ZOrderHelper.GetChildrenInZOrder(parent));
    }

    // ──── The order is kept (plan 17, round 2, layout-zorder-sorted-allocs) ────
    //
    // The renderer and the hit tester ask for every element of every frame and every pointer
    // move. With a z-index or sticky child the order was sorted again each time: an index array,
    // a closure, a comparison and a result array — three sorts a frame on the perf suite's sticky
    // sections, 82 % of what its frame allocated — and every parent scanned its children to find
    // out whether it needed one. The parent keeps it now, until something it depends on changes.

    /// <summary>A parent of six: a sticky header first, one child raised, one lowered.</summary>
    private static (BoxElement Parent, BoxElement[] Children) Stacked()
    {
        var children = Enumerable.Range(0, 6).Select(_ => BoxElement.New()).ToArray();
        children[0].Position.Type = PositionType.Sticky;
        children[2].Position.ZIndex = 1;
        children[4].Position.ZIndex = -1;
        var parent = BoxElement.New();
        parent.ReplaceChildren(children);
        return (parent, children);
    }

    [Fact]
    public void TheOrderIsWorkedOutOnce_AndAskingAgainAllocatesNothing()
    {
        var (parent, c) = Stacked();
        var first = ZOrderHelper.GetChildrenInZOrder(parent);
        Assert.Equal([c[4], c[1], c[3], c[5], c[0], c[2]], first);

        Assert.Equal(0, Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++)
                ZOrderHelper.GetChildrenInZOrder(parent);
        }));
        Assert.Same(first, ZOrderHelper.GetChildrenInZOrder(parent));
    }

    /// <summary>A ZIndex written in a handler asks for a repaint only (the style-mutation
    /// analyzer says so), and layout never runs: the parent hears of it from the setter.</summary>
    [Fact]
    public void SettingAChildsZIndex_SortsAgain()
    {
        var (parent, c) = Stacked();
        var before = ZOrderHelper.GetChildrenInZOrder(parent);

        c[1].Position.ZIndex = 2;

        var after = ZOrderHelper.GetChildrenInZOrder(parent);
        Assert.NotSame(before, after);
        Assert.Equal([c[4], c[3], c[5], c[0], c[2], c[1]], after);

        c[1].Position.ZIndex = 2;   // the same value: nothing to do again
        Assert.Same(after, ZOrderHelper.GetChildrenInZOrder(parent));
    }

    [Fact]
    public void AChildTurningStickyOrBack_SortsAgain()
    {
        var (parent, c) = Stacked();
        ZOrderHelper.GetChildrenInZOrder(parent);

        c[3].Position.Type = PositionType.Sticky;
        Assert.Equal([c[4], c[1], c[5], c[0], c[3], c[2]], ZOrderHelper.GetChildrenInZOrder(parent));

        c[0].Position.Type = PositionType.Relative;
        Assert.Equal([c[4], c[0], c[1], c[5], c[3], c[2]], ZOrderHelper.GetChildrenInZOrder(parent));
    }

    /// <summary>The last raised child going back to zero leaves the list in its own order.</summary>
    [Fact]
    public void NoChildRaisedAnyMore_GivesTheListBack()
    {
        var parent = BoxElement.New();
        var a = BoxElement.New();
        var b = BoxElement.New();
        a.Position.ZIndex = 1;
        parent.ReplaceChildren(a, b);
        Assert.Equal([b, a], ZOrderHelper.GetChildrenInZOrder(parent));

        a.Position.ZIndex = 0;

        Assert.Same(parent.Children, ZOrderHelper.GetChildrenInZOrder(parent));
    }

    [Fact]
    public void AddingAChild_SortsAgain()
    {
        var (parent, c) = Stacked();
        ZOrderHelper.GetChildrenInZOrder(parent);
        var added = BoxElement.New();
        added.Position.ZIndex = -2;

        parent.AddChild(added);

        Assert.Equal([added, c[4], c[1], c[3], c[5], c[0], c[2]], ZOrderHelper.GetChildrenInZOrder(parent));
    }

    [Fact]
    public void InsertingAChild_SortsAgain()
    {
        var (parent, c) = Stacked();
        ZOrderHelper.GetChildrenInZOrder(parent);
        var inserted = BoxElement.New();

        parent.InsertChild(2, inserted);

        Assert.Equal([c[4], c[1], inserted, c[3], c[5], c[0], c[2]], ZOrderHelper.GetChildrenInZOrder(parent));
    }

    [Fact]
    public void RemovingAChild_SortsAgain()
    {
        var (parent, c) = Stacked();
        ZOrderHelper.GetChildrenInZOrder(parent);

        parent.RemoveChild(c[2]);

        Assert.Equal([c[4], c[1], c[3], c[5], c[0]], ZOrderHelper.GetChildrenInZOrder(parent));
    }

    /// <summary>A reorder (a keyed rebuild that moved rows) replaces the children.</summary>
    [Fact]
    public void ReplacingTheChildren_SortsAgain()
    {
        var (parent, c) = Stacked();
        ZOrderHelper.GetChildrenInZOrder(parent);

        parent.ReplaceChildren(c[5], c[4], c[3], c[2], c[1], c[0]);

        Assert.Equal([c[4], c[5], c[3], c[1], c[0], c[2]], ZOrderHelper.GetChildrenInZOrder(parent));
    }

    /// <summary>A child moved to another parent leaves the first one's order with it.</summary>
    [Fact]
    public void MovingAChildAway_SortsAgain()
    {
        var (parent, c) = Stacked();
        ZOrderHelper.GetChildrenInZOrder(parent);
        var elsewhere = BoxElement.New();

        elsewhere.AddChild(c[2]);

        Assert.Equal([c[4], c[1], c[3], c[5], c[0]], ZOrderHelper.GetChildrenInZOrder(parent));
    }

    /// <summary>A rebuild that changes a kept child's ZIndex patches it in through
    /// <see cref="PositionStyle"/>'s setters, like a handler would.</summary>
    [Fact]
    public void APatchThatChangesAZIndex_SortsAgain()
    {
        var (parent, c) = Stacked();
        ZOrderHelper.GetChildrenInZOrder(parent);
        var rebuilt = BoxElement.New();
        rebuilt.Position.ZIndex = 5;

        c[1].PatchFrom(rebuilt);

        Assert.Equal([c[4], c[3], c[5], c[0], c[2], c[1]], ZOrderHelper.GetChildrenInZOrder(parent));
    }

    /// <summary>The kept order is the one the sort it replaced gave, by ZIndex, then sticky, then
    /// place: random lists of every length up to 300 against that sort, written out again.</summary>
    [Fact]
    public void TheOrderIsTheStableSortByZIndexThenStickyThenPlace()
    {
        var random = new Random(1711);
        for (int n = 2; n <= 300; n++)
        {
            var children = new BoxElement[n];
            for (int i = 0; i < n; i++)
            {
                children[i] = BoxElement.New();
                int roll = random.Next(10);
                if (roll < 3) children[i].Position.ZIndex = random.Next(-3, 4);
                if (roll == 9) children[i].Position.ZIndex = random.Next(2) == 0 ? int.MinValue : int.MaxValue;
                if (random.Next(6) == 0) children[i].Position.Type = PositionType.Sticky;
            }
            var parent = BoxElement.New();
            parent.ReplaceChildren(children);

            Assert.Equal(ReferenceOrder(children), ZOrderHelper.GetChildrenInZOrder(parent));
        }
    }

    private static Element[] ReferenceOrder(Element[] children)
    {
        var order = Enumerable.Range(0, children.Length).ToArray();
        Array.Sort(order, (a, b) =>
        {
            var pa = children[a].PositionRead;
            var pb = children[b].PositionRead;
            int c = pa.ZIndex.CompareTo(pb.ZIndex);
            if (c != 0) return c;
            c = (pa.Type == PositionType.Sticky).CompareTo(pb.Type == PositionType.Sticky);
            return c != 0 ? c : a.CompareTo(b);
        });
        return order.Select(i => children[i]).ToArray();
    }
}
