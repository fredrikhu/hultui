using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="Element.ScreenBorderBox"/> — where an element actually is, as opposed to where
/// its layout box says it is.
///
/// <para>A scroll view never moves its children: their boxes are computed once and stay where
/// the content puts them, and scrolling is a translate applied at paint time and mirrored in
/// the hit tester. Everything that positions itself <i>against</i> an element — an overlay, a
/// tooltip, a synthesized click — needs the screen box, and every one of them used to compute
/// its own version of this or, more often, forget to.</para>
/// </summary>
public class ScreenBoxTests
{
    [Fact]
    public void WithNothingScrolledItIsTheLayoutBox()
    {
        using var session = HeadlessSession.Create(400, 300);
        var target = Box.New().Height(50f).FlexShrink(0);
        session.Application.SetRoot(ScrollView.New().Height(300f).Children(
            Box.New().Height(80f).FlexShrink(0), target));
        session.PumpFrames(2);

        Assert.Equal(target.Layout.AbsoluteBorderBox, target.ScreenBorderBox);
        Assert.Equal(0f, target.AncestorScrollOffset.Y);
    }

    [Fact]
    public void ScrollingMovesTheScreenBoxAndLeavesTheLayoutBoxAlone()
    {
        using var session = HeadlessSession.Create(400, 300);
        var target = Box.New().Height(50f).FlexShrink(0);
        var scroll = ScrollView.New().Height(300f).Children(
            Box.New().Height(500f).FlexShrink(0), target, Box.New().Height(500f).FlexShrink(0));
        session.Application.SetRoot(scroll);
        session.PumpFrames(2);

        float layoutTop = target.Layout.AbsoluteBorderBox.Y;
        scroll.ScrollOffsetY = 120f;
        session.PumpFrames(2);

        Assert.Equal(layoutTop, target.Layout.AbsoluteBorderBox.Y);
        Assert.Equal(layoutTop - 120f, target.ScreenBorderBox.Y);
        Assert.Equal(120f, target.AncestorScrollOffset.Y);
    }

    /// <summary>
    /// Nested views compose, on both axes. A pane that scrolls inside a page that scrolls is
    /// the case a single-level correction gets wrong, and it is not a rare layout.
    ///
    /// <para>The outer view scrolls horizontally on purpose. A vertical scroll view nested
    /// directly inside another vertical one measures its content as zero and cannot scroll at
    /// all — a known nesting limitation, and not what this test is about.</para>
    /// </summary>
    [Fact]
    public void NestedScrollViewsAccumulate()
    {
        using var session = HeadlessSession.Create(400, 300);
        var target = Box.New().Height(50f).FlexShrink(0);

        var inner = ScrollView.New().Height(200f).Width(300f).FlexShrink(0).Children(
            Box.New().Height(400f).FlexShrink(0), target, Box.New().Height(400f).FlexShrink(0));
        var outer = ScrollView.Horizontal().Height(300f).Children(
            Box.New().Width(300f).FlexShrink(0), inner, Box.New().Width(300f).FlexShrink(0));

        session.Application.SetRoot(outer);
        session.PumpFrames(2);

        var layoutBox = target.Layout.AbsoluteBorderBox;
        outer.ScrollOffsetX = 90f;
        inner.ScrollOffsetY = 40f;
        session.PumpFrames(2);

        Assert.Equal(90f, target.AncestorScrollOffset.X);
        Assert.Equal(40f, target.AncestorScrollOffset.Y);
        Assert.Equal(layoutBox.X - 90f, target.ScreenBorderBox.X);
        Assert.Equal(layoutBox.Y - 40f, target.ScreenBorderBox.Y);
    }
}
