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
/// A virtual list's rows used to be made again from the template on every rebuild of their
/// owner — a key walk down a History list made every row on screen for each key, to change
/// the look of two. A list given <c>.RenderWith((item, selected) => …)</c> promises its rows
/// show only their item and whether it is selected, and keeps a realized row until one of
/// those changes. These pin the contract: what is kept, what is made again, what an app must
/// say with <see cref="VirtualListElement{T}.InvalidateRows"/>, and that a kept row looks and
/// reads exactly as a made one.
/// </summary>
public class VirtualListItemRowTests
{
    private const float RowHeight = 24f;
    private static readonly Color Selected = new(40, 90, 200);
    private static readonly Color Hovered = new(220, 220, 228);

    private sealed record Entry(int Id, string Name);

    private static List<Entry> Entries(int count) =>
        Enumerable.Range(0, count).Select(i => new Entry(i, $"entry {i}")).ToList();

    /// <summary>A pane whose Build reads the selection, as Junction's History does.</summary>
    private sealed class Pane(bool itemRows, List<Entry> items) : Component
    {
        public List<Entry> Items = items;
        public int SelectedId;
        /// <summary>Something a row shows that is neither its item nor its selection.</summary>
        public string Suffix = "";
        /// <summary>Rows made by the template.</summary>
        public int Made;
        public readonly Ref<VirtualListElement<Entry>> List = new();

        public override Element Build()
        {
            int selectedId = SelectedId;
            var list = VirtualListElement<Entry>.New()
                .ItemHeight(RowHeight)
                .Items(Items)
                .AccessibleItemLabel(e => e.Name)
                .SelectedIndex(Items.FindIndex(e => e.Id == selectedId))
                .FlexGrow(1);
            if (itemRows) list.RenderWith((e, selected) => Row(e, selected));
            else list.RenderWith(e => Row(e, e.Id == selectedId));
            return Flex.Column().FlexGrow(1).Children(list.WithRef(List));
        }

        private Element Row(Entry e, bool selected)
        {
            Made++;
            var row = Flex.Row().Padding(new Thickness(2, 8))
                .Background(selected ? Selected : Color.Transparent)
                .Children(TextB.New(e.Name + Suffix).FontSize(12f));
            if (!selected) row.OnHover(b => b.BackgroundColor = Hovered);
            return row;
        }

        public void Select(int id) { SelectedId = id; MarkDirty(); }

        public void Rebuild() => MarkDirty();
    }

