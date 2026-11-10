using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A pan's viewport is a laid-out width, and a build comes before the layout of its frame: an
/// owner that reads the width after the frame (a resize, or a pane beside it dragged, lays out
/// again without building) assigns it then, and the scrollbar on screen follows with no build
/// (plan 17 round 10 step 13).
/// </summary>
public class HorizontalPanFollowTests
{
    private sealed class Panned : Component
    {
        public readonly HorizontalPan Pan;
        public int Builds;
        public void Rebuild() => MarkDirty();

        public Panned() => Pan = new HorizontalPan(MarkDirty) { ContentWidth = 1000, ViewportWidth = 400 };

        public override Element Build()
        {
            Builds++;
            return Flex.Column(gap: 0).Width(500).Children(Box.New().Height(40), Pan.Scrollbar(Color.Black, Color.White, Color.Black));
        }
    }

    private static Rect Thumb(Panned panned) => panned.Pan.ScrollbarElement!.Children[1].Layout.AbsoluteBorderBox;
    private static Rect Track(Panned panned) => panned.Pan.ScrollbarElement!.Layout.AbsoluteContentBox;

    [Fact]
    public void AViewportAssignedAfterTheBuild_MovesTheThumbOnScreen_WithNoBuild()
    {
        using var session = HeadlessSession.Create(width: 800, height: 600);
        var panned = new Panned();
        session.Window.SetRoot(panned.Mount());
        session.PumpFrame();
        Assert.Equal(200, Thumb(panned).Width, 1);   // 400 of 1000 over a 500 px track

        panned.Pan.ViewportWidth = 250;
        session.PumpFrame();
        Assert.Equal(125, Thumb(panned).Width, 1);
        Assert.Equal(1, panned.Builds);

        // At the end of the track, and kept there when the content grows: the thumb shrinks
        // and the offset, now short of the end, puts it where it is along the way.
        panned.Pan.PanTo(float.MaxValue);   // builds, as an offset that moves does
        session.PumpFrame();
        Assert.Equal(Track(panned).X + Track(panned).Width, Thumb(panned).X + Thumb(panned).Width, 1);
        int builds = panned.Builds;
        panned.Pan.ContentWidth = 1500;
        session.PumpFrame();
        Assert.Equal(500 * 250f / 1500, Thumb(panned).Width, 1);
        float travel = Track(panned).Width - Thumb(panned).Width;
        Assert.Equal(Track(panned).X + travel * (750f / 1250f), Thumb(panned).X, 1);
        Assert.Equal(builds, panned.Builds);
    }

    [Fact]
    public void WhatTheThumbFollows_IsWhatABuildMakes()
    {
        using var session = HeadlessSession.Create(width: 800, height: 600);
        var panned = new Panned();
        session.Window.SetRoot(panned.Mount());
        session.PumpFrame();
        panned.Pan.PanBy(300);
        session.PumpFrame();

        panned.Pan.ViewportWidth = 330;
        session.PumpFrame();
        var followed = Thumb(panned);

        panned.Rebuild();
        session.PumpFrame();
        Assert.Equal(followed, Thumb(panned));
    }
}
