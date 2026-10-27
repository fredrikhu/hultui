using System.Collections;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Testing;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// The spans a TextArea draws a line with when it has <see cref="TextAreaElement.Highlights"/>:
/// its spans and its highlights that reach the line, the highlights as background-only spans,
/// in start order with a span first at an equal start — the list Meridian and Junction made by
/// merging their find tints into a copy of every span and sorting it, sliced as before. The
/// merge now happens per line drawn, so a find step hands the area its matches alone: the spans
/// are not read again, let alone copied.
/// </summary>
public class TextAreaHighlightLineTests
{
    private static readonly Color Ink = new(200, 120, 40, 255);
    private static readonly Color Tint = new(40, 80, 200, 120);
    private static readonly Color Current = new(240, 160, 20, 200);

    // The old scan over one list: every span from the first, until one starts past the line.
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

    // The highlights merged in as the apps merged them, sorted stably: a span before a highlight
    // at the same start.
    private static List<TextSpan> Merged(IReadOnlyList<TextSpan> spans, IReadOnlyList<TextHighlight> highlights) =>
        [.. spans.Select(s => (s, 0))
            .Concat(highlights.Select(h => (new TextSpan(h.Start, h.Length, default, h.Background), 1)))
            .OrderBy(p => p.Item1.Start).ThenBy(p => p.Item2)
            .Select(p => p.Item1)];

