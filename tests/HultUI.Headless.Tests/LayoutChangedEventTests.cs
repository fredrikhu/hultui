using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="LayoutChangedEvent"/>: an element hears its laid-out box after the frame's layout,
/// and what the handler changes is laid out in that frame. An app team used a canvas's draw as
/// that hook, and a canvas of no size is never drawn (finding 11).
/// </summary>
public class LayoutChangedEventTests
{
    /// <summary>A toolbar that shows as many 50 px items as its width fits.</summary>
    private sealed class FittingToolbar : Component
    {
        public readonly Signal<int> Fits = new(0);
        public readonly Ref<BoxElement> Bar = new();
        public int Builds;

        public override Element Build()
        {
            Builds++;
            var bar = Flex.Row().Height(Dimension.Px(30)).WithRef(Bar)
                .OnLayoutChanged(e => { if (e.SizeChanged) Fits.Value = (int)(e.Box.Width / 50); });
            for (int i = 0; i < Fits.Value; i++)
                bar.AddChild(Box.New().Width(Dimension.Px(50)).Height(Dimension.Px(30)));
            return bar;
        }
    }

    [Fact]
    public void WhatAHandlerChanges_IsLaidOutInTheSameFrame()
    {
        using var session = HeadlessSession.Create(400, 300);
        var toolbar = new FittingToolbar();
        session.Window.SetRoot(Flex.Column().Children(toolbar.Mount()));

        session.PumpFrame();
        var bar = toolbar.Bar.Current!;
        Assert.Equal(8, toolbar.Fits.Value);
        Assert.Equal(2, toolbar.Builds);   // the first build, and the one the handler asked for
        Assert.Equal(8, bar.Children.Count);
        Assert.Equal(350, bar.Children[7].Layout.AbsoluteX);   // laid out before this frame drew

        session.Window.SetClientSize(200, 300);
        session.PumpFrame();
        Assert.Equal(4, toolbar.Fits.Value);
        Assert.Equal(4, toolbar.Bar.Current!.Children.Count);
        Assert.Equal(150, toolbar.Bar.Current!.Children[3].Layout.AbsoluteX);
    }

    [Fact]
    public void ABoxOfNoHeight_HearsItsWidth()
    {
        using var session = HeadlessSession.Create(400, 300);
        var told = new List<LayoutChangedEvent>();
        var hook = Box.New().Height(Dimension.Px(0)).OnLayoutChanged(told.Add);
        session.Window.SetRoot(Flex.Column().Children(hook));

        session.PumpFrame();
        var first = Assert.Single(told);
        Assert.Null(first.Previous);
        Assert.True(first.SizeChanged);
        Assert.Equal(new Rect(0, 0, 400, 0), first.Box);

        session.Window.SetClientSize(300, 300);
        session.PumpFrame();
        Assert.Equal(2, told.Count);
        Assert.Equal(new Rect(0, 0, 300, 0), told[1].Box);
        Assert.Equal(new Rect(0, 0, 400, 0), told[1].Previous);
    }

    [Fact]
    public void OnlyABoxThatMoved_IsTold_AndAMoveIsNotASizeChange()
    {
        using var session = HeadlessSession.Create(400, 300);
        var top = new List<LayoutChangedEvent>();
        var bottom = new List<LayoutChangedEvent>();
        var middle = Box.New().Height(Dimension.Px(20));
        session.Window.SetRoot(Flex.Column().Children(
            Box.New().Height(Dimension.Px(10)).OnLayoutChanged(top.Add),
            middle,
            Box.New().Height(Dimension.Px(10)).OnLayoutChanged(bottom.Add)));
        session.PumpFrame();
        Assert.Single(top);
        Assert.Single(bottom);

        middle.Box.Height = Dimension.Px(50);
        middle.MarkLayoutDirty();
        session.PumpFrame();
        Assert.Single(top);                      // laid out again where it was
        Assert.Equal(2, bottom.Count);
        Assert.False(bottom[1].SizeChanged);     // pushed down 30 px, same size
        Assert.Equal(60, bottom[1].Box.Y);
        Assert.Equal(30, bottom[1].Previous!.Value.Y);

        session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.Single(top);
        Assert.Equal(2, bottom.Count);
    }

    [Fact]
    public void AHandlerThatNeverSettles_IsStoppedAfterTheRounds_AndGoesOnInTheNextFrame()
    {
        using var session = HeadlessSession.Create(400, 300);
        int told = 0;
        var grows = Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(10));
        grows.OnLayoutChanged(e =>
        {
            told++;
            grows.Box.Width = Dimension.Px(e.Box.Width + 1);
            grows.MarkLayoutDirty();
        });
        session.Window.SetRoot(Flex.Column().Children(grows));

        session.PumpFrame();   // returns: the frame finishes
        Assert.Equal(Window.LayoutChangeRounds, told);
        Assert.Equal(10 + Window.LayoutChangeRounds, grows.Layout.Width);
        Assert.True(session.Window.Window.NeedsRender, "the untold box asks for the next frame");

        session.PumpFrame();
        Assert.Equal(2 * Window.LayoutChangeRounds, told);
    }

    private sealed class Pane : Component
    {
        public bool Listen;
        public readonly List<LayoutChangedEvent> Told = [];
        public readonly Ref<BoxElement> Body = new();

        public override Element Build()
        {
            var body = Box.New().Height(Dimension.Px(40)).WithRef(Body);
            if (Listen) body.OnLayoutChanged(Told.Add);
            return Flex.Column().Children(body);
        }

        public void Rebuild() => MarkDirty();
    }

    [Fact]
    public void ARebuildThatAddsTheFirstHandler_TellsTheBox_WithNothingMoving()
    {
        using var session = HeadlessSession.Create(400, 300);
        var pane = new Pane();
        session.Window.SetRoot(pane.Mount());
        session.PumpFrames(2);
        var body = pane.Body.Current!;

        pane.Listen = true;
        pane.Rebuild();
        session.PumpFrame();

        Assert.Same(body, pane.Body.Current);   // patched, not made again
        var first = Assert.Single(pane.Told);
        Assert.Null(first.Previous);
        Assert.Equal(400, first.Box.Width);

        pane.Rebuild();   // the handler stays: nothing new to tell
        session.PumpFrame();
        Assert.Single(pane.Told);
    }
}
