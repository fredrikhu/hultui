using System.IO;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using VesperCommander.Theme;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The panes', the comparison's and the viewer's lists are on item rows: a rebuild keeps every
/// realized row whose entry (or line) is unchanged, and the cursor is the panes' selection, so
/// a key that moves it makes the two rows it moved between. Everything else a row shows is in
/// a shape the app compares each build, and a change makes the rows again. Each test here
/// changes one of those things and nothing else, and checks the rows show it: a field missing
/// from a shape leaves the kept rows showing the old state.
/// </summary>
public class KeptRowTests
{
    private const int Files = 40;

    private static string Name(int i) => $"f{i:00}.txt";

    /// <summary>Two folders; with <paramref name="sameNames"/> the right one holds the left
    /// one's names, so the F3 partner follows the cursor across.</summary>
    private static DiffViewTests.Fixture Open(bool sameNames = false)
    {
        var t = new DiffViewTests.Fixture();
        Directory.CreateDirectory(Path.Combine(t.Left, "docs"));
        for (int i = 0; i < Files; i++)
        {
            t.WriteLeft(Name(i), $"left {i}\n");
            t.WriteRight(sameNames ? Name(i) : $"other{i:00}.txt", $"right {i}\n");
        }
        t.Open();
        t.Session.PumpFrames(2);
        return t;
    }

    private static VirtualListElement<FileEntry> List(DiffViewTests.Fixture t, char side) =>
        t.Field<Ref<VirtualListElement<FileEntry>>>(side == 'L' ? "_vlistL" : "_vlistR").Current
        ?? throw new Xunit.Sdk.XunitException($"the {side} pane has no list");

    /// <summary>The realized rows by the name they show.</summary>
    private static Dictionary<string, Element> Rows(DiffViewTests.Fixture t, char side)
    {
        var list = List(t, side);
        var rows = new Dictionary<string, Element>();
        for (int i = 1; i < list.Children.Count; i++)
            rows[NameLabel(list.Children[i]).Content] = list.Children[i];
        return rows;
    }

    /// <summary>The name label: the row's second cell, or the first label in it when a result
    /// set adds the source folder beside the name.</summary>
    private static TextElement NameLabel(Element row) =>
        row.Children[1] as TextElement ?? (TextElement)row.Children[1].Children[0];

    private static void Rebuild(DiffViewTests.Fixture t)
    {
        t.MarkDirty();
        t.Session.PumpFrames(2);
    }

    [Fact]
    public void ACursorMove_MakesTheTwoRowsItMovedBetween_AndKeepsTheRest()
    {
        using var t = Open();
        t.PutCursorOn('L', Name(3));
        var left = Rows(t, 'L');
        var right = Rows(t, 'R');

        t.Press(KeyCode.Down);
        t.Session.PumpFrames(1);

        var leftAfter = Rows(t, 'L');
        foreach (var (name, row) in leftAfter)
        {
            if (name == Name(3) || name == Name(4)) Assert.NotSame(left[name], row);
            else if (left.TryGetValue(name, out var before)) Assert.Same(before, row);
        }
        Assert.All(Rows(t, 'R'), r => Assert.Same(right[r.Key], r.Value));

        // The ring moved, and the list tells assistive technology which entry the cursor is on.
        Assert.Equal(Nocturne.Accent600, ((BoxElement)leftAfter[Name(4)]).BorderColor);
        Assert.Equal(Color.Transparent, ((BoxElement)leftAfter[Name(3)]).BorderColor);
        Assert.Equal(t.Cursor('L'), List(t, 'L').SelectedIndex);
        Assert.Equal(-1, List(t, 'R').SelectedIndex);
    }

    [Fact]
    public void AnUnchangedRebuild_KeepsEveryRow()
    {
        using var t = Open();
        var left = Rows(t, 'L');
        Rebuild(t);
        Assert.All(Rows(t, 'L'), r => Assert.Same(left[r.Key], r.Value));
    }

