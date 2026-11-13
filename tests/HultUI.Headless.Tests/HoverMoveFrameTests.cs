using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// A pointer move that changes the hovered element draws a frame only when something shows the new
/// hover, the rule <see cref="HoverAfterRebuildTests"/> pins for the settle after a build. What a
/// hover changes on screen mostly asks for its frame itself: a hover or pressed look marks its element,
/// a scrollbar its bar, a tooltip opens an overlay, and an owned control's enter and leave handlers
/// mark what they change. An app's enter or leave handler gets the frame any handler gets. Only a
/// list's tint under the row (<c>HoverHighlight</c>, drawn from the row's hover) and the DevTools
/// inspector need one from the hover itself. Every hover change asked for a frame: a sweep of the
/// pointer over Junction's diff drew 42 frames, 39 of them the pixels already on screen (plan 17
/// round 13).
/// </summary>
public class HoverMoveFrameTests
{
    private static readonly Color Plain = new(30, 30, 220), Red = new(220, 30, 30), Tint = new(30, 150, 90);

    /// <summary>Two 200 × 100 boxes, one above the other, in a 400 × 300 window, drawn and idle.</summary>
    private static HeadlessSession Idle(Element top, Element bottom)
    {
        var session = HeadlessSession.Create(400, 300);
        session.Window.SetRoot(Flex.Column().AlignItems(AlignItems.FlexStart).Children(top, bottom));
        session.PumpFrames(2);
        Assert.False(session.PumpFrame());
        return session;
    }

    private static BoxElement Plain200() => Box.New().Width(200f).Height(100f).Background(Plain);

    private static (byte, byte, byte) PixelAt(HeadlessSession session, int x, int y)
    {
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        var pixel = bitmap.GetPixel(x, y);
        return (pixel.Red, pixel.Green, pixel.Blue);
    }

    [Fact]
    public void AMoveOntoAnotherElement_DrawsNoFrame_WhenNothingDrawsItsHover()
    {
        var top = Plain200();
        var bottom = Plain200();
        using var session = Idle(top, bottom);

        session.Window.MoveMouse(50, 20);
        Assert.True(top.IsHovered);
        Assert.False(session.PumpFrame(), "the pointer came onto a box that draws no hover, and a frame drew the same pixels");

        session.Window.MoveMouse(50, 150);
        Assert.True(bottom.IsHovered);
        Assert.False(top.IsHovered);
        Assert.False(session.PumpFrame(), "hover moved between two boxes that draw none, and a frame drew the same pixels");

        session.Window.MouseLeaveWindow();
        Assert.False(bottom.IsHovered);
        Assert.False(session.PumpFrame(), "the pointer left the window from a box that draws no hover, and a frame drew");
    }

    [Fact]
    public void AMoveOntoAHoverStyledElement_DrawsItsLook_AndTheMoveOffDrawsItGone()
    {
        var top = Plain200();
        var bottom = Plain200().OnHover(b => b.Background(Red));
        using var session = Idle(top, bottom);
        session.Window.MoveMouse(50, 20);
        session.PumpFrames(2);

        session.Window.MoveMouse(50, 150);
        Assert.True(session.PumpFrame(), "the hover look drew no frame");
        Assert.Equal((Red.R, Red.G, Red.B), PixelAt(session, 50, 150));

        session.Window.MoveMouse(50, 20);
        Assert.True(session.PumpFrame(), "the hover look's end drew no frame");
        Assert.Equal((Plain.R, Plain.G, Plain.B), PixelAt(session, 50, 150));
    }

    [Fact]
    public void AMoveOntoAListRow_DrawsTheTintUnderIt()
    {
        using var session = HeadlessSession.Create(400, 300);
        var list = VirtualListElement<string>.New().ItemHeight(24f)
            .Items(Enumerable.Range(0, 20).Select(i => $"row {i}").ToList())
            .RenderWith((item, _) => Flex.Row().Children(Text.New(item).FontSize(12f)))
            .HoverHighlight(Tint)
            .FlexGrow(1);
        session.Window.SetRoot(Flex.Column().Width(400f).Height(300f).Children(list));
        session.PumpFrames(2);
        session.Window.MoveMouse(200, 12);   // the first row
        session.PumpFrames(2);

        session.Window.MoveMouse(200, 36);   // the second
        Assert.True(session.PumpFrame(), "the tint under the row the pointer came onto drew no frame");
        Assert.Equal((Tint.R, Tint.G, Tint.B), PixelAt(session, 380, 36));
        Assert.NotEqual((Tint.R, Tint.G, Tint.B), PixelAt(session, 380, 12));

        session.Window.MouseLeaveWindow();
        Assert.True(session.PumpFrame(), "the tint under the row the pointer left stayed on screen");
        Assert.NotEqual((Tint.R, Tint.G, Tint.B), PixelAt(session, 380, 36));
    }

    /// <summary>An app's enter or leave handler may change what is drawn without marking it.</summary>
    [Fact]
    public void AMoveOntoAnElementWithAnEnterHandler_DrawsAFrame()
    {
        int entered = 0, left = 0;
        var top = Plain200();
        var bottom = Plain200().OnPointerEnter(_ => entered++).OnPointerLeave(_ => left++);
        using var session = Idle(top, bottom);
        session.Window.MoveMouse(50, 20);
        session.PumpFrames(2);

        session.Window.MoveMouse(50, 150);
        Assert.Equal(1, entered);
        Assert.True(session.PumpFrame(), "the enter handler ran and no frame drew what it may have changed");

        session.Window.MoveMouse(50, 20);
        Assert.Equal(1, left);
        Assert.True(session.PumpFrame(), "the leave handler ran and no frame drew what it may have changed");
    }

    /// <summary>The inspector draws the hovered element's box: a move onto another element draws, a
    /// move within one does not.</summary>
    [Fact]
    public void WithDevToolsOpen_AMoveOntoAnotherElement_DrawsAFrame()
    {
        var top = Plain200();
        var bottom = Plain200();
        using var session = Idle(top, bottom);
        session.Window.KeyDown(KeyCode.F12);
        session.Window.KeyUp(KeyCode.F12);
        session.PumpFrames(2);
        Assert.True(session.Window.Window.DevTools.IsEnabled);
        session.Window.MoveMouse(50, 20);
        session.PumpFrames(2);

        session.Window.MoveMouse(50, 150);
        Assert.True(session.PumpFrame(), "the inspector's box did not follow the hover");

        session.Window.MoveMouse(60, 160);
        Assert.False(session.PumpFrame(), "a move within the hovered element drew a frame");
    }
}