    private static (HeadlessSession Session, Pane Pane) Mount(bool itemRows, int count = 200,
        int width = 400, int height = 300)
    {
        var session = HeadlessSession.Create(width, height);
        try
        {
            var pane = new Pane(itemRows, Entries(count));
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

    private static Element RowShowing(Pane pane, string name) =>
        Rows(pane).Single(r => r.Name == name).Row;

    [Fact]
    public void ARebuildThatChangesNothing_MakesNoRow_AndKeepsEveryOne()
    {
        var (session, pane) = Mount(itemRows: true);
        using var _ = session;
        var before = Rows(pane);
        Assert.True(before.Count > 10);
        pane.Made = 0;

        pane.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(0, pane.Made);
        Assert.Equal(before.Select(r => r.Row), Rows(pane).Select(r => r.Row));
    }

    /// <summary>The baseline the opt-in is measured against: a one-argument template is still
    /// asked for every row on screen on every rebuild, as it always was.</summary>
    [Fact]
    public void TheItemTemplate_StillMakesEveryRowOnEveryRebuild()
    {
        var (session, pane) = Mount(itemRows: false);
        using var _ = session;
        int onScreen = Rows(pane).Count;
        var before = RowShowing(pane, "entry 5");
        pane.Made = 0;

        pane.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(onScreen, pane.Made);
        Assert.NotSame(before, RowShowing(pane, "entry 5"));
    }

    [Fact]
    public void ASelectionMove_MakesTheOldAndTheNewRowAgain_AndKeepsTheRest()
    {
        var (session, pane) = Mount(itemRows: true);
        using var _ = session;
        pane.Select(3);
        session.PumpFrames(2);
        var before = Rows(pane).ToDictionary(r => r.Name, r => r.Row);
        pane.Made = 0;

        pane.Select(4);
        session.PumpFrames(2);

        Assert.Equal(2, pane.Made);
        var after = Rows(pane).ToDictionary(r => r.Name, r => r.Row);
        Assert.Equal(before.Keys, after.Keys);
        foreach (var (name, row) in after)
        {
            if (name is "entry 3" or "entry 4") Assert.NotSame(before[name], row);
            else Assert.Same(before[name], row);
        }
        Assert.Equal(Selected, ((BoxElement)after["entry 4"]).BackgroundColor);
        Assert.Equal(Color.Transparent, ((BoxElement)after["entry 3"]).BackgroundColor);
        Assert.Equal(4, pane.List.Current!.SelectedIndex);
    }

    /// <summary>Setting the selected index on the mounted list, with no rebuild, is a selection
    /// move too.</summary>
    [Fact]
    public void SelectedIndexSetOnTheMountedList_MakesTheTwoRowsAgain()
    {
        var (session, pane) = Mount(itemRows: true);
        using var _ = session;
        pane.Made = 0;

        pane.List.Current!.SelectedIndex = 6;
        session.PumpFrames(2);

        Assert.Equal(2, pane.Made);   // row 0 (it had the selection) and row 6
        Assert.Equal(Selected, ((BoxElement)RowShowing(pane, "entry 6")).BackgroundColor);
        Assert.Equal(Color.Transparent, ((BoxElement)RowShowing(pane, "entry 0")).BackgroundColor);
    }

    /// <summary>Items are compared with the default equality of their type: a record that comes
    /// back equal (a reload that read the same data) keeps its row; one that differs is made
    /// again.</summary>
    [Fact]
    public void AChangedItem_MakesOnlyItsRowAgain_AndAnEqualOneKeepsItsRow()
    {
        var (session, pane) = Mount(itemRows: true);
        using var _ = session;
        var before = Rows(pane).ToDictionary(r => r.Name, r => r.Row);
        pane.Made = 0;

        var items = pane.Items.ToList();          // a new list, as a reload makes
        items[5] = new Entry(5, "entry 5 renamed");
        items[6] = new Entry(6, "entry 6");       // a new object, equal to the old
        pane.Items = items;
        pane.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(1, pane.Made);
        var after = Rows(pane).ToDictionary(r => r.Name, r => r.Row);
        Assert.True(after.ContainsKey("entry 5 renamed"));
        Assert.Same(before["entry 6"], after["entry 6"]);
        Assert.Same(before["entry 4"], after["entry 4"]);
    }

    /// <summary>The contract's other half: what a row shows beyond its item and selection is
    /// not seen by the list. Until the app says so the kept rows show the old value; after
    /// <see cref="VirtualListElement{T}.InvalidateRows"/> every row is made again.</summary>
    [Fact]
    public void StateARowShowsBesideItsItem_NeedsInvalidateRows()
    {
        var (session, pane) = Mount(itemRows: true);
        using var _ = session;
        int onScreen = Rows(pane).Count;
        pane.Made = 0;

        pane.Suffix = " *";
        pane.Rebuild();
        session.PumpFrames(2);
        Assert.Equal(0, pane.Made);
        Assert.Contains(Rows(pane), r => r.Name == "entry 2");   // stale, as documented

        pane.List.Current!.InvalidateRows();
        session.PumpFrames(2);
        Assert.Equal(onScreen, pane.Made);
        Assert.All(Rows(pane), r => Assert.EndsWith(" *", r.Name));
    }

    /// <summary>Called through the ref at the top of a rebuild, before the list is patched — the
    /// way an app that keeps a "what the rows show" key says it changed.</summary>
    [Fact]
    public void InvalidateRowsBeforeTheRebuild_MakesTheRowsFromTheNewBuild()
    {
        var (session, pane) = Mount(itemRows: true);
        using var _ = session;

        pane.Suffix = " !";
        pane.List.Current!.InvalidateRows();
        pane.Rebuild();
        session.PumpFrames(2);

        Assert.All(Rows(pane), r => Assert.EndsWith(" !", r.Name));
    }

    [Fact]
    public void ASwitchToAnotherTheme_MakesEveryRowAgain()
    {
        var (session, pane) = Mount(itemRows: true);
        using var _ = session;
        int onScreen = Rows(pane).Count;
        pane.Made = 0;

        session.Application.Theme = Theme.Dark;
        session.PumpFrames(2);
        Assert.Equal(onScreen, pane.Made);

        // The style pass hands the list its theme on every rebuild; the same one makes nothing.
        pane.Made = 0;
        pane.Rebuild();
        session.PumpFrames(2);
        Assert.Equal(0, pane.Made);
    }

    /// <summary>Row heights from a function are worked out again on every rebuild, as they always
    /// were: a kept row takes its new height without being made again.</summary>
    [Fact]
    public void AKeptRow_TakesANewHeight_WithoutBeingMadeAgain()
    {
        using var session = HeadlessSession.Create(400, 300);
        var host = new Tall();
        session.Application.SetRoot(host.Mount());
        session.PumpFrames(3);
        var list = host.List.Current!;
        var row2 = list.Children[3];
        Assert.Equal(RowHeight, row2.Layout.Height);
        host.Made = 0;

        host.Height = 30f;
        host.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(0, host.Made);
        Assert.Same(row2, list.Children[3]);
        Assert.Equal(30f, row2.Layout.Height);
        Assert.Equal(list.Children[2].Layout.AbsoluteY + 30f, row2.Layout.AbsoluteY);
    }

    private sealed class Tall : Component
    {
        public float Height = RowHeight;
        public int Made;
        public readonly Ref<VirtualListElement<Entry>> List = new();

        public override Element Build()
        {
            var list = VirtualListElement<Entry>.New()
                .ItemHeight(RowHeight)
                .Items(Entries(100))
                .RenderWith((e, selected) => { Made++; return Flex.Row().Children(TextB.New(e.Name).FontSize(12f)); })
                .FlexGrow(1);
            float height = Height;
            list.RowHeights((_, _) => height);
            return Flex.Column().FlexGrow(1).Children(list.WithRef(List));
        }

        public void Rebuild() => MarkDirty();
    }

    /// <summary>A multi-selection changed by assistive technology is a selection change too.</summary>
    [Fact]
    public void AMultiSelectionChange_MakesTheRowAgain()
    {
        var (session, pane) = Mount(itemRows: true);
        using var _ = session;
        var list = pane.List.Current!;
        list.CanSelectMultiple = true;
        list.InvalidateRows();
        session.PumpFrames(2);
        var before = RowShowing(pane, "entry 7");
        pane.Made = 0;

        ((IAccessibleItemSource)list).SetItemSelected(7, true);
        session.PumpFrames(2);

        Assert.Equal(1, pane.Made);
        Assert.NotSame(before, RowShowing(pane, "entry 7"));
        Assert.Equal(Selected, ((BoxElement)RowShowing(pane, "entry 7")).BackgroundColor);
    }

    /// <summary>A kept row is not resolved by the template's pass alone: the style pass resolves
    /// it again under the list. A row made beside it must inherit the same way, or kept and made
    /// rows would differ in any text style an ancestor sets.</summary>
    [Fact]
    public void KeptAndMadeRows_InheritTheSameStyle()
    {
        using var session = HeadlessSession.Create(400, 300);
        var pane = new Pane(itemRows: true, Entries(200));
        var inherited = new Color(200, 30, 30);
        session.Application.SetRoot(Flex.Column().FlexGrow(1).TextColor(inherited).Children(pane.Mount()));
        session.PumpFrames(3);

        pane.Select(4);
        session.PumpFrames(2);

        Assert.All(Rows(pane), r =>
            Assert.Equal(inherited, r.Row.Children[0].ResolvedInherited.TextColor));
    }

    /// <summary>Assistive technology sees the list's items, not its rows: the same nodes, names
    /// and selected states either way.</summary>
    [Fact]
    public void TheAccessibilityTree_IsTheSameAsWithTheItemTemplate()
    {
        // One application per process: the two runs take turns.
        var legacy = Walk(itemRows: false);
        var kept = Walk(itemRows: true);

        Assert.Equal(legacy.Count, kept.Count);
        for (int i = 0; i < legacy.Count; i++)
            Assert.Equal(legacy[i], kept[i]);
        Assert.Equal(200, kept[^1].Count);
        Assert.Equal(("entry 30", true), kept[^1].Single(i => i.Selected == true));

        static List<List<(string? Name, bool? Selected)>> Walk(bool itemRows)
        {
            var (session, pane) = Mount(itemRows);
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

    /// <summary>
    /// Kept rows draw exactly what made rows draw: the same pixels after selection moves on
    /// screen and past its edge (which scrolls), with a row hovered, and after a theme switch.
    /// </summary>
    [Fact]
    public void KeptRows_DrawTheSamePixelsAsMadeRows()
    {
        (string What, Action<HeadlessSession, Pane> Step)[] steps =
        [
            ("selection on row 2", (_, p) => p.Select(2)),
            ("selection moved to row 3", (_, p) => p.Select(3)),
            ("row 5 hovered", (s, _) => s.Window.MoveMouse(100, 5 * RowHeight + 8)),
            ("the hovered row selected", (_, p) => p.Select(5)),
            ("row 8 hovered", (s, _) => s.Window.MoveMouse(100, 8 * RowHeight + 8)),
            ("selection past the edge, scrolled", (_, p) =>
            {
                p.Select(40);
                p.List.Current!.ScrollRangeIntoView(40 * RowHeight, RowHeight);
            }),
            ("selection moved up", (_, p) => p.Select(39)),
            ("dark theme", (s, _) => s.Application.Theme = Theme.Dark),
            ("a move in the dark theme", (_, p) => p.Select(38)),
        ];

        // One application per process: the two runs take turns.
        var legacy = Shoot(itemRows: false);
        var kept = Shoot(itemRows: true);

        for (int i = 0; i < steps.Length; i++)
            Assert.True(legacy[i].AsSpan().SequenceEqual(kept[i]), $"{steps[i].What}: the kept rows drew other pixels");

        List<byte[]> Shoot(bool itemRows)
        {
            var (session, pane) = Mount(itemRows);
            using var _ = session;
            var shots = new List<byte[]>();
            foreach (var (_, step) in steps)
            {
                step(session, pane);
                session.PumpFrames(2);
                using var image = session.Window.CapturePixels();
                using var bitmap = SKBitmap.FromImage(image);
                shots.Add(bitmap.Bytes);
            }
            return shots;
        }
    }
}
