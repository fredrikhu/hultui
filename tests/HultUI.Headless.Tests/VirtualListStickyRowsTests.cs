using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using SkiaSharp;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// <c>.StickyRows(…)</c>: one list of rows with a section's header among them, the header held at
/// the top while its rows go by under it and pushed off by the next, as <c>Sticky()</c> holds a
/// header in a column of its own. Junction's Changes list is the list it was made for.
/// </summary>
public class VirtualListStickyRowsTests
{
    private const float RowHeight = 24f, HeaderHeight = 32f;
    private const int Width = 400, Height = 300, Section = 20, Rows = 200;
    private static readonly Color HeaderFill = new(235, 235, 240);
    private static readonly Color Picked = new(40, 90, 200);
    private static readonly Color Tint = new(30, 150, 90);
    private static readonly Color Mark = new(220, 40, 160);

    private sealed record Entry(int Id, bool Header, string Name);

    private static bool IsHeader(int index) => index % Section == 0;

    private static float HeightOf(int index) => IsHeader(index) ? HeaderHeight : RowHeight;

    private static float Top(int index)
    {
        int headers = (index + Section - 1) / Section;
        return headers * HeaderHeight + (index - headers) * RowHeight;
    }

    private static List<Entry> Entries(int count) =>
        Enumerable.Range(0, count).Select(i => new Entry(i, IsHeader(i), IsHeader(i) ? $"header {i}" : $"row {i}")).ToList();

    private static Element MakeRow(Entry e) =>
        e.Header
            ? Flex.Row().AlignItems(AlignItems.Center).Padding(new Thickness(0, 8)).Background(HeaderFill)
                .Children(TextB.New(e.Name).FontSize(12f))
            : Flex.Row().AlignItems(AlignItems.Center).Padding(new Thickness(0, 8))
                .Children(TextB.New(e.Name).FontSize(12f));

    private sealed class Pane(int count) : Component
    {
        public List<Entry> Items = Entries(count);
        public int SelectedIndex = -1;
        public bool Holds = true, Heights = true, Composite, PlainRows, KeyedRows, ClearHeaders;
        public RowHighlight? Highlight;
        public RowHighlight? Hover;
        public Func<Element?>? Overlay;
        public Func<Entry, int, bool>? Sticky;
        public int Made, Clicks, Asked;
        public int LastClicked = -1;
        public readonly Ref<VirtualListElement<Entry>> List = new();

        public override Element Build()
        {
            var list = VirtualListElement<Entry>.New()
                .ItemHeight(RowHeight)
                .Items(Items)
                .AccessibleItemLabel(e => e.Name)
                .SelectedIndex(SelectedIndex)
                .RenderWith((e, _) => Row(e))
                .FlexGrow(1);
            if (PlainRows) list.ItemTemplate = Row;
            if (Heights) list.RowHeights((_, index) => HeightOf(index));
            if (Holds) list.StickyRows(Sticky ?? ((e, _) => { Asked++; return e.Header; }));
            list.RowsAreComposite = Composite;
            list.SelectionHighlight(Highlight);
            list.HoverHighlight = Hover;
            list.SelectionOverlay(Overlay?.Invoke());
            return Flex.Column().FlexGrow(1).Children(list.WithRef(List));
        }

        private Element Row(Entry e)
        {
            Made++;
            var row = MakeRow(e);
            if (ClearHeaders && row is BoxElement box) box.BackgroundColor = Color.Transparent;
            if (KeyedRows) row.Key = e.Name;
            row.OnClick(_ => { Clicks++; LastClicked = e.Id; });
            return row;
        }

        public void Rebuild() => MarkDirty();
    }

