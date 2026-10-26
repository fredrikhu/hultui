using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using SkiaSharp;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// On item rows a selection move made two rows again — the one the selection left and the one
/// it reached — when all that changed in them was their background. A list given
/// <c>.SelectionHighlight(…)</c> draws the selection itself, under the selected rows, and makes
/// its rows unselected, so a move makes none. These pin it: no row made on a move, the same
/// pixels as rows that drew the same fill (snapped the same, at 1x and 1.5x, with a radius, a
/// bottom border or a ring), the paint marked when only the selection moves, each selected row
/// highlighted in a multi-selection, and the same accessibility tree.
/// </summary>
public class VirtualListSelectionHighlightTests
{
    private const float RowHeight = 24f;
    private const float Radius = 5f;
    private static readonly Color Selected = new(40, 90, 200);
    private static readonly Color Other = new(200, 120, 30);
    private static readonly Color Line = new(120, 120, 140);
    private static readonly Color Ring = new(240, 200, 60);

    private sealed record Entry(int Id, string Name);

    private static List<Entry> Entries(int count) =>
        Enumerable.Range(0, count).Select(i => new Entry(i, $"entry {i}")).ToList();

    /// <summary>How the selection is drawn.</summary>
    private enum Draw
    {
        /// <summary>By the rows, from the template's <c>selected</c>: a fill, rounded, over a bottom border.</summary>
        Rows,
        /// <summary>By the list, the same fill and radius; the rows draw their border alone.</summary>
        List,
        /// <summary>By the rows: a fill and a uniform ring, rounded.</summary>
        RowsRing,
        /// <summary>By the list: the same fill and ring; the rows keep a transparent border of that width.</summary>
        ListRing,
    }

    /// <summary>A pane whose Build reads the selection, as Junction's Tags view does.</summary>
    private sealed class Pane(Draw draw, List<Entry> items) : Component
    {
        public int SelectedId;
        /// <summary>The list's highlight, for <see cref="Draw.List"/>; null leaves it to the rows.</summary>
        public RowHighlight? Highlight = draw switch
        {
            Draw.List => new RowHighlight(Selected, new CornerRadius(Radius)),
            Draw.ListRing => new RowHighlight(Selected, new CornerRadius(Radius), Ring, 1f),
            _ => null,
        };
        public bool CanSelectMultiple;
        /// <summary>Rows the template made, and how many of them it was told were selected.</summary>
        public int Made, MadeSelected;
        public readonly Ref<VirtualListElement<Entry>> List = new();
        private Func<Entry, string?>? _label;

        public override Element Build()
        {
            int selectedId = SelectedId;
            var list = VirtualListElement<Entry>.New()
                .ItemHeight(RowHeight)
                .Items(items)
                .AccessibleItemLabel(_label ??= e => e.Name)
                .SelectedIndex(items.FindIndex(e => e.Id == selectedId))
                .CanSelectMultiple(CanSelectMultiple)
                .RenderWith((e, selected) => Row(e, selected))
                .SelectionHighlight(Highlight)
                .FlexGrow(1);
            return Flex.Column().FlexGrow(1).Children(list.WithRef(List));
        }

        private Element Row(Entry e, bool selected)
        {
            Made++;
            if (selected) MadeSelected++;
            var row = Flex.Row().Padding(new Thickness(2, 8)).CornerRadius(Radius)
                .Background(selected ? Selected : Color.Transparent)
                .Children(TextB.New(e.Name).FontSize(12f));
            if (draw is Draw.Rows or Draw.List)
                row.BorderColor(Line).BorderWidth(new Thickness(0, 0, 1, 0));
            else
                row.BorderWidth(1f).BorderColor(selected ? Ring : Color.Transparent);
            return row;
        }

        public void Select(int id) { SelectedId = id; MarkDirty(); }

        public void Rebuild() => MarkDirty();
    }

