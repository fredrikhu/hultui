using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// Covers 2D transforms: the fluent builders/reconciliation wiring and, most
/// importantly, that hit-testing maps the pointer through the inverse transform so
/// a rotated/scaled element is clickable where it actually appears.
/// </summary>
public class TransformTests
{
    private static BoxElement Layout(BoxElement box, float w, float h)
    {
        StyleResolver.Resolve(box, Theme.Light);
        new LayoutEngine(new StubMeasureContext()).PerformLayout(box, w, h);
        return box;
    }

    [Fact]
    public void NoTransform_HitTestUsesLayoutBounds()
    {
        var box = Layout(Box.New().Width(100).Height(100), 100, 100);
        Assert.Same(box, HitTester.HitTest(box, new Point(50, 50)));
        Assert.Null(HitTester.HitTest(box, new Point(150, 50)));
    }

    [Fact]
    public void Scale_ExpandsHitRegion()
    {
        var box = Layout(Box.New().Width(100).Height(100), 100, 100);
        // (140,50) is outside the 100×100 box…
        Assert.Null(HitTester.HitTest(box, new Point(140, 50)));

        box.Scale(2f); // …but a 2× scale about the centre (50,50) reaches x∈[-50,150].
        Assert.Same(box, HitTester.HitTest(box, new Point(140, 50)));
    }

    [Fact]
    public void Rotate90_MovesHitRegion()
    {
        var box = Layout(Box.New().Width(100).Height(40), 100, 40);
        // Unrotated the box is y∈[0,40]; (50,60) is below it.
        Assert.Null(HitTester.HitTest(box, new Point(50, 60)));

        box.Rotate(90f); // Rotated about centre (50,20) it fills x∈[30,70], y∈[-30,70].
        Assert.Same(box, HitTester.HitTest(box, new Point(50, 60)));
    }

    [Fact]
    public void Rotate_Then_Scale_MergeIntoOneTransform()
    {
        var box = Box.New().Rotate(10f).Scale(1.5f);
        Assert.NotNull(box.Transform);
        Assert.Equal(10f, box.Transform!.Value.RotationDegrees);
        Assert.Equal(1.5f, box.Transform.Value.ScaleX);
        Assert.Equal(1.5f, box.Transform.Value.ScaleY);
    }

    [Fact]
    public void PatchFrom_CopiesTransform()
    {
        var live = Box.New();
        var incoming = Box.New().Rotate(45f);
        live.PatchFrom(incoming);
        Assert.Equal(45f, live.Transform!.Value.RotationDegrees);
    }
}
