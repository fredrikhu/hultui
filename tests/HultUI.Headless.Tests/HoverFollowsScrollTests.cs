using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Content that scrolls under a resting pointer takes the hover, as browsers do: the frame that shows
/// the scroll hit-tests the pointer again before it draws, so the row now under the pointer gets its
/// enter, its hover look and its list's tint in that frame, with no frame of its own. Until plan 17's
/// round 14 hover stayed on the row that scrolled away until the pointer moved (Junction's History
/// after 4 of 4 wheel series). Not while a press holds the pointer, nor when the pointer is outside
/// the window.
/// </summary>
public class HoverFollowsScrollTests
{
    private static readonly Color Tint = new(30, 150, 90);
    private static readonly Color Red = new(200, 30, 30);

    /// <summary>A hundred rows of 24 px that draw <see cref="Tint"/> under the hovered one.</summary>
    private static VirtualListElement<string> TintedList() =>
        VirtualListElement<string>.New().ItemHeight(24f)
            .Items(Enumerable.Range(0, 100).Select(i => $"r{i}").ToList())
            .RenderWith((item, _) => Flex.Row().Children(HultUI.Core.Builders.Text.New(item).FontSize(12f)))
            .HoverHighlight(Tint)
            .FlexGrow(1);

    /// <summary>A scroll view of twenty 50 px boxes that turn <see cref="Red"/> under the pointer.</summary>
    private static (ScrollViewElement View, List<BoxElement> Boxes) StyledBoxes(Action<int, BoxElement>? each = null,
        bool styled = true)
    {
        var boxes = new List<BoxElement>();
        for (int i = 0; i < 20; i++)
        {
            var box = Box.New().Height(50f).FlexShrink(0);
            if (styled) box.OnHover(b => b.Background(Red));
            each?.Invoke(i, box);
            boxes.Add(box);
        }
        var view = ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children([.. boxes]));
        return (view, boxes);
    }

    private static HeadlessSession Mount(Element content)
    {
        var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().Width(400f).Height(300f).Children(content));
        session.PumpFrames(2);
        return session;
    }

    /// <summary>The frame the window last drew, read from its surface (not a fresh render).</summary>
    private static SKBitmap Shown(HeadlessSession session)
    {
        using var image = ((SkiaRasterRenderSurface)session.Window.Window.PlatformWindow.Surface).Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static (byte, byte, byte) Rgb(SKColor c) => (c.Red, c.Green, c.Blue);
    private static (byte, byte, byte) Rgb(Color c) => (c.R, c.G, c.B);

    /// <summary>The list's row under window point (<paramref name="x"/>, <paramref name="y"/>).</summary>
    private static Element RowAt(VirtualListElement<string> list, float x, float y) =>
        list.Children.Single(row => row.WindowBorderBox.Contains(new Point(x, y)));

    [Fact]
    public void AWheelSeriesUnderAStillPointer_TintsTheRowUnderIt_InEachScrollsOwnFrame()
    {
        var list = TintedList();
        using var session = Mount(list);
        session.Window.MoveMouse(200, 36);   // the second row
        session.PumpFrames(2);
        var first = RowAt(list, 200, 36);
        Assert.True(first.IsHovered);

        for (int notch = 1; notch <= 3; notch++)
        {
            session.Window.Wheel(200, 36, 0, -1);
            Assert.True(session.PumpFrame());    // the scroll's frame
            Assert.Equal(40f * notch, list.ScrollOffsetY);
            var under = RowAt(list, 200, 36);
            Assert.True(under.IsHovered, $"notch {notch}: the row under the pointer has no hover");
            Assert.Single(list.Children, row => row.IsHovered);
            using (var frame = Shown(session))
                Assert.Equal(Rgb(Tint), Rgb(frame.GetPixel(380, 36)));
            Assert.False(session.PumpFrame());   // and no frame of its own
        }
        Assert.False(first.IsHovered);
    }

    [Fact]
    public void AWheelUnderAStillPointer_MovesTheHoverStyle_InTheScrollsOwnFrame()
    {
        var (view, boxes) = StyledBoxes();
        using var session = Mount(view);
        session.Window.MoveMouse(200, 75);   // the second box, 50 to 100
        session.PumpFrames(2);
        Assert.Equal(Red, boxes[1].BackgroundColor);

        session.Window.Wheel(200, 75, 0, -1);   // 40 px: the pointer is over the third box, 100 to 150
        Assert.True(session.PumpFrame());

        Assert.True(boxes[2].IsHovered);
        Assert.False(boxes[1].IsHovered);
        Assert.Equal(Red, boxes[2].BackgroundColor);
        Assert.NotEqual(Red, boxes[1].BackgroundColor);
        using (var frame = Shown(session))
        {
            Assert.Equal(Rgb(Red), Rgb(frame.GetPixel(200, 75)));
            Assert.NotEqual(Rgb(Red), Rgb(frame.GetPixel(200, 30)));   // the second box, moved up to -40..10
        }
        Assert.False(session.PumpFrame());
    }

    /// <summary>A scroll from code (a key's, a reveal's, an app's) is followed the same way.</summary>
    [Fact]
    public void AScrollFromCode_MovesHoverInItsOwnFrame()
    {
        var list = TintedList();
        using var session = Mount(list);
        session.Window.MoveMouse(200, 36);
        session.PumpFrames(2);

        list.ScrollOffsetY = 1200f;   // fifty rows: the row now under the pointer was never made
        Assert.True(session.PumpFrame());

        var under = RowAt(list, 200, 36);
        Assert.True(under.IsHovered);
        Assert.Single(list.Children, row => row.IsHovered);
        using (var frame = Shown(session))
            Assert.Equal(Rgb(Tint), Rgb(frame.GetPixel(380, 36)));
        Assert.False(session.PumpFrame());
    }

    /// <summary>An enter handler that changes a layout the frame has done is laid out again in that
    /// frame: left marked, the end of the frame dropped the mark and the box kept its old size.</summary>
    [Fact]
    public void AnEnterHandlerThatMarksLayout_IsLaidOutInTheScrollsFrame()
    {
        var (view, boxes) = StyledBoxes((i, box) =>
        {
            if (i == 2) box.OnPointerEnter(_ => { box.Box.Height = 80f; box.MarkLayoutDirty(); });
        });
        using var session = Mount(view);
        session.Window.MoveMouse(200, 75);
        session.PumpFrames(2);

        session.Window.Wheel(200, 75, 0, -1);
        Assert.True(session.PumpFrame());

        Assert.True(boxes[2].IsHovered);
        Assert.Equal(80f, boxes[2].Layout.Height);
        Assert.Equal(180f, boxes[3].Layout.AbsoluteY);
    }

    [Fact]
    public void AHeldButton_KeepsItsTarget_AndTheReleaseFollows()
    {
        var (view, boxes) = StyledBoxes();
        using var session = Mount(view);
        session.Window.MoveMouse(200, 75);
        session.PumpFrames(2);
        session.Window.MouseDown(200, 75);
        session.PumpFrames(2);

        session.Window.Wheel(200, 75, 0, -1);
        session.PumpFrames(2);
        Assert.True(boxes[1].IsHovered);    // a press keeps its target
        Assert.False(boxes[2].IsHovered);

        session.Window.MouseUp(200, 75);
        Assert.True(session.PumpFrame());   // the release's frame follows
        Assert.True(boxes[2].IsHovered);
        Assert.False(boxes[1].IsHovered);
        Assert.Equal(Red, boxes[2].BackgroundColor);
    }

    /// <summary>Boxes that show no hover and hear no press: the release draws nothing of its own, so
    /// it asks for the frame that follows the scroll.</summary>
    [Fact]
    public void AReleaseAfterAHeldScroll_AsksForTheFrameThatFollows()
    {
        var (view, boxes) = StyledBoxes(styled: false);
        using var session = Mount(view);
        session.Window.MoveMouse(200, 75);
        session.PumpFrames(2);
        session.Window.MouseDown(200, 75);
        session.PumpFrames(2);
        session.Window.Wheel(200, 75, 0, -1);
        session.PumpFrames(2);
        Assert.True(boxes[1].IsHovered);

        session.Window.MouseUp(200, 75);
        Assert.True(session.PumpFrame());
        Assert.True(boxes[2].IsHovered);
        Assert.False(boxes[1].IsHovered);
    }

    [Fact]
    public void APointerOutsideTheWindow_FollowsNothing()
    {
        int entered = 0;
        var (view, boxes) = StyledBoxes((i, box) => { if (i == 2) box.OnPointerEnter(_ => entered++); });
        using var session = Mount(view);
        session.Window.MoveMouse(200, 75);
        session.PumpFrames(2);
        session.Window.MouseLeaveWindow();
        session.PumpFrames(2);

        view.ScrollOffsetY = 40f;
        session.PumpFrames(2);

        Assert.Equal(0, entered);
        Assert.DoesNotContain(boxes, b => b.IsHovered);
    }

    /// <summary>A tooltip showing for what scrolled away closes, as on a move off it; the one for what
    /// is now under the pointer opens after the usual delay.</summary>
    [Fact]
    public void ATooltipOfWhatScrolledAway_Closes_AndTheNewOneWaitsItsDelay()
    {
        var (view, _) = StyledBoxes((i, box) => box.Tooltip($"box {i}"));
        using var session = Mount(view);
        session.Window.MoveMouse(200, 75);
        session.PumpFrame(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { "box 1" }, TooltipTexts(session));

        session.Window.Wheel(200, 75, 0, -1);
        session.PumpFrame();

        Assert.Empty(TooltipTexts(session));
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        Assert.Empty(TooltipTexts(session));
        session.PumpFrame(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { "box 2" }, TooltipTexts(session));
    }

    private static List<string> TooltipTexts(HeadlessSession session) =>
        session.Window.OverlayContents
            .SelectMany(o => o.Children)
            .OfType<TextElement>()
            .Select(t => t.Content)
            .ToList();
}
