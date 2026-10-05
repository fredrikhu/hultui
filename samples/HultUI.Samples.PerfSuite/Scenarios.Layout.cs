using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace PerfSuite;

static partial class Scenarios
{
    // Flex, grid and the newer layout features (baseline, aspect ratio, wrap), each under
    // the one operation that makes all of it relayout: the window being resized.
    private static IEnumerable<Scenario> Layout()
    {
        yield return new Scenario("layout/resize 2,000-row list",
            "Window resize drag over a scrolled column of 2,000 rows: every row re-laid out at a new width",
            ctx =>
            {
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 2_000)));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        yield return new Scenario("layout/deep nesting leaf edit",
            "A label 60 boxes deep changes width: the change climbs every ancestor and back down",
            ctx =>
            {
                var leaf = Text.New("leaf").FontSize(13f);
                Element node = leaf;
                for (int depth = 0; depth < 60; depth++)
                    node = (depth % 2 == 0 ? Flex.Row(gap: 2) : Flex.Column(gap: 2))
                        .Padding(2).Children(Text.New($"d{depth}").FontSize(10f), node);
                ctx.Mount(Flex.Column().Children(node, Ui.Rows(ctx.Rng, 200)));
                return new Op { Run = step => leaf.Content = new string('x', 4 + step % 30) };
            });

        yield return new Scenario("layout/wrap 1,000 chips resize",
            "A wrapping row of 1,000 chips during a resize drag: every line broken again",
            ctx =>
            {
                var chips = new Element[1_000];
                for (int i = 0; i < chips.Length; i++)
                    chips[i] = Box.New().Padding(new Thickness(4, 10)).CornerRadius(12f)
                        .Background(Ui.Selected).Children(Text.New(Ui.Label(ctx.Rng)).FontSize(12f));
                var wrap = Flex.Row(gap: 6).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(12).Children(chips);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(wrap));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        yield return new Scenario("layout/baseline rows resize",
            "500 rows aligned on their text baselines (mixed font sizes), resize drag",
            ctx =>
            {
                var rows = new Element[500];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = Flex.Row(gap: 10).FlexShrink(0).AlignItems(AlignItems.Baseline).Padding(4).Children(
                        Text.New($"{i}").FontSize(22f).FontWeight(FontWeight.Bold),
                        Text.New(Ui.Label(ctx.Rng)).FontSize(13f).FlexGrow(1),
                        Text.New(Ui.Paragraph(ctx.Rng, 4)).FontSize(10f).TextColor(Ui.Muted));
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        yield return new Scenario("layout/aspect-ratio tiles resize",
            "A wrapping gallery of 600 16:9 tiles sized in percent, resize drag: every tile's height follows its width",
            ctx =>
            {
                var tiles = new Element[600];
                for (int i = 0; i < tiles.Length; i++)
                    tiles[i] = Box.New().Width(Dimension.Percent(23)).AspectRatio(16, 9)
                        .Background(i % 2 == 0 ? Ui.Selected : Ui.CardFill).CornerRadius(6f)
                        .Children(Text.New($"{i}").FontSize(11f));
                var gallery = Flex.Row(gap: 8).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(8).Children(tiles);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(gallery));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        // The flex-shrink path, which nothing else here exercises: every container below
        // holds more than fits, so each resize step redistributes the overflow by shrink
        // factor. TODO.md asks for this before an automatic minimum size (CSS
        // min-width:auto) is attempted, so that its cost shows.
        yield return new Scenario("layout/shrink 300 overfull rows resize",
            "300 rows of 8 items whose bases add up to more than the width (mixed shrink factors), resize drag",
            ctx =>
            {
                var rows = new Element[300];
                for (int r = 0; r < rows.Length; r++)
                {
                    var items = new Element[8];
                    for (int i = 0; i < items.Length; i++)
                        items[i] = Box.New().FlexBasis(Dimension.Px(150 + 20 * i)).FlexShrink(1 + i % 3)
                            .MinWidth(Dimension.Px(24)).Padding(new Thickness(2, 6)).Background(i % 2 == 0 ? Ui.Selected : Ui.CardFill)
                            .Children(Text.New(Ui.Label(ctx.Rng)).FontSize(12f).MaxLines(1).TextOverflow(TextOverflow.Clip));
                    rows[r] = Flex.Row(gap: 4).FlexShrink(0).Padding(2).Children(items);
                }
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)));
                var first = rows[0].Children[0];
                return new Op
                {
                    Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760),
                    Check = _ => first.Layout.Width < 150 ? null : $"the first item kept its 150 px basis ({first.Layout.Width:0} px): nothing shrank",
                };
            });

