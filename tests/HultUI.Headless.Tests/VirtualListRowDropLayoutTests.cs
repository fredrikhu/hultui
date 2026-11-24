using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using SkiaSharp;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A list that lets go of rows whose state changed (selected, current, or told with
/// <see cref="VirtualListElement{T}.InvalidateRow"/>) has the frame's drawing make them again,
/// and lays nothing out around itself (plan 17 round 19). It marked its layout, which reset
/// every ancestor's measurements: a key in Junction's Files tree onto a folder laid out 65 boxes
/// and measured 227 containers and 176 leaves around the list, for a list whose own box never
/// comes from its rows. These pin both halves: no box around the list is laid out or measured
/// for a row-state change, the frame is the one a fresh layout of the same state draws, and a
/// row whose height moves with its state still moves the rows below it and the list's extent.
/// </summary>
public class VirtualListRowDropLayoutTests
{
    private const float RowHeight = 24f;
    private const float TallRow = 52f;
    private static readonly Color Selected = new(40, 90, 200);

    private sealed record Entry(int Id, string Name);

    /// <summary>A view built around a list as an app's is: a header, a side column of labels,
    /// a label above the list in its column, a footer. Its build reads the selection.</summary>
    private sealed class Screen(int count) : Component
    {
        public readonly List<Entry> Items = Enumerable.Range(0, count).Select(i => new Entry(i, $"entry {i}")).ToList();
        public int SelectedIndex;
        public int CurrentIndex = -1;
        /// <summary>Unequal heights by index, which no state changes.</summary>
        public bool Unequal;
        /// <summary>The selected row is taller: a height function rebuilt with the selection.</summary>
        public bool TallSelected;
        /// <summary>The row of <see cref="TallId"/> is taller and starred, read live: the app tells the
        /// list itself.</summary>
        public bool TallById;
        public int TallId = -1;
        public int Made;
        public readonly Ref<VirtualListElement<Entry>> List = new();

        public override Element Build()
        {
            int selected = SelectedIndex;
            var list = VirtualListElement<Entry>.New()
                .ItemHeight(RowHeight)
                .Items(Items)
                .SelectedIndex(selected)
                .CurrentIndex(CurrentIndex)
                .RenderWithState((e, state) => Row(e, state))
                .FlexGrow(1);
            if (TallSelected) list.RowHeights((_, i) => i == selected ? TallRow : RowHeight);
            else if (Unequal) list.RowHeights((_, i) => i % 3 == 0 ? 36f : RowHeight);
            else if (TallById) list.RowHeights((e, _) => e.Id == TallId ? TallRow : RowHeight);
            return Flex.Column(gap: 4).Padding(new Thickness(8)).Children(
                TextB.New("header of the view").FontSize(14f),
                Flex.Row(gap: 6).FlexGrow(1).Children(
                    Flex.Column(gap: 2).Width(Dimension.Px(120)).Children(
                        Enumerable.Range(0, 6).Select(i => (Element)TextB.New($"side {i}").FontSize(12f)).ToArray()),
                    Flex.Column(gap: 2).FlexGrow(1).Children(
                        TextB.New("above the list").FontSize(12f),
                        list.WithRef(List))),
                TextB.New("footer").FontSize(12f));
        }

        private Element Row(Entry e, ListItemState state)
        {
            Made++;
            string text = e.Name + (state.IsCurrent ? " <" : "") + (e.Id == TallId ? " *" : "");
            return Flex.Row().Padding(new Thickness(2, 8))
                .Background(state.IsSelected ? Selected : Color.Transparent)
                .Children(TextB.New(text).FontSize(12f));
        }

        public void Rebuild() => MarkDirty();
    }

    private static (HeadlessSession Session, Screen Screen) Mount(Action<Screen>? set = null, int count = 200)
    {
        var session = HeadlessSession.Create(500, 400);
        try
        {
            var screen = new Screen(count);
            set?.Invoke(screen);
            session.Application.SetRoot(screen.Mount());
            session.PumpFrames(3);
            return (session, screen);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>The boxes the window's layout pass lays out and measures while
    /// <paramref name="act"/> runs and a frame is drawn: element scopes of the frame tracer. The
    /// list's rows are laid out by the list itself, which records none.</summary>
    private static (int Layouts, int Measures) LayoutWorkOf(HeadlessSession session, Action act)
    {
        var tracer = session.Window.Window.Tracer;
        string path = Path.Combine(Path.GetTempPath(), $"hultui-rowdrop-{Guid.NewGuid():N}.json");
        tracer.StartRecording();
        try
        {
            act();
            session.PumpFrames(1);
        }
        finally { tracer.StopRecording(path); }
        string json = File.ReadAllText(path);
        File.Delete(path);
        return (Count(json, "\"cat\":\"Layout\""), Count(json, "\"cat\":\"Measure\""));
    }

    private static int Count(string s, string what)
    {
        int n = 0;
        for (int i = s.IndexOf(what, StringComparison.Ordinal); i >= 0; i = s.IndexOf(what, i + what.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    private static List<string> LayoutDirty(Element e, List<string>? into = null)
    {
        into ??= [];
        if ((e.DirtyFlags & DirtyFlags.Layout) != 0) into.Add(e.GetType().Name);
        foreach (var c in e.Children) LayoutDirty(c, into);
        return into;
    }

    /// <summary>Every box of the realized rows, in the order they stand, with what they show.</summary>
    private static List<string> RowBoxes(VirtualListElement<Entry> list)
    {
        var boxes = new List<string>();
        void Walk(Element e, int depth)
        {
            var b = e.Layout.AbsoluteBorderBox;
            boxes.Add($"{depth} {e.GetType().Name} {(e as TextElement)?.Content} {b.X:R},{b.Y:R} {b.Width:R}x{b.Height:R}");
            foreach (var c in e.Children) Walk(c, depth + 1);
        }
        foreach (var c in list.Children) Walk(c, 0);
        return boxes;
    }

    private static byte[] Pixels(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        using var pixels = image.PeekPixels();
        return pixels.GetPixelSpan().ToArray();
    }

    public static TheoryData<string> Changes => new() { "selected", "current", "rebuild", "row" };

    /// <summary>Makes the change: the selection set on the live list, the cursor set on it, the
    /// selection from a rebuild of the view (the patch drops the rows), or one row told.</summary>
    private static void Change(Screen screen, string how)
    {
        var list = screen.List.Current!;
        switch (how)
        {
            case "selected": list.SelectedIndex = 5; break;
            case "current": list.CurrentIndex = 4; break;
            case "rebuild": screen.SelectedIndex = 5; screen.Rebuild(); break;
            case "row": list.InvalidateRow(3); break;
        }
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public void ARowStateChange_LaysOutAndMeasuresNothingAroundTheList(string how)
    {
        var (session, screen) = Mount(s => s.SelectedIndex = 2);
        using var _ = session;
        int made = screen.Made;
        Element root = screen.List.Current!;
        while (root.Parent is { } parent) root = parent;

        var (layouts, measures) = LayoutWorkOf(session, () =>
        {
            Change(screen, how);
            // Set on the live list, the change marks nothing for layout up to the window.
            if (how is "selected" or "current" or "row") Assert.Empty(LayoutDirty(root));
        });

        Assert.Equal(0, layouts);
        Assert.Equal(0, measures);
        // The rows it let go of were made again in that frame: the two a selection moved between,
        // the one the cursor came to (there was none), the one told.
        Assert.Equal(how is "row" or "current" ? 1 : 2, screen.Made - made);
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public void ARowStateChange_DrawsTheFrameAFreshLayoutDraws(string how)
    {
        foreach (bool unequal in new[] { false, true })
        {
            var (session, screen) = Mount(s => { s.SelectedIndex = 2; s.Unequal = unequal; });
            using var _ = session;
            Change(screen, how);
            session.PumpFrames(2);

            var (fresh, freshScreen) = Mount(s =>
            {
                s.Unequal = unequal;
                s.SelectedIndex = how is "selected" or "rebuild" ? 5 : 2;
                s.CurrentIndex = how == "current" ? 4 : -1;
            });
            using var __ = fresh;

            Assert.Equal(RowBoxes(freshScreen.List.Current!), RowBoxes(screen.List.Current!));
            Assert.True(Pixels(fresh).AsSpan().SequenceEqual(Pixels(session)), $"{how}, unequal {unequal}: the frame differs from a fresh one");
        }
    }

    /// <summary>A height that moves with the selection comes from a height function the rebuild
    /// makes again: the patch works the heights out, and the rows below the taller row move down by
    /// what it grew, as does the list's extent.</summary>
    [Fact]
    public void ARowMadeTallerByItsSelection_MovesTheRowsBelowIt()
    {
        var (session, screen) = Mount(s => { s.TallSelected = true; s.SelectedIndex = -1; });
        using var _ = session;
        var list = screen.List.Current!;
        Assert.Equal(200 * RowHeight, list.ContentHeight);

        screen.SelectedIndex = 5;
        screen.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(199 * RowHeight + TallRow, list.ContentHeight);
        float top = list.Layout.AbsoluteContentBox.Y;
        var rows = list.Children.Skip(1).ToList();   // the spacer first
        Assert.Equal(top + 4 * RowHeight, rows[4].Layout.AbsoluteBorderBox.Y);
        Assert.Equal(TallRow, rows[5].Layout.AbsoluteBorderBox.Height);
        Assert.Equal(top + 5 * RowHeight + TallRow, rows[6].Layout.AbsoluteBorderBox.Y);

        var (fresh, freshScreen) = Mount(s => { s.TallSelected = true; s.SelectedIndex = 5; });
        using var __ = fresh;
        Assert.Equal(RowBoxes(freshScreen.List.Current!), RowBoxes(list));
        Assert.True(Pixels(fresh).AsSpan().SequenceEqual(Pixels(session)), "the frame differs from a fresh one");
    }

    /// <summary>The same when the app tells the list itself: the row made again, and the heights
    /// worked out again (<see cref="VirtualListElement{T}.InvalidateHeights"/>), with no rebuild.</summary>
    [Fact]
    public void ARowToldTallerByTheApp_MovesTheRowsBelowIt()
    {
        var (session, screen) = Mount(s => s.TallById = true);
        using var _ = session;
        var list = screen.List.Current!;
        Assert.Equal(200 * RowHeight, list.ContentHeight);

        screen.TallId = 4;
        list.InvalidateRow(4);
        list.InvalidateHeights();
        session.PumpFrames(2);

        Assert.Equal(199 * RowHeight + TallRow, list.ContentHeight);
        float top = list.Layout.AbsoluteContentBox.Y;
        var rows = list.Children.Skip(1).ToList();
        Assert.Equal(top + 3 * RowHeight, rows[3].Layout.AbsoluteBorderBox.Y);
        Assert.Equal(TallRow, rows[4].Layout.AbsoluteBorderBox.Height);
        Assert.Equal(top + 4 * RowHeight + TallRow, rows[5].Layout.AbsoluteBorderBox.Y);

        var (fresh, freshScreen) = Mount(s => { s.TallById = true; s.TallId = 4; });
        using var __ = fresh;
        Assert.Equal(RowBoxes(freshScreen.List.Current!), RowBoxes(list));
        Assert.True(Pixels(fresh).AsSpan().SequenceEqual(Pixels(session)), "the frame differs from a fresh one");
    }
}
