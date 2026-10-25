using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
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
        /// <summary>Something only one row shows beside its item and selection, as a second
        /// highlight does: the row of this id is starred, in its text and its label.</summary>
        public int StarredId = -1;
        /// <summary>Rows made by the template.</summary>
        public int Made;
        /// <summary>Labels made by the label delegate.</summary>
        public int LabelsMade;
        public readonly Ref<VirtualListElement<Entry>> List = new();
        private Func<Entry, string?>? _label;

        public override Element Build()
        {
            int selectedId = SelectedId;
            var list = VirtualListElement<Entry>.New()
                .ItemHeight(RowHeight)
                .Items(Items)
                // One delegate every build, reading the star live: the list keeps the labels.
                .AccessibleItemLabel(_label ??= e => { LabelsMade++; return e.Id == StarredId ? e.Name + ", starred" : e.Name; })
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
                .Children(TextB.New(e.Name + Suffix + (e.Id == StarredId ? " ★" : "")).FontSize(12f));
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

    /// <summary>A highlight only one row shows moves from one row to another: the app tells the
    /// list those two rows, before the rebuild, and only they are made again — from the new
    /// build's template — while every other row is kept.</summary>
    [Fact]
    public void InvalidateRow_MakesOnlyTheRowsItNames_FromTheNewBuild()
    {
        var (session, pane) = Mount(itemRows: true);
        using var _ = session;
        pane.StarredId = 3;
        pane.List.Current!.InvalidateRows();
        pane.Rebuild();
        session.PumpFrames(2);
        Assert.Equal("entry 3 ★", ((TextElement)pane.List.Current!.Children[1 + 3].Children[0]).Content);
        var before = Rows(pane);
        pane.Made = 0;

        pane.StarredId = 7;
        pane.List.Current!.InvalidateRow(3);
        pane.List.Current!.InvalidateRow(7);
        pane.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(2, pane.Made);
        var after = Rows(pane);
        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < after.Count; i++)
        {
            if (i == 3 || i == 7) Assert.NotSame(before[i].Row, after[i].Row);
            else Assert.Same(before[i].Row, after[i].Row);
        }
        Assert.Equal("entry 3", after[3].Name);
        Assert.Equal("entry 7 ★", after[7].Name);
    }

    /// <summary>The row's kept label goes with it, and only that label: the others are still the
    /// ones the list kept.</summary>
    [Fact]
    public void InvalidateRow_LetsGoOfThatItemsLabel_AndKeepsTheOthers()
    {
        var (session, pane) = Mount(itemRows: true);
        using var _ = session;
        var items = (IAccessibleItemSource)pane.List.Current!;
        for (int i = 0; i < 20; i++) items.GetItemLabel(i);   // every label made and kept
        pane.LabelsMade = 0;

        pane.StarredId = 12;
        pane.List.Current!.InvalidateRow(12);   // not realized: a label alone
        pane.List.Current!.InvalidateRow(2);    // realized: the row and its label

        Assert.Equal("entry 12, starred", items.GetItemLabel(12));
        Assert.Equal("entry 2", items.GetItemLabel(2));
        Assert.Equal("entry 5", items.GetItemLabel(5));
        Assert.Equal(2, pane.LabelsMade);
    }

    /// <summary>Indices the list does not hold are ignored, and a row that is not realized costs no
    /// row.</summary>
    [Fact]
    public void InvalidateRow_OutsideTheItemsOrTheWindow_MakesNoRow()
    {
        var (session, pane) = Mount(itemRows: true);
        using var _ = session;
        var before = Rows(pane);
        pane.Made = 0;

        pane.List.Current!.InvalidateRow(-1);
        pane.List.Current!.InvalidateRow(pane.Items.Count);
        pane.List.Current!.InvalidateRow(150);   // far below the window
        pane.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(0, pane.Made);
        Assert.Equal(before.Select(r => r.Row), Rows(pane).Select(r => r.Row));
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

    // ── A display scale change (plan 17, round 3, step 17) ──

    /// <summary>Glyphs cut at the display scale, the way an app's icon cache cuts them (Junction's
    /// Phosphor): a raster per scale; a scale change retires the rasters handed out, and the next
    /// change frees them.</summary>
    private sealed class ScaledGlyphs : IDisposable
    {
        private readonly Dictionary<int, SkiaImageSource> _cache = [];
        private readonly List<SkiaImageSource> _retired = [];
        public float Scale { get; private set; } = 1f;

        public void ScaleTo(float scale)
        {
            if (scale == Scale) return;
            Scale = scale;
            foreach (var retired in _retired) retired.Dispose();
            _retired.Clear();
            _retired.AddRange(_cache.Values);
            _cache.Clear();
        }

        public SkiaImageSource Glyph(float size)
        {
            int px = (int)MathF.Ceiling(size * Scale);
            if (_cache.TryGetValue(px, out var cached)) return cached;
            var bitmap = new SKBitmap(px, px, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            using (var paint = new SKPaint { IsAntialias = true, Color = new SKColor(30, 120, 60) })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawCircle(px / 2f, px / 2f, px * 0.4f, paint);
            }
            return _cache[px] = new SkiaImageSource(bitmap);
        }

        public void Dispose()
        {
            foreach (var source in _cache.Values) source.Dispose();
            foreach (var source in _retired) source.Dispose();
        }
    }

    /// <summary>A list whose rows draw such a glyph, and whose owner follows the window's scale as
    /// Junction does (the cache to the new scale, then a rebuild) but has no scale in anything it
    /// tells the rows with: what Junction History shipped in junction-v1.20.0.</summary>
    private sealed class GlyphPane : Component
    {
        public const float GlyphSize = 12f;
        public readonly ScaledGlyphs Glyphs = new();
        public int Made;
        public readonly Ref<VirtualListElement<Entry>> List = new();

        public GlyphPane(Window window) =>
            window.Resized += () => { Glyphs.ScaleTo(window.DisplayScale); MarkDirty(); };

        public override Element Build()
        {
            var list = VirtualListElement<Entry>.New()
                .ItemHeight(RowHeight)
                .Items(Entries(200))
                .RenderWith((e, selected) =>
                {
                    Made++;
                    return Flex.Row(gap: 6).Padding(new Thickness(2, 8)).Children(
                        Image.New(Glyphs.Glyph(GlyphSize)).Width(GlyphSize).Height(GlyphSize).FlexShrink(0),
                        TextB.New(e.Name).FontSize(12f));
                })
                .FlexGrow(1);
            return Flex.Column().FlexGrow(1).Children(list.WithRef(List));
        }

        public void Rebuild() => MarkDirty();
    }

    /// <summary>
    /// Kept rows were not made again on a display scale change, so an owner that left the scale out
    /// of what it invalidates the rows on kept rasters cut for the old scale, and after a second
    /// change freed ones: Junction's icons vanished after a move to 150 % and back (11ced00 fixed
    /// it there by putting the scale in its shapes). A scale change makes every row again, as a
    /// theme switch does, and a round trip draws exactly what a fresh list draws.
    /// </summary>
    [Fact]
    public void ADisplayScaleChange_MakesEveryRowAgain_AndARoundTripDrawsAsAFreshList()
    {
        // One application per process: the fresh list first.
        byte[] fresh;
        using (var first = HeadlessSession.Create(400, 300))
        {
            var freshPane = new GlyphPane(first.Window.Window);
            first.Application.SetRoot(freshPane.Mount());
            first.PumpFrames(3);
            fresh = Pixels(first);
            freshPane.Glyphs.Dispose();
        }

        using var session = HeadlessSession.Create(400, 300);
        var pane = new GlyphPane(session.Window.Window);
        session.Application.SetRoot(pane.Mount());
        session.PumpFrames(3);
        var list = pane.List.Current!;
        int onScreen = list.Children.Count - 1;
        Assert.True(onScreen > 10);

        foreach (float scale in new[] { 1.5f, 1f })
        {
            pane.Made = 0;
            session.Window.SetScale(scale);
            session.PumpFrames(2);
            Assert.True(onScreen == pane.Made, $"at {scale}: {pane.Made} of {onScreen} rows made again");
            int px = (int)MathF.Ceiling(GlyphPane.GlyphSize * scale);
            for (int i = 1; i < list.Children.Count; i++)
            {
                var glyph = (ImageElement)list.Children[i].Children[0];
                Assert.True(glyph.Source is { IsLoaded: true } source && source.IntrinsicSize.Width == px,
                    $"at {scale}, row {i}: its glyph is not the {px} px raster");
            }
        }

        var after = Pixels(session);
        int differ = 0;
        for (int i = 0; i < after.Length; i += 4)
            if (!after.AsSpan(i, 4).SequenceEqual(fresh.AsSpan(i, 4))) differ++;
        Assert.True(differ == 0, $"{differ} pixels differ from a fresh list after 1 → 1.5 → 1");

        // The same scale makes nothing: a rebuild keeps every row, as before.
        pane.Made = 0;
        pane.Rebuild();
        session.PumpFrames(2);
        Assert.Equal(0, pane.Made);
        pane.Glyphs.Dispose();
    }

    /// <summary>A list on item rows in an overlay (a popover, a palette) is in no build of the
    /// window's root: the window tells its overlays of the scale too.</summary>
    [Fact]
    public void AListInAnOverlay_IsMadeAgainOnADisplayScaleChange()
    {
        using var session = HeadlessSession.Create(400, 300);
        session.Application.SetRoot(Box.New().FlexGrow(1));
        int made = 0;
        var list = VirtualListElement<Entry>.New()
            .ItemHeight(RowHeight)
            .Items(Entries(50))
            .RenderWith((e, selected) => { made++; return Flex.Row().Children(TextB.New(e.Name).FontSize(12f)); })
            .Width(200).Height(150);
        session.Window.Window.ShowOverlay(list, 20, 20);
        session.PumpFrames(3);
        int onScreen = list.Children.Count - 1;
        Assert.True(onScreen > 3);
        made = 0;

        session.Window.SetScale(1.5f);
        session.PumpFrames(2);

        Assert.Equal(onScreen, made);
    }

    private static byte[] Pixels(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Bytes;
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
