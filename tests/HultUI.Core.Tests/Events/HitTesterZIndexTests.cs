using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Events;

public class HitTesterZIndexTests
{
    private static void SetLayout(Element el, float x, float y, float w, float h)
    {
        el.Layout = new LayoutResult(x, y, w, h, default, default, default)
        {
            AbsoluteX = x,
            AbsoluteY = y
        };
    }

    [Fact]
    public void HitTest_ReturnsHigherZIndexChild()
    {
        // Two overlapping children — higher ZIndex should be hit
        var root = BoxElement.New();
        SetLayout(root, 0, 0, 200, 200);

        var back = BoxElement.New();
        back.Position.ZIndex = 0;
        SetLayout(back, 0, 0, 100, 100);

        var front = BoxElement.New();
        front.Position.ZIndex = 1;
        SetLayout(front, 0, 0, 100, 100);

        root.ReplaceChildren(back, front);

        var hit = HitTester.HitTest(root, new Point(50, 50));

        Assert.Same(front, hit);
    }

    [Fact]
    public void HitTest_LowerZIndexNotHitWhenOverlapped()
    {
        var root = BoxElement.New();
        SetLayout(root, 0, 0, 200, 200);

        var back = BoxElement.New();
        back.Position.ZIndex = -1;
        SetLayout(back, 0, 0, 100, 100);

        var front = BoxElement.New();
        front.Position.ZIndex = 1;
        SetLayout(front, 0, 0, 100, 100);

        // Add front first in tree order — but back has lower ZIndex, front higher
        root.ReplaceChildren(front, back);

        var hit = HitTester.HitTest(root, new Point(50, 50));

        // Front should still be hit despite being first in tree order
        Assert.Same(front, hit);
    }

    [Fact]
    public void HitTest_NoZIndex_UsesInsertionOrderLastChildOnTop()
    {
        var root = BoxElement.New();
        SetLayout(root, 0, 0, 200, 200);

        var first = BoxElement.New();
        SetLayout(first, 0, 0, 100, 100);

        var second = BoxElement.New();
        SetLayout(second, 0, 0, 100, 100);

        root.ReplaceChildren(first, second);

        var hit = HitTester.HitTest(root, new Point(50, 50));

        // Last child = rendered on top = hit first
        Assert.Same(second, hit);
    }

    [Fact]
    public void HitTest_NonOverlapping_HitsCorrectChild()
    {
        var root = BoxElement.New();
        SetLayout(root, 0, 0, 200, 100);

        var left = BoxElement.New();
        left.Position.ZIndex = 10;
        SetLayout(left, 0, 0, 100, 100);

        var right = BoxElement.New();
        right.Position.ZIndex = 0;
        SetLayout(right, 100, 0, 100, 100);

        root.ReplaceChildren(left, right);

        // Click on right side — should hit right child even though it has lower ZIndex
        var hit = HitTester.HitTest(root, new Point(150, 50));
        Assert.Same(right, hit);

        // Click on left side — should hit left child
        hit = HitTester.HitTest(root, new Point(50, 50));
        Assert.Same(left, hit);
    }
}