    /// <summary>The row draws red and a reader hears it marked: the list keeps each entry's
    /// label too, and lets it go with the rows.</summary>
    [Fact]
    public void AMark_DrawsTheMarkedRowRed_AndIsHeard()
    {
        using var t = Open();
        var bridge = t.Session.Window.InstallAccessibilityBridge();
        t.PutCursorOn('L', Name(1));
        t.Session.PumpFrames(2);
        Assert.Contains(ItemNames(bridge), n => n.StartsWith(Name(5) + ",", System.StringComparison.Ordinal) && !n.EndsWith("marked", System.StringComparison.Ordinal));

        t.Mark('L', Name(5));   // not the cursor's row: only the marks change
        Rebuild(t);

        Assert.Equal(Nocturne.MarkRed, NameLabel(Rows(t, 'L')[Name(5)]).ResolvedInherited.TextColor);
        Assert.Contains(ItemNames(bridge), n => n.StartsWith(Name(5) + ",", System.StringComparison.Ordinal) && n.EndsWith(", marked", System.StringComparison.Ordinal));
    }

    private static List<string> ItemNames(HultUI.Headless.HeadlessAccessibilityBridge bridge)
    {
        var names = new List<string>();
        Walk(bridge.CurrentTree!);
        return names;

        void Walk(HultUI.Core.Accessibility.AccessibilityNode node)
        {
            if (node.Role == HultUI.Core.Accessibility.AccessibilityRole.ListItem) names.Add(node.Name ?? "");
            foreach (var child in node.Children) Walk(child);
        }
    }

    [Fact]
    public void TheF3Partner_FollowsTheCursorAcross()
    {
        using var t = Open(sameNames: true);
        t.PutCursorOn('L', Name(2));
        Assert.Equal(Nocturne.Accent700, ((BoxElement)Rows(t, 'R')[Name(2)]).BorderColor);

        t.PutCursorOn('L', Name(6));
        var right = Rows(t, 'R');
        Assert.Equal(Nocturne.Accent700, ((BoxElement)right[Name(6)]).BorderColor);
        Assert.Equal(Color.Transparent, ((BoxElement)right[Name(2)]).BorderColor);
    }

    /// <summary>The partner is not in the shape: a key that moves it makes the row it left and the
    /// row it reached over here, and keeps every other row of the pane.</summary>
    [Fact]
    public void TheF3PartnerMoving_MakesOnlyTheRowItLeftAndTheRowItReached()
    {
        using var t = Open(sameNames: true);
        t.PutCursorOn('L', Name(2));
        var right = Rows(t, 'R');
        Assert.Equal(Nocturne.Accent700, ((BoxElement)right[Name(2)]).BorderColor);

        t.Press(KeyCode.Down);
        t.Session.PumpFrames(1);

        var after = Rows(t, 'R');
        Assert.Equal(right.Keys, after.Keys);
        foreach (var (name, row) in after)
        {
            if (name == Name(2) || name == Name(3)) Assert.NotSame(right[name], row);
            else Assert.Same(right[name], row);
        }
        Assert.Equal(Nocturne.Accent700, ((BoxElement)after[Name(3)]).BorderColor);
        Assert.Equal(Color.Transparent, ((BoxElement)after[Name(2)]).BorderColor);

        // Off a file, nothing over here pairs with the cursor, and the ring goes.
        t.PutCursorOn('L', "docs");
        Assert.All(Rows(t, 'R').Values, r => Assert.NotEqual(Nocturne.Accent700, ((BoxElement)r).BorderColor));
    }

