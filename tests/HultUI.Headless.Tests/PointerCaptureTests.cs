using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="PointerDownEvent.CapturePointer"/>: the element that captured on its press
/// gets every move and the release wherever the pointer goes, hover stands still until
/// then, and a slider therefore keeps following a drag that leaves its track.
/// </summary>
public class PointerCaptureTests
{
    [Fact]
    public void ACapturedElement_GetsMovesAndTheRelease_OutsideItsBounds()
    {
        using var session = HeadlessSession.Create(400, 300);
        var moves = new List<Point>();
        int leaves = 0, ups = 0, clicks = 0, neighbourEnters = 0;
        var pad = Box.New().Width(100f).Height(40f).AutomationId("pad")
            .OnPointerDown(e => e.CapturePointer())
            .OnPointerLeave(_ => leaves++)
            .OnPointerUp(_ => ups++)
            .OnClick(_ => clicks++);
        pad.On<PointerMoveEvent>(e => moves.Add(e.LocalPosition));
        var neighbour = Box.New().Width(100f).Height(40f).AutomationId("neighbour")
            .OnPointerEnter(_ => neighbourEnters++);
        session.Application.SetRoot(Flex.Column(gap: 0).AlignItems(AlignItems.FlexStart).Children(pad, neighbour));
        session.PumpFrame();

        session.Window.MouseDown(50, 20);
        moves.Clear();                            // MouseDown moves there first; that hover move is not the drag
        session.Window.MoveMouse(150, 200);       // far outside the pad, over nothing
        session.Window.MoveMouse(50, 60);         // over the neighbour

        Assert.Equal([new Point(150, 200), new Point(50, 60)], moves);
        Assert.Equal(0, leaves);                  // hover stood still
        Assert.Equal(0, neighbourEnters);

        session.Window.MouseUp(50, 60);
        Assert.Equal(1, ups);
        Assert.Equal(0, clicks);                  // released elsewhere: no click
        Assert.Equal(1, leaves);                  // hover caught up on the release
        Assert.Equal(1, neighbourEnters);

        session.Window.MoveMouse(60, 70);
        Assert.Equal(2, moves.Count);             // the capture ended with the release
    }

    [Fact]
    public void AReleaseOnTheCapturedElement_StillClicks()
    {
        using var session = HeadlessSession.Create(400, 300);
        int clicks = 0;
        var pad = Box.New().Width(100f).Height(40f)
            .OnPointerDown(e => e.CapturePointer())
            .OnClick(_ => clicks++);
        session.Application.SetRoot(Box.New().AlignItems(AlignItems.FlexStart).Children(pad));
        session.PumpFrame();

        session.Window.MouseDown(50, 20);
        session.Window.MoveMouse(300, 250);
        session.Window.MoveMouse(60, 20);
        session.Window.MouseUp(60, 20);
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void ASlider_KeepsFollowingTheDrag_AboveAndBelowItsTrack()
    {
        using var session = HeadlessSession.Create(400, 300);
        var slider = Slider.New(0, 0, 100).Width(216f);   // 200 of track + a thumb radius each side
        slider.ThumbRadius = 8f;
        session.Application.SetRoot(Box.New().Padding(50f).AlignItems(AlignItems.FlexStart).Children(slider));
        session.PumpFrame();

        var box = slider.Layout.AbsoluteContentBox;
        float trackLeft = box.X + 8f, midY = box.Y + box.Height / 2;
        session.Window.MouseDown(trackLeft, midY);
        Assert.Equal(0, slider.Value);

        session.Window.MoveMouse(trackLeft + 100, midY + 120);   // well below the track
        Assert.Equal(50, slider.Value);
        session.Window.MoveMouse(trackLeft + 150, box.Y - 90);   // well above it
        Assert.Equal(75, slider.Value);

        session.Window.MouseUp(trackLeft + 150, box.Y - 90);
        session.Window.MoveMouse(trackLeft + 20, box.Y - 90);    // released: a plain move changes nothing
        Assert.Equal(75, slider.Value);
    }
}
