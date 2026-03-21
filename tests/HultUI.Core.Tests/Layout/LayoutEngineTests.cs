using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Layout;

public class LayoutEngineTests
{
    private readonly LayoutEngine _engine = new(new StubMeasureContext());

    private void Layout(Element root, float width = 800, float height = 600)
    {
        _engine.PerformLayout(root, width, height);
    }

    [Fact]
    public void SingleBox_FixedSize()
    {
        var box = Box.New().Width(100).Height(50);
        Layout(box);

        Assert.Equal(100, box.Layout.Width);
        Assert.Equal(50, box.Layout.Height);
    }

    [Fact]
    public void SingleBox_AutoSize_ExpandsToAvailable()
    {
        var box = Box.New(); // auto width/height
        Layout(box, 800, 600);

        // Auto-sized root should take available space (no children to size from)
        Assert.Equal(800, box.Layout.Width);
        Assert.Equal(600, box.Layout.Height);
    }

    [Fact]
    public void Row_TwoFixedChildren()
    {
        var root = Flex.Row().Children(
            Box.New().Width(100).Height(50),
            Box.New().Width(200).Height(50)
        );
        Layout(root);

        var child0 = root.Children[0];
        var child1 = root.Children[1];

        // First child at x=0
        Assert.Equal(0, child0.Layout.AbsoluteX);
        Assert.Equal(100, child0.Layout.Width);

        // Second child after first
        Assert.Equal(100, child1.Layout.AbsoluteX);
        Assert.Equal(200, child1.Layout.Width);
    }

    [Fact]
    public void Column_TwoFixedChildren()
    {
        var root = Flex.Column().Children(
            Box.New().Width(100).Height(50),
            Box.New().Width(100).Height(30)
        );
        Layout(root);

        var child0 = root.Children[0];
        var child1 = root.Children[1];

        Assert.Equal(0, child0.Layout.AbsoluteY);
        Assert.Equal(50, child0.Layout.Height);

        Assert.Equal(50, child1.Layout.AbsoluteY);
        Assert.Equal(30, child1.Layout.Height);
    }

    [Fact]
    public void Row_WithGap()
    {
        var root = Flex.Row(gap: 10).Children(
            Box.New().Width(100).Height(50),
            Box.New().Width(100).Height(50)
        );
        Layout(root);

        var child1 = root.Children[1];

        // Second child should be at 100 (first width) + 10 (gap) = 110
        Assert.Equal(110, child1.Layout.AbsoluteX);
    }

    [Fact]
    public void Column_WithGap()
    {
        var root = Flex.Column(gap: 8).Children(
            Box.New().Width(100).Height(40),
            Box.New().Width(100).Height(40)
        );
        Layout(root);

        var child1 = root.Children[1];
        Assert.Equal(48, child1.Layout.AbsoluteY);
    }

    [Fact]
    public void Box_WithPadding()
    {
        var root = Box.New().Width(200).Height(100).Padding(10);
        var child = Box.New().Width(50).Height(30);
        root.AddChild(child);
        Layout(root);

        // Child should be offset by padding
        Assert.Equal(10, child.Layout.AbsoluteX);
        Assert.Equal(10, child.Layout.AbsoluteY);

        // Root content box
        var contentBox = root.Layout.ContentBox;
        Assert.Equal(10, contentBox.X);
        Assert.Equal(10, contentBox.Y);
        Assert.Equal(180, contentBox.Width);
        Assert.Equal(80, contentBox.Height);
    }

    [Fact]
    public void Box_WithMargin()
    {
        var root = Flex.Row().Children(
            Box.New().Width(100).Height(50).Margin(5),
            Box.New().Width(100).Height(50)
        );
        Layout(root);

        var child0 = root.Children[0];
        var child1 = root.Children[1];

        // First child offset by its margin
        Assert.Equal(5, child0.Layout.AbsoluteX);
        Assert.Equal(5, child0.Layout.AbsoluteY);

        // Second child after first child + its margins
        Assert.Equal(110, child1.Layout.AbsoluteX); // 5 + 100 + 5 = 110
    }

    [Fact]
    public void NestedLayout()
    {
        var root = Flex.Column().Width(400).Height(300).Children(
            Flex.Row(gap: 10).Height(50).Children(
                Box.New().Width(100).Height(50),
                Box.New().Width(100).Height(50)
            ),
            Box.New().Width(400).Height(100)
        );
        Layout(root);

        var row = root.Children[0];
        var innerChild0 = row.Children[0];
        var innerChild1 = row.Children[1];

        Assert.Equal(0, innerChild0.Layout.AbsoluteY);
        Assert.Equal(0, innerChild0.Layout.AbsoluteX);
        Assert.Equal(110, innerChild1.Layout.AbsoluteX); // 100 + 10 gap

        var bottomBox = root.Children[1];
        Assert.Equal(50, bottomBox.Layout.AbsoluteY);
    }

