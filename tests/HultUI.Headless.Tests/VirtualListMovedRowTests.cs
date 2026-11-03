using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A list on item rows (<c>.RenderWith((item, selected) => …)</c>) matched its rows by index after
/// a patch, so an insertion above them, a removal or a filter made every row it shifted again,
/// though each showed the same item as before (Vesper's type-to-find made every row on screen for
/// each key). A row let go of now goes back to the index where an equal item stands, in the same
/// selected state, before the template is asked. These pin what moves, what is still made, and
/// what a moved row keeps: its object, its focus, its key, the height of its new place.
/// </summary>
public class VirtualListMovedRowTests
{
    private const float RowHeight = 24f;
    private static readonly Color Selected = new(40, 90, 200);

    private sealed record Entry(int Id, string Name);

    private static List<Entry> Entries(int count) =>
        Enumerable.Range(0, count).Select(i => new Entry(i, $"entry {i}")).ToList();

    private static readonly Entry Arrived = new(1000, "arrived");

    private sealed class Pane(List<Entry> items) : Component
    {
        public List<Entry> Items = items;
        public int SelectedId = -1;
        /// <summary>A row shows a star beside its item: what an app follows by place and tells
        /// the list with InvalidateRow.</summary>
        public int StarredId = -1;
        /// <summary>The index told with InvalidateRow at the top of the next build.</summary>
        public int TellRow = -1;
        public bool Fields, Keyed;
        public Func<Entry, int, float>? Heights;
        /// <summary>Rows made by the template.</summary>
        public int Made;
        public readonly Ref<VirtualListElement<Entry>> List = new();

        public override Element Build()
        {
            if (TellRow >= 0) { List.Current?.InvalidateRow(TellRow); TellRow = -1; }
            int selectedId = SelectedId;
            var list = VirtualListElement<Entry>.New()
                .ItemHeight(RowHeight)
                .Items(Items)
                .AccessibleItemLabel(e => e.Name)
                .SelectedIndex(Items.FindIndex(e => e.Id == selectedId))
                .RenderWith((e, selected) => Row(e, selected))
                .FlexGrow(1);
            if (Heights is { } heights) list.RowHeights(heights);
            return Flex.Column().FlexGrow(1).Children(list.WithRef(List));
        }

        private Element Row(Entry e, bool selected)
        {
            Made++;
            var text = TextB.New(e.Name + (e.Id == StarredId ? " ★" : "")).FontSize(12f);
            var row = Flex.Row().Padding(new Thickness(2, 8)).Background(selected ? Selected : Color.Transparent);
            if (Fields) row.Children(text, TextInput.New(e.Name).Width(Dimension.Px(120)));
            else row.Children(text);
            if (Keyed) row.Key = e.Name;
            return row;
        }

        public void Rebuild() => MarkDirty();
    }

