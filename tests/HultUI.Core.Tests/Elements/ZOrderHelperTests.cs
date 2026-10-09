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
}
