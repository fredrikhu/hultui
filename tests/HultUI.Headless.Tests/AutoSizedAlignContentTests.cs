using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="AlignContent"/> on a wrapping container whose cross size comes from its lines:
/// the cross-axis twin of <see cref="AutoSizedJustifyTests"/>. Such a container is exactly as
/// tall as its lines, so there is no space to align them in. It used to align them against the
/// space <em>offered</em> to it and then report where the last line ended as its height — and
/// <see cref="AlignContent.Stretch"/> stretched the lines to fill whatever was offered, which
/// inside a vertical scroll view is unbounded.
/// </summary>
public sealed class AutoSizedAlignContentTests
{
    private static Rect Painted(Element e) => LayoutProbe.Painted(e);

    [Fact]
    public void ACentredWrappingRowInAFixedHeightRowIsAsTallAsItsTextAndCentred()
    {
        using var session = HeadlessSession.Create(600, 200);
        var text = TextElement.New("Pull").FontSize(13);
        var wrapped = Flex.Row().FlexWrap(FlexWrap.Wrap).AlignContent(AlignContent.Center).FlexGrow(1).Children(text);
        var row = Flex.Row().AlignItems(AlignItems.Center).Height(60).Width(500).Children(wrapped);
        session.Window.SetRoot(Box.New().Children(row));
        session.PumpFrame();

        var r = Painted(row);
        var w = Painted(wrapped);
        var t = Painted(text);
        Assert.Equal(60f, r.Height, 0.01f);
        Assert.InRange(t.Height, 10f, 30f);

        Assert.Equal(t.Height, w.Height, 0.01f);
        Assert.Equal(w.Y, t.Y, 0.01f);
        Assert.Equal(t.Y - r.Y, r.Bottom - t.Bottom, 0.01f);
    }

    [Fact]
    public void AStretchedWrappingRowInAScrollViewIsAsTallAsItsLines()
    {
        using var session = HeadlessSession.Create(600, 200);
        var first = TextElement.New("Pull").FontSize(13);
        var second = TextElement.New("Push").FontSize(13);
        var wrapped = Flex.Row().FlexWrap(FlexWrap.Wrap).AlignContent(AlignContent.Stretch)
            .AlignItems(AlignItems.FlexStart).Children(
                Box.New().Width(400).FlexShrink(0).Children(first),
                Box.New().Width(400).FlexShrink(0).Children(second));
        var scroller = ScrollView.Vertical().Height(150).Children(wrapped);
        session.Window.SetRoot(Box.New().Children(scroller));
        session.PumpFrame();

        var a = Painted(first);
        var b = Painted(second);
        Assert.InRange(a.Height, 10f, 30f);
        Assert.Equal(a.Bottom, b.Y, 0.01f);   // two lines, one under the other

        Assert.Equal(a.Height + b.Height, Painted(wrapped).Height, 0.01f);
    }
}
