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
    public void FlexGrow_AccountsForSiblingMargins()
    {
        // Sibling has explicit width 50 + margin 10 → occupies 70 outer.
        // Grow child should take the remaining 230 (not 250).
        var root = Flex.Row().Width(300).Height(50).Children(
            Box.New().Width(50).Height(50).Margin(new Thickness(0, 10, 0, 10)),
            Box.New().Height(50).FlexGrow(1)
        );
        Layout(root);

        var growChild = root.Children[1];
        Assert.Equal(230, growChild.Layout.Width, 0.5f);
    }

    [Fact]
    public void Row_AutoSizedChildWithMargin_AdvancesByOuterSize()
    {
        // Auto-sized leaf with explicit width but no FlexBasis would still hit the
        // explicit-basis path. Use a child container so the auto-sizing path runs.
        var first = Flex.Row().Margin(new Thickness(0, 8, 0, 8))
            .Children(Box.New().Width(40).Height(20));
        var second = Box.New().Width(30).Height(20);

        var root = Flex.Row().Width(200).Height(50).Children(first, second);
        Layout(root);

        // First child outer width = 40 + 16 = 56. Second child should sit at x=56.
        Assert.Equal(8, first.Layout.AbsoluteX);
        Assert.Equal(56, second.Layout.AbsoluteX, 0.5f);
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

    [Fact]
    public void AbsolutePosition_BottomRight()
    {
        var root = Box.New().Width(400).Height(300);
        var absChild = Box.New().Width(50).Height(50);
        absChild.Position.Type = PositionType.Absolute;
        absChild.Position.Bottom = 10;
        absChild.Position.Right = 20;
        root.AddChild(absChild);

        Layout(root);

        // Right edge: 400 - 50 - 20 = 330
        Assert.Equal(330, absChild.Layout.AbsoluteX, 1f);
        // Bottom edge: 300 - 50 - 10 = 240
        Assert.Equal(240, absChild.Layout.AbsoluteY, 1f);
    }

    // ──── Phase 2: Flex Wrap ────

    [Fact]
    public void Row_Wrap_ItemsWrapToNextLine()
    {
        var root = Flex.Row().Width(300).Height(200)
            .FlexWrap(FlexWrap.Wrap)
            .Children(
                Box.New().Width(150).Height(50),
                Box.New().Width(150).Height(50),
                Box.New().Width(150).Height(50)  // wraps to second line
            );
        Layout(root);

        var child0 = root.Children[0];
        var child1 = root.Children[1];
        var child2 = root.Children[2];

        // First line: two 150px items fit in 300px
        Assert.Equal(0, child0.Layout.AbsoluteX, 1f);
        Assert.Equal(0, child0.Layout.AbsoluteY, 1f);
        Assert.Equal(150, child1.Layout.AbsoluteX, 1f);
        Assert.Equal(0, child1.Layout.AbsoluteY, 1f);

        // Second line: third item wraps
        Assert.Equal(0, child2.Layout.AbsoluteX, 1f);
        Assert.Equal(50, child2.Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void Row_Wrap_ThreeLines()
    {
        var root = Flex.Row().Width(200).Height(300)
            .FlexWrap(FlexWrap.Wrap)
            .Children(
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(60),
                Box.New().Width(100).Height(60),
                Box.New().Width(100).Height(30)
            );
        Layout(root);

        // Line 1: items 0,1 (100+100=200, fits)
        Assert.Equal(0, root.Children[0].Layout.AbsoluteY, 1f);
        Assert.Equal(0, root.Children[1].Layout.AbsoluteY, 1f);

        // Line 2: items 2,3 (100+100=200, fits), y = 40 (max height of line 1)
        Assert.Equal(40, root.Children[2].Layout.AbsoluteY, 1f);
        Assert.Equal(40, root.Children[3].Layout.AbsoluteY, 1f);

        // Line 3: item 4, y = 40 + 60 = 100
        Assert.Equal(100, root.Children[4].Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void Row_Wrap_WithMainGap()
    {
        // Use ColumnGap (main axis gap for row) without cross gap
        var root = Box.New().Width(300).Height(200);
        root.Flex.Direction = FlexDirection.Row;
        root.Flex.Wrap = FlexWrap.Wrap;
        root.Flex.ColumnGap = 10;
        root.ReplaceChildren(
            Box.New().Width(140).Height(50),
            Box.New().Width(140).Height(50),
            Box.New().Width(140).Height(50)  // 140+10+140=290 fits, but 3rd doesn't
        );
        Layout(root);

        // First line: two items fit (140+10+140=290 < 300)
        Assert.Equal(0, root.Children[0].Layout.AbsoluteX, 1f);
        Assert.Equal(150, root.Children[1].Layout.AbsoluteX, 1f);

        // Third item wraps to second line (no cross gap set)
        Assert.Equal(0, root.Children[2].Layout.AbsoluteX, 1f);
        Assert.Equal(50, root.Children[2].Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void Row_Wrap_CrossGap()
    {
        // RowGap applies between lines in a row wrap
        var root = Box.New().Width(200).Height(300);
        root.Flex.Direction = FlexDirection.Row;
        root.Flex.Wrap = FlexWrap.Wrap;
        root.Flex.RowGap = 10;
        root.ReplaceChildren(
            Box.New().Width(100).Height(40),
            Box.New().Width(100).Height(40),
            Box.New().Width(100).Height(40),
            Box.New().Width(100).Height(40)
        );
        Layout(root);

        // Line 1: items 0,1 at y=0
        Assert.Equal(0, root.Children[0].Layout.AbsoluteY, 1f);
        // Line 2: items 2,3 at y = 40 + 10 (cross gap) = 50
        Assert.Equal(50, root.Children[2].Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void Row_WrapReverse()
    {
        var root = Flex.Row().Width(200).Height(200)
            .FlexWrap(FlexWrap.WrapReverse)
            .Children(
                Box.New().Width(100).Height(50),
                Box.New().Width(100).Height(50),
                Box.New().Width(100).Height(50)  // wraps
            );
        Layout(root);

        // With wrap-reverse, the second line appears ABOVE the first
        // Line ordering is reversed: line with item 2 comes first visually
        var child0 = root.Children[0];
        var child2 = root.Children[2];

        // The wrapped line (child2) should be above the first line (child0,child1)
        Assert.True(child2.Layout.AbsoluteY < child0.Layout.AbsoluteY,
            $"Wrapped item Y ({child2.Layout.AbsoluteY}) should be above first line Y ({child0.Layout.AbsoluteY})");
    }

    [Fact]
    public void Column_Wrap()
    {
        var root = Flex.Column().Width(300).Height(100)
            .FlexWrap(FlexWrap.Wrap)
            .Children(
                Box.New().Width(80).Height(50),
                Box.New().Width(80).Height(50),
                Box.New().Width(80).Height(50)  // wraps to second column
            );
        Layout(root);

        // First column: items 0,1 (50+50=100, fits height)
        Assert.Equal(0, root.Children[0].Layout.AbsoluteX, 1f);
        Assert.Equal(0, root.Children[0].Layout.AbsoluteY, 1f);
        Assert.Equal(0, root.Children[1].Layout.AbsoluteX, 1f);
        Assert.Equal(50, root.Children[1].Layout.AbsoluteY, 1f);

        // Second column: item 2 wraps
        Assert.Equal(80, root.Children[2].Layout.AbsoluteX, 1f);
        Assert.Equal(0, root.Children[2].Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void Row_Wrap_PerLineGrow()
    {
        // Grow distributes within each line independently
        // 120*2=240 fits, 120*3=360 wraps
        var root = Flex.Row().Width(300).Height(200)
            .FlexWrap(FlexWrap.Wrap)
            .Children(
                Box.New().Width(120).Height(50).FlexGrow(1),
                Box.New().Width(120).Height(50).FlexGrow(1),
                Box.New().Width(120).Height(50).FlexGrow(1)  // wraps
            );
        Layout(root);

        // Line 1: items 0,1 (240px) grow to fill 300px → 150 each
        Assert.Equal(150, root.Children[0].Layout.Width, 1f);
        Assert.Equal(150, root.Children[1].Layout.Width, 1f);

        // Line 2: item 2 (120px) grows to fill entire 300px
        Assert.Equal(300, root.Children[2].Layout.Width, 1f);
    }

    // ──── Phase 2: AlignContent ────

    [Fact]
    public void AlignContent_Center()
    {
        var root = Flex.Row().Width(200).Height(300)
            .FlexWrap(FlexWrap.Wrap)
            .AlignContent(AlignContent.Center)
            .Children(
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40)
            );
        Layout(root);

        // Two lines, each 40px tall = 80px total
        // Centered in 300px: offset = (300 - 80) / 2 = 110
        Assert.Equal(110, root.Children[0].Layout.AbsoluteY, 1f);
        Assert.Equal(150, root.Children[2].Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void AlignContent_FlexEnd()
    {
        var root = Flex.Row().Width(200).Height(300)
            .FlexWrap(FlexWrap.Wrap)
            .AlignContent(AlignContent.FlexEnd)
            .Children(
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40)
            );
        Layout(root);

        // Two lines, 80px total. FlexEnd pushes to bottom: offset = 300 - 80 = 220
        Assert.Equal(220, root.Children[0].Layout.AbsoluteY, 1f);
        Assert.Equal(260, root.Children[2].Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void AlignContent_SpaceBetween()
    {
        var root = Flex.Row().Width(200).Height(300)
            .FlexWrap(FlexWrap.Wrap)
            .AlignContent(AlignContent.SpaceBetween)
            .Children(
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40)
            );
        Layout(root);

        // Two lines of 40px. SpaceBetween: line1 at 0, line2 at 300-40=260
        Assert.Equal(0, root.Children[0].Layout.AbsoluteY, 1f);
        Assert.Equal(260, root.Children[2].Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void AlignContent_Stretch()
    {
        var root = Flex.Row().Width(200).Height(300)
            .FlexWrap(FlexWrap.Wrap)
            .AlignContent(AlignContent.Stretch)
            .AlignItems(AlignItems.FlexStart)
            .Children(
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40)
            );
        Layout(root);

        // Two lines, each 40px natural. Stretch distributes remaining 220px:
        // Each line gets 40 + 110 = 150px
        // Line 1 at y=0, line 2 at y=150
        Assert.Equal(0, root.Children[0].Layout.AbsoluteY, 1f);
        Assert.Equal(150, root.Children[2].Layout.AbsoluteY, 1f);
    }

    [Fact]
    public void AlignContent_Stretch_LeavesRoomForTheGaps()
    {
        var root = Flex.Row().Width(200).Height(300)
            .FlexWrap(FlexWrap.Wrap).RowGap(20)
            .AlignContent(AlignContent.Stretch)
            .AlignItems(AlignItems.FlexStart)
            .Children(
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40)
            );
        Layout(root);

        // 300 - 80 of lines - 20 of gap = 200 to share: each line 140, the second at 160.
        Assert.Equal(160, root.Children[2].Layout.AbsoluteY, 0.01f);
    }

    /// <summary>Two wrapped lines of 40 in a row whose height comes from them, which the
    /// 300px row it sits in doesn't stretch — so there is no space to align them in.</summary>
    private static (Element Wrapped, Element Root) WrappedLines(AlignContent align, float? minHeight = null)
    {
        var wrapped = Flex.Row().Width(200).FlexWrap(FlexWrap.Wrap).AlignContent(align)
            .AlignItems(AlignItems.FlexStart)
            .Children(
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40),
                Box.New().Width(100).Height(40));
        if (minHeight is { } min) wrapped.MinHeight(min);
        var root = Flex.Row().Width(400).Height(300).AlignItems(AlignItems.FlexStart).Children(wrapped);
        return (wrapped, root);
    }

    [Theory]
    [InlineData(AlignContent.Center)]
    [InlineData(AlignContent.FlexEnd)]
    [InlineData(AlignContent.SpaceBetween)]
    [InlineData(AlignContent.SpaceAround)]
    [InlineData(AlignContent.Stretch)]
    public void AlignContent_AutoCrossSize_HasNoFreeSpace(AlignContent align)
    {
        var (wrapped, root) = WrappedLines(align);
        Layout(root);

        Assert.Equal(80, wrapped.Layout.Height, 0.01f);
        Assert.Equal(0, wrapped.Children[0].Layout.AbsoluteY, 0.01f);
        Assert.Equal(40, wrapped.Children[2].Layout.AbsoluteY, 0.01f);
    }

    [Theory]
    [InlineData(AlignContent.Center, 20, 60)]
    [InlineData(AlignContent.FlexEnd, 40, 80)]
    [InlineData(AlignContent.SpaceBetween, 0, 80)]
    [InlineData(AlignContent.Stretch, 0, 60)]
    public void AlignContent_AutoCrossSize_DistributesUpToItsMinimum(AlignContent align, float first, float second)
    {
        var (wrapped, root) = WrappedLines(align, minHeight: 120);
        Layout(root);

        Assert.Equal(120, wrapped.Layout.Height, 0.01f);
        Assert.Equal(first, wrapped.Children[0].Layout.AbsoluteY, 0.01f);
        Assert.Equal(second, wrapped.Children[2].Layout.AbsoluteY, 0.01f);
    }

    [Theory]
    [InlineData(AlignContent.Center)]
    [InlineData(AlignContent.FlexEnd)]
    [InlineData(AlignContent.SpaceAround)]
    public void AlignContent_OnASingleLine_HasNoEffect(AlignContent align)
    {
        // As in CSS: AlignContent places wrapped lines, and a nowrap row has one line.
        var row = Flex.Row().AlignContent(align).Children(Box.New().Width(100).Height(40));
        var root = Flex.Row().Width(400).Height(300).AlignItems(AlignItems.FlexStart).Children(row);
        Layout(root);

        Assert.Equal(40, row.Layout.Height, 0.01f);
        Assert.Equal(0, row.Children[0].Layout.AbsoluteY, 0.01f);
    }

    // ──── Single line: held within the container's min and max cross size ────
    // CSS Flexbox §9.4 step 8: a single-line container's line is clamped to its min and max
    // cross size, so its items are aligned in the box it ends up as — not in a line only as
    // tall as they are, at the top of a box its min made bigger.

    [Fact]
    public void SingleLine_AutoCrossSize_CentresInItsMinimum()
    {
        var row = Flex.Row().MinHeight(60).AlignItems(AlignItems.Center).Children(Box.New().Width(20).Height(20));
        var root = Flex.Row().Width(400).Height(200).AlignItems(AlignItems.FlexStart).Children(row);
        Layout(root);

        Assert.Equal(60, row.Layout.Height, 0.01f);
        Assert.Equal(20, row.Children[0].Layout.AbsoluteY, 0.01f);
    }

    [Fact]
    public void SingleLine_AutoCrossSize_StretchesToItsMinimum()
    {
        // A card with a min width, centred where nothing stretches it: its children fill it.
        var card = Flex.Column().MinWidth(320).Padding(new Thickness(0, 10, 0, 10)).Children(Box.New().Height(20));
        var root = Flex.Column().Width(800).Height(200).AlignItems(AlignItems.Center).Children(card);
        Layout(root);

        Assert.Equal(320, card.Layout.Width, 0.01f);
        Assert.Equal(300, card.Children[0].Layout.Width, 0.01f);
    }

    [Fact]
    public void SingleLine_AutoCrossSize_StretchesNoFurtherThanItsMaximum()
    {
        var stretched = Box.New().Width(20);
        var row = Flex.Row().MaxHeight(30).Children(stretched, Box.New().Width(20).Height(50));
        var root = Flex.Row().Width(400).Height(200).AlignItems(AlignItems.FlexStart).Children(row);
        Layout(root);

        Assert.Equal(30, row.Layout.Height, 0.01f);
        Assert.Equal(30, stretched.Layout.Height, 0.01f);
    }

    [Fact]
    public void SingleLine_DefiniteCrossSize_IsUnchanged()
    {
        // Forced by its parent's stretch, the row is already its own height: the min is moot.
        var row = Flex.Row().MinHeight(60).AlignItems(AlignItems.Center).Children(Box.New().Width(20).Height(20));
        var root = Flex.Row().Width(400).Height(100).Children(row);
        Layout(root);

        Assert.Equal(100, row.Layout.Height, 0.01f);
        Assert.Equal(40, row.Children[0].Layout.AbsoluteY, 0.01f);
    }

    // ──── Phase 2: Reverse Directions ────

    [Fact]
    public void RowReverse()
    {
        var root = Flex.Row().Width(300).Height(50)
            .FlexDirection(FlexDirection.RowReverse)
            .Children(
                Box.New().Width(100).Height(50),
                Box.New().Width(100).Height(50)
            );
        Layout(root);

        // In row-reverse, items are placed right-to-left
        // child0 (first in source) should be on the RIGHT
        var child0 = root.Children[0];
        var child1 = root.Children[1];

        Assert.True(child0.Layout.AbsoluteX > child1.Layout.AbsoluteX,
            $"In row-reverse, first child ({child0.Layout.AbsoluteX}) should be to the right of second ({child1.Layout.AbsoluteX})");
    }

    [Fact]
    public void ColumnReverse()
    {
        var root = Flex.Column().Width(100).Height(300)
            .FlexDirection(FlexDirection.ColumnReverse)
            .Children(
                Box.New().Width(100).Height(50),
                Box.New().Width(100).Height(50)
            );
        Layout(root);

        var child0 = root.Children[0];
        var child1 = root.Children[1];

        Assert.True(child0.Layout.AbsoluteY > child1.Layout.AbsoluteY,
            $"In column-reverse, first child ({child0.Layout.AbsoluteY}) should be below second ({child1.Layout.AbsoluteY})");
    }

    // ──── Phase 2: FlexShrink ────

    [Fact]
    public void FlexShrink_ShrinkOverflow()
    {
        // Two children totaling 400px in a 300px container
        var root = Flex.Row().Width(300).Height(50).Children(
            Box.New().Height(50).FlexBasis(200).FlexShrink(1),
            Box.New().Height(50).FlexBasis(200).FlexShrink(1)
        );
        Layout(root);

        // Total = 400, available = 300, overflow = 100
        // Both have equal shrink * basis, so each shrinks by 50
        Assert.Equal(150, root.Children[0].Layout.Width, 1f);
        Assert.Equal(150, root.Children[1].Layout.Width, 1f);
    }

    [Fact]
    public void FlexShrink_ProportionalToSize()
    {
        // Different basis sizes with equal shrink factor
        var root = Flex.Row().Width(300).Height(50).Children(
            Box.New().Height(50).FlexBasis(300).FlexShrink(1),
            Box.New().Height(50).FlexBasis(100).FlexShrink(1)
        );
        Layout(root);

        // Total = 400, overflow = 100
        // Shrink proportional to basis * shrink: 300*1=300, 100*1=100
        // child0 shrinks: 100 * (300/400) = 75 → 225
        // child1 shrinks: 100 * (100/400) = 25 → 75
        Assert.Equal(225, root.Children[0].Layout.Width, 1f);
        Assert.Equal(75, root.Children[1].Layout.Width, 1f);
    }

    [Fact]
    public void FlexShrink_ZeroDoesNotShrink()
    {
        var root = Flex.Row().Width(200).Height(50).Children(
            Box.New().Height(50).FlexBasis(150).FlexShrink(0),
            Box.New().Height(50).FlexBasis(150).FlexShrink(1)
        );
        Layout(root);

        // Only child1 shrinks. Overflow = 100. child1 takes all the reduction.
        Assert.Equal(150, root.Children[0].Layout.Width, 1f);
        Assert.Equal(50, root.Children[1].Layout.Width, 1f);
    }

    // ──── Phase 2: FlexBasis ────

    [Fact]
    public void FlexBasis_OverridesWidth()
    {
        var root = Flex.Row().Width(400).Height(50).Children(
            Box.New().Width(100).Height(50).FlexBasis(200)
        );
        Layout(root);

        // FlexBasis takes precedence over Width for sizing in flex layout
        Assert.Equal(200, root.Children[0].Layout.Width, 1f);
    }

    [Fact]
    public void FlexBasis_Percentage()
    {
        var root = Flex.Row().Width(400).Height(50).Children(
            Box.New().Height(50).FlexBasis(Dimension.Percent(25))
        );
        Layout(root);

        Assert.Equal(100, root.Children[0].Layout.Width, 1f);
    }

    // ──── Phase 2: Min/Max with Flex ────

    [Fact]
    public void MaxWidth_ClampsFlexGrow()
    {
        var root = Flex.Row().Width(400).Height(50).Children(
            Box.New().Height(50).FlexGrow(1).MaxWidth(100),
            Box.New().Height(50).FlexGrow(1)
        );
        Layout(root);

        // Both want 200px from grow, but child0 is clamped to 100
        Assert.Equal(100, root.Children[0].Layout.Width, 1f);
    }

    [Fact]
    public void MinWidth_PreventsFlexShrink()
    {
        var root = Flex.Row().Width(200).Height(50).Children(
            Box.New().Height(50).FlexBasis(150).FlexShrink(1).MinWidth(120),
            Box.New().Height(50).FlexBasis(150).FlexShrink(1)
        );
        Layout(root);

        // MinWidth ensures child0 doesn't go below 120
        Assert.True(root.Children[0].Layout.Width >= 120,
            $"Width {root.Children[0].Layout.Width} should be >= 120 (MinWidth)");
    }

    // ──── Phase 2: Percentage Height ────

    [Fact]
    public void PercentageHeight()
    {
        var root = Flex.Column().Width(100).Height(400).Children(
            Box.New().Width(100).Height(Dimension.Percent(25))
        );
        Layout(root);

        Assert.Equal(100, root.Children[0].Layout.Height, 1f);
    }

    // ──── Phase 2: AlignSelf ────

    [Fact]
    public void AlignSelf_OverridesAlignItems()
    {
        var root = Flex.Row().Width(400).Height(100)
            .AlignItems(AlignItems.FlexStart)
            .Children(
                Box.New().Width(100).Height(40), // stays at top
                Box.New().Width(100).Height(40).AlignSelf(AlignSelf.Center), // centered
                Box.New().Width(100).Height(40).AlignSelf(AlignSelf.FlexEnd)  // at bottom
            );
        Layout(root);

        Assert.Equal(0, root.Children[0].Layout.AbsoluteY, 1f);
        Assert.Equal(30, root.Children[1].Layout.AbsoluteY, 1f);  // (100-40)/2
        Assert.Equal(60, root.Children[2].Layout.AbsoluteY, 1f);  // 100-40
    }

    // ──── Phase 2: JustifyContent additional ────

    [Fact]
    public void JustifyContent_FlexEnd()
    {
        var root = Flex.Row().Width(400).Height(50)
            .JustifyContent(JustifyContent.FlexEnd)
            .Children(
                Box.New().Width(100).Height(50),
                Box.New().Width(100).Height(50)
            );
        Layout(root);

        // Items pushed to end: 400 - 200 = 200 offset
        Assert.Equal(200, root.Children[0].Layout.AbsoluteX, 1f);
        Assert.Equal(300, root.Children[1].Layout.AbsoluteX, 1f);
    }

    [Fact]
    public void JustifyContent_SpaceEvenly()
    {
        var root = Flex.Row().Width(400).Height(50)
            .JustifyContent(JustifyContent.SpaceEvenly)
            .Children(
                Box.New().Width(50).Height(50),
                Box.New().Width(50).Height(50),
                Box.New().Width(50).Height(50)
            );
        Layout(root);

        // SpaceEvenly: free = 400 - 150 = 250, gaps = 250/4 = 62.5
        Assert.Equal(62.5f, root.Children[0].Layout.AbsoluteX, 1f);
        Assert.Equal(175, root.Children[1].Layout.AbsoluteX, 1f);
        Assert.Equal(287.5f, root.Children[2].Layout.AbsoluteX, 1f);
    }

    [Fact]
    public void JustifyContent_SpaceAround()
    {
        var root = Flex.Row().Width(400).Height(50)
            .JustifyContent(JustifyContent.SpaceAround)
            .Children(
                Box.New().Width(50).Height(50),
                Box.New().Width(50).Height(50)
            );
        Layout(root);

        // SpaceAround: free = 300, each item gets 300/2 = 150 space around
        // Half-space before first: 75, full space between: 150
        Assert.Equal(75, root.Children[0].Layout.AbsoluteX, 1f);
        Assert.Equal(275, root.Children[1].Layout.AbsoluteX, 1f);
    }

    [Theory]
    [InlineData(JustifyContent.Center)]
    [InlineData(JustifyContent.FlexEnd)]
    [InlineData(JustifyContent.SpaceBetween)]
    [InlineData(JustifyContent.SpaceAround)]
    [InlineData(JustifyContent.SpaceEvenly)]
    public void JustifyContent_AutoMainSize_HasNoFreeSpace(JustifyContent justify)
    {
        // The column's height comes from its children, so it has none to distribute — not
        // the row's 100px it happens to sit in, which AlignItems.Center doesn't stretch it to.
        var column = Flex.Column(gap: 4).JustifyContent(justify).Children(
            Box.New().Width(50).Height(20),
            Box.New().Width(50).Height(10));
        var root = Flex.Row().Width(400).Height(100).AlignItems(AlignItems.Center).Children(column);
        Layout(root);

        Assert.Equal(34, column.Layout.Height, 0.01f);
        Assert.Equal(33, column.Layout.AbsoluteY, 0.01f);   // (100 - 34) / 2
        Assert.Equal(33, column.Children[0].Layout.AbsoluteY, 0.01f);
        Assert.Equal(57, column.Children[1].Layout.AbsoluteY, 0.01f);
    }

    [Fact]
    public void JustifyContent_AutoMainSize_DistributesUpToItsMinimum()
    {
        var column = Flex.Column().MinHeight(60).JustifyContent(JustifyContent.FlexEnd).Children(
            Box.New().Width(50).Height(20));
        var root = Flex.Row().Width(400).Height(100).AlignItems(AlignItems.FlexStart).Children(column);
        Layout(root);

        Assert.Equal(60, column.Layout.Height, 0.01f);
        Assert.Equal(40, column.Children[0].Layout.AbsoluteY, 0.01f);
    }

    [Fact]
    public void JustifyContent_AutoMainSize_DistributesWithinItsMaximum()
    {
        // An auto-width wrapping row is as wide as its longest line, capped by its max, and
        // every line is justified within that width — not within its longest line.
        var row = Flex.Row().FlexWrap(FlexWrap.Wrap).MaxWidth(150).JustifyContent(JustifyContent.Center).Children(
            Box.New().Width(200).Height(10).FlexShrink(0),
            Box.New().Width(60).Height(10));
        var root = Flex.Column().Width(400).Height(100).AlignItems(AlignItems.FlexStart).Children(row);
        Layout(root);

        Assert.Equal(150, row.Layout.Width, 0.01f);
        Assert.Equal(0, row.Children[0].Layout.AbsoluteX, 0.01f);    // overflows: no free space
        Assert.Equal(45, row.Children[1].Layout.AbsoluteX, 0.01f);   // (150 - 60) / 2
    }

    // ──── Phase 2: NoWrap overflow ────

    [Fact]
    public void Row_NoWrap_ShrinksByDefault()
    {
        // Without wrap, items shrink rather than wrap
        var root = Flex.Row().Width(200).Height(50).Children(
            Box.New().Width(150).Height(50),
            Box.New().Width(150).Height(50)
        );
        Layout(root);

        // Default flex-shrink is 1, so both shrink proportionally
        // Total = 300, available = 200, overflow = 100
        Assert.Equal(100, root.Children[0].Layout.Width, 1f);
        Assert.Equal(100, root.Children[1].Layout.Width, 1f);
    }
}
