using System.Collections;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// The spans a TextArea draws a line with: the ones of <see cref="TextAreaElement.Spans"/> that
/// reach into it, clipped and rebased to the line. They used to be found by walking the list
/// from its first span for every line drawn — 600,000 spans a frame over a 23,000-span JSON
/// response — and are now found from the first span that can reach the line, by a binary
/// search on the spans' running maximum end.
///
/// <para>Running maximum, not End: the spans are sorted by start only, and may overlap.
/// Meridian and Junction merge search tints into syntax colours and sort by start, so a long
/// string's span is followed by the short tint inside it and End goes down. A search on End
/// would skip the long span for a line it reaches. Every list here has such places, and every
/// answer is held to the old linear scan.</para>
/// </summary>
public class TextAreaSpanTests
{
    private static readonly Color Ink = new(200, 120, 40, 255);
    private static readonly Color Tint = new(40, 80, 200, 120);

    // The old scan, as it was: every span from the first, until one starts past the line.
    private static List<TextSpan>? Linear(IReadOnlyList<TextSpan> spans, int lineStart, int lineEnd)
    {
        List<TextSpan>? result = null;
        foreach (var s in spans)
        {
            if (s.End <= lineStart) continue;
            if (s.Start >= lineEnd) break;
            int start = Math.Max(s.Start, lineStart);
            int end = Math.Min(s.End, lineEnd);
            if (end <= start) continue;
            (result ??= []).Add(new TextSpan(start - lineStart, end - start, s.Color, s.Background));
        }
        return result;
    }

    /// <summary>
    /// Sorted by start, as the apps make them: tokens on lines of a document, some of them
    /// long, then search hits inside them merged in and the list sorted by start alone (an
    /// unstable sort, so equal starts come in either order). Now and then a span of no length,
    /// one longer than the document, or one that covers many lines.
    /// </summary>
    private static List<TextSpan> RandomSpans(Random rng, int docLength, out int descents)
    {
        var spans = new List<TextSpan>();
        int at = 0;
        while (at < docLength)
        {
            at += rng.Next(0, 6);
            int length = rng.Next(10) switch { 0 => 0, 1 => rng.Next(40, 400), 2 => rng.Next(400, 3000), _ => rng.Next(1, 12) };
            spans.Add(new TextSpan(at, length, Ink));
            at += Math.Max(1, length / (rng.Next(3) + 1));
        }
        int hits = rng.Next(0, spans.Count / 3 + 1);
        for (int h = 0; h < hits; h++)
        {
            var host = spans[rng.Next(spans.Count)];
            int start = host.Start + rng.Next(0, Math.Max(1, host.Length));
            spans.Add(new TextSpan(start, rng.Next(0, 6), default, Tint));
        }
        spans.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        descents = 0;
        for (int k = 1; k < spans.Count; k++)
            if (spans[k].End < spans[k - 1].End) descents++;
        return spans;
    }

