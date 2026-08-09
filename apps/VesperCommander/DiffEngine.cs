using DiffPlex;
namespace VesperCommander;

/// <summary>Line-level diff for the F3 builtin view. Produces aligned side-by-side rows
/// (each carries the left and/or right line), pairing adjacent delete+insert runs into
/// "change" rows so edits line up. Pure and unit-testable; the view handles collapse and
/// painting. Line matching comes from DiffPlex's linear-space Myers differ; the pairing,
/// numbering and intra-line spans are ours.</summary>
public static class DiffEngine
{
    public enum Op { Equal, Add, Del, Change }

    /// <summary>One aligned row. <see cref="LeftNo"/>/<see cref="RightNo"/> are 1-based line
    /// numbers, or 0 when that side is absent (a pure add/delete).</summary>
    public sealed record Line(Op Op, int LeftNo, int RightNo, string? Left, string? Right);

    public sealed record Result(IReadOnlyList<Line> Lines, int Adds, int Dels, int Changes, bool Identical);

    /// <summary>
    /// Lines per side beyond which the caller shows its "too large" state.
    /// <para>
    /// This used to be a memory limit: the old LCS table needed n·m ints, so two 4,000-line
    /// files already cost 61MB and the guard multiplied the two lengths. Myers needs linear
    /// memory, so what is left to bound is time — and time depends on how <i>different</i>
    /// the files are, not how big they are. A 200,000-line pair with scattered edits diffs in
    /// ~130ms; two 20,000-line files where every line differs take ~700ms, and it climbs
    /// quadratically from there. The cap is set by that worst case, since it cannot be known
    /// before diffing which case a pair is.
    /// </para>
    /// <para>
    /// Raising it further means moving the diff off the UI thread, where a slow pathological
    /// pair costs a progress indicator rather than a frozen window.
    /// </para>
    /// </summary>
    public const int MaxLines = 20_000;

    public static bool TooLarge(string[] a, string[] b) => a.Length > MaxLines || b.Length > MaxLines;

    public static Result Diff(string[] a, string[] b)
    {
        int n = a.Length, m = b.Length;
        var ops = BuildOpStream(a, b);

        // Pair each delete-run with the insert-run that follows it into change rows.
        var lines = new List<Line>();
        int adds = 0, dels = 0, changes = 0;
        for (int i = 0; i < ops.Count;)
        {
            if (ops[i].op == Op.Equal)
            {
                var o = ops[i++];
                lines.Add(new Line(Op.Equal, o.ai + 1, o.bi + 1, a[o.ai], b[o.bi]));
                continue;
            }
            int delStart = i; while (i < ops.Count && ops[i].op == Op.Del) i++;
            int delEnd = i; while (i < ops.Count && ops[i].op == Op.Add) i++;
            int addEnd = i;
            int dc = delEnd - delStart, ac = addEnd - delEnd, pair = Math.Min(dc, ac);
            for (int k = 0; k < pair; k++)
            {
                var d = ops[delStart + k]; var ad = ops[delEnd + k];
                lines.Add(new Line(Op.Change, d.ai + 1, ad.bi + 1, a[d.ai], b[ad.bi])); changes++;
            }
            for (int k = pair; k < dc; k++) { var d = ops[delStart + k]; lines.Add(new Line(Op.Del, d.ai + 1, 0, a[d.ai], null)); dels++; }
            for (int k = pair; k < ac; k++) { var ad = ops[delEnd + k]; lines.Add(new Line(Op.Add, 0, ad.bi + 1, null, b[ad.bi])); adds++; }
        }

        return new Result(lines, adds, dels, changes, adds == 0 && dels == 0 && changes == 0);
    }

    /// <summary>
    /// The flat Equal/Del/Add stream the row pairing below consumes, from DiffPlex's
    /// linear-space Myers differ. It replaced a hand-written LCS table that cost O(n·m) in
    /// both time and memory — 61MB of ints for two 4,000-line files, and why comparing
    /// anything larger used to be refused outright.
    /// <para>
    /// The lines are joined and re-split rather than handed over directly because every
    /// DiffPlex entry point takes text plus a chunker. Splitting on '\n' inverts the join
    /// exactly, since a line can never contain one, so the differ sees precisely the array
    /// passed in and the line numbers below stay honest.
    /// </para>
    /// </summary>
    private static List<(Op op, int ai, int bi)> BuildOpStream(string[] a, string[] b)
    {
        var diff = Differ.Instance.CreateCustomDiffs(
            string.Join('\n', a), string.Join('\n', b),
            ignoreWhiteSpace: false, ignoreCase: false, LineChunker);

        var ops = new List<(Op op, int ai, int bi)>(a.Length + b.Length);
        int x = 0, y = 0;
        foreach (var block in diff.DiffBlocks)
        {
            // Everything before this block matched on both sides.
            while (x < block.DeleteStartA && y < block.InsertStartB)
            {
                ops.Add((Op.Equal, x, y)); x++; y++;
            }
            for (int k = 0; k < block.DeleteCountA; k++) { ops.Add((Op.Del, x, -1)); x++; }
            for (int k = 0; k < block.InsertCountB; k++) { ops.Add((Op.Add, -1, y)); y++; }
        }
        while (x < a.Length && y < b.Length) { ops.Add((Op.Equal, x, y)); x++; y++; }
        while (x < a.Length) { ops.Add((Op.Del, x, -1)); x++; }
        while (y < b.Length) { ops.Add((Op.Add, -1, y)); y++; }
        return ops;
    }

    private static readonly Func<string, string[]> LineChunker = static text => text.Split('\n');

    /// <summary>Common prefix/suffix lengths of two changed lines, for intra-line
    /// highlighting: the differing middle is <c>[prefix, len-suffix)</c> on each side.</summary>
    public static (int Prefix, int Suffix) IntraSpan(string left, string right)
    {
        int p = 0, max = Math.Min(left.Length, right.Length);
        while (p < max && left[p] == right[p]) p++;
        int s = 0;
        while (s < max - p && left[^(s + 1)] == right[^(s + 1)]) s++;
        return (p, s);
    }
}
