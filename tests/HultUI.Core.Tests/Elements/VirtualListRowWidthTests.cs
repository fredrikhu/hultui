using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// <see cref="VirtualListElement{T}.RowWidth"/>: rows wider than the viewport, scrolled sideways by
/// the list itself, and stretched to the viewport when it is the wider.
/// </summary>
public class VirtualListRowWidthTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private VirtualListElement<string> List(float viewportWidth, float rowWidth)
    {
        var vl = VirtualListElement<string>.New()
            .ItemHeight(40)
            .Items(Enumerable.Range(0, 50).Select(i => $"Item {i}").ToList())
            .RenderWith(item => Flex.Row().Children(TextElement.New(item)))
            .RowWidth(rowWidth);
        vl.Box.Width = viewportWidth;
        vl.Box.Height = 200;
        _layout.PerformLayout(vl, viewportWidth, 200);
        StyleResolver.Resolve(vl);
        vl.UpdateContentSize();
        _layout.PerformLayout(vl, viewportWidth, 200);
        return vl;
    }

    private static Element FirstRow(VirtualListElement<string> vl) => vl.Children[1];

    [Fact]
    public void RowsAreLaidOutAtTheRowWidth_InANarrowerViewport()
    {
        var vl = List(viewportWidth: 300, rowWidth: 800);

        Assert.Equal(800, vl.ContentWidth);
        Assert.Equal(800, FirstRow(vl).Layout.AbsoluteBorderBox.Width);
        vl.ScrollOffsetX = 9_999;
        Assert.Equal(500, vl.ScrollOffsetX);   // clamped to 800 - 300
    }

    [Fact]
    public void RowsAreStretchedToAWiderViewport()
    {
        var vl = List(viewportWidth: 1_000, rowWidth: 800);

        Assert.Equal(1_000, vl.ContentWidth);
        Assert.Equal(1_000, FirstRow(vl).Layout.AbsoluteBorderBox.Width);
        vl.ScrollOffsetX = 50;
        Assert.Equal(0, vl.ScrollOffsetX);
    }

    [Fact]
    public void NoRowWidth_KeepsTheContentAtTheViewport()
    {
        var vl = List(viewportWidth: 300, rowWidth: 0);

        Assert.Equal(300, vl.ContentWidth);
        Assert.False(vl.ShowScrollbarX);
        vl.ScrollOffsetX = 50;
        Assert.Equal(0, vl.ScrollOffsetX);
    }
}
