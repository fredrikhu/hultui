using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
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
}
