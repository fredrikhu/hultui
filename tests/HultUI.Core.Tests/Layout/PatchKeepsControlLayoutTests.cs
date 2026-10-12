using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// A canvas and a slider patched by a rebuild that changes nothing about their size leave their
/// layout standing (Element.PatchMarksOwnLayout), as a box or a label does; they marked it on
/// every patch, so a video player's every rebuild laid it out again. A slider's ThumbRadius is
/// the one patched value its measure reads, and a patch that changes it is laid out again.
/// </summary>
public class PatchKeepsControlLayoutTests
{
    private static BoxElement Bar(double value, float thumb, Action<HultUI.Core.Rendering.ICanvasDrawingContext> draw)
    {
        var slider = Slider.New(value, 0, 100);
        slider.ThumbRadius = thumb;
        return Flex.Row(gap: 4).Children(
            Canvas.New(draw).Width(16).Height(16),
            slider);
    }

    private static void ClearFlags(Element element)
    {
        element.DirtyFlags = DirtyFlags.None;
        foreach (var child in element.Children) ClearFlags(child);
    }

    [Fact]
    public void ACanvasAndASliderThatKeepTheirSize_KeepTheirLayout()
    {
        var parent = Box.New();
        var live = Bar(10, 8f, _ => { });
        parent.AddChild(live);
        new LayoutEngine(new StubMeasureContext()).PerformLayout(parent, 400, 100);
        ClearFlags(parent);   // as a frame leaves them

        // A new value and a new drawing callback: paint, not size.
        TreeDiffer.Reconcile(parent, live, Bar(20, 8f, _ => { }));

        var canvas = live.Children[0];
        var slider = (SliderElement)live.Children[1];
        Assert.Equal(20, slider.Value);
        Assert.False(canvas.DirtyFlags.HasFlag(DirtyFlags.Layout), "the canvas marked its layout");
        Assert.False(slider.DirtyFlags.HasFlag(DirtyFlags.Layout), "the slider marked its layout");
        Assert.False(parent.DirtyFlags.HasFlag(DirtyFlags.Layout));
        Assert.True(slider.DirtyFlags.HasFlag(DirtyFlags.Paint));
    }

    [Fact]
    public void ASliderPatchedWithALargerThumb_IsLaidOutAgain()
    {
        var parent = Box.New();
        var live = Bar(10, 8f, _ => { });
        parent.AddChild(live);
        var engine = new LayoutEngine(new StubMeasureContext());
        engine.PerformLayout(parent, 400, 100);
        var slider = (SliderElement)live.Children[1];
        Assert.Equal(20f, slider.Layout.Height);   // at least 20, else the thumb's diameter
        ClearFlags(parent);

        TreeDiffer.Reconcile(parent, live, Bar(10, 15f, _ => { }));

        Assert.True(slider.DirtyFlags.HasFlag(DirtyFlags.Layout));
        engine.PerformLayout(parent, 400, 100);
        Assert.Equal(30f, slider.Layout.Height);
    }
}