        yield return new Scenario("layout/shrink bounded columns resize",
            "Six fixed-height columns of 60 items each, too tall to fit, during a vertical resize drag",
            ctx =>
            {
                var columns = new Element[6];
                for (int c = 0; c < columns.Length; c++)
                {
                    var items = new Element[60];
                    for (int i = 0; i < items.Length; i++)
                        items[i] = Box.New().Height(Dimension.Px(24 + i % 4 * 6)).FlexShrink(1 + i % 2)
                            .Background(i % 2 == 0 ? Ui.Selected : Ui.CardFill)
                            .Children(Text.New(Ui.Label(ctx.Rng)).FontSize(11f));
                    columns[c] = Flex.Column(gap: 2).FlexGrow(1).FlexBasis(Dimension.Px(0)).Children(items);
                }
                ctx.Mount(Flex.Row(gap: 8).FlexGrow(1).Padding(8).Children(columns));
                var first = columns[0].Children[0];
                return new Op
                {
                    Run = step => ctx.Window.SetClientSize(1040, 760 - 4 * (1 + step % 40)),
                    Check = _ => first.Layout.Height < 24 ? null : $"the first item kept its 24 px height ({first.Layout.Height:0} px): nothing shrank",
                };
            });

        yield return new Scenario("layout/cards create 300",
            "Replace a wrapping panel with 300 fresh cards (title, wrapped text, footer)",
            ctx =>
            {
                var panel = Flex.Row(gap: 12).FlexWrap(FlexWrap.Wrap).FlexShrink(0).Padding(12);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(panel));
                int next = 0;
                return new Op
                {
                    Reset = _ => panel.ReplaceChildren(),
                    Run = _ =>
                    {
                        var cards = new Element[300];
                        for (int i = 0; i < cards.Length; i++) cards[i] = Ui.Card(ctx.Rng, next++);
                        panel.ReplaceChildren(cards);
                    },
                };
            });

        yield return new Scenario("grid/create 300×6",
            "Replace a grid's 1,800 cells (columns 60px auto 1fr 2fr auto 80px)",
            ctx =>
            {
                var grid = Grid.Of("60px auto 1fr 2fr auto 80px", gap: 4).FlexShrink(0);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(grid));
                return new Op
                {
                    Reset = _ => grid.ReplaceChildren(),
                    Run = _ => grid.ReplaceChildren(GridCells(ctx.Rng, 300)),
                };
            });

        yield return new Scenario("grid/resize 300×6",
            "The same 1,800-cell grid during a resize drag: auto columns reused, fr columns redistributed",
            ctx =>
            {
                var grid = Grid.Of("60px auto 1fr 2fr auto 80px", gap: 4).FlexShrink(0).Children(GridCells(ctx.Rng, 300));
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(grid));
                return new Op { Run = step => ctx.Window.SetClientSize(Ui.DragWidth(step), 760) };
            });

        yield return new Scenario("grid/edit auto-column cell",
            "One cell of an auto-sized column changes width: the column re-measures all 300 of its cells",
            ctx =>
            {
                var cells = GridCells(ctx.Rng, 300);
                var grid = Grid.Of("60px auto 1fr 2fr auto 80px", gap: 4).FlexShrink(0).Children(cells);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(grid));
                var target = (TextElement)cells[6 * 150 + 1];   // row 150, the first auto column
                return new Op { Run = step => target.Content = new string('w', 8 + step % 24) };
            });
    }

    private static Element[] GridCells(Random rng, int rows)
    {
        var cells = new Element[rows * 6];
        for (int r = 0; r < rows; r++)
        {
            cells[r * 6 + 0] = Text.New($"{r}").FontSize(12f).TextColor(Ui.Muted).GridCell(r, 0);
            cells[r * 6 + 1] = Text.New(Ui.Label(rng)).FontSize(12f).GridCell(r, 1);
            cells[r * 6 + 2] = Text.New(Ui.Label(rng)).FontSize(12f).GridCell(r, 2);
            cells[r * 6 + 3] = Text.New(Ui.Paragraph(rng, 5)).FontSize(12f).MaxLines(1).GridCell(r, 3);
            cells[r * 6 + 4] = Text.New($"{rng.Next(1, 99_999):N0} kB").FontSize(12f).GridCell(r, 4);
            cells[r * 6 + 5] = Box.New().Height(Dimension.Px(14)).Background(Ui.Selected).CornerRadius(3f).GridCell(r, 5);
        }
        return cells;
    }
}
