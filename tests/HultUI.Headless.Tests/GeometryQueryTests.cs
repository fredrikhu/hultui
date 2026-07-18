using HultUI.Core.Builders;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

public class GeometryQueryTests(ITestOutputHelper output)
{
    [Fact]
    public void QueryReturnsRectangles_NoRecordingNeeded()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var button = Box.New().Width(120).Height(32).Children(Text.New("Click me"));
        session.Window.SetRoot(Flex.Column().Padding(20).Children(button));
        session.PumpFrame();

        // Via the accessibility query API...
        var rect = session.Window.GetByText("Click me").Bounds;
        output.WriteLine($"a11y bounds:   {rect}");

        // ...or straight off the element, with the full box model available.
        var layout = button.Layout;
        output.WriteLine($"border box:    {layout.AbsoluteBorderBox}");
        output.WriteLine($"content box:   {layout.AbsoluteContentBox}");

        Assert.Equal(20f, layout.AbsoluteBorderBox.X);
        Assert.Equal(120f, layout.AbsoluteBorderBox.Width);
    }

    [Fact]
    public void LayoutBounds_AreUnscrolled_DrawOpsAreOnScreen()
    {
        // ElementRenderer applies scroll as a render-time translation rather
        // than folding it into layout, so the two answer different questions:
        // layout says where the row is in the list, the draw op says where it
        // landed on screen.
        using var session = HeadlessSession.Create(width: 200, height: 200,
            options: HeadlessOptions.DeterministicRecording());

        // FlexShrink(0) so the rows keep their heights and the content really
        // overflows — flex children shrink to fit by default.
        var row = Box.New().Height(40).FlexShrink(0).Background(Color.Red);
        var scroll = ScrollView.New().Height(100).Children(
            Flex.Column().Children(row, Box.New().Height(400).FlexShrink(0)));
        session.Window.SetRoot(scroll);
        session.PumpFrame();

        var beforeLayout = row.Layout.AbsoluteBorderBox;

        scroll.ScrollOffsetY = 60;
        session.Window.RenderLog.Clear();
        session.PumpFrame();

        var afterLayout = row.Layout.AbsoluteBorderBox;
        output.WriteLine($"layout before scroll: {beforeLayout}");
        output.WriteLine($"layout after scroll:  {afterLayout}");
        output.WriteLine(session.Window.RenderLog.ToString());

        // Layout position is unchanged by scrolling...
        Assert.Equal(beforeLayout, afterLayout);
        // ...but the frame translated the content.
        Assert.Contains(session.Window.RenderLog.Ops, o => o.Name == "PushTranslate");
    }

    [Fact]
    public void CulledElement_HasLayoutBoundsButNoDrawOps()
    {
        // The gap layout queries can't see: an element scrolled out of view is
        // still laid out, but ElementRenderer culls it and nothing is painted.
        using var session = HeadlessSession.Create(width: 200, height: 200,
            options: HeadlessOptions.DeterministicRecording());

        var offscreen = Box.New().Height(30).FlexShrink(0).Background(Color.Blue);
        var scroll = ScrollView.New().Height(60).Children(
            Flex.Column().Children(
                Box.New().Height(500).FlexShrink(0),   // pushes the blue box below the viewport
                offscreen));
        session.Window.SetRoot(scroll);
        session.PumpFrame();

        // It has real layout geometry.
        Assert.True(offscreen.Layout.AbsoluteBorderBox.Height > 0);
        output.WriteLine($"culled element layout: {offscreen.Layout.AbsoluteBorderBox}");

        // But nothing blue was drawn.
        var log = session.Window.RenderLog.ToString();
        output.WriteLine(log);
        Assert.DoesNotContain("#0000FF", log);
    }
}
