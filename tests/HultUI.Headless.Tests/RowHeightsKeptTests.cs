using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using HultUI.Testing;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A list that takes its row heights from <c>RowHeights</c> works them out into an array of
/// offsets, one float a row. An owner rebuilt per key hands it a new height function every
/// build, and every patch made a new array for it, the same heights or not — and after
/// <see cref="VirtualListElement{T}.InvalidateRows"/> a second one in the layout pass: Vesper's
/// viewer on a 20,000-line log paid 160 KB a keystroke of find for them, on the large object heap
/// past 21,250 rows. The list now works them out into a spare array it keeps, compares them with
/// the last ones, and swaps only when one moved. These pin that a rebuild over the same heights
/// costs no array, told or not, and that heights that do move — back and forth, through the one
/// spare — still put every row where they say.
/// </summary>
public class RowHeightsKeptTests
{
    private const float Row = 18f;

    /// <summary>A pane rebuilt on demand over a list of <paramref name="count"/> lines on item rows,
    /// every seventh two rows tall unless <see cref="Tall"/> says otherwise.</summary>
    private sealed class Pane(int count) : Component
    {
        public int Count = count;
        public float Tall = 2 * Row;
        public bool Tell;
        public int Made;
        public readonly Ref<VirtualListElement<int>> List = new();
        private int[] _items = [];

        public override Element Build()
        {
            if (Tell) List.Current?.InvalidateRows();
            // The same items while the count holds: only the height function is new each build.
            if (_items.Length != Count) _items = Enumerable.Range(0, Count).ToArray();
            var items = _items;
            float tall = Tall;
            return Flex.Column().FlexGrow(1).Children(
                VirtualListElement<int>.New()
                    .ItemHeight(Row)
                    .RowHeights((_, i) => i % 7 == 3 ? tall : Row)
                    .Items(items)
                    .RenderWith((i, _) => { Made++; return TextB.New($"line {i}").FontSize(12f); })
                    .FlexGrow(1)
                    .WithRef(List));
        }

        public void Rebuild() => MarkDirty();

        public float Top(int index) => index * Row + (index + 3) / 7 * (Tall - Row);
    }

    private static (HeadlessSession Session, Pane Pane) Mount(int count)
    {
        var session = HeadlessSession.Create(600, 400);
        var pane = new Pane(count);
        session.Application.SetRoot(pane.Mount());
        session.PumpFrames(3);
        return (session, pane);
    }

    /// <summary>Null when every realized row stands where the heights put it.</summary>
    private static string? RowsWhereTheHeightsSay(Pane pane)
    {
        var list = pane.List.Current!;
        var rows = list.Children;   // the spacer, then the rows from the top
        Assert.True(rows.Count > 5, $"only {rows.Count} children realized");
        for (int i = 1; i < rows.Count; i++)
        {
            float top = rows[i].Layout.AbsoluteBorderBox.Y - list.Layout.AbsoluteContentBox.Y;
            float want = pane.Top(i - 1);
            if (Math.Abs(top - want) > 0.5f) return $"row {i - 1} stands at {top}, not {want}";
            float height = rows[i].Layout.Height;
            float wantHeight = (i - 1) % 7 == 3 ? pane.Tall : Row;
            if (Math.Abs(height - wantHeight) > 0.5f) return $"row {i - 1} is {height} tall, not {wantHeight}";
        }
        return null;
    }

    // 200,000 rows: one offsets array is 800 KB, far more than the rows a told rebuild makes again.
    private const int Lines = 200_000;
    private const long OneArray = 4L * (Lines + 1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARebuildOverTheSameHeights_MakesNoOffsetsArray(bool told)
    {
        var (session, pane) = Mount(Lines);
        using var _ = session;
        pane.Tell = told;
        void Round() { pane.Rebuild(); session.PumpFrames(1); }
        Round();
        Round();

        long bytes = Allocations.Least(Round, goal: OneArray / 8);

        Assert.True(bytes < OneArray / 4, $"a rebuild allocated {bytes:N0} B; one offsets array is {OneArray:N0} B");
        Assert.Null(RowsWhereTheHeightsSay(pane));
        if (told) Assert.True(pane.Made > 0, "the told rows were never made again");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeightsThatMoveBackAndForth_PutTheRowsWhereTheySay(bool told)
    {
        var (session, pane) = Mount(500);
        using var _ = session;
        pane.Tell = told;
        Assert.Null(RowsWhereTheHeightsSay(pane));

        // Through the one spare array each way: a swap that kept a stale array would show here. A
        // pixel's move keeps the same rows on screen, so only the heights tell them to move.
        foreach (float tall in new[] { 3 * Row, 2 * Row, 3 * Row, 3 * Row, Row, 2 * Row, 2 * Row + 1, 2 * Row, 2 * Row + 1 })
        {
            pane.Tall = tall;
            pane.Rebuild();
            session.PumpFrames(2);
            Assert.Null(RowsWhereTheHeightsSay(pane));
        }
    }

    [Fact]
    public void MoreLinesFromARebuild_AreLaidOutAndScrolledTo()
    {
        var (session, pane) = Mount(50);
        using var _ = session;
        var list = pane.List.Current!;

        pane.Count = 5_000;
        pane.Rebuild();
        session.PumpFrames(2);
        Assert.Null(RowsWhereTheHeightsSay(pane));

        // The extent is the new lines': the end is the last one's bottom.
        list.ScrollOffsetY = float.MaxValue;
        session.PumpFrames(2);
        float bottom = pane.Top(5_000);
        Assert.Equal(bottom - list.Layout.AbsoluteContentBox.Height, list.ScrollOffsetY, 1);

        pane.Count = 40;
        pane.Rebuild();
        session.PumpFrames(2);
        Assert.Equal(Math.Max(0, pane.Top(40) - list.Layout.AbsoluteContentBox.Height), list.ScrollOffsetY, 1);
    }
}