    [Fact]
    public void TheSizeAndDateColumns_FollowTheSettings()
    {
        using var t = Open();
        var settings = t.Field<Settings>("_settings");
        bool size = settings.ShowSize, date = settings.ShowDate;
        try
        {
            settings.ShowSize = true; settings.ShowDate = true;
            Rebuild(t);
            int both = Rows(t, 'L')[Name(1)].Children.Count;

            settings.ShowSize = false;
            Rebuild(t);
            Assert.Equal(both - 1, Rows(t, 'L')[Name(1)].Children.Count);

            settings.ShowDate = false;
            Rebuild(t);
            Assert.Equal(both - 2, Rows(t, 'L')[Name(1)].Children.Count);
        }
        finally { settings.ShowSize = size; settings.ShowDate = date; }
    }

    [Fact]
    public void TheZebraTint_FollowsTheSetting()
    {
        using var t = Open();
        var settings = t.Field<Settings>("_settings");
        bool zebra = settings.Zebra;
        try
        {
            t.PutCursorOn('L', Name(10));   // away from the rows looked at
            settings.Zebra = false;
            Rebuild(t);
            Assert.All(new[] { Name(1), Name(2) }, n =>
                Assert.Equal(Color.Transparent, ((BoxElement)Rows(t, 'L')[n]).BackgroundColor));

            settings.Zebra = true;
            Rebuild(t);
            var rows = Rows(t, 'L');
            Assert.Contains(new[] { Name(1), Name(2) }, n => ((BoxElement)rows[n]).BackgroundColor != Color.Transparent);
        }
        finally { settings.Zebra = zebra; }
    }

    [Fact]
    public void AFileCutForAMove_IsGhosted()
    {
        using var t = Open();
        t.PutCursorOn('L', Name(1));
        t.Field<HashSet<string>>("_cutPending").Add(Path.Combine(t.Left, Name(4)));
        Rebuild(t);
        Assert.Equal(0.45f, Rows(t, 'L')[Name(4)].Children[0].Opacity);
    }

    [Fact]
    public void AFolderMeasuredWithSpace_ShowsItsTotal()
    {
        using var t = Open();
        var settings = t.Field<Settings>("_settings");
        bool size = settings.ShowSize;
        try
        {
            settings.ShowSize = true;
            File.WriteAllText(Path.Combine(t.Left, "docs", "inside.txt"), new string('x', 3000));
            Rebuild(t);
            string before = SizeText(Rows(t, 'L')["docs"]);

            var docs = t.Rows('L').Single(e => e.Name == "docs");
            t.Invoke("StartDirSize", docs);
            t.Session.PumpUntil(() => SizeText(Rows(t, 'L')["docs"]) != before, System.TimeSpan.FromSeconds(10));
            Assert.Equal("2.9 K", SizeText(Rows(t, 'L')["docs"]));
        }
        finally { settings.ShowSize = size; }

        static string SizeText(Element row) => ((TextElement)row.Children[2]).Content;
    }

    [Fact]
    public void AFolderADragHovers_IsTinted()
    {
        using var t = Open();
        t.PutCursorOn('L', Name(1));
        t.SetField("_dropTarget", ('L', Path.Combine(t.Left, "docs")));
        Rebuild(t);
        Assert.Equal(Nocturne.Alpha(Nocturne.Accent, 0.28), ((BoxElement)Rows(t, 'L')["docs"]).BackgroundColor);
    }

