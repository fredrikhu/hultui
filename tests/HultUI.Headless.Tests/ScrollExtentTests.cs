using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A scroll view knows how far it can scroll as soon as it has been laid out, whether or not
/// anything has painted it.
///
/// <para>The extent used to be computed by the renderer, and the renderer culls: a subtree that
/// falls outside a clipping ancestor is skipped entirely. So a scroll view that had not been
/// painted reported a content size of zero, and because the offset setters clamp against that,
/// scrolling it did nothing and reported nothing. A vertical scroll view nested inside another
/// vertical one hit this every time — whatever sits above it in the outer view is usually tall
/// enough to push it outside the outer's clip on the first frame, so it arrived permanently
/// unable to scroll.</para>
///
/// <para>Note that merely being off the bottom of the <i>window</i> is not enough to reproduce
/// it: there is no clip above the root, so the renderer still visits those elements. It takes a
/// clipping ancestor, which is exactly what the reported case had.</para>
/// </summary>
public class ScrollExtentTests
{
    [Fact]
    public void AViewClippedAwayByAnAncestorKnowsItsExtent()
    {
        using var session = HeadlessSession.Create(400, 300);

        var clipped = ScrollView.New().Height(200f).FlexShrink(0).Children(
            Box.New().Height(900f).FlexShrink(0));

        // 400px of filler inside a 300px clip: `clipped` is culled, not painted.
        session.Application.SetRoot(Flex.Column(gap: 0)
            .Height(300f).Overflow(Overflow.Hidden)
            .Children(Box.New().Height(400f).FlexShrink(0), clipped));
        session.PumpFrames(2);

        Assert.Equal(900f, clipped.ContentHeight);

        clipped.ScrollOffsetY = 300f;
        Assert.Equal(300f, clipped.ScrollOffsetY);
    }

    /// <summary>The case that was reported: a scrolling pane inside a scrolling page.</summary>
    [Fact]
    public void AVerticalViewNestedInAVerticalViewScrolls()
    {
        using var session = HeadlessSession.Create(400, 300);

        var inner = ScrollView.New().Height(200f).FlexShrink(0).Children(
            Box.New().Height(400f).FlexShrink(0),
            Box.New().Height(400f).FlexShrink(0));
        var outer = ScrollView.New().Height(300f).Children(
            Box.New().Height(300f).FlexShrink(0), inner, Box.New().Height(300f).FlexShrink(0));

        session.Application.SetRoot(outer);
        session.PumpFrames(2);

        Assert.Equal(800f, inner.ContentHeight);
        Assert.Equal(800f, outer.ContentHeight);

        inner.ScrollOffsetY = 250f;
        outer.ScrollOffsetY = 120f;
        session.PumpFrames(2);

        Assert.Equal(250f, inner.ScrollOffsetY);
        Assert.Equal(120f, outer.ScrollOffsetY);
    }

    /// <summary>Both offsets land on the element under them, which is the point of getting the
    /// extent right — an extent of zero is not a wrong number on screen, it is a view that
    /// refuses to move.</summary>
    [Fact]
    public void BothOffsetsReachTheScreenBox()
    {
        using var session = HeadlessSession.Create(400, 300);

        var target = Box.New().Height(50f).FlexShrink(0);
        var inner = ScrollView.New().Height(200f).FlexShrink(0).Children(
            Box.New().Height(400f).FlexShrink(0), target, Box.New().Height(400f).FlexShrink(0));
        var outer = ScrollView.New().Height(300f).Children(
            Box.New().Height(300f).FlexShrink(0), inner, Box.New().Height(300f).FlexShrink(0));

        session.Application.SetRoot(outer);
        session.PumpFrames(2);

        var layoutBox = target.Layout.AbsoluteBorderBox;
        outer.ScrollOffsetY = 90f;
        inner.ScrollOffsetY = 40f;
        session.PumpFrames(2);

        Assert.Equal(130f, target.AncestorScrollOffset.Y);
        Assert.Equal(layoutBox.Y - 130f, target.ScreenBorderBox.Y);
    }

    /// <summary>
    /// The wheel reaches a nested pane. Coverage rather than a regression guard for the fix
    /// above, and worth being clear about which: this pane is inside the outer's clip from the
    /// first frame, so it was painted, so it knew its extent even before. Wheeling was never
    /// the broken path — the wheel only arrives once the pane is on screen, and being on screen
    /// is exactly what used to make it measurable. What was broken is everything that scrolls a
    /// view <i>before</i> it has been painted: restoring a saved offset, revealing a search hit,
    /// scrolling an item into view on the frame the pane is built.
    ///
    /// <para>The inner view still swallows the wheel once it has nothing left to give, rather
    /// than handing the rest to the page. That is a separate, older complaint about which view
    /// claims the event, and it is untouched here.</para>
    /// </summary>
    [Fact]
    public void TheWheelScrollsTheInnerView()
    {
        using var session = HeadlessSession.Create(400, 300);

        var inner = ScrollView.New().Height(250f).FlexShrink(0).Children(
            Box.New().Height(400f).FlexShrink(0), Box.New().Height(400f).FlexShrink(0));
        var outer = ScrollView.New().Height(300f).Children(
            Box.New().Height(20f).FlexShrink(0), inner, Box.New().Height(600f).FlexShrink(0));

        session.Application.SetRoot(outer);
        session.PumpFrames(2);

        session.Window.Wheel(200, 150, 0, -3);   // pointer inside the inner pane
        session.PumpFrames(2);

        Assert.True(inner.ScrollOffsetY > 0, $"inner did not move (offset {inner.ScrollOffsetY})");
        Assert.Equal(0f, outer.ScrollOffsetY);
    }

    /// <summary>A view whose content shrinks below the viewport gives its offset back. The
    /// clamp used to be part of the same render-time call, so this is worth keeping honest.</summary>
    [Fact]
    public void ShrinkingTheContentReleasesTheOffset()
    {
        using var session = HeadlessSession.Create(400, 300);

        var tall = Box.New().Height(900f).FlexShrink(0);
        var scroll = ScrollView.New().Height(200f).Children(tall);
        session.Application.SetRoot(scroll);
        session.PumpFrames(2);

        scroll.ScrollOffsetY = 500f;
        Assert.Equal(500f, scroll.ScrollOffsetY);

        tall.Height(100f);
        tall.MarkLayoutDirty();
        session.PumpFrames(2);

        Assert.Equal(100f, scroll.ContentHeight);
        Assert.Equal(0f, scroll.ScrollOffsetY);
    }
}
