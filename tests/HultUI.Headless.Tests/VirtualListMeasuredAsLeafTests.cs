using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A parent measures a <see cref="VirtualListElement{T}"/> as a leaf: its own size, padding and border,
/// without the rows it has realized. Measured through them as a column, every visible row was laid out
/// again at each width a flex ancestor measured the list at — in Junction's diff, nested in growing rows
/// and columns, each code line at four widths a resize step, three of them never drawn — though the list
/// lays its rows out at its own width. A list's size never came from its rows where anything set or
/// stretched it; a box sized from its content around a list is no longer widened by them.
/// </summary>
public class VirtualListMeasuredAsLeafTests
{
    /// <summary>A leaf that measures like a long wrapped paragraph — as wide as it is offered, as many
    /// lines tall as that width needs — and notes each width it is measured at.</summary>
    private sealed class Paragraph : Element
    {
        public readonly List<float> Widths = [];

        internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
        {
            if (!Widths.Contains(availableWidth)) Widths.Add(availableWidth);
            float width = Math.Min(availableWidth, 3000f);
            return new Size(width, 14f * MathF.Ceiling(3000f / Math.Max(1f, width)));
        }
    }

    /// <summary>Junction's diff in small: a sidebar beside a growing main column, a row in it of a file
    /// list beside a growing diff column, the list growing in that, its rows wrapped paragraphs.</summary>
    private sealed class Pane : Component
    {
        public readonly Ref<VirtualListElement<int>> List = new();
        public readonly Dictionary<int, Paragraph> Paragraphs = [];
        private readonly List<int> _items = Enumerable.Range(0, 200).ToList();

        public override Element Build()
        {
            var list = VirtualListElement<int>.New()
                .Items(_items)
                .RowHeights((_, _) => 60f)
                .RenderWith((i, _) =>
                {
                    var paragraph = new Paragraph();
                    Paragraphs[i] = paragraph;
                    return Flex.Row().Children(paragraph.FlexGrow(1));
                })
                .FlexGrow(1);
            return Flex.Row().FlexGrow(1).Children(
                Box.New().Width(Dimension.Px(120)).FlexShrink(0),
                Flex.Column().FlexGrow(1).Children(
                    TextB.New("toolbar").FlexShrink(0),
                    Flex.Row().FlexGrow(1).Children(
                        Box.New().Width(Dimension.Px(160)).FlexShrink(0),
                        Box.New().Width(Dimension.Px(6)).FlexShrink(0),
                        Flex.Column().FlexGrow(1).Children(
                            TextB.New("file header").FlexShrink(0),
                            list.WithRef(List)))));
        }
    }

    [Fact]
    public void ARowInAListInNestedFlexRows_IsLaidOutAtTheWidthItIsDrawnAtOnly()
    {
        using var session = HeadlessSession.Create(900, 500);
        var pane = new Pane();
        session.Application.SetRoot(pane.Mount());
        session.PumpFrames(3);
        var list = pane.List.Current!;
        Assert.InRange(pane.Paragraphs.Count, 8, 20);

        foreach (var width in new[] { 894, 888, 900 })
        {
            foreach (var p in pane.Paragraphs.Values) p.Widths.Clear();
            session.Window.SetClientSize(width, 500);
            session.PumpFrames(2);

            // The list fills what the sidebar, the file list and the splitter leave it, and each row
            // on screen was measured at one width: the one its paragraph is drawn at.
            Assert.Equal(width - 120 - 160 - 6, list.Layout.Width);
            int onScreen = 0;
            foreach (var row in list.Children)
            {
                if (row.Children.Count != 1 || row.Children[0] is not Paragraph p) continue;
                onScreen++;
                Assert.Equal(new[] { p.Layout.Width }, p.Widths);
            }
            Assert.InRange(onScreen, 8, 20);
        }
    }

    [Fact]
    public void AColumnSizedFromItsContent_IsNotWidenedByTheListsRows()
    {
        using var session = HeadlessSession.Create(900, 500);
        var list = VirtualListElement<int>.New()
            .Items(Enumerable.Range(0, 100).ToList())
            .ItemHeight(20f)
            .RenderWith((i, _) => Flex.Row().Children(Box.New().Width(Dimension.Px(600)).Height(Dimension.Px(20))))
            .Height(Dimension.Px(200));
        var header = Box.New().Width(Dimension.Px(150)).Height(Dimension.Px(20));
        var column = Flex.Column().Children(header, list);
        session.Application.SetRoot(Flex.Row().AlignItems(AlignItems.FlexStart).FlexGrow(1).Children(column));
        session.PumpFrames(3);
        Assert.True(list.Children.Count > 5);

        // The column is an item of the row of its content's width: its header's, at every layout. Measured
        // through its rows, the list made it as wide as them at the first layout after they were made (here
        // a resize) and not before; the list stretches across the column as before.
        foreach (var width in new[] { 899, 900 })
        {
            session.Window.SetClientSize(width, 500);
            session.PumpFrames(2);
            Assert.Equal(150f, column.Layout.Width);
            Assert.Equal(150f, list.Layout.Width);
            Assert.Equal(200f, list.Layout.Height);
        }
    }
}