    /// <summary>A result set over the same files at the same places: the entries are equal, so
    /// only the shape can say the rows now name their folders.</summary>
    [Fact]
    public void AResultSet_NamesEachFilesFolder()
    {
        using var t = Open();
        t.PutCursorOn('L', Name(10));
        var entries = t.Rows('L').Where(e => !e.IsParent).ToList();
        var listingType = typeof(CommanderApp).GetNestedType("ResultListing", BindingFlags.NonPublic)!;
        var listing = System.Activator.CreateInstance(listingType, Path.GetDirectoryName(t.Left)!, "f", entries, 0)!;
        var tab = typeof(CommanderApp).GetMethod("ActiveTab", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(t.App, ['L'])!;
        tab.GetType().GetField("Results")!.SetValue(tab, listing);
        Rebuild(t);

        var row = List(t, 'L').Children.Skip(1).First(r => NameLabel(r).Content == Name(1));
        Assert.Contains(row.Children[1].Children.OfType<TextElement>(), l => l.Content == "· left");
    }

    [Fact]
    public void ANewDisplayScale_CutsTheIconsAgain()
    {
        using var t = Open();
        var left = Rows(t, 'L');
        float scale = Icons.DisplayScale;
        try
        {
            Icons.DisplayScale = scale + 1f;
            Rebuild(t);
            Assert.All(Rows(t, 'L'), r => Assert.NotSame(left.GetValueOrDefault(r.Key), r.Value));
        }
        finally { Icons.DisplayScale = scale; }
    }

    // ── The grid views ───────────────────────────────────────────────────

    /// <summary>The thumbnail grid's rows by the first name on each.</summary>
    private static Dictionary<string, Element> ThumbRows(DiffViewTests.Fixture t, char side)
    {
        var list = t.Field<Ref<VirtualListElement<CommanderApp.ThumbRowItem>>>(side == 'L' ? "_thumbVListL" : "_thumbVListR").Current
            ?? throw new Xunit.Sdk.XunitException($"the {side} pane has no thumbnail grid");
        var rows = new Dictionary<string, Element>();
        for (int i = 1; i < list.Children.Count; i++)
            rows[TileName(list.Children[i].Children[0])] = list.Children[i];
        return rows;
    }

    private static string TileName(Element tile) => tile.Children.OfType<TextElement>().Single().Content;

    private static BoxElement TileOf(Dictionary<string, Element> rows, string name) =>
        (BoxElement)rows.Values.SelectMany(r => r.Children).Single(tile => TileName(tile) == name);

    [Fact]
    public void AThumbnailCursorMove_MakesTheRowsItMovedBetween_AndKeepsTheRest()
    {
        using var t = Open();
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);   // view: Thumbnails
        t.Session.PumpFrames(2);
        int perRow = t.Field<int[]>("_thumbCols")[0];
        // The listing is "..", "docs", then the files: the first row ends on file perRow - 3.
        t.PutCursorOn('L', Name(perRow - 3));
        var before = ThumbRows(t, 'L');
        Assert.Contains("..", before.Keys);

        t.Press(KeyCode.Right);                    // onto the first tile of the second row
        t.Session.PumpFrames(1);

        var after = ThumbRows(t, 'L');
        Assert.Equal(before.Keys, after.Keys);
        foreach (var (first, row) in after)
        {
            if (first == ".." || first == Name(perRow - 2)) Assert.NotSame(before[first], row);
            else Assert.Same(before[first], row);
        }
        Assert.Equal(Nocturne.Accent600, TileOf(after, Name(perRow - 2)).BorderColor);
        Assert.Equal(Color.Transparent, TileOf(after, Name(perRow - 3)).BorderColor);
    }

    /// <summary>A mark is in the grid's shape: the rows are made again and the tile turns red.</summary>
    [Fact]
    public void AThumbnailMark_IsShownByTheKeptGrid()
    {
        using var t = Open();
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.Mark('L', Name(6));
        t.Session.PumpFrames(2);
        var name = TileOf(ThumbRows(t, 'L'), Name(6)).Children.OfType<TextElement>().Single();
        Assert.Equal(Nocturne.MarkRed, name.ResolvedInherited.TextColor);
    }

    /// <summary>The Brief grid's cells by the name they show.</summary>
    private static Dictionary<string, BoxElement> BriefCells(DiffViewTests.Fixture t, char side)
    {
        var scroll = t.Field<Ref<ScrollViewElement>>(side == 'L' ? "_briefScrollL" : "_briefScrollR").Current
            ?? throw new Xunit.Sdk.XunitException($"the {side} pane has no Brief grid");
        var cells = new Dictionary<string, BoxElement>();
        foreach (var column in scroll.Children[0].Children)
            foreach (var cell in column.Children)
                cells[cell.Children.OfType<TextElement>().Single().Content] = (BoxElement)cell;
        return cells;
    }