    [Fact]
    public void FlexGrow_DistributesSpace()
    {
        var root = Flex.Row().Width(300).Height(50).Children(
            Box.New().Width(50).Height(50),
            Box.New().Height(50).FlexGrow(1) // should take remaining 250px
        );
        Layout(root);

        var growChild = root.Children[1];
        Assert.Equal(250, growChild.Layout.Width, 1f);
    }

    [Fact]
    public void FlexGrow_TwoGrowChildren()
    {
        var root = Flex.Row().Width(300).Height(50).Children(
            Box.New().Height(50).FlexGrow(1),
            Box.New().Height(50).FlexGrow(2)
        );
        Layout(root);

        var child0 = root.Children[0];
        var child1 = root.Children[1];

        // 300px split 1:2 = 100, 200
        Assert.Equal(100, child0.Layout.Width, 1f);
        Assert.Equal(200, child1.Layout.Width, 1f);
    }

    [Fact]
    public void AlignItems_Center()
    {
        var root = Flex.Row().Width(400).Height(100)
            .AlignItems(AlignItems.Center)
            .Children(
                Box.New().Width(100).Height(40)
            );
        Layout(root);

        var child = root.Children[0];
        // Centered in 100px cross axis: (100 - 40) / 2 = 30
        Assert.Equal(30, child.Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void AlignItems_FlexEnd()
    {
        var root = Flex.Row().Width(400).Height(100)
            .AlignItems(AlignItems.FlexEnd)
            .Children(
                Box.New().Width(100).Height(40)
            );
        Layout(root);

        var child = root.Children[0];
        Assert.Equal(60, child.Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void JustifyContent_Center()
    {
        var root = Flex.Row().Width(400).Height(50)
            .JustifyContent(JustifyContent.Center)
            .Children(
                Box.New().Width(100).Height(50)
            );
        Layout(root);

        var child = root.Children[0];
        // Centered: (400 - 100) / 2 = 150
        Assert.Equal(150, child.Layout.AbsoluteX, 1f);
    }

    [Fact]
    public void JustifyContent_SpaceBetween()
    {
        var root = Flex.Row().Width(400).Height(50)
            .JustifyContent(JustifyContent.SpaceBetween)
            .Children(
                Box.New().Width(50).Height(50),
                Box.New().Width(50).Height(50),
                Box.New().Width(50).Height(50)
            );
        Layout(root);

        var child0 = root.Children[0];
        var child1 = root.Children[1];
        var child2 = root.Children[2];

        Assert.Equal(0, child0.Layout.AbsoluteX, 1f);
        // Space between: (400 - 150) / 2 = 125 between each
        Assert.Equal(175, child1.Layout.AbsoluteX, 1f);
        Assert.Equal(350, child2.Layout.AbsoluteX, 1f);
    }

    [Fact]
    public void TextElement_MeasuresContent()
    {
        var root = Flex.Column().Width(800).Height(600).Children(
            Text.New("Hello")  // 5 chars * 7px = 35px wide, 14px tall
        );
        Layout(root);

        var text = root.Children[0];
        // With AlignItems.Stretch (default), width stretches to container
        Assert.Equal(14, text.Layout.Height, 1f);
    }

    [Fact]
    public void PercentageWidth()
    {
        var root = Flex.Row().Width(400).Height(100).Children(
            Box.New().Width(Dimension.Percent(50)).Height(100)
        );
        Layout(root);

        var child = root.Children[0];
        Assert.Equal(200, child.Layout.Width, 1f);
    }

    [Fact]
    public void DirtyFlags_ClearedAfterLayout()
    {
        var box = Box.New().Width(100).Height(50);
        Assert.True(box.DirtyFlags.HasFlag(DirtyFlags.Layout));

        Layout(box);

        Assert.False(box.DirtyFlags.HasFlag(DirtyFlags.Layout));
    }

    [Fact]
    public void AbsolutePosition_TopLeft()
    {
        var root = Box.New().Width(400).Height(300);
        var absChild = Box.New().Width(50).Height(50);
        absChild.Position.Type = PositionType.Absolute;
        absChild.Position.Top = 10;
        absChild.Position.Left = 20;
        root.AddChild(absChild);

        Layout(root);

        Assert.Equal(20, absChild.Layout.AbsoluteX, 1f);
        Assert.Equal(10, absChild.Layout.AbsoluteY, 1f);
    }
}
