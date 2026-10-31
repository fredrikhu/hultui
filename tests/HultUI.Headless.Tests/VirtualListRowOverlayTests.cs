using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using SkiaSharp;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// What a list gives rows it keeps, beside the selection it draws
/// (<see cref="VirtualListSelectionHighlightTests"/>): a tint under the row the pointer is on
/// (<c>.HoverHighlight</c>), and an element over the selected row (<c>.SelectionOverlay</c>), so
/// neither makes a row when the pointer or the selection moves. Junction's History is the list
/// these were made for.
/// </summary>
public class VirtualListRowOverlayTests
{
    private const float RowHeight = 24f;
    private const int Width = 400;
    private const int EndInset = 16;
    private static readonly Color Picked = new(40, 90, 200);
    private static readonly Color Tint = new(30, 150, 90);
    private static readonly Color Line = new(120, 120, 140);
    private static readonly Color Mark = new(220, 40, 160);

    private sealed record Entry(int Id, string Name);

    private sealed class Pane(int count) : Component
    {
        public int SelectedIndex;
        public bool Hover = true;
        public bool Highlight = true;
        /// <summary>Rows tint themselves under the pointer, as they did before the list could.</summary>
        public bool RowsHover;
        public bool Composite;
        /// <summary>Rows from the template that is asked again on every patch.</summary>
        public bool PlainRows;
        public Func<Element?>? Overlay;
        public Func<Entry, int, float>? Heights;
        public int Made, Clicks;
        public int LastClicked = -1;
        public readonly Ref<VirtualListElement<Entry>> List = new();
        private readonly List<Entry> _items = Enumerable.Range(0, count).Select(i => new Entry(i, $"entry {i}")).ToList();
        private Func<Entry, string?>? _label;

        public override Element Build()
        {
            var list = VirtualListElement<Entry>.New()
                .ItemHeight(RowHeight)
                .Items(_items)
                .AccessibleItemLabel(_label ??= e => e.Name)
                .SelectedIndex(SelectedIndex)
                .RenderWith((e, _) => Row(e))
                .SelectionHighlight(Highlight ? new RowHighlight(Picked) : null)
                .FlexGrow(1);
            list.RowsAreComposite = Composite;
            if (PlainRows) list.ItemTemplate = Row;
            list.ItemHeightFor = Heights;
            if (Hover) list.HoverHighlight(Tint);
            list.SelectionOverlay(Overlay?.Invoke());
            return Flex.Column().FlexGrow(1).Children(list.WithRef(List));
        }

        private Element Row(Entry e)
        {
            Made++;
            var row = Flex.Row().Padding(new Thickness(2, 8))
                .BorderColor(Line).BorderWidth(new Thickness(0, 0, 1, 0))
                .Children(TextB.New(e.Name).FontSize(12f));
            row.OnClick(_ => { Clicks++; LastClicked = e.Id; });
            if (RowsHover) row.OnHover(b => b.BackgroundColor = Tint);
            return row;
        }

        public void Select(int index) { SelectedIndex = index; MarkDirty(); }
        public void Rebuild() => MarkDirty();
    }

    private static Element Chip(string text = "Details") =>
        Flex.Row().Padding(new Thickness(0, 24, 0, 8)).Background(Mark)
            .Children(TextB.New(text).FontSize(11f));