    private static (HeadlessSession Session, Pane Pane) Mount(bool fields = false, bool keyed = false,
        Func<Entry, int, float>? heights = null)
    {
        var session = HeadlessSession.Create(400, 300);
        try
        {
            var pane = new Pane(Entries(200)) { Fields = fields, Keyed = keyed, Heights = heights };
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

    /// <summary>The realized rows by the text they show, in the order they stand.</summary>
    private static List<(string Name, Element Row)> Rows(Pane pane)
    {
        var list = pane.List.Current!;
        var rows = new List<(string, Element)>();
        for (int i = 1; i < list.Children.Count; i++)
            rows.Add((((TextElement)list.Children[i].Children[0]).Content, list.Children[i]));
        return rows;
    }

    private static Element RowShowing(Pane pane, string name) => Rows(pane).Single(r => r.Name == name).Row;

    private static TextInputElement FieldOf(Element row) => (TextInputElement)row.Children[1];

    private static void Change(HeadlessSession session, Pane pane, List<Entry> items)
    {
        pane.Items = items;
        pane.Rebuild();
        session.PumpFrames(2);
    }

    /// <summary>Every row on screen shows the item at the index where it is drawn, at that index's
    /// height: read by where the rows stand, not by how the list made them.</summary>
    private static void AssertRowsShowTheirItems(Pane pane)
    {
        var list = pane.List.Current!;
        var offsets = new List<float> { 0 };
        for (int i = 0; i < pane.Items.Count; i++)
            offsets.Add(offsets[^1] + (pane.Heights?.Invoke(pane.Items[i], i) ?? RowHeight));
        var rows = Rows(pane);
        Assert.NotEmpty(rows);
        foreach (var (name, row) in rows)
        {
            float top = row.Layout.AbsoluteBorderBox.Y - list.Layout.AbsoluteContentBox.Y + list.ScrollOffsetY;
            int index = offsets.FindIndex(o => Math.Abs(o - top) < 0.5f);
            Assert.True(index >= 0 && index < pane.Items.Count, $"\"{name}\" stands at {top}, where no row starts");
            var item = pane.Items[index];
            Assert.Equal(item.Name + (item.Id == pane.StarredId ? " ★" : ""), name);
            Assert.Equal(offsets[index + 1] - offsets[index], row.Layout.AbsoluteBorderBox.Height, 0.01f);
        }
    }

    [Fact]
    public void AnInsertionAboveTheRows_MakesOnlyTheRowThatComesIn_AndMovesTheRest()
    {
        var (session, pane) = Mount();
        using var _ = session;
        var before = Rows(pane).ToDictionary(r => r.Name, r => r.Row);
        Assert.True(before.Count > 10);
        pane.Made = 0;

        Change(session, pane, [Arrived, .. pane.Items]);

        Assert.Equal(1, pane.Made);
        var after = Rows(pane);
        Assert.Equal("arrived", after[0].Name);
        Assert.All(after.Skip(1), r => Assert.Same(before[r.Name], r.Row));
        AssertRowsShowTheirItems(pane);

        // And gone again: the rows move back, and only the row that comes in at the bottom is made.
        pane.Made = 0;
        Change(session, pane, pane.Items.Skip(1).ToList());
        Assert.Equal(1, pane.Made);
        AssertRowsShowTheirItems(pane);
    }

    [Fact]
    public void AFilter_MovesTheRowsThatStay_AndMakesOnlyTheRowsThatComeIn()
    {
        var (session, pane) = Mount();
        using var _ = session;
        var all = pane.Items;
        var before = Rows(pane).ToDictionary(r => r.Name, r => r.Row);
        pane.Made = 0;

        Change(session, pane, all.Where(e => e.Id % 3 != 1).ToList());

        var after = Rows(pane);
        int cameIn = after.Count(r => !before.ContainsKey(r.Name));
        Assert.InRange(cameIn, 1, after.Count / 2);
        Assert.Equal(cameIn, pane.Made);
        Assert.All(after.Where(r => before.ContainsKey(r.Name)), r => Assert.Same(before[r.Name], r.Row));
        AssertRowsShowTheirItems(pane);

        // The filter taken away: the rows that stayed on screen move back.
        before = after.ToDictionary(r => r.Name, r => r.Row);
        pane.Made = 0;
        Change(session, pane, all);
        after = Rows(pane);
        Assert.Equal(after.Count(r => !before.ContainsKey(r.Name)), pane.Made);
        AssertRowsShowTheirItems(pane);
    }

    /// <summary>A selected row moves with its selection; one whose selection changed as it moved
    /// is made again.</summary>
    [Fact]
    public void ARowMoves_OnlyInTheSelectedStateItWasMadeIn()
    {
        var (session, pane) = Mount();
        using var _ = session;
        pane.SelectedId = 3;
        pane.Rebuild();
        session.PumpFrames(2);
        var selected = RowShowing(pane, "entry 3");
        var other = RowShowing(pane, "entry 5");
        pane.Made = 0;

        Change(session, pane, [Arrived, .. pane.Items]);
        Assert.Equal(1, pane.Made);
        Assert.Same(selected, RowShowing(pane, "entry 3"));
        Assert.Equal(4, pane.List.Current!.SelectedIndex);

        pane.SelectedId = 5;
        pane.Made = 0;
        Change(session, pane, pane.Items.Skip(1).ToList());
        Assert.Equal(3, pane.Made);   // entry 3 and entry 5 in their new states, the row at the bottom
        Assert.NotSame(selected, RowShowing(pane, "entry 3"));
        Assert.NotSame(other, RowShowing(pane, "entry 5"));
        Assert.Equal(Selected, ((BoxElement)RowShowing(pane, "entry 5")).BackgroundColor);
        Assert.Equal(Color.Transparent, ((BoxElement)RowShowing(pane, "entry 3")).BackgroundColor);
        AssertRowsShowTheirItems(pane);
    }

    /// <summary>The field keeps the focus, its object and its place in the tree a reader hears: the
    /// row is read through its elements under the item it now stands at. Made again, as moved rows
    /// were, the field left the tree and took the focus with it.</summary>
    [Fact]
    public void AFocusedFieldInAMovedRow_KeepsTheFocus_AndIsReadAtItsNewItem()
    {
        var (session, pane) = Mount(fields: true);
        using var _ = session;
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrames(2);
        var field = FieldOf(RowShowing(pane, "entry 5"));
        field.Focus();
        session.PumpFrames(2);
        Assert.Same(field, session.Window.Window.FocusedElement);
        // Past the refreshes a burst of changes leaves pending, so the change's frame refreshes.
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        AssertReadAt(bridge, field, "entry 5", 5);

        pane.Items = [Arrived, .. pane.Items];
        pane.Rebuild();
        session.PumpFrame();

        // In the frame of the change: the tree that frame reads has the field at its new item.
        Assert.Same(field, session.Window.Window.FocusedElement);
        Assert.True(field.IsFocused);
        Assert.Same(field, FieldOf(RowShowing(pane, "entry 5")));
        AssertReadAt(bridge, field, "entry 5", 6);
        Assert.Equal(field.Id, bridge.FocusedNode?.ElementId);

        session.PumpFrame(TimeSpan.FromMilliseconds(120));   // past a coalesced refresh
        AssertRowsShowTheirItems(pane);
        AssertReadAt(bridge, field, "entry 5", 6);
        var diff = AccessibilityTreeDiffer.Diff(bridge.CurrentTree!, session.Window.Window.BuildAccessibilityTree());
        Assert.True(diff.IsEmpty, $"the reader's tree is stale: {diff.Added.Count} added, {diff.Removed.Count} removed, {diff.PropertyChanged.Count} changed");
    }

    private static void AssertReadAt(HeadlessAccessibilityBridge bridge, Element field, string name, int index)
    {
        var node = Find(bridge.CurrentTree!, field.Id);
        Assert.NotNull(node);
        Assert.True(node!.IsFocused);
        var item = node.Parent;
        while (item is not null && item.Role != AccessibilityRole.ListItem) item = item.Parent;
        Assert.Equal(name, item?.Name);
        Assert.Equal(index, item!.Parent!.Children.ToList().IndexOf(item));
    }

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    /// <summary>A row under the pointer is made again, as before: moved, it would take its hover
    /// away from the pointer until the pointer moves.</summary>
    [Fact]
    public void ARowUnderThePointer_IsMadeAgain_NotMoved()
    {
        var (session, pane) = Mount();
        using var _ = session;
        var hovered = RowShowing(pane, "entry 5");
        var box = hovered.WindowBorderBox;
        session.Window.MoveMouse(box.X + 20, box.Y + box.Height / 2);
        session.PumpFrames(1);
        Assert.True(hovered.IsHovered);
        pane.Made = 0;

        Change(session, pane, [Arrived, .. pane.Items]);

        Assert.Equal(2, pane.Made);   // the arrival, and entry 5 at its new place
        Assert.NotSame(hovered, RowShowing(pane, "entry 5"));
        Assert.False(RowShowing(pane, "entry 5").IsHovered);
        AssertRowsShowTheirItems(pane);
    }

    /// <summary>What an app tells with InvalidateRow follows a place: in a build that also moves the
    /// rows, the told row's item comes from the template, not from the row that moved there.</summary>
    [Fact]
    public void ARowToldWithInvalidateRow_InTheBuildThatMovesIt_ShowsWhatWasTold()
    {
        var (session, pane) = Mount();
        using var _ = session;

        // A star comes to entry 7 as an arrival moves every row one down; the app tells the
        // list the star's place in the new items, at the top of the build.
        pane.StarredId = 7;
        pane.TellRow = 8;
        Change(session, pane, [Arrived, .. pane.Items]);

        Assert.Contains(Rows(pane), r => r.Name == "entry 7 ★");
        AssertRowsShowTheirItems(pane);

        // The next change moves rows again.
        pane.Made = 0;
        Change(session, pane, pane.Items.Skip(1).ToList());
        Assert.Equal(1, pane.Made);
        AssertRowsShowTheirItems(pane);
    }

    /// <summary>A keyed row moves as itself: one object for its key, not a second from the template.</summary>
    [Fact]
    public void AKeyedRow_MovesAsItself()
    {
        var (session, pane) = Mount(keyed: true);
        using var _ = session;
        var before = Rows(pane).ToDictionary(r => r.Name, r => r.Row);
        pane.Made = 0;

        Change(session, pane, [Arrived, .. pane.Items]);

        Assert.Equal(1, pane.Made);
        var after = Rows(pane);
        Assert.All(after.Skip(1), r => Assert.Same(before[r.Name], r.Row));
        Assert.Equal(after.Count, after.Select(r => r.Row.Key).Distinct().Count());
        AssertRowsShowTheirItems(pane);
    }

    /// <summary>Heights by place: a row that moves takes the height of the index it moves to.</summary>
    [Fact]
    public void AMovedRow_TakesTheHeightOfItsNewPlace()
    {
        var (session, pane) = Mount(heights: (_, i) => i % 2 == 0 ? 24f : 36f);
        using var _ = session;
        var before = Rows(pane).ToDictionary(r => r.Name, r => r.Row);
        pane.Made = 0;

        Change(session, pane, [Arrived, .. pane.Items]);

        Assert.Equal(1, pane.Made);
        Assert.Same(before["entry 4"], RowShowing(pane, "entry 4"));
        AssertRowsShowTheirItems(pane);
    }
}