    private static (HeadlessSession Session, Pane Pane) Mount(Action<Pane>? setUp = null, int count = Rows)
    {
        var session = HeadlessSession.Create(Width, Height);
        try
        {
            var pane = new Pane(count);
            setUp?.Invoke(pane);
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

    private static void ScrollTo(HeadlessSession session, Pane pane, float offset)
    {
        pane.List.Current!.ScrollOffsetY = offset;
        session.PumpFrames(2);
    }

    /// <summary>The realized row showing <paramref name="name"/>.</summary>
    private static Element? RowNamed(Pane pane, string name)
    {
        foreach (var child in pane.List.Current!.Children)
            if (child.Children.Count > 0 && child.Children[0] is TextElement { Content: var text } && text == name)
                return child;
        return null;
    }

    private static int RealizedRows(Pane pane) =>
        pane.List.Current!.Children.Count(c => c.Children.Count > 0);

    private static SKBitmap Shot(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        return SKBitmap.FromImage(image);
    }

    private static SKColor Sk(Color c) => new(c.R, c.G, c.B, c.A);

    /// <summary>The colour near the end of the row band at <paramref name="y"/>, clear of its text
    /// and of the scroll bar.</summary>
    private static SKColor Fill(SKBitmap shot, float y) => shot.GetPixel(Width - 24, (int)y);

    // ── Held, pushed off ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheHeaderOfTheRowsOnScreen_IsHeldAtTheTop_RealizedFarAboveThem(bool heights)
    {
        var (session, pane) = Mount(p => p.Heights = heights);
        using (session)
        {
            float rowTop = heights ? Top(35) : 35 * RowHeight;
            ScrollTo(session, pane, rowTop);

            var held = RowNamed(pane, "header 20");
            Assert.NotNull(held);
            var view = pane.List.Current!.ScreenContentBox;
            Assert.Equal(view.Y, held!.ScreenBorderBox.Y, 0.01f);
            Assert.Equal(PositionType.Sticky, held.PositionRead.Type);
            // Fifteen rows above the window, and those fifteen are not elements.
            Assert.Null(RowNamed(pane, "row 25"));
            Assert.InRange(RealizedRows(pane), 2, 2 + Height / (int)RowHeight + 4);
        }
    }

    [Fact]
    public void TheNextHeader_PushesItOff_AtFractionalOffsets()
    {
        var (session, pane) = Mount();
        using (session)
        {
            var view = pane.List.Current!.ScreenContentBox;
            foreach (float before in new[] { 40.25f, 31.75f, 20.5f, 10.25f, 0.5f })
            {
                ScrollTo(session, pane, Top(40) - before);
                var held = RowNamed(pane, "header 20")!;
                var next = RowNamed(pane, "header 40")!;
                Assert.Equal(view.Y + before, next.ScreenBorderBox.Y, 0.01f);
                // At the top until the next reaches its bottom, then on top of it.
                Assert.Equal(view.Y + MathF.Min(0f, before - HeaderHeight), held.ScreenBorderBox.Y, 0.01f);
            }

            ScrollTo(session, pane, Top(40));
            Assert.Equal(view.Y, RowNamed(pane, "header 40")!.ScreenBorderBox.Y, 0.01f);
            ScrollTo(session, pane, Top(40) + 60.5f);
            Assert.Equal(view.Y, RowNamed(pane, "header 40")!.ScreenBorderBox.Y, 0.01f);
            Assert.Null(RowNamed(pane, "header 20"));
        }
    }

    [Fact]
    public void TheHeadersScrollBack_AndAHeaderWithNoRowIsNotHeld()
    {
        var (session, pane) = Mount(p => p.Sticky = (e, i) => e.Header || i == 21);
        using (session)
        {
            // Row 21 is sticky too, right under header 20: it pushes that one off at once.
            var view = pane.List.Current!.ScreenContentBox;
            ScrollTo(session, pane, Top(21) + 3.5f);
            Assert.Equal(view.Y, RowNamed(pane, "row 21")!.ScreenBorderBox.Y, 0.01f);
            Assert.Equal(Top(20) - (Top(21) + 3.5f) + view.Y, RowNamed(pane, "header 20")?.ScreenBorderBox.Y ?? Top(20) - (Top(21) + 3.5f) + view.Y, 0.01f);

            ScrollTo(session, pane, 0f);
            Assert.Equal(view.Y, RowNamed(pane, "header 0")!.ScreenBorderBox.Y, 0.01f);
            Assert.Equal(2 + Height / (int)RowHeight, RealizedRows(pane), 3f);
        }
    }

    /// <summary>The list draws what a scroll view of sections with <c>Sticky()</c> headers draws,
    /// at every offset, a fractional one and a push-off among them.</summary>
    [Fact]
    public void ItDrawsWhatAScrollViewOfStickyHeadersDraws()
    {
        float[] offsets = [0f, 7.25f, 31.5f, Top(20) - 40.75f, Top(20) - 10.25f, Top(20) - 0.5f,
            Top(20), Top(20) + 5.75f, Top(40) - 31.75f, Top(100) + 300.5f, Top(Rows) - Height];
        var mine = new List<byte[]>();
        var (session, pane) = Mount();
        using (session)
        {
            foreach (float offset in offsets)
            {
                ScrollTo(session, pane, offset);
                Assert.Equal(offset, pane.List.Current!.ScrollOffsetY);
                using var shot = Shot(session);
                mine.Add(shot.Bytes);
            }
        }

        using (var twin = HeadlessSession.Create(Width, Height))
        {
            var entries = Entries(Rows);
            var scroll = ScrollView.New().FlexGrow(1);
            for (int s = 0; s < Rows; s += Section)
            {
                var column = Flex.Column(gap: 0).FlexShrink(0);
                for (int i = s; i < Math.Min(Rows, s + Section); i++)
                {
                    var row = MakeRow(entries[i]).FlexShrink(0).Height(Dimension.Px(HeightOf(i)));
                    column.AddChild(i == s ? row.Sticky() : row);
                }
                scroll.AddChild(column);
            }
            twin.Application.SetRoot(Flex.Column().FlexGrow(1).Children(scroll));
            twin.PumpFrames(3);

            for (int i = 0; i < offsets.Length; i++)
            {
                scroll.ScrollOffsetY = offsets[i];
                twin.PumpFrames(2);
                Assert.Equal(offsets[i], scroll.ScrollOffsetY);
                using var theirs = Shot(twin);
                Assert.True(mine[i].AsSpan().SequenceEqual(theirs.Bytes), $"the pixels differ at offset {offsets[i]}");
            }
        }
    }

    [Fact]
    public void APressOnTheHeldRow_IsAPressOnIt_NotOnTheRowUnderIt()
    {
        var (session, pane) = Mount();
        using (session)
        {
            ScrollTo(session, pane, Top(35));
            var box = RowNamed(pane, "header 20")!.WindowBorderBox;
            session.Window.Click(box.X + 100, box.Y + box.Height / 2);
            session.PumpFrames(2);
            Assert.Equal(20, pane.LastClicked);

            session.Window.Click(box.X + 100, box.Bottom + RowHeight);
            session.PumpFrames(2);
            Assert.NotEqual(20, pane.LastClicked);
        }
    }

    /// <summary>The rows stand in the flow, spacers and all: a layout pass of the whole window leaves
    /// each where the heights put it.</summary>
    [Fact]
    public void ALayoutPass_LeavesTheRowsWhereTheyStand()
    {
        var (session, pane) = Mount();
        using (session)
        {
            ScrollTo(session, pane, Top(35));
            var list = pane.List.Current!;
            var view = list.ScreenContentBox;
            list.MarkLayoutDirty();
            session.PumpFrames(2);
            Assert.Equal(view.Y, RowNamed(pane, "header 20")!.ScreenBorderBox.Y, 0.01f);
            Assert.Equal(view.Y, RowNamed(pane, "row 35")!.ScreenBorderBox.Y, 0.01f);
            Assert.Equal(view.Y + (Top(38) - Top(35)), RowNamed(pane, "row 38")!.ScreenBorderBox.Y, 0.01f);

        }
    }

    // ── Item rows ──

    [Fact]
    public void ARebuild_KeepsTheHeldRow_AndAMoveMakesOnlyTheRowsThatChange()
    {
        var (session, pane) = Mount();
        using (session)
        {
            ScrollTo(session, pane, Top(35));
            var held = RowNamed(pane, "header 20");
            int made = pane.Made;

            pane.Rebuild();
            session.PumpFrames(2);
            Assert.Equal(made, pane.Made);
            Assert.Same(held, RowNamed(pane, "header 20"));

            // One row further: the one scrolled in, and no other.
            ScrollTo(session, pane, Top(36));
            Assert.Equal(1, pane.Made - made);
            Assert.Same(held, RowNamed(pane, "header 20"));
        }
    }

    [Fact]
    public void RowsFromThePlainTemplate_AreHeldToo()
    {
        var (session, pane) = Mount(p => p.PlainRows = true);
        using (session)
        {
            ScrollTo(session, pane, Top(35));
            pane.Rebuild();
            session.PumpFrames(2);
            var view = pane.List.Current!.ScreenContentBox;
            Assert.Equal(view.Y, RowNamed(pane, "header 20")!.ScreenBorderBox.Y, 0.01f);
        }
    }

    [Fact]
    public void AKeyedHeldRow_StaysSticky_ThroughARebuildThatPatchesIt()
    {
        var (session, pane) = Mount(p => { p.KeyedRows = true; p.PlainRows = true; });
        using (session)
        {
            ScrollTo(session, pane, Top(35));
            var held = RowNamed(pane, "header 20");
            pane.Rebuild();
            session.PumpFrames(2);
            Assert.Same(held, RowNamed(pane, "header 20"));
            Assert.Equal(PositionType.Sticky, held!.PositionRead.Type);
            Assert.Equal(pane.List.Current!.ScreenContentBox.Y, held.ScreenBorderBox.Y, 0.01f);
        }
    }

    [Fact]
    public void NewItems_MoveWhatIsHeld_AndItsNextHeader()
    {
        var (session, pane) = Mount();
        using (session)
        {
            ScrollTo(session, pane, Top(35));
            // Row 30 turns into a header of the same height as the row it was: it is held now.
            pane.Sticky = (e, i) => e.Header || i == 30;
            pane.Rebuild();
            session.PumpFrames(2);
            var view = pane.List.Current!.ScreenContentBox;
            Assert.Null(RowNamed(pane, "header 20"));
            Assert.Equal(view.Y, RowNamed(pane, "row 30")!.ScreenBorderBox.Y, 0.01f);

            pane.Sticky = null;
            pane.Rebuild();
            session.PumpFrames(2);
            Assert.Null(RowNamed(pane, "row 30"));
            Assert.Equal(view.Y, RowNamed(pane, "header 20")!.ScreenBorderBox.Y, 0.01f);
        }
    }

    [Fact]
    public void ItemsSetOnTheMountedList_MoveTheStickyRowsToo()
    {
        var (session, pane) = Mount();
        using (session)
        {
            ScrollTo(session, pane, Top(35));
            var list = pane.List.Current!;
            // The same rows with another row as the header above them.
            var items = Entries(Rows);
            items[20] = items[20] with { Header = false, Name = "row 20" };
            items[30] = items[30] with { Header = true, Name = "header 30" };
            list.Items = items;
            session.PumpFrames(2);
            Assert.Null(RowNamed(pane, "row 20"));
            Assert.Equal(list.ScreenContentBox.Y, RowNamed(pane, "header 30")!.ScreenBorderBox.Y, 0.01f);
        }
    }

    [Fact]
    public void AKeptRowIsToldItIsNoLongerSticky()
    {
        var (session, pane) = Mount();
        using (session)
        {
            ScrollTo(session, pane, Top(38));
            var row = RowNamed(pane, "header 40")!;
            Assert.Equal(PositionType.Sticky, row.PositionRead.Type);

            pane.Sticky = (e, i) => e.Header && i != 40;
            pane.Rebuild();
            session.PumpFrames(2);
            Assert.Same(row, RowNamed(pane, "header 40"));
            Assert.Equal(PositionType.Relative, row.PositionRead.Type);
        }
    }

    [Fact]
    public void AListThatStopsHolding_MakesItsRowsPlainAgain()
    {
        var (session, pane) = Mount();
        using (session)
        {
            ScrollTo(session, pane, Top(38));
            pane.Holds = false;
            pane.Rebuild();
            session.PumpFrames(2);
            Assert.Null(RowNamed(pane, "header 20"));
            Assert.Equal(PositionType.Relative, RowNamed(pane, "header 40")!.PositionRead.Type);
        }
    }

    [Fact]
    public void AListThatShrinks_LetsGoOfTheHeldRow_DownToNothing()
    {
        var (session, pane) = Mount();
        using (session)
        {
            ScrollTo(session, pane, Top(55));
            Assert.NotNull(RowNamed(pane, "header 40"));

            pane.Items = Entries(30);
            pane.Rebuild();
            session.PumpFrames(3);
            Assert.Null(RowNamed(pane, "header 40"));
            Assert.Equal(MathF.Max(0f, Top(30) - Height), pane.List.Current!.ScrollOffsetY, 0.5f);
            Assert.NotNull(RowNamed(pane, "header 20"));

            pane.Items = [];
            pane.Rebuild();
            session.PumpFrames(3);
            Assert.Empty(pane.List.Current!.Children);

            pane.Items = Entries(Rows);
            pane.Rebuild();
            session.PumpFrames(3);
            Assert.NotNull(RowNamed(pane, "header 0"));
        }
    }

    // ── What the list draws for a row ──

    [Fact]
    public void TheSelection_GoesOnTheHeldRowsFill_WhereItIsDrawn()
    {
        var (session, pane) = Mount(p =>
        {
            p.Highlight = new RowHighlight(Picked);
            p.SelectedIndex = 20;
        });
        using (session)
        {
            ScrollTo(session, pane, Top(35));
            int made = pane.Made;
            using (var shot = Shot(session))
                Assert.Equal(Sk(Picked), Fill(shot, HeaderHeight / 2));

            pane.SelectedIndex = 37;
            pane.Rebuild();
            session.PumpFrames(2);
            using (var shot = Shot(session))
            {
                Assert.Equal(Sk(HeaderFill), Fill(shot, HeaderHeight / 2));
                Assert.Equal(Sk(Picked), Fill(shot, Top(37) - Top(35) + RowHeight / 2));
            }
            Assert.Equal(made, pane.Made);
        }
    }

    [Fact]
    public void TheHover_GoesOnTheHeldRowsFill_AndNotOnTheRowUnderIt()
    {
        var (session, pane) = Mount(p => p.Hover = new RowHighlight(Tint));
        using (session)
        {
            ScrollTo(session, pane, Top(35));
            session.Window.MoveMouse(120, HeaderHeight / 2);
            session.PumpFrames(2);
            using (var shot = Shot(session))
                Assert.Equal(Sk(Tint), Fill(shot, HeaderHeight / 2));

            session.Window.MoveMouse(120, HeaderHeight + RowHeight / 2);
            session.PumpFrames(2);
            using (var shot = Shot(session))
            {
                Assert.Equal(Sk(HeaderFill), Fill(shot, HeaderHeight / 2));
                Assert.Equal(Sk(Tint), Fill(shot, HeaderHeight + RowHeight / 2));
            }
        }
    }

    /// <summary>A highlight that lets its row show through goes on once: the row's fill is not drawn
    /// over one under it, and the pointer's tint is left off a selected row, held or not.</summary>
    [Fact]
    public void ATranslucentHighlight_GoesOnASelectedStickyRowOnce()
    {
        var half = new Color(40, 90, 200, 128);
        var (session, pane) = Mount(p =>
        {
            p.Highlight = new RowHighlight(half);
            p.Hover = new RowHighlight(Tint);
            p.SelectedIndex = 20;
            p.ClearHeaders = true;
        });
        using (session)
        {
            using var once = Shot(session);
            var expected = once.GetPixel(Width - 24, 2);   // the window's background, nothing on it
            byte Blend(byte under, byte over) => (byte)Math.Round((under * (255 - 128) + over * 128) / 255.0);
            var blended = new SKColor(Blend(expected.Red, half.R), Blend(expected.Green, half.G), Blend(expected.Blue, half.B));

            // At its place: under the rows, it would go on twice.
            ScrollTo(session, pane, Top(20) - 40f);
            using (var shot = Shot(session))
                AssertClose(blended, Fill(shot, 40f + HeaderHeight / 2));

            // Held, with the pointer on it.
            ScrollTo(session, pane, Top(35));
            session.Window.MoveMouse(120, HeaderHeight / 2);
            session.PumpFrames(2);
            using (var shot = Shot(session))
                AssertClose(blended, Fill(shot, HeaderHeight / 2));
        }
    }

    private static void AssertClose(SKColor expected, SKColor actual)
    {
        Assert.InRange(actual.Red, expected.Red - 2, expected.Red + 2);
        Assert.InRange(actual.Green, expected.Green - 2, expected.Green + 2);
        Assert.InRange(actual.Blue, expected.Blue - 2, expected.Blue + 2);
    }

    [Fact]
    public void ASelectedRowGoingByUnderTheHeldRow_DoesNotShowOverIt()
    {
        var (session, pane) = Mount(p =>
        {
            p.Highlight = new RowHighlight(Picked);
            p.SelectedIndex = 35;
        });
        using (session)
        {
            // The row stands from 20 to 44, its top twelve under the held row.
            ScrollTo(session, pane, Top(35) - 20f);
            using var shot = Shot(session);
            Assert.Equal(Sk(HeaderFill), Fill(shot, 26f));
            Assert.Equal(Sk(Picked), Fill(shot, 38f));
        }
    }

    private static Element Chip() =>
        Flex.Row().Padding(new Thickness(0, 24, 0, 8)).Background(Mark).Children(TextB.New("Details").FontSize(11f));

    [Fact]
    public void TheOverlay_GoesWithTheHeldRow_OverIt()
    {
        var (session, pane) = Mount(p =>
        {
            p.Overlay = Chip;
            p.SelectedIndex = 20;
        });
        using (session)
        {
            ScrollTo(session, pane, Top(35));
            using var shot = Shot(session);
            Assert.Equal(Sk(Mark), Fill(shot, HeaderHeight / 2));
        }
    }

    [Fact]
    public void TheOverlayOfARowUnderTheHeldRow_GoesUnderItToo()
    {
        var (session, pane) = Mount(p =>
        {
            p.Overlay = Chip;
            p.SelectedIndex = 35;
        });
        using (session)
        {
            ScrollTo(session, pane, Top(35) - 20f);
            using var shot = Shot(session);
            Assert.Equal(Sk(HeaderFill), Fill(shot, 26f));
            Assert.Equal(Sk(Mark), Fill(shot, 38f));
        }
    }

    // ── A reveal ──

    [Fact]
    public void ARevealLeavesTheRowClearOfTheHeldRow_ThoughTheHeldRowIsNotAnElementYet()
    {
        var (session, pane) = Mount();
        using (session)
        {
            var list = pane.List.Current!;
            ScrollTo(session, pane, Top(100));

            // Far up the list, under a header nothing has realized: the offset leaves it room.
            Assert.Null(RowNamed(pane, "header 40"));
            list.ScrollRangeIntoView(Top(45), RowHeight);
            session.PumpFrames(2);
            Assert.Equal(Top(45) - HeaderHeight, list.ScrollOffsetY, 0.01f);
            var view = list.ScreenContentBox;
            Assert.Equal(view.Y, RowNamed(pane, "header 40")!.ScreenBorderBox.Y, 0.01f);
            Assert.Equal(view.Y + HeaderHeight, RowNamed(pane, "row 45")!.ScreenBorderBox.Y, 0.01f);

            // The row under its own header takes the header back to its place.
            list.ScrollRangeIntoView(Top(41), RowHeight);
            session.PumpFrames(2);
            Assert.Equal(Top(40), list.ScrollOffsetY, 0.01f);
        }
    }

    [Fact]
    public void ARevealOfAStickyRow_IsNotScrolledBackOffItself()
    {
        var (session, pane) = Mount();
        using (session)
        {
            var list = pane.List.Current!;
            ScrollTo(session, pane, Top(100));
            list.ScrollRangeIntoView(Top(40), HeaderHeight);
            session.PumpFrames(2);
            Assert.Equal(Top(40), list.ScrollOffsetY, 0.01f);
        }
    }

    // ── A reader ──

    private static IEnumerable<AccessibilityNode> Nodes(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Nodes(child))
                yield return n;
    }

    [Fact]
    public void CompositeRows_TheHeldRowIsInTheTree_WhereItIsDrawn_BeforeTheRowsOnScreen()
    {
        var (session, pane) = Mount(p => p.Composite = true);
        using (session)
        {
            session.Window.InstallAccessibilityBridge();
            ScrollTo(session, pane, Top(35));
            session.PumpFrame(TimeSpan.FromMilliseconds(200));
            session.PumpFrames(2);

            var view = pane.List.Current!.WindowContentBox;
            var names = Nodes(session.Window.AccessibilityTree).Where(n => n.Name is not null).ToList();
            var held = names.First(n => n.Name == "header 20");
            Assert.Equal(view.Y, held.BoundingRect.Y, 0.5f);
            Assert.False(held.IsOffscreen);
            Assert.True(names.FindIndex(n => n.Name == "header 20") < names.FindIndex(n => n.Name == "row 35"));
            // A row off screen is not an element, so a reader finds none; the header stays.
            Assert.DoesNotContain(names, n => n.Name == "row 25");
        }
    }

    [Fact]
    public void AnItemsNode_IsWhereTheHeldRowIsDrawn()
    {
        var (session, pane) = Mount();
        using (session)
        {
            session.Window.InstallAccessibilityBridge();
            ScrollTo(session, pane, Top(35));
            session.PumpFrame(TimeSpan.FromMilliseconds(200));
            session.PumpFrames(2);

            var view = pane.List.Current!.WindowContentBox;
            var nodes = Nodes(session.Window.AccessibilityTree).ToList();
            var held = nodes.First(n => n.Name == "header 20");
            Assert.Equal(view.Y, held.BoundingRect.Y, 0.5f);
            Assert.Equal(HeaderHeight, held.BoundingRect.Height, 0.5f);
            Assert.False(held.IsOffscreen);
            // Every item is still a node, and the rest stay where they are.
            Assert.Contains(nodes, n => n.Name == "row 25");
            var row = nodes.First(n => n.Name == "row 36");
            Assert.Equal(view.Y + (Top(36) - Top(35)), row.BoundingRect.Y, 0.5f);
        }
    }

    // ── No cost without it ──

    [Fact]
    public void AListWithoutStickyRows_HasNoHeldRow_AndNoGapInItsChildren()
    {
        var (session, pane) = Mount(p => p.Holds = false);
        using (session)
        {
            ScrollTo(session, pane, Top(35));
            var children = pane.List.Current!.Children;
            Assert.Null(RowNamed(pane, "header 20"));
            // The spacer, then the rows, and no other box.
            Assert.Equal(1, children.Count(c => c.Children.Count == 0));
            Assert.All(children, c => Assert.Equal(PositionType.Relative, c.PositionRead.Type));
            Assert.Equal(0, pane.Asked);
        }
    }

    [Fact]
    public void ThePredicateIsAskedOncePerRowPerInvalidation_NotPerFrame()
    {
        var (session, pane) = Mount();
        using (session)
        {
            int asked = pane.Asked;
            Assert.Equal(Rows, asked);
            for (int i = 1; i <= 10; i++) ScrollTo(session, pane, i * 500f);
            Assert.Equal(asked, pane.Asked);
        }
    }
}
