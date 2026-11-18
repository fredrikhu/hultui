using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A list's current item, the keyboard's cursor, apart from its selection: a file manager walks
/// a list with the cursor and marks files apart from it. With <c>.RenderWithState((item, state) => …)</c>
/// a move made on the mounted list needs no rebuild of the owner and makes only the row the
/// cursor left and the row it reached; the rest are kept, as item rows keep theirs.
/// </summary>
public class VirtualListCurrentItemTests
{
    private const float RowHeight = 24f;

    private sealed record Entry(int Id, string Name);

    private static List<Entry> Entries(int count) =>
        Enumerable.Range(0, count).Select(i => new Entry(i, $"entry {i}")).ToList();

    private static Element Row(Entry e, bool current) =>
        Flex.Row().Padding(new Thickness(2, 8))
            .Background(current ? new Color(40, 90, 200) : Color.Transparent)
            .Children(TextB.New(e.Name).FontSize(12f));

    /// <summary>The rows the list shows, by the index of their item, as the elements they are.</summary>
    private static Dictionary<int, Element> Shown(Element list, Dictionary<Element, int> madeFor)
    {
        var shown = new Dictionary<int, Element>();
        foreach (var child in list.Children)
            if (madeFor.TryGetValue(child, out int index)) shown[index] = child;
        return shown;
    }

