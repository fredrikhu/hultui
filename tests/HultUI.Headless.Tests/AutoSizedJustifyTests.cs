using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="JustifyContent"/> on a container whose main size comes from its content. Such a
/// container is exactly as long as its children, so there is no free space to distribute and
/// justifying must change nothing. It used to measure the free space against the space
/// <em>available</em> to the container instead, and then report where the last child ended as
/// its own size: a centred column in a 32px row came out 24.6px tall around 17.3px of text,
/// with the text at its bottom (found 2026-10-05; the command palette worked around it).
/// A scroll view has the opposite problem: its children are distributed against unbounded
/// space, so it justified against that rather than its viewport.
/// </summary>
public sealed class AutoSizedJustifyTests
{
    private static Rect Painted(Element e) => LayoutProbe.Painted(e);

    [Fact]
    public void ACentredColumnInAFixedHeightRowIsAsTallAsItsTextAndCentred()
    {
        using var session = HeadlessSession.Create(600, 200);
        var text = TextElement.New("Pull").FontSize(13);
        var column = Flex.Column(gap: 1).FlexGrow(1).JustifyContent(JustifyContent.Center).Children(text);
        var row = Flex.Row().AlignItems(AlignItems.Center).Height(32).Width(500).Children(column);
        session.Window.SetRoot(Box.New().Children(row));
        session.PumpFrame();

        var r = Painted(row);
        var c = Painted(column);
        var t = Painted(text);
        // Centring has to mean something: the text is well short of the row.
        Assert.Equal(32f, r.Height, 0.01f);
        Assert.InRange(t.Height, 10f, 26f);

        Assert.Equal(t.Height, c.Height, 0.01f);
        Assert.Equal(c.Y, t.Y, 0.01f);
        Assert.Equal(t.Y - r.Y, r.Bottom - t.Bottom, 0.01f);
    }

    [Fact]
    public void ACentredRowInACentringColumnIsAsWideAsItsTextAndCentred()
    {
        using var session = HeadlessSession.Create(600, 200);
        var text = TextElement.New("Pull").FontSize(13);
        var row = Flex.Row().JustifyContent(JustifyContent.Center).Children(text);
        var column = Flex.Column().AlignItems(AlignItems.Center).Width(500).Height(100).Children(row);
        session.Window.SetRoot(Box.New().Children(column));
        session.PumpFrame();

        var c = Painted(column);
        var r = Painted(row);
        var t = Painted(text);
        Assert.InRange(t.Width, 10f, 200f);

        Assert.Equal(t.Width, r.Width, 0.01f);
        Assert.Equal(r.X, t.X, 0.01f);
        Assert.Equal(t.X - c.X, c.Right - t.Right, 0.01f);
    }

    [Fact]
    public void AMinimumHeightIsTheSpaceAnAutoHeightColumnCentresIn()
    {
        using var session = HeadlessSession.Create(600, 200);
        var text = TextElement.New("Pull").FontSize(13);
        var column = Flex.Column().MinHeight(40).JustifyContent(JustifyContent.Center).Children(text);
        var row = Flex.Row().AlignItems(AlignItems.Center).Height(60).Width(500).Children(column);
        session.Window.SetRoot(Box.New().Children(row));
        session.PumpFrame();

        var r = Painted(row);
        var c = Painted(column);
        var t = Painted(text);
        Assert.InRange(t.Height, 10f, 30f);

        Assert.Equal(40f, c.Height, 0.01f);
        Assert.Equal(t.Y - c.Y, c.Bottom - t.Bottom, 0.01f);
        Assert.Equal(c.Y - r.Y, r.Bottom - c.Bottom, 0.01f);
    }

    [Fact]
    public void ACentringScrollViewCentresContentThatFitsInItsViewport()
    {
        // A scroll view distributes its children against an unbounded height so they overflow
        // and scroll; centring against that put the text half of float.MaxValue down.
        using var session = HeadlessSession.Create(600, 200);
        var text = TextElement.New("Pull").FontSize(13);
        var scroller = ScrollView.Vertical().Height(150).JustifyContent(JustifyContent.Center).Children(text);
        session.Window.SetRoot(Box.New().Children(scroller));
        session.PumpFrame();

        var s = LayoutProbe.Inner(scroller);
        var t = Painted(text);
        Assert.Equal(150f, s.Height, 0.01f);
        Assert.InRange(t.Height, 10f, 30f);

        Assert.Equal(t.Y - s.Y, s.Bottom - t.Bottom, 0.01f);
    }

    [Fact]
    public void ACentringScrollViewStartsContentThatOverflowsAtTheTop()
    {
        using var session = HeadlessSession.Create(600, 200);
        var tall = Box.New().Height(400).FlexShrink(0);
        var scroller = ScrollView.Vertical().Height(150).JustifyContent(JustifyContent.Center).Children(tall);
        session.Window.SetRoot(Box.New().Children(scroller));
        session.PumpFrame();

        Assert.Equal(LayoutProbe.Inner(scroller).Y, Painted(tall).Y, 0.01f);
        Assert.Equal(400f, Painted(tall).Height, 0.01f);
    }
}