    private static void AssertLikeTheLinearScan(TextAreaElement area, IReadOnlyList<TextSpan> spans, int lineStart, int lineEnd)
    {
        var expected = Linear(spans, lineStart, lineEnd);
        var actual = area.SpansForLine(lineStart, lineEnd);
        if (expected is null)
        {
            Assert.True(actual is null, $"line [{lineStart}, {lineEnd}): expected no spans, got {actual?.Count}");
            return;
        }
        Assert.True(actual is not null, $"line [{lineStart}, {lineEnd}): expected {expected.Count} spans, got none");
        Assert.True(expected.SequenceEqual(actual),
            $"line [{lineStart}, {lineEnd}): expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    }

    [Fact]
    public void EachLinesSpansAreTheOnesTheLinearScanFinds()
    {
        var rng = new Random(20261009);
        int lists = 0, descents = 0, compared = 0, withSpans = 0;
        var area = TextAreaElement.New("");
        for (int list = 0; list < 300; list++)
        {
            int docLength = rng.Next(1, 20_000);
            var spans = RandomSpans(rng, docLength, out int d);
            if (spans.Count == 0) continue;
            lists++;
            descents += d;
            area.Spans = spans;
            for (int q = 0; q < 200; q++)
            {
                // Lines of every length, some empty of spans, some past the end of them all.
                int lineStart = rng.Next(-5, docLength + 50);
                int lineEnd = lineStart + rng.Next(1, rng.Next(2) == 0 ? 80 : 3000);
                AssertLikeTheLinearScan(area, spans, lineStart, lineEnd);
                compared++;
                if (Linear(spans, lineStart, lineEnd) is not null) withSpans++;
            }
        }
        // Lists where End goes down are what a search on End would get wrong: there must be many.
        Assert.True(descents > 3_000, $"only {descents} places where a span ends before the one before it");
        Assert.True(withSpans > compared / 2, $"only {withSpans} of {compared} lines had spans");
        Assert.True(lists > 250);
    }

    /// <summary>A line whose only span is a long one, followed by short ones that end before
    /// the line starts: End goes down, so a search on End lands past the long span.</summary>
    [Fact]
    public void ALongSpanReachesALineAfterTheShortOnesInsideIt()
    {
        var area = TextAreaElement.New("");
        TextSpan[] spans =
        [
            new(0, 100, Ink),        // a string running over many lines
            new(10, 3, default, Tint),
            new(20, 3, default, Tint),
            new(30, 3, default, Tint),
            new(120, 4, Ink),
        ];
        area.Spans = spans;
        var line = area.SpansForLine(50, 60);
        Assert.NotNull(line);
        Assert.Equal([new TextSpan(0, 10, Ink)], line);
        AssertLikeTheLinearScan(area, spans, 50, 60);
        AssertLikeTheLinearScan(area, spans, 95, 125);
    }

    /// <summary>The index belongs to one list: another list of the same length is indexed
    /// anew.</summary>
    [Fact]
    public void AnotherListIsIndexedAgain()
    {
        var area = TextAreaElement.New("");
        TextSpan[] first = [new(0, 1, Ink), new(10, 1, Ink), new(20, 1, Ink)];
        TextSpan[] second = [new(0, 30, Ink), new(10, 1, default, Tint), new(20, 1, default, Tint)];

        area.Spans = first;
        AssertLikeTheLinearScan(area, first, 25, 28);
        area.Spans = second;
        AssertLikeTheLinearScan(area, second, 25, 28);
        area.Spans = first;
        AssertLikeTheLinearScan(area, first, 0, 15);
    }

    /// <summary>A list the app added to in place, and set again, is indexed again.</summary>
    [Fact]
    public void AListThatGrewIsIndexedAgain()
    {
        var area = TextAreaElement.New("");
        var spans = new List<TextSpan> { new(0, 1, Ink), new(10, 1, Ink) };
        area.Spans = spans;
        AssertLikeTheLinearScan(area, spans, 0, 15);

        spans.Add(new TextSpan(20, 5, Ink));
        area.Spans = spans;
        AssertLikeTheLinearScan(area, spans, 22, 24);
    }

    /// <summary>
    /// A line in the middle of a 20,000-span list reads the few spans that can reach it, not
    /// the half of the list before it. The first line after a new list reads every span once,
    /// to index them.
    /// </summary>
    [Fact]
    public void ALineReadsOnlyTheSpansThatCanReachIt()
    {
        var spans = new List<TextSpan>();
        for (int line = 0; line < 4_000; line++)
            for (int token = 0; token < 5; token++)
                spans.Add(new TextSpan(line * 60 + token * 10, 6, Ink));
        var counted = new CountingSpans(spans);
        var area = TextAreaElement.New("");
        area.Spans = counted;

        area.SpansForLine(0, 59);
        Assert.InRange(counted.Reads, spans.Count, spans.Count + 20);

        counted.Reads = 0;
        var middle = area.SpansForLine(2_000 * 60, 2_000 * 60 + 59);
        Assert.Equal(5, middle?.Count);
        Assert.InRange(counted.Reads, 5, 8);
    }

    /// <summary>One list serves every line: drawing the lines again, as the next frame does,
    /// allocates nothing. The renderer reads the spans only during its DrawText call.</summary>
    [Fact]
    public void DrawingALineAllocatesNothing()
    {
        var rng = new Random(7);
        var spans = RandomSpans(rng, 50_000, out _);
        var area = TextAreaElement.New("");
        area.Spans = spans;
        // The first frame indexes the list and grows the line list to the most spans a line has.
        for (int line = 0; line < 600; line++)
            area.SpansForLine(line * 80, line * 80 + 79);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int found = 0;
        for (int line = 0; line < 600; line++)
            found += area.SpansForLine(line * 80, line * 80 + 79)?.Count ?? 0;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(found > 600, $"only {found} spans on 600 lines");
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void NoSpansMeansNone()
    {
        var area = TextAreaElement.New("");
        Assert.Null(area.SpansForLine(0, 10));
        area.Spans = [];
        Assert.Null(area.SpansForLine(0, 10));
        area.Spans = [new TextSpan(20, 5, Ink)];
        Assert.Null(area.SpansForLine(0, 10));
        Assert.Null(area.SpansForLine(30, 40));
    }

    private sealed class CountingSpans(List<TextSpan> inner) : IReadOnlyList<TextSpan>
    {
        public int Reads;

        public TextSpan this[int index]
        {
            get { Reads++; return inner[index]; }
        }

        public int Count => inner.Count;

        public IEnumerator<TextSpan> GetEnumerator()
        {
            foreach (var s in inner)
            {
                Reads++;
                yield return s;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