    private static (HeadlessSession Session, Pane Pane) Mount(Func<Element?>? overlay = null, int count = 200,
        float scale = 1f, Action<Pane>? setUp = null)
    {
        var session = HeadlessSession.Create(Width, 300, scale);
        try
        {
            var pane = new Pane(count) { Overlay = overlay };
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

    private static SKBitmap Shot(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        return SKBitmap.FromImage(image);
    }

    private static float RowY(int row) => row * RowHeight + RowHeight / 2;

    /// <summary>The colour near the end of a row, clear of the scroll bar and of its text.</summary>
    private static SKColor Fill(SKBitmap shot, int row, float scale = 1f) =>
        shot.GetPixel((int)((Width - EndInset) * scale), (int)(RowY(row) * scale));

    private static SKColor Sk(Color c) => new(c.R, c.G, c.B, c.A);

    private static float ListX(HeadlessSession session, Pane pane) => pane.List.Current!.Layout.AbsoluteContentBox.X + 120;

    // ── The tint under the pointer ──

    [Fact]
    public void TheTint_FollowsThePointer_AndMakesNoRow()
    {
        var (session, pane) = Mount();
        using (session)
        {
            int made = pane.Made;
            float x = ListX(session, pane);

            session.Window.MoveMouse(x, RowY(3));
            session.PumpFrames(2);
            using (var shot = Shot(session))
            {
                Assert.Equal(Sk(Tint), Fill(shot, 3));
                Assert.NotEqual(Sk(Tint), Fill(shot, 2));
                Assert.NotEqual(Sk(Tint), Fill(shot, 4));
            }

            session.Window.MoveMouse(x, RowY(4));
            session.PumpFrames(2);
            using (var shot = Shot(session))
            {
                Assert.NotEqual(Sk(Tint), Fill(shot, 3));
                Assert.Equal(Sk(Tint), Fill(shot, 4));
            }

            session.Window.MouseLeaveWindow();
            session.PumpFrames(2);
            using (var shot = Shot(session))
                Assert.NotEqual(Sk(Tint), Fill(shot, 4));
            Assert.Equal(made, pane.Made);
        }
    }

    [Fact]
    public void TheSelectedRow_KeepsItsHighlight_UnderThePointer()
    {
        var (session, pane) = Mount();
        using (session)
        {
            session.Window.MoveMouse(ListX(session, pane), RowY(0));
            session.PumpFrames(2);
            using var shot = Shot(session);
            Assert.Equal(Sk(Picked), Fill(shot, 0));
        }
    }

    /// <summary>A list that draws no selection still leaves the selected row untinted.</summary>
    [Fact]
    public void TheSelectedRow_TakesNoTint_WhereTheListDrawsNoHighlight()
    {
        var (session, pane) = Mount(setUp: p => p.Highlight = false);
        using (session)
        {
            session.Window.MoveMouse(ListX(session, pane), RowY(0));
            session.PumpFrames(2);
            using var shot = Shot(session);
            Assert.NotEqual(Sk(Tint), Fill(shot, 0));
        }
    }

    /// <summary>The tint is read as the list draws, so the pointer reaching another row has to draw
    /// a frame, which the window asks for, and a move inside a row does not.</summary>
    [Fact]
    public void ThePointerReachingARow_DrawsAFrame_AndMovingWithinItDoesNot()
    {
        var (session, pane) = Mount();
        using (session)
        {
            float x = ListX(session, pane);
            session.Window.MoveMouse(x, RowY(3));
            session.PumpFrames(3);
            Assert.False(session.PumpFrame(), "an idle pane drew a frame");

            session.Window.MoveMouse(x, RowY(4));
            Assert.True(session.PumpFrame(), "the tint's move drew no frame");
            session.PumpFrames(2);

            session.Window.MoveMouse(x + 20, RowY(4) + 2);
            Assert.False(session.PumpFrame(), "a move within the row drew a frame");
        }
    }

    /// <summary>The row is kept, so it keeps the pointer: the selection reaching it covers the tint
    /// and the selection leaving shows it again, and no row is made either way.</summary>
    [Fact]
    public void TheSelectionPassingUnderThePointer_LeavesTheTintOnTheRow()
    {
        var (session, pane) = Mount();
        using (session)
        {
            session.Window.MoveMouse(ListX(session, pane), RowY(1));
            session.PumpFrames(2);
            int made = pane.Made;

            pane.Select(1);
            session.PumpFrames(2);
            using (var shot = Shot(session))
                Assert.Equal(Sk(Picked), Fill(shot, 1));

            pane.Select(2);
            session.PumpFrames(2);
            using (var shot = Shot(session))
            {
                Assert.Equal(Sk(Tint), Fill(shot, 1));
                Assert.Equal(Sk(Picked), Fill(shot, 2));
            }
            Assert.Equal(made, pane.Made);
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void TheListDrawsTheSamePixelsAsARowThatTintedItself(float scale)
    {
        byte[] Pixels(bool rowsHover)
        {
            var (session, pane) = Mount(scale: scale, setUp: p => { p.Hover = !rowsHover; p.RowsHover = rowsHover; });
            using (session)
            {
                session.Window.MoveMouse(ListX(session, pane), RowY(3));
                session.PumpFrames(2);
                using var shot = Shot(session);
                return shot.Bytes;
            }
        }

        Assert.Equal(Pixels(rowsHover: true), Pixels(rowsHover: false));
    }

    // ── The overlay over the selected row ──

    [Fact]
    public void TheOverlay_IsAtTheEndOfTheSelectedRow_AndMovesWithNoRowMade()
    {
        var chip = Chip();
        var (session, pane) = Mount(() => chip);
        using (session)
        {
            var list = pane.List.Current!;
            var content = list.Layout.AbsoluteContentBox;
            var box = chip.Layout.AbsoluteBorderBox;
            Assert.Equal(content.X + content.Width, box.X + box.Width);
            Assert.Equal(content.Y, box.Y);
            Assert.Equal(RowHeight, box.Height);

            int made = pane.Made;
            for (int i = 1; i <= 4; i++)
            {
                pane.Select(i);
                session.PumpFrames(2);
                Assert.Equal(content.Y + i * RowHeight, chip.Layout.AbsoluteBorderBox.Y);
                Assert.Equal(content.X + content.Width, chip.Layout.AbsoluteBorderBox.X + chip.Layout.AbsoluteBorderBox.Width);
                using var shot = Shot(session);
                Assert.Equal(Sk(Mark), Fill(shot, i));
                Assert.NotEqual(Sk(Mark), Fill(shot, i - 1));
            }
            Assert.Equal(made, pane.Made);
            Assert.Same(chip, list.SelectionOverlay);
        }
    }

    /// <summary>A key's move of the selection is drawing: the overlay moves when the list is drawn,
    /// and no element is marked for layout, so the boxes around the list are not laid out.</summary>
    [Fact]
    public void AMove_LaysNothingOut()
    {
        var chip = Chip();
        var (session, pane) = Mount(() => chip);
        using (session)
        {
            session.PumpFrames(2);
            pane.List.Current!.SelectedIndex = 3;
            Assert.Equal(DirtyFlags.None, pane.List.Current.DirtyFlags & DirtyFlags.Layout);
            session.PumpFrames(1);
            Assert.Equal(DirtyFlags.None, chip.DirtyFlags & DirtyFlags.Layout);
            Assert.Equal(DirtyFlags.None, pane.List.Current.DirtyFlags & DirtyFlags.Layout);
        }
    }

    /// <summary>Set on the mounted list, with nothing else to draw and no row to make: the overlay
    /// still moves.</summary>
    [Fact]
    public void ASelectionSetOnTheMountedList_MovesIt_WithNoHighlightAsWell()
    {
        var chip = Chip();
        var (session, pane) = Mount(() => chip, setUp: p => { p.Highlight = false; p.PlainRows = true; });
        using (session)
        {
            var y = chip.Layout.AbsoluteBorderBox.Y;
            session.PumpFrames(2);
            Assert.False(session.PumpFrame(), "an idle pane drew a frame");
            pane.List.Current!.SelectedIndex = 3;
            Assert.True(session.PumpFrame(), "the move drew no frame");
            Assert.Equal(y + 3 * RowHeight, chip.Layout.AbsoluteBorderBox.Y);
        }
    }

    [Fact]
    public void ThePointerGoesThroughIt_ToTheRowUnderIt()
    {
        var (session, pane) = Mount(() => Chip());
        using (session)
        {
            pane.Select(2);
            session.PumpFrames(2);
            session.Window.Click(Width - EndInset, RowY(2));
            session.PumpFrames(2);
            Assert.Equal(1, pane.Clicks);
            Assert.Equal(2, pane.LastClicked);
        }
    }

    [Fact]
    public void ItScrollsWithTheRows_AndIsClippedToTheList()
    {
        var (session, pane) = Mount(() => Chip());
        using (session)
        {
            pane.Select(2);
            session.PumpFrames(2);
            var list = pane.List.Current!;

            list.ScrollOffsetY = RowHeight * 2;
            session.PumpFrames(2);
            using (var shot = Shot(session))
                Assert.Equal(Sk(Mark), Fill(shot, 0));

            list.ScrollOffsetY = RowHeight * 40;
            session.PumpFrames(2);
            using (var shot = Shot(session))
                for (int row = 0; row < 12; row++)
                    Assert.NotEqual(Sk(Mark), Fill(shot, row));

            list.ScrollOffsetY = RowHeight * 2;
            session.PumpFrames(2);
            using (var shot = Shot(session))
                Assert.Equal(Sk(Mark), Fill(shot, 0));
        }
    }

    [Fact]
    public void NoSelection_TakesItOut_AndASelectionPutsItBack()
    {
        var chip = Chip();
        var (session, pane) = Mount(() => chip);
        using (session)
        {
            Assert.True(chip.IsHosted);
            int made = pane.Made;

            pane.Select(-1);
            session.PumpFrames(2);
            Assert.False(chip.IsHosted);
            using (var shot = Shot(session))
                Assert.NotEqual(Sk(Mark), Fill(shot, 0));

            pane.Select(3);
            session.PumpFrames(2);
            Assert.True(chip.IsHosted);
            using (var shot = Shot(session))
                Assert.Equal(Sk(Mark), Fill(shot, 3));
            Assert.Equal(made, pane.Made);
        }
    }

    [Fact]
    public void ANewElement_IsPatchedIntoTheOneOnScreen_AndNoneTakesItAway()
    {
        string text = "Details";
        bool shown = true;
        var (session, pane) = Mount(() => shown ? Chip(text) : null);
        using (session)
        {
            var live = pane.List.Current!.SelectionOverlay!;
            Assert.True(live.IsHosted);

            text = "Compare";
            pane.Rebuild();
            session.PumpFrames(2);
            Assert.Same(live, pane.List.Current!.SelectionOverlay);
            Assert.Equal("Compare", ((TextElement)live.Children[0]).Content);

            shown = false;
            pane.Rebuild();
            session.PumpFrames(2);
            Assert.Null(pane.List.Current!.SelectionOverlay);
            Assert.False(live.IsHosted);
            using var shot = Shot(session);
            Assert.NotEqual(Sk(Mark), Fill(shot, 0));
        }
    }

    [Fact]
    public void AnElementOfAnotherKind_ReplacesTheOneOnScreen()
    {
        bool box = true;
        var (session, pane) = Mount(() => box ? Chip() : TextB.New("plain").FontSize(11f));
        using (session)
        {
            var live = pane.List.Current!.SelectionOverlay!;

            box = false;
            pane.Rebuild();
            session.PumpFrames(2);

            var now = pane.List.Current!.SelectionOverlay!;
            Assert.IsType<TextElement>(now);
            Assert.True(now.IsHosted);
            Assert.False(live.IsHosted);
        }
    }

    [Fact]
    public void WithRowsOfManyHeights_ItIsAsTallAsTheSelectedRow()
    {
        var chip = Chip();
        var (session, pane) = Mount(() => chip, setUp: p => p.Heights = (e, _) => e.Id % 2 == 0 ? 24f : 48f);
        using (session)
        {
            var content = pane.List.Current!.Layout.AbsoluteContentBox;
            Assert.Equal(24f, chip.Layout.AbsoluteBorderBox.Height);

            pane.Select(3);   // 24 + 48 + 24 above it
            session.PumpFrames(2);
            Assert.Equal(48f, chip.Layout.AbsoluteBorderBox.Height);
            Assert.Equal(content.Y + 96f, chip.Layout.AbsoluteBorderBox.Y);
        }
    }

    /// <summary>Items are nodes of the list's own: the overlay adds nothing a reader hears, and the
    /// tree is the same with it and without it, a selection move included.</summary>
    [Fact]
    public void TheAccessibilityTree_IsTheSameWithIt()
    {
        List<(string? Name, bool? Selected)> Items(Func<Element?>? overlay)
        {
            var (session, pane) = Mount(overlay, count: 6);
            using (session)
            {
                session.Window.InstallAccessibilityBridge();
                pane.Select(2);
                session.PumpFrames(3);
                var nodes = new List<(string?, bool?)>();
                void Walk(AccessibilityNode n)
                {
                    if (n.Role == AccessibilityRole.ListItem) nodes.Add((n.Name, n.IsSelected));
                    else if (n.Role == AccessibilityRole.Label) nodes.Add((n.Name, null));
                    foreach (var c in n.Children) Walk(c);
                }
                Walk(session.Window.AccessibilityTree);
                return nodes;
            }
        }

        var with = Items(() => Chip());
        Assert.Equal(Items(null), with);
        Assert.Equal(6, with.Count);
    }

    /// <summary>Composite rows are read as elements, so what the overlay holds is read too.</summary>
    [Fact]
    public void OnCompositeRows_ItIsReadAfterThem()
    {
        var (session, pane) = Mount(() => Chip(), count: 3, setUp: p => p.Composite = true);
        using (session)
        {
            session.Window.InstallAccessibilityBridge();
            session.PumpFrames(3);
            var named = new List<string?>();
            void Walk(AccessibilityNode n)
            {
                if (n.Role is AccessibilityRole.Label or AccessibilityRole.Button) named.Add(n.Name);
                foreach (var c in n.Children) Walk(c);
            }
            Walk(session.Window.AccessibilityTree);
            Assert.Equal(["entry 0", "entry 1", "entry 2", "Details"], named);
        }
    }
}