    /// <summary>Tokens, some long, some of no length, some past the document's end.</summary>
    private static List<TextSpan> RandomSpans(Random rng, int docLength)
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
        return spans;
    }

    /// <summary>Matches: most apart, some side by side, some starting where a token starts,
    /// now and then one of no length, one over many lines or one inside another (a list may
    /// overlap).</summary>
    private static List<TextHighlight> RandomHighlights(Random rng, IReadOnlyList<TextSpan> spans, int docLength)
    {
        var highlights = new List<TextHighlight>();
        int at = rng.Next(0, 50);
        while (at < docLength)
        {
            int length = rng.Next(12) switch { 0 => 0, 1 => rng.Next(100, 2000), _ => rng.Next(1, 9) };
            if (rng.Next(4) == 0 && spans.Count > 0) at = Math.Max(at, spans[rng.Next(spans.Count)].Start);
            highlights.Add(new TextHighlight(at, length, rng.Next(5) == 0 ? Current : Tint));
            if (rng.Next(10) == 0) highlights.Add(new TextHighlight(at + 1, Math.Max(0, length - 2), Tint));
            at += rng.Next(3) == 0 ? length : length + rng.Next(1, 400);
        }
        highlights.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return highlights;
    }

    [Fact]
    public void EachLinesSpansAreTheMergedListsSlice()
    {
        var rng = new Random(20261027);
        var area = TextAreaElement.New("");
        int compared = 0, withHighlights = 0;
        for (int list = 0; list < 300; list++)
        {
            int docLength = rng.Next(1, 20_000);
            List<TextSpan>? spans = rng.Next(8) == 0 ? null : RandomSpans(rng, docLength);
            var highlights = RandomHighlights(rng, spans ?? [], docLength);
            var merged = Merged(spans ?? [], highlights);
            area.Spans = spans;
            area.Highlights = highlights;
            for (int q = 0; q < 200; q++)
            {
                int lineStart = rng.Next(-5, docLength + 50);
                int lineEnd = lineStart + rng.Next(1, rng.Next(2) == 0 ? 80 : 3000);
                var expected = Linear(merged, lineStart, lineEnd);
                var actual = area.SpansForLine(lineStart, lineEnd);
                if (expected is null)
                    Assert.True(actual is null, $"line [{lineStart}, {lineEnd}): expected no spans, got {actual?.Count}");
                else
                {
                    Assert.True(actual is not null && expected.SequenceEqual(actual),
                        $"line [{lineStart}, {lineEnd}): expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual ?? [])}]");
                    if (expected.Any(s => s.Color == default)) withHighlights++;
                }
                compared++;
            }
        }
        Assert.True(withHighlights > compared / 3, $"only {withHighlights} of {compared} lines had highlights");
    }

    /// <summary>
    /// A find step hands the area another list of highlights and the same spans. Drawing a line
    /// afterwards reads the few spans that reach it, as on any other frame — the spans are not
    /// indexed again — and the new highlights once, to index them.
    /// </summary>
    [Fact]
    public void AnotherListOfHighlights_ReadsNoSpanButTheLinesOwn()
    {
        var spans = new List<TextSpan>();
        for (int line = 0; line < 4_000; line++)
            for (int token = 0; token < 5; token++)
                spans.Add(new TextSpan(line * 60 + token * 10, 6, Ink));
        var counted = new Counting<TextSpan>(spans);
        var area = TextAreaElement.New("");
        area.Spans = counted;
        area.SpansForLine(0, 59);

        for (int step = 0; step < 5; step++)
        {
            var highlights = new Counting<TextHighlight>(
                [.. Enumerable.Range(0, 2_000).Select(i => new TextHighlight(i * 120 + step, 4, i == step ? Current : Tint))]);
            area.Highlights = highlights;
            counted.Reads = 0;
            var middle = area.SpansForLine(2_000 * 60, 2_000 * 60 + 59);
            Assert.Equal(6, middle?.Count);
            Assert.InRange(counted.Reads, 5, 8);
            Assert.InRange(highlights.Reads, 2_000, 2_004);

            // The next frame reads the line's own of both.
            counted.Reads = highlights.Reads = 0;
            area.SpansForLine(2_000 * 60, 2_000 * 60 + 59);
            Assert.InRange(counted.Reads, 5, 8);
            Assert.InRange(highlights.Reads, 1, 3);
        }
    }

    /// <summary>The same list again, as every rebuild that does not change the search hands
    /// it, is not indexed again.</summary>
    [Fact]
    public void TheSameHighlightsAgain_AreNotIndexedAgain()
    {
        var highlights = new Counting<TextHighlight>(
            [.. Enumerable.Range(0, 1_000).Select(i => new TextHighlight(i * 50, 3, Tint))]);
        var area = TextAreaElement.New("");
        area.Spans = [new TextSpan(0, 5, Ink)];
        area.Highlights = highlights;
        area.SpansForLine(0, 49);
        Assert.InRange(highlights.Reads, 1_000, 1_003);

        area.Highlights = highlights;
        highlights.Reads = 0;
        var line = area.SpansForLine(25_000, 25_049);
        Assert.Equal([new TextSpan(0, 3, default, Tint)], line);
        Assert.InRange(highlights.Reads, 1, 3);
    }

    [Fact]
    public void HighlightsWithoutSpans_AreDrawn()
    {
        var area = TextAreaElement.New("");
        area.Highlights = [new TextHighlight(5, 10, Tint), new TextHighlight(40, 2, Current)];
        Assert.Equal([new TextSpan(3, 5, default, Tint)], area.SpansForLine(2, 10));
        Assert.Equal([new TextSpan(0, 2, default, Current)], area.SpansForLine(40, 50));
        Assert.Null(area.SpansForLine(20, 30));
        area.Highlights = [];
        Assert.Null(area.SpansForLine(2, 10));
        area.Highlights = null;
        Assert.Null(area.SpansForLine(2, 10));
    }

    /// <summary>A patch hands the live area the highlights the rebuild made, as it does the spans.</summary>
    [Fact]
    public void APatchHandsOverTheHighlights()
    {
        var live = TextAreaElement.New("abcdef");
        var rebuilt = TextAreaElement.New("abcdef");
        TextHighlight[] highlights = [new TextHighlight(1, 2, Tint)];
        rebuilt.Highlights = highlights;
        live.PatchFrom(rebuilt);
        Assert.Same(highlights, live.Highlights);
        live.PatchFrom(TextAreaElement.New("abcdef"));
        Assert.Null(live.Highlights);
    }

    [Fact]
    public void DrawingALineWithHighlightsAllocatesNothing()
    {
        var rng = new Random(9);
        var spans = RandomSpans(rng, 50_000);
        var area = TextAreaElement.New("");
        area.Spans = spans;
        area.Highlights = RandomHighlights(rng, spans, 50_000);
        for (int line = 0; line < 600; line++)
            area.SpansForLine(line * 80, line * 80 + 79);

        int found = 0;
        long allocated = Allocations.Least(() =>
        {
            found = 0;
            for (int line = 0; line < 600; line++)
                found += area.SpansForLine(line * 80, line * 80 + 79)?.Count ?? 0;
        });

        Assert.True(found > 600, $"only {found} spans on 600 lines");
        Assert.Equal(0, allocated);
    }

    private sealed class Counting<T>(List<T> inner) : IReadOnlyList<T>
    {
        public int Reads;

        public T this[int index]
        {
            get { Reads++; return inner[index]; }
        }

        public int Count => inner.Count;

        public IEnumerator<T> GetEnumerator()
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