    /// <summary>The Brief grid keeps its cells: a key makes the two the cursor moved between (and
    /// patches them into the ones they replace) and hands every other cell back untouched. A
    /// patch copies what the build gave the cell, so a mark left on a cell that no build gave it
    /// is still there on the cells nothing patched.</summary>
    [Fact]
    public void ABriefCursorMove_MakesTheTwoCellsItMovedBetween_AndLeavesTheRestUntouched()
    {
        using var t = Open();
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);   // view: Brief
        t.Session.PumpFrames(2);
        t.PutCursorOn('L', Name(3));
        var before = BriefCells(t, 'L');
        foreach (var cell in before.Values) cell.AutomationId = "untouched";

        t.Press(KeyCode.Down);
        t.Session.PumpFrames(1);

        var after = BriefCells(t, 'L');
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (name, cell) in after)
        {
            Assert.Same(before[name], cell);   // patched or kept, the cell is the same element
            if (name == Name(3) || name == Name(4)) Assert.Null(cell.AutomationId);
            else Assert.Equal("untouched", cell.AutomationId);
        }
        Assert.Equal(Nocturne.Accent600, after[Name(4)].BorderColor);
        Assert.Equal(Color.Transparent, after[Name(3)].BorderColor);
    }

    /// <summary>A mark is in the Brief grid's shape: every cell is made again, and the marked one
    /// is red.</summary>
    [Fact]
    public void ABriefMark_IsShownByTheKeptGrid()
    {
        using var t = Open();
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        t.Mark('L', Name(6));
        t.Session.PumpFrames(2);
        var name = BriefCells(t, 'L')[Name(6)].Children.OfType<TextElement>().Single();
        Assert.Equal(Nocturne.MarkRed, name.ResolvedInherited.TextColor);
    }

    // ── The comparison ───────────────────────────────────────────────────

    private static IReadOnlyList<Element> DiffRows(DiffViewTests.Fixture t) => t.BodyRows();

    private static DiffViewTests.Fixture OpenDiff(string ext = "txt")
    {
        var t = new DiffViewTests.Fixture();
        var left = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"int line{i} = {i}; // needle {i}"));
        var right = string.Join("\n", Enumerable.Range(0, 60).Select(i => i % 9 == 4 ? $"int line{i} = -{i}; // needle" : $"int line{i} = {i}; // needle {i}"));
        t.WriteLeft($"a.{ext}", left + "\n");
        t.WriteRight($"a.{ext}", right + "\n");
        t.Open();
        t.OpenDiffOn($"a.{ext}");
        t.Session.PumpFrames(2);
        return t;
    }

    [Fact]
    public void TheComparison_KeepsItsRowsAcrossARebuild()
    {
        using var t = OpenDiff();
        var rows = DiffRows(t).ToList();
        Assert.True(rows.Count > 10);
        Rebuild(t);
        Assert.Equal(rows, DiffRows(t));
    }

    [Fact]
    public void TheComparison_RingsTheChangeF8JumpsTo()
    {
        using var t = OpenDiff();
        t.Press(KeyCode.N);
        t.Press(KeyCode.N);
        var current = t.CurrentChangeRow();
        Assert.Contains(current, DiffRows(t));
        Assert.True(((BoxElement)current).Outline.Width > 0);
        Assert.Single(DiffRows(t), r => ((BoxElement)r).Outline.Width > 0);
    }

    [Fact]
    public void TheComparison_TintsWhatFindFinds()
    {
        using var t = OpenDiff();
        t.Press(KeyCode.F, KeyModifiers.Ctrl);
        t.Session.Window.TypeText("needle");
        t.Session.PumpFrames(2);
        var (_, label) = t.FirstLineCell();
        Assert.Contains(((TextElement)label).Spans ?? [], s => s.Background == Nocturne.FindHitBg || s.Background == Nocturne.FindCurrentBg);
    }

    [Fact]
    public void TheComparison_ColoursItsRowsWhenTheHighlightLands()
    {
        using var t = OpenDiff("cs");
        t.WaitForHighlight();
        t.Session.PumpFrames(2);
        Assert.True(Coloured(left: true) && Coloured(left: false));

        // Each side's colouring on its own: taken away, then landing again, the rows follow.
        foreach (var (field, left) in new[] { ("_diffHlLeft", true), ("_diffHlRight", false) })
        {
            var spans = t.Field<object>(field);
            t.SetField(field, null);
            Rebuild(t);
            Assert.False(Coloured(left), $"{field} gone, the rows kept their colours");
            t.SetField(field, spans);
            Rebuild(t);
            Assert.True(Coloured(left), $"{field} landed, the rows stayed plain");
        }

        bool Coloured(bool left)
        {
            var label = left ? t.FirstLineCell().text : t.FirstLineRightCell().text;
            return (((TextElement)label).Spans ?? []).Any(s => s.Color != default(Color));
        }
    }

    // ── The viewer ───────────────────────────────────────────────────────

    private static DiffViewTests.Fixture OpenViewer(string name)
    {
        var t = new DiffViewTests.Fixture();
        t.WriteLeft(name, string.Join("\n", Enumerable.Range(0, 80).Select(i => $"int value{i} = {i}; // needle")) + "\n");
        t.Open();
        t.PutCursorOn('L', name);
        t.Press(KeyCode.F3, KeyModifiers.Shift);
        t.Session.PumpUntil(() => ViewerRows(t).Count > 10, System.TimeSpan.FromSeconds(30));
        return t;
    }

    private static IReadOnlyList<Element> ViewerRows(DiffViewTests.Fixture t) =>
        t.Field<Ref<ScrollViewElement>>("_viewerScrollRef").Current is { } list ? list.Children.Skip(1).ToList() : [];

    /// <summary>The line's label: the text cell's label, inside the pan's clipping cell.</summary>
    private static TextElement ViewerLabel(Element row) =>
        HultUI.Headless.LayoutProbe.Descendants<TextElement>(row.Children[1]).First();

    [Fact]
    public void TheViewer_KeepsItsRowsAcrossARebuild()
    {
        using var t = OpenViewer("a.txt");
        var rows = ViewerRows(t).ToList();
        Rebuild(t);
        Assert.Equal(rows, ViewerRows(t));
    }

    [Fact]
    public void TheViewer_TintsWhatFindFinds()
    {
        using var t = OpenViewer("a.txt");
        t.Press(KeyCode.F, KeyModifiers.Ctrl);
        t.Session.Window.TypeText("needle");
        t.Session.PumpFrames(2);
        Assert.Contains(ViewerLabel(ViewerRows(t)[0]).Spans ?? [], s => s.Background == Nocturne.FindHitBg || s.Background == Nocturne.FindCurrentBg);
    }

    [Fact]
    public void TheViewer_ColoursItsRowsWhenTheHighlightLands()
    {
        using var t = OpenViewer("a.cs");
        t.Session.PumpUntil(() => t.Field<object?>("_viewerHl") != null, System.TimeSpan.FromSeconds(20));
        t.Session.PumpFrames(2);
        Assert.True(Coloured());

        // Taken away, then landing again: the rows follow.
        var spans = t.Field<object>("_viewerHl");
        t.SetField("_viewerHl", null);
        Rebuild(t);
        Assert.False(Coloured(), "the colouring gone, the rows kept it");
        t.SetField("_viewerHl", spans);
        Rebuild(t);
        Assert.True(Coloured(), "the colouring landed, the rows stayed plain");

        bool Coloured() => (ViewerLabel(ViewerRows(t)[0]).Spans ?? []).Any(s => s.Color != default(Color));
    }
}
