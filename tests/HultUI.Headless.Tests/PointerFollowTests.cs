using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A splitter's, a pan's and a slider's drag, and a slider's hover, follow the pointer once per
/// frame that may be drawn, at its latest position, not on every move (plan 17 round 3; the user
/// chose these, and every other pointer-move handler still gets every move). A 1,000 Hz mouse
/// sends about sixteen moves for each 60 Hz frame, and each rebuilt the owner. The release ends
/// the drag exactly where the pointer was, with or without a frame in between.
/// </summary>
public class PointerFollowTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    /// <summary>A pane, a splitter and the rest, rebuilt at the pane's size; counts its builds
    /// and the moves an app handler on its root hears.</summary>
    private sealed class Split : Component
    {
        public readonly PaneSplitter Splitter;
        public int Builds, Commits, MovesHeard;
        public ComponentHost? Host;

        public Split() => Splitter = new PaneSplitter(MarkDirty, () => Commits++, 100, 600, 200);

        public override Element Build()
        {
            Builds++;
            var root = Flex.Row().AlignItems(AlignItems.Stretch).Children(
                Box.New().Width(Dimension.Px(Splitter.Size)).FlexShrink(0),
                Splitter.Handle(),
                Box.New().FlexGrow(1));
            root.On<PointerMoveEvent>(_ => MovesHeard++);
            return root;
        }

        public Element Pane => Host!.Children[0].Children[0];
        public Element Handle => Host!.Children[0].Children[1];
    }

    private static (HeadlessSession Session, Split Split, Point Grab) MountSplit()
    {
        var session = HeadlessSession.Create(800, 400);
        var split = new Split();
        var host = split.Mount();
        split.Host = host;
        split.Splitter.AttachDrag(host);
        session.Window.SetRoot(host);
        session.PumpFrame();
        var b = split.Handle.Layout.AbsoluteBorderBox;
        return (session, split, new Point(b.X + b.Width / 2, b.Y + b.Height / 2));
    }

    [Fact]
    public void MovesBetweenFrames_RebuildTheSplitterOnce_AtTheLastPosition()
    {
        var (session, split, grab) = MountSplit();
        using var _ = session;
        session.Window.MouseDown(grab.X, grab.Y);
        session.PumpFrame();
        int builds = split.Builds, heard = split.MovesHeard;

        for (int i = 1; i <= 10; i++)
            session.Window.MoveMouse(grab.X + 3 * i, grab.Y);

        // Nothing rebuilt yet, every move heard by the app's own handler.
        Assert.Equal(builds, split.Builds);
        Assert.Equal(200, split.Splitter.Size, 1);
        Assert.Equal(heard + 10, split.MovesHeard);

        Assert.True(session.PumpFrame());
        Assert.Equal(builds + 1, split.Builds);
        Assert.Equal(230, split.Splitter.Size, 1);
        Assert.Equal(230, split.Pane.Layout.AbsoluteBorderBox.Width, 1);
    }

    [Fact]
    public void TheRelease_EndsTheDragExactlyWhereThePointerWas_WithNoFrameBetween()
    {
        var (session, split, grab) = MountSplit();
        using var _ = session;
        session.Window.MouseDown(grab.X, grab.Y);
        for (int i = 1; i <= 7; i++)
            session.Window.MoveMouse(grab.X + 5 * i, grab.Y);
        session.Window.MouseUp(grab.X + 35, grab.Y);

        Assert.Equal(235, split.Splitter.Size, 1);
        Assert.False(split.Splitter.IsDragging);
        Assert.Equal(1, split.Commits);
        session.PumpFrames(2);
        Assert.Equal(235, split.Splitter.Size, 1);
        Assert.Equal(235, split.Pane.Layout.AbsoluteBorderBox.Width, 1);
    }

    [Fact]
    public void A1kHzMouse_RebuildsTheSplitterAboutOncePerFrame_InTheRealLoop()
    {
        var (session, split, grab) = MountSplit();
        using var _ = session;
        int frames = 0;
        session.Window.Window.FrameRendered += () => frames++;
        session.Window.MouseDown(grab.X, grab.Y);
        session.PumpFrame();
        int builds = split.Builds, heard = split.MovesHeard, moves = 0;
        frames = 0;
        float x = grab.X;
        Action move = () => { x += 0.5f; moves++; session.Window.MoveMouse(x, grab.Y); };

        session.RunLoop(() => frames >= 20, eventEvery: TimeSpan.FromMilliseconds(1),
            onEvent: () => Application.InvokeOnMainThread(move));

        int rebuilt = split.Builds - builds;
        Assert.True(moves >= 300, $"{moves} moves in 20 frames");
        Assert.Equal(moves, split.MovesHeard - heard);
        Assert.True(rebuilt <= frames + 1, $"{rebuilt} rebuilds for {frames} frames drawn ({moves} moves)");
        session.Window.MouseUp(x, grab.Y);
        // Within the half pixel PaneSplitter ignores.
        Assert.True(Math.Abs(200 + (x - grab.X) - split.Splitter.Size) < 0.5f,
            $"the pane ended at {split.Splitter.Size}, the pointer {x - grab.X} px on from 200");
    }

    /// <summary>Content twice the width of its 400-unit cell, and the pan's scrollbar under it:
    /// a thumb of 80 on a 400 track, so a pixel of thumb travel is 5 of offset.</summary>
    private sealed class Panned : Component
    {
        public readonly HorizontalPan Pan;
        public int Changes;

        public Panned() => Pan = new HorizontalPan(() => { Changes++; MarkDirty(); }) { ContentWidth = 2000, ViewportWidth = 400 };

        public override Element Build() =>
            Flex.Column().Width(Dimension.Px(400)).Children(
                Pan.Cell(Box.New().Width(Dimension.Px(2000)).Height(Dimension.Px(50))),
                Pan.Scrollbar(Color.Black, Color.White, Color.Black));
    }

    [Fact]
    public void APanDrag_MovesTheContentOnceAFrame_AndEndsWhereThePointerWas()
    {
        using var session = HeadlessSession.Create(600, 200);
        var panned = new Panned();
        var host = panned.Mount();
        panned.Pan.AttachDrag(host);
        session.Window.SetRoot(host);
        session.PumpFrames(2);
        var track = host.Children[0].Children[1].Layout.AbsoluteContentBox;
        float x = track.X + 40, y = track.Y + track.Height / 2;    // the middle of the thumb
        session.Window.MouseDown(x, y);
        session.PumpFrame();
        Assert.True(panned.Pan.IsDragging);
        int changes = panned.Changes;

        for (int i = 1; i <= 10; i++)
            session.Window.MoveMouse(x + 4 * i, y);
        Assert.Equal(changes, panned.Changes);
        Assert.Equal(0, panned.Pan.Offset);

        session.PumpFrame();
        Assert.Equal(changes + 1, panned.Changes);
        Assert.Equal(200, panned.Pan.Offset, 1);

        session.Window.MoveMouse(x + 44, y);
        session.Window.MouseUp(x + 44, y);
        Assert.Equal(220, panned.Pan.Offset, 1);
        Assert.False(panned.Pan.IsDragging);
    }

    /// <summary>A slider whose owner keeps the value and rebuilds from it, and records what the
    /// slider reports.</summary>
    private sealed class Owner : Component
    {
        public double Value = 0;
        public int Builds;
        public readonly List<double> Changes = [];
        public readonly List<double?> Hovers = [];
        public int MovesHeard;

        public override Element Build()
        {
            Builds++;
            var slider = Slider.New(Value, 0, 200).Width(Dimension.Px(216))
                .OnValueChanged(v => { Value = v; Changes.Add(v); MarkDirty(); });
            slider.ThumbRadius = 8f;
            slider.OnHoverValue = at => Hovers.Add(at);
            slider.On<PointerMoveEvent>(_ => MovesHeard++);
            return Box.New().Padding(20f).AlignItems(AlignItems.FlexStart).Children(slider);
        }
    }

    /// <summary>The owner mounted; x of the track's left end (value 0, 1 px a unit) and its middle y.</summary>
    private static (HeadlessSession, Owner, ComponentHost, float Left, float Y) MountSlider()
    {
        var session = HeadlessSession.Create(400, 200);
        var owner = new Owner();
        var host = owner.Mount();
        session.Window.SetRoot(host);
        session.PumpFrame();
        var slider = (SliderElement)host.Children[0].Children[0];
        var box = slider.Layout.AbsoluteContentBox;
        return (session, owner, host, box.X + 8f, box.Y + box.Height / 2);
    }

    [Fact]
    public void ASliderDrag_ReportsOnceAFrame_AndEndsOnTheLastValue()
    {
        var (session, owner, host, left, y) = MountSlider();
        using var _ = session;
        session.Window.MouseDown(left + 10, y);
        Assert.Equal([10d], owner.Changes);           // the press sets the value at once
        session.PumpFrame();
        owner.Hovers.Clear();
        int heard = owner.MovesHeard;

        for (int i = 1; i <= 8; i++)
            session.Window.MoveMouse(left + 10 + 5 * i, y);
        Assert.Equal([10d], owner.Changes);
        Assert.Empty(owner.Hovers);
        Assert.Equal(heard + 8, owner.MovesHeard);     // the app's own move handler hears every one

        session.PumpFrame();
        Assert.Equal([10d, 50d], owner.Changes);
        Assert.Equal(50, Assert.Single(owner.Hovers)!.Value, 3);

        session.Window.MoveMouse(left + 70, y);
        session.Window.MoveMouse(left + 73, y + 60);   // below the track: the capture keeps it
        session.Window.MouseUp(left + 73, y + 60);
        Assert.Equal([10d, 50d, 73d], owner.Changes);
        Assert.Equal(73, owner.Value);
        session.PumpFrames(2);
        Assert.Equal(73, ((SliderElement)host.Children[0].Children[0]).Value);
        Assert.Equal([10d, 50d, 73d], owner.Changes);
    }

    [Fact]
    public void HoveringASlider_ReportsTheValueUnderThePointerOnceAFrame_AndLeavingDropsWhatWaited()
    {
        var (session, owner, _, left, y) = MountSlider();
        using var _ = session;
        for (int i = 0; i < 5; i++)
            session.Window.MoveMouse(left + 20 + i, y);
        Assert.Empty(owner.Hovers);
        session.PumpFrame();
        Assert.Equal(24, Assert.Single(owner.Hovers)!.Value, 3);   // the last of the five, unsnapped

        // Over it and off it again before the next frame: the value it pointed at goes with it.
        session.Window.MoveMouse(left + 40, y);
        session.Window.MoveMouse(left + 40, y + 80);
        session.PumpFrame();
        Assert.Equal(2, owner.Hovers.Count);
        Assert.Null(owner.Hovers[1]);
        Assert.Empty(owner.Changes);                  // pointing is not setting
    }
}
