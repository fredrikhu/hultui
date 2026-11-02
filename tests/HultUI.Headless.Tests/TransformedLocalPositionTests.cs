using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="PointerEvent.LocalPosition"/> inside a transformed subtree: the point is in the
/// element's own layout units, as hit testing sees it — so a canvas laid out at full size and
/// shown scaled down (a monitor preview, a thumbnail of a live view) reads drags correctly.
/// </summary>
public class TransformedLocalPositionTests
{
    [Fact]
    public void InAHalfScaleSubtree_TheLocalPositionIsInTheSubtreesOwnUnits()
    {
        using var session = HeadlessSession.Create(800, 600);
        var points = new List<Point>();
        var pad = Box.New().Width(400f).Height(200f).AutomationId("pad");
        pad.On<PointerDownEvent>(e => points.Add(e.LocalPosition));
        // Laid out at 400×200 at the origin, drawn at half size from the origin: scale about the
        // centre (200, 100), then shift back by a quarter of the size.
        var host = Box.New().Width(400f).Height(200f).Children(pad);
        host.Transform = new Transform2D(0f, 0.5f, 0.5f, -100f, -50f);
        session.Application.SetRoot(Box.New().AlignItems(AlignItems.FlexStart).Children(host));
        session.PumpFrame();

        session.Window.MouseDown(100, 50);    // the middle of the drawn 200×100
        session.Window.MouseUp(100, 50);
        session.Window.MouseDown(10, 5);
        session.Window.MouseUp(10, 5);

        Assert.Equal(2, points.Count);
        Assert.Equal(200f, points[0].X, 3);
        Assert.Equal(100f, points[0].Y, 3);
        Assert.Equal(20f, points[1].X, 3);
        Assert.Equal(10f, points[1].Y, 3);
    }

    [Fact]
    public void WithoutTransforms_ToElementSpace_LeavesThePointAlone()
    {
        using var session = HeadlessSession.Create(400, 300);
        var pad = Box.New().Width(100f).Height(40f);
        session.Application.SetRoot(Box.New().Padding(10).AlignItems(AlignItems.FlexStart).Children(pad));
        session.PumpFrame();

        Assert.Equal(new Point(33, 21), HitTester.ToElementSpace(pad, new Point(33, 21)));
    }

    [Fact]
    public void NestedTransforms_AreUndoneOutermostFirst()
    {
        using var session = HeadlessSession.Create(800, 600);
        var pad = Box.New().Width(400f).Height(200f);
        var inner = Box.New().Width(400f).Height(200f).Children(pad);
        inner.Transform = Transform2D.Translate(40f, 0f);
        var outer = Box.New().Width(400f).Height(200f).Children(inner);
        outer.Transform = new Transform2D(0f, 0.5f, 0.5f, -100f, -50f);
        session.Application.SetRoot(Box.New().AlignItems(AlignItems.FlexStart).Children(outer));
        session.PumpFrame();

        // Window (120, 50) → outer space (240, 100) → inner space (200, 100).
        var p = HitTester.ToElementSpace(pad, new Point(120, 50));
        Assert.Equal(200f, p.X, 3);
        Assert.Equal(100f, p.Y, 3);
    }
}