    private static (HeadlessSession Session, Pane Pane) Mount(Draw draw, int count = 200, float scale = 1f)
    {
        var session = HeadlessSession.Create(400, 300, scale);
        try
        {
            var pane = new Pane(draw, Entries(count));
            session.Application.SetRoot(pane.Mount());
            session.PumpFrames(3);
            return (session, pane);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>The realized rows by the name they show, in the order they stand.</summary>
    private static List<(string Name, Element Row)> Rows(Pane pane)
    {
        var list = pane.List.Current!;
        var rows = new List<(string, Element)>();
        for (int i = 1; i < list.Children.Count; i++)
        {
            var row = list.Children[i];
            rows.Add((((TextElement)row.Children[0]).Content, row));
        }
        return rows;
    }

    private static SKBitmap Shot(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        return SKBitmap.FromImage(image);
    }

    /// <summary>The colour at the left end of row <paramref name="row"/> as drawn now, clear of
    /// its text and its rounded corners (window coordinates, unscrolled list at the top).</summary>
    private static SKColor RowFill(SKBitmap shot, int row, float scale = 1f) =>
        shot.GetPixel((int)(4 * scale), (int)((row * RowHeight + RowHeight / 2) * scale));

    private static SKColor Sk(Color c) => new(c.R, c.G, c.B, c.A);

    [Fact]
    public void ASelectionMove_MakesNoRow_AndKeepsEveryOne()
    {
        var (session, pane) = Mount(Draw.List);
        using var _ = session;
        pane.Select(2);
        session.PumpFrames(2);
        var before = Rows(pane);
        Assert.True(before.Count > 10);
        pane.Made = 0;

        foreach (int id in new[] { 3, 4, 9, 1 })
        {
            pane.Select(id);
            session.PumpFrames(2);
        }

        Assert.Equal(0, pane.Made);
        Assert.Equal(before.Select(r => r.Row), Rows(pane).Select(r => r.Row));
        Assert.Equal(0, pane.MadeSelected);
    }

    /// <summary>The baseline: rows that draw the selection make the two they move between.</summary>
    [Fact]
    public void WithoutIt_ASelectionMove_StillMakesTheTwoRows()
    {
        var (session, pane) = Mount(Draw.Rows);
        using var _ = session;
        pane.Select(2);
        session.PumpFrames(2);
        pane.Made = pane.MadeSelected = 0;

        pane.Select(3);
        session.PumpFrames(2);

        Assert.Equal((2, 1), (pane.Made, pane.MadeSelected));
    }

    /// <summary>
    /// The list draws what the rows drew: the same pixels after moves on screen and past its
    /// edge (which scrolls), with the selection scrolled away, with the pointer over rows, after
    /// a theme switch and with another highlight colour — a fill and radius over the rows' own
    /// bottom border, and a fill with a ring — at 1x and at 1.5x, where fills and borders snap.
    /// </summary>
    [Theory]
    [InlineData(1f, false)]
    [InlineData(1.5f, false)]
    [InlineData(1f, true)]
    [InlineData(1.5f, true)]
    public void TheListDrawsTheSamePixelsAsRowsThatDrewTheSelection(float scale, bool ring)
    {
        (string What, Action<HeadlessSession, Pane> Step)[] steps =
        [
            ("selection on row 2", (_, p) => p.Select(2)),
            ("selection moved to row 3", (_, p) => p.Select(3)),
            ("row 5 under the pointer", (s, _) => s.Window.MoveMouse(100, 5 * RowHeight + 8)),
            ("the row under the pointer selected", (_, p) => p.Select(5)),
            ("selection past the edge, scrolled", (_, p) =>
            {
                p.Select(40);
                p.List.Current!.ScrollRangeIntoView(40 * RowHeight, RowHeight);
            }),
            ("selection moved up", (_, p) => p.Select(39)),
            ("the selection scrolled away", (_, p) => p.List.Current!.ScrollOffsetY = 80 * RowHeight),
            ("selection on screen again", (_, p) => p.Select(81)),
            ("dark theme", (s, _) => s.Application.Theme = Theme.Dark),
            ("a move in the dark theme", (_, p) => p.Select(82)),
        ];

        // One application per process: the two runs take turns.
        var byRows = Shoot(ring ? Draw.RowsRing : Draw.Rows);
        var byList = Shoot(ring ? Draw.ListRing : Draw.List);

        for (int i = 0; i < steps.Length; i++)
            Assert.True(byRows[i].AsSpan().SequenceEqual(byList[i]), $"{steps[i].What}: the list drew other pixels than the rows");
        // Not a false green: the selection is in the pictures, and each step changed something.
        for (int i = 1; i < steps.Length; i++)
            if (steps[i].What != "row 5 under the pointer")
                Assert.False(byRows[i - 1].AsSpan().SequenceEqual(byRows[i]), $"{steps[i].What}: nothing moved");

        List<byte[]> Shoot(Draw draw)
        {
            var (session, pane) = Mount(draw, scale: scale);
            using var _ = session;
            var shots = new List<byte[]>();
            foreach (var (_, step) in steps)
            {
                step(session, pane);
                session.PumpFrames(2);
                using var bitmap = Shot(session);
                shots.Add(bitmap.Bytes);
            }
            return shots;
        }
    }

    /// <summary>Set on the mounted list, with no rebuild, the selection still moves on screen:
    /// the list marks its paint when nothing else is made.</summary>
    [Fact]
    public void SelectedIndexSetOnTheMountedList_MovesTheHighlight()
    {
        var (session, pane) = Mount(Draw.List);
        using var _ = session;
        pane.Select(2);
        session.PumpFrames(2);
        using (var shot = Shot(session))
            Assert.Equal(Sk(Selected), RowFill(shot, 2));
        pane.Made = 0;

        Assert.False(session.PumpFrame(), "an idle pane drew a frame");
        pane.List.Current!.SelectedIndex = 6;
        // The pump draws only what is marked (a capture draws anyway).
        Assert.True(session.PumpFrame(), "the move drew no frame");

        using var after = Shot(session);
        Assert.Equal(Sk(Selected), RowFill(after, 6));
        Assert.NotEqual(Sk(Selected), RowFill(after, 2));
        Assert.Equal(0, pane.Made);
    }

    /// <summary>Another highlight from a rebuild is drawn on the next frame, and makes no row.</summary>
    [Fact]
    public void AnotherHighlight_IsDrawnWithoutMakingARow()
    {
        var (session, pane) = Mount(Draw.List);
        using var _ = session;
        pane.Select(4);
        session.PumpFrames(2);
        pane.Made = 0;

        // Set on the mounted list, as a patch sets it: the frame is drawn for it alone.
        Assert.False(session.PumpFrame(), "an idle pane drew a frame");
        pane.List.Current!.SelectionHighlight = new RowHighlight(Other, new CornerRadius(Radius));
        Assert.True(session.PumpFrame(), "the new highlight drew no frame");
        using (var mounted = Shot(session))
            Assert.Equal(Sk(Other), RowFill(mounted, 4));

        pane.Highlight = new RowHighlight(Selected, new CornerRadius(Radius));
        pane.Rebuild();
        session.PumpFrames(2);
        using (var back = Shot(session))
            Assert.Equal(Sk(Selected), RowFill(back, 4));

        pane.Highlight = new RowHighlight(Other, new CornerRadius(Radius));
        pane.Rebuild();
        session.PumpFrames(2);

        using var shot = Shot(session);
        Assert.Equal(Sk(Other), RowFill(shot, 4));
        Assert.Equal(0, pane.Made);
    }

    /// <summary>A multi-selection is highlighted on each selected row on screen, and a change to
    /// it (here by assistive technology) makes no row.</summary>
    [Fact]
    public void AMultiSelection_IsHighlightedOnEachSelectedRow_AndMakesNoRow()
    {
        var (session, pane) = Mount(Draw.List);
        using var _ = session;
        pane.CanSelectMultiple = true;
        pane.Rebuild();
        session.PumpFrames(2);
        var list = (IAccessibleItemSource)pane.List.Current!;
        pane.Made = 0;

        Assert.False(session.PumpFrame(), "an idle pane drew a frame");
        list.SetItemSelected(2, true);
        list.SetItemSelected(5, true);
        list.SetItemSelected(6, true);
        Assert.True(session.PumpFrame(), "the selection drew no frame");
        session.PumpFrames(1);

        using (var shot = Shot(session))
        {
            foreach (int row in new[] { 2, 5, 6 })
                Assert.Equal(Sk(Selected), RowFill(shot, row));
            foreach (int row in new[] { 1, 3, 4, 7 })
                Assert.NotEqual(Sk(Selected), RowFill(shot, row));
        }

        list.SetItemSelected(5, false);
        session.PumpFrames(2);
        using (var shot = Shot(session))
        {
            Assert.NotEqual(Sk(Selected), RowFill(shot, 5));
            Assert.Equal(Sk(Selected), RowFill(shot, 6));
        }
        Assert.Equal(0, pane.Made);
    }

    /// <summary>Turned on, the rows the template made selected are made again unselected; turned
    /// off, the selected row is made again for the template to draw.</summary>
    [Fact]
    public void TurningItOnOrOff_MakesTheSelectedRowAgain()
    {
        var (session, pane) = Mount(Draw.List);
        using var _ = session;
        pane.Select(3);
        session.PumpFrames(2);
        pane.Made = pane.MadeSelected = 0;

        pane.Highlight = null;
        pane.Rebuild();
        session.PumpFrames(2);
        Assert.Equal((1, 1), (pane.Made, pane.MadeSelected));
        using (var shot = Shot(session))
            Assert.Equal(Sk(Selected), RowFill(shot, 3));   // the row draws it now

        pane.Made = pane.MadeSelected = 0;
        pane.Highlight = new RowHighlight(Other);
        pane.Rebuild();
        session.PumpFrames(2);
        Assert.Equal((1, 0), (pane.Made, pane.MadeSelected));
        using (var shot = Shot(session))
            Assert.Equal(Sk(Other), RowFill(shot, 3));
    }

    /// <summary>Assistive technology reads the list's selection, not the rows': the same items,
    /// names and selected states whoever draws it.</summary>
    [Fact]
    public void TheAccessibilityTree_IsTheSameAsWhenTheRowsDrawTheSelection()
    {
        // One application per process: the two runs take turns.
        var byRows = Walk(Draw.Rows);
        var byList = Walk(Draw.List);

        Assert.Equal(byRows.Count, byList.Count);
        for (int i = 0; i < byRows.Count; i++)
            Assert.Equal(byRows[i], byList[i]);
        Assert.Equal(200, byList[^1].Count);
        Assert.Equal(("entry 30", true), byList[^1].Single(i => i.Selected == true));

        static List<List<(string? Name, bool? Selected)>> Walk(Draw draw)
        {
            var (session, pane) = Mount(draw);
            using var _ = session;
            var bridge = session.Window.InstallAccessibilityBridge();
            var trees = new List<List<(string?, bool?)>>();
            foreach (int id in new[] { 1, 2, 3, 9, 30 })
            {
                pane.Select(id);
                session.PumpFrames(2);
                trees.Add(Items(bridge.CurrentTree!));
            }
            return trees;
        }
    }

    private static List<(string? Name, bool? Selected)> Items(AccessibilityNode root)
    {
        var found = new List<(string?, bool?)>();
        Walk(root);
        return found;

        void Walk(AccessibilityNode node)
        {
            if (node.Role == AccessibilityRole.ListItem) found.Add((node.Name, node.IsSelected));
            foreach (var child in node.Children) Walk(child);
        }
    }
}
