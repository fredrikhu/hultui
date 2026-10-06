using System.Collections.Immutable;

namespace HultUI.Core.RichText;

/// <summary>
/// Operations on a block's span list. Every one returns a normalized list — sorted, inside the
/// text, no empty or plain span, no two neighbours with the same marks — so the document's
/// equality is the equality of what it says, not of how it was edited into shape.
/// </summary>
internal static class RichSpans
{
    /// <summary>Sorts, clamps to <paramref name="length"/>, drops empty and plain spans and merges
    /// neighbours that carry the same marks. Overlapping input keeps the later span's marks where
    /// two overlap.</summary>
    public static ImmutableArray<RichSpan> Normalize(IEnumerable<RichSpan> spans, int length)
    {
        var sorted = spans
            .Select(s => Clip(s, 0, length))
            .Where(s => s.Length > 0 && !s.Marks.IsNone)
            .OrderBy(s => s.Start)
            .ToList();
        if (sorted.Count == 0) return [];

        var result = ImmutableArray.CreateBuilder<RichSpan>(sorted.Count);
        foreach (var s in sorted)
        {
            var span = s;
            if (result.Count > 0)
            {
                var last = result[^1];
                if (span.Start < last.End)
                {
                    // Overlap: the later span wins its own range.
                    var head = last with { Length = span.Start - last.Start };
                    var tail = last.End > span.End ? last with { Start = span.End, Length = last.End - span.End } : default;
                    result.RemoveAt(result.Count - 1);
                    if (head.Length > 0) result.Add(head);
                    Add(result, span);
                    if (tail.Length > 0) Add(result, tail);
                    continue;
                }
            }
            Add(result, span);
        }
        return result.ToImmutable();
    }

    // Appends, merging into the last span when it ends where this one starts with the same marks.
    private static void Add(ImmutableArray<RichSpan>.Builder into, RichSpan span)
    {
        if (span.Length <= 0 || span.Marks.IsNone) return;
        if (into.Count > 0 && into[^1].End == span.Start && into[^1].Marks == span.Marks)
            into[^1] = into[^1] with { Length = into[^1].Length + span.Length };
        else
            into.Add(span);
    }

    private static RichSpan Clip(RichSpan s, int from, int to)
    {
        int start = Math.Clamp(s.Start, from, to);
        int end = Math.Clamp(s.End, from, to);
        return s with { Start = start, Length = Math.Max(0, end - start) };
    }

    /// <summary>The spans over <c>[from, to)</c>, moved to start at 0.</summary>
    public static ImmutableArray<RichSpan> Slice(ImmutableArray<RichSpan> spans, int from, int to)
    {
        var result = ImmutableArray.CreateBuilder<RichSpan>();
        foreach (var s in spans)
        {
            if (s.End <= from) continue;
            if (s.Start >= to) break;
            var clipped = Clip(s, from, to);
            Add(result, clipped with { Start = clipped.Start - from });
        }
        return result.ToImmutable();
    }

    /// <summary><paramref name="head"/>'s spans, then <paramref name="tail"/>'s moved on by
    /// <paramref name="headLength"/>, merged where they meet.</summary>
    public static ImmutableArray<RichSpan> Concat(ImmutableArray<RichSpan> head, int headLength,
        ImmutableArray<RichSpan> tail)
    {
        if (tail.IsEmpty) return head;
        var result = ImmutableArray.CreateBuilder<RichSpan>(head.Length + tail.Length);
        foreach (var s in head) Add(result, s);
        foreach (var s in tail) Add(result, s with { Start = s.Start + headLength });
        return result.ToImmutable();
    }

    /// <summary>
    /// Runs <paramref name="change"/> over the marks of every character in <c>[from, to)</c> —
    /// plain characters included, as <see cref="RichMarks.None"/> — and puts the results back.
    /// </summary>
    public static ImmutableArray<RichSpan> Map(ImmutableArray<RichSpan> spans, int length, int from, int to,
        Func<RichMarks, RichMarks> change)
    {
        from = Math.Clamp(from, 0, length);
        to = Math.Clamp(to, from, length);
        if (from == to) return spans;

        var result = ImmutableArray.CreateBuilder<RichSpan>();
        foreach (var run in Runs(spans, length))
        {
            // The part of the run before, inside and after the range.
            int s = run.Start, e = run.End;
            if (e <= from || s >= to)
            {
                Add(result, run);
                continue;
            }
            if (s < from) Add(result, run with { Length = from - s });
            int ms = Math.Max(s, from), me = Math.Min(e, to);
            Add(result, new RichSpan(ms, me - ms, change(run.Marks)));
            if (e > to) Add(result, run with { Start = to, Length = e - to });
        }
        return result.ToImmutable();
    }

    /// <summary>Every character in runs of equal marks, plain runs included, covering
    /// <c>[0, length)</c>.</summary>
    public static IEnumerable<RichSpan> Runs(ImmutableArray<RichSpan> spans, int length)
    {
        int at = 0;
        foreach (var s in spans)
        {
            if (s.Start > at) yield return new RichSpan(at, s.Start - at, RichMarks.None);
            yield return s;
            at = s.End;
        }
        if (at < length) yield return new RichSpan(at, length - at, RichMarks.None);
    }
}
