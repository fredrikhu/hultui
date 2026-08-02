namespace VesperCommander;

/// <summary>Line-level diff for the F3 builtin view. Produces aligned side-by-side rows
/// (each carries the left and/or right line), pairing adjacent delete+insert runs into
/// "change" rows so edits line up. Pure and unit-testable; the view handles collapse and
/// painting. An LCS backtrack — fine for source files, guarded against huge inputs.</summary>
public static class DiffEngine
{
    public enum Op { Equal, Add, Del, Change }

    /// <summary>One aligned row. <see cref="LeftNo"/>/<see cref="RightNo"/> are 1-based line
    /// numbers, or 0 when that side is absent (a pure add/delete).</summary>
    public sealed record Line(Op Op, int LeftNo, int RightNo, string? Left, string? Right);

    public sealed record Result(IReadOnlyList<Line> Lines, int Adds, int Dels, int Changes, bool Identical);

    /// <summary>Above this many lines on a side, the O(n·m) table is skipped — the caller
    /// shows the "too large" state instead.</summary>
    public const int MaxLines = 4000;

    public static bool TooLarge(string[] a, string[] b) => (long)a.Length * b.Length > (long)MaxLines * MaxLines;

    public static Result Diff(string[] a, string[] b)
    {
        int n = a.Length, m = b.Length;

        // LCS length table, filled from the bottom-right.
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        // Backtrack into a flat op stream (Equal / Del / Add).
        var ops = new List<(Op op, int ai, int bi)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { ops.Add((Op.Equal, x, y)); x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) { ops.Add((Op.Del, x, -1)); x++; }
            else { ops.Add((Op.Add, -1, y)); y++; }
        }
        while (x < n) { ops.Add((Op.Del, x, -1)); x++; }
        while (y < m) { ops.Add((Op.Add, -1, y)); y++; }

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