    [Fact]
    public void ACurrentMoveOnTheMountedList_MakesTheTwoRowsAgain_AndKeepsTheRest()
    {
        using var session = HeadlessSession.Create(400, 300);
        int made = 0;
        var madeFor = new Dictionary<Element, int>();
        var list = VirtualListElement<Entry>.New()
            .ItemHeight(RowHeight)
            .Items(Entries(200))
            .CurrentIndex(3)
            .RenderWithState((e, state) =>
            {
                made++;
                var row = Row(e, state.IsCurrent);
                madeFor[row] = e.Id;
                return row;
            })
            .FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        session.PumpFrames(2);
        var before = Shown(list, madeFor);
        int madeBefore = made;

        list.CurrentIndex = 4;
        session.PumpFrame();

        Assert.Equal(2, made - madeBefore);
        var after = Shown(list, madeFor);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (index, row) in after)
        {
            if (index is 3 or 4) Assert.NotSame(before[index], row);
            else Assert.Same(before[index], row);
        }
    }

    [Fact]
    public void ACurrentMove_WithTheSelectedTemplate_MakesNoRow()
    {
        using var session = HeadlessSession.Create(400, 300);
        int made = 0;
        var list = VirtualListElement<Entry>.New()
            .ItemHeight(RowHeight)
            .Items(Entries(200))
            .RenderWith((e, selected) => { made++; return Row(e, selected); })
            .FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        session.PumpFrames(2);
        int madeBefore = made;

        list.CurrentIndex = 4;
        session.PumpFrame();
        list.CurrentIndex = 9;
        session.PumpFrame();

        Assert.Equal(madeBefore, made);
    }

    /// <summary>A pane whose Build gives the list its cursor, as an app that keeps it in its state does.</summary>
    private sealed class Pane(int count) : Component
    {
        public List<Entry> Items = Entries(count);
        public int Cursor;
        public int Made;
        /// <summary>Each row made, by the entry it was made for.</summary>
        public readonly Dictionary<Element, int> MadeFor = [];
        public readonly Ref<VirtualListElement<Entry>> List = new();
        private bool _reveal;

        public override Element Build()
        {
            var list = VirtualListElement<Entry>.New()
                .ItemHeight(RowHeight)
                .Items(Items)
                .CurrentIndex(Cursor)
                .RenderWithState((e, state) =>
                {
                    Made++;
                    var row = Row(e, state.IsCurrent);
                    MadeFor[row] = e.Id;
                    return row;
                })
                .FlexGrow(1);
            return Flex.Column().FlexGrow(1).Children(list.WithRef(List));
        }

        public void Replace(List<Entry> items, int cursor)
        {
            Items = items;
            Cursor = cursor;
            MarkDirty();
        }

        public void MoveTo(int index, bool reveal = false)
        {
            Cursor = index;
            _reveal = reveal;
            MarkDirty();
        }

        protected internal override void OnAfterBuild()
        {
            if (!_reveal) return;
            _reveal = false;
            List.Current?.ScrollIndexIntoView(Cursor);
        }
    }

    [Fact]
    public void ARebuildThatMovesTheCurrentIndex_PatchesItOntoTheMountedList()
    {
        using var session = HeadlessSession.Create(400, 300);
        var pane = new Pane(200) { Cursor = 3 };
        session.Application.SetRoot(pane.Mount());
        session.PumpFrames(3);
        var mounted = pane.List.Current!;
        int madeBefore = pane.Made;

        pane.MoveTo(5);
        session.PumpFrames(2);

        Assert.Same(mounted, pane.List.Current);
        Assert.Equal(5, mounted.CurrentIndex);
        Assert.Equal(2, pane.Made - madeBefore);
    }

    [Fact]
    public void ScrollIndexIntoView_FromOnAfterBuild_LandsOnTheRow_AndAnArrowCarriesOnFromThere()
    {
        using var session = HeadlessSession.Create(400, 300);
        var pane = new Pane(1_000);
        session.Application.SetRoot(pane.Mount());
        session.PumpFrames(3);

        pane.MoveTo(500, reveal: true);
        session.PumpFrames(2);
        var list = pane.List.Current!;
        AssertShownWhole(list, 500);

        // The arrow is the app's: the cursor and the reveal on the mounted list, no rebuild.
        list.CurrentIndex = 501;
        list.ScrollIndexIntoView(501);
        session.PumpFrame();
        AssertShownWhole(list, 501);
        Assert.Equal(501, list.CurrentIndex);
    }

    private static void AssertShownWhole(VirtualListElement<Entry> list, int index)
    {
        var view = list.WindowContentBox;
        float top = view.Y + index * RowHeight - list.ScrollOffsetY;
        Assert.True(top >= view.Y - 0.5f && top + RowHeight <= view.Bottom + 0.5f,
            $"row {index} is not shown whole: top {top}, view {view.Y}–{view.Bottom}");
    }

    [Fact]
    public void SettingCurrentIndex_AllocatesNothing()
    {
        using var session = HeadlessSession.Create(400, 300);
        var list = VirtualListElement<Entry>.New()
            .ItemHeight(RowHeight)
            .Items(Entries(200))
            .CurrentIndex(0)
            .RenderWithState((e, state) => Row(e, state.IsCurrent))
            .FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        session.PumpFrames(2);
        list.CurrentIndex = 1;   // warm the path once
        session.PumpFrame();

        long before = GC.GetAllocatedBytesForCurrentThread();
        list.CurrentIndex = 2;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CursorMoveWithSelectionHighlightMakesTwoRows()
    {
        using var session = HeadlessSession.Create(400, 300);
        int made = 0, madeSelected = 0;
        var list = VirtualListElement<Entry>.New()
            .ItemHeight(RowHeight)
            .Items(Entries(200))
            .SelectedIndex(6)
            .CurrentIndex(3)
            .SelectionHighlight(new Color(40, 90, 200))
            .RenderWithState((e, state) =>
            {
                made++;
                if (state.IsSelected) madeSelected++;
                return Row(e, state.IsCurrent);
            })
            .FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        session.PumpFrames(2);
        int madeBefore = made;

        list.CurrentIndex = 4;
        session.PumpFrame();
        Assert.Equal(2, made - madeBefore);

        // The list draws the selection: a selection move makes no row, and no row is made selected.
        madeBefore = made;
        list.SelectedIndex = 7;
        session.PumpFrame();
        Assert.Equal(madeBefore, made);
        Assert.Equal(0, madeSelected);
    }

    [Fact]
    public void PatchMovedRowKeepsCurrentState()
    {
        using var session = HeadlessSession.Create(400, 300);
        var pane = new Pane(200) { Cursor = 3 };
        session.Application.SetRoot(pane.Mount());
        session.PumpFrames(3);
        var list = pane.List.Current!;
        var before = Shown(list, pane.MadeFor);
        int madeBefore = pane.Made;

        // An entry arrives above, and the cursor stays on its item: every row moves down one with its
        // item, the current one included, and only the new entry's row is made.
        var items = pane.Items.Prepend(new Entry(1000, "arrived")).ToList();
        pane.Replace(items, cursor: 4);
        session.PumpFrames(2);

        Assert.Equal(1, pane.Made - madeBefore);
        var after = Shown(list, pane.MadeFor);
        Assert.Same(before[3], after[3]);
        Assert.Same(before[4], after[4]);

        // Another arrives, and the cursor stays at its index: the rows move with their items, and the
        // two whose state changed, the cursor's old item and its new one, are made again.
        madeBefore = pane.Made;
        pane.Replace(pane.Items.Prepend(new Entry(1001, "arrived too")).ToList(), cursor: 4);
        session.PumpFrames(2);

        Assert.Equal(3, pane.Made - madeBefore);
        var last = Shown(list, pane.MadeFor);
        Assert.NotSame(after[3], last[3]);
        Assert.NotSame(after[2], last[2]);
        Assert.Same(after[4], last[4]);
        Assert.Same(after[1], last[1]);
    }

    [Fact]
    public void StickyRowShowsCurrentState()
    {
        using var session = HeadlessSession.Create(400, 300);
        var madeFor = new Dictionary<Element, (int Id, bool Current)>();
        var list = VirtualListElement<Entry>.New()
            .ItemHeight(RowHeight)
            .Items(Entries(200))
            .StickyRows((e, i) => i % 40 == 0)
            .CurrentIndex(0)
            .RenderWithState((e, state) =>
            {
                var row = Row(e, state.IsCurrent);
                madeFor[row] = (e.Id, state.IsCurrent);
                return row;
            })
            .FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        session.PumpFrames(2);
        list.ScrollIndexIntoView(10, ScrollAlignment.Start);
        session.PumpFrames(2);

        // Row 0 is far above the window (the reveal backs off by the header it holds): it is shown
        // only because it is held, as its section's header.
        Assert.True(list.ScrollOffsetY >= 5 * RowHeight, $"scrolled to {list.ScrollOffsetY}");
        Assert.Equal((0, true), HeldHeader());

        list.CurrentIndex = 1;
        session.PumpFrame();
        Assert.Equal((0, false), HeldHeader());

        (int, bool) HeldHeader() =>
            list.Children.Where(madeFor.ContainsKey).Select(c => madeFor[c]).Single(m => m.Id == 0);
    }
}
