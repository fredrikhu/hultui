using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using SkiaSharp;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A virtual list of a million rows: what an app listing a huge folder gives it. Mounting it must
/// cost the rows on screen, not the million; its last row must be reachable and drawn where the
/// list ends; and its rows must still tile down there, where a million rows of 24 px put the
/// offsets past the 16.7 million at which a float stops holding every whole pixel: exactly at 1.0,
/// within the limit ELEMENTS.md's "Very long lists" states at a fractional scale.
/// </summary>
public class VirtualListMillionTests
{
    private const int Million = 1_000_000;
    private const float RowHeight = 24f;

    /// <summary>A row of the list: its index and one of a few labels.</summary>
    private sealed record Row(int Index, string Label);

    /// <summary>A million rows made as they are read, as the perf suite's <c>SyntheticRows</c>: nothing per row.</summary>
    private sealed class Rows(int count) : IReadOnlyList<Row>
    {
        private static readonly string[] Labels = [.. Enumerable.Range(0, 997).Select(i => $"row label {i}")];

        public int Count => count;

        public Row this[int index]
        {
            get
            {
                if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
                return new Row(index, Labels[index % Labels.Length]);
            }
        }

        public IEnumerator<Row> GetEnumerator()
        {
            for (int i = 0; i < count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void MountingAMillionRowListAllocatesOnlyTheVisibleRows()
    {
        using var session = HeadlessSession.Create(800, 600);
        var list = VirtualListElement<Row>.New()
            .ItemHeight(RowHeight)
            .Items(new Rows(Million))
            .RenderWith(r => Flex.Row().Children(TextB.New(r.Label).FontSize(12f)))
            .FlexGrow(1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        session.PumpFrame();
        session.PumpFrame();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 1024 * 1024,
            $"mounting a million-row list allocated {allocated / 1024} KB; the rows on screen cost far less");
    }

    [Fact]
    public void TheLastRowOfAMillionIsReachedAndDrawn()
    {
        using var session = HeadlessSession.Create(800, 600);
        var made = new Dictionary<int, Element>();
        var list = VirtualListElement<Row>.New()
            .ItemHeight(RowHeight)
            .Items(new Rows(Million))
            .RenderWith(r => made[r.Index] = Flex.Row().Children(TextB.New(r.Label).FontSize(12f)))
            .FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        session.PumpFrame();

        list.ScrollOffsetY = float.MaxValue;
        session.PumpFrame();

        Assert.True(made.TryGetValue(Million - 1, out var last), "the last row was never made");
        Assert.Same(list, last.Parent);
        float listBottom = list.WindowContentBox.Bottom;
        Assert.InRange(last.WindowBorderBox.Bottom, listBottom - 0.5f, listBottom + 0.5f);
    }

    /// <summary>
    /// Rows of alternating black and white, scrolled to the end of a million: where each colour
    /// starts down one pixel column. At 1.0 the rows still tile exactly. At a fractional scale they
    /// do not: past 2^24 logical px a float holds only every other pixel, and at 1.25 consecutive
    /// rows come out 28 and 32 device px tall instead of 30, the bound this asserts (ELEMENTS.md,
    /// "Very long lists"). Thin lines inside rows fare worse there: a 1 px border is not drawn at
    /// all, which is why the rows are told apart by their fill, not by a border.
    /// </summary>
    [Theory]
    [InlineData(1.0f, 0)]
    [InlineData(1.25f, 2)]
    public void RowsTileExactlyAtTheEndOfAMillion(float scale, int toleranceDevicePx)
    {
        using var session = HeadlessSession.Create(800, 600, scale);
        var list = VirtualListElement<Row>.New()
            .ItemHeight(RowHeight)
            .Items(new Rows(Million))
            .RenderWith(r => Flex.Row().FlexGrow(1).Background(r.Index % 2 == 0 ? Color.White : Color.Black))
            .FlexGrow(1);
        // A 13 px header puts the list's content box at an odd Y.
        var header = Box.New().Height(13f).FlexShrink(0).Background(new Color(128, 0, 0));
        session.Window.SetRoot(Flex.Column(gap: 0).FlexGrow(1).Children(header, list));
        session.PumpFrame();

        list.ScrollOffsetY = float.MaxValue;
        session.PumpFrame();

        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        var view = list.WindowContentBox;
        int x = (int)(view.X * scale) + 20;
        int top = (int)MathF.Ceiling(view.Y * scale) + 1;
        int bottom = (int)MathF.Floor(view.Bottom * scale) - 1;
        // Where each row starts: the first pure pixel of the other colour; antialiased rows between count for neither.
        var starts = new List<int>();
        int previous = -1;
        for (int y = top; y < bottom; y++)
        {
            int red = bitmap.GetPixel(x, y).Red;
            int colour = red > 200 ? 1 : red < 55 ? 0 : -1;
            if (colour < 0 || colour == previous) continue;
            if (previous >= 0) starts.Add(y);
            previous = colour;
        }

        Assert.True(starts.Count >= 2, $"found {starts.Count} row edges in the viewport");
        float pitch = RowHeight * scale;
        for (int i = 1; i < starts.Count; i++)
        {
            int height = starts[i] - starts[i - 1];
            Assert.True(MathF.Abs(height - pitch) <= toleranceDevicePx + 0.01f,
                $"a row of the viewport is {height} device px tall, not {pitch} ± {toleranceDevicePx}");
        }
    }
}
