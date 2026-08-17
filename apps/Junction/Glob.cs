namespace Junction;

/// <summary>
/// Shell-style wildcard matching, for the search boxes where the thing being searched is
/// a <i>name</i> rather than prose: <c>feature/*</c>, <c>*-hotfix</c>, <c>PC-1?4-*</c>.
///
/// <para>The semantics are git's own — <c>git branch --list &lt;pattern&gt;</c> — so a
/// pattern that works at the command line works here: <c>*</c> stands for any run of
/// characters (including <c>/</c>, so <c>feature/*</c> reaches every depth beneath it),
/// <c>?</c> for exactly one, and the pattern is anchored at both ends, which is what makes
/// <c>main*</c> mean "starts with main" rather than "contains" it. Matching is
/// case-insensitive, like the substring search it stands in for.</para>
/// </summary>
internal static class Glob
{
    /// <summary>Whether a query is a pattern at all. Callers use it to decide between
    /// this and a plain substring search — typing a name should not become a pattern
    /// nobody asked for.</summary>
    public static bool HasWildcard(string? pattern) =>
        pattern is not null && pattern.AsSpan().IndexOfAny('*', '?') >= 0;

    /// <summary>True when <paramref name="text"/> matches <paramref name="pattern"/> end
    /// to end.</summary>
    public static bool IsMatch(string? text, string? pattern) =>
        IsMatch(text.AsSpan(), pattern.AsSpan());

    /// <summary>
    /// The classic two-pointer wildcard match: walk both, and on a mismatch fall back to
    /// the last <c>*</c> and let it swallow one more character. Linear on the patterns
    /// anyone types, and it never recurses — a pathological <c>*a*a*a*</c> costs time, not
    /// stack.
    /// </summary>
    public static bool IsMatch(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern)
    {
        int t = 0, p = 0;
        int star = -1;      // the last '*' seen in the pattern, -1 for none yet
        int resume = 0;     // where in the text that '*' would resume from

        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || Same(pattern[p], text[t])))
            {
                t++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = t;
            }
            else if (star >= 0)
            {
                // Backtrack: the star takes one more character than it did last time.
                p = star + 1;
                t = ++resume;
            }
            else
            {
                return false;
            }
        }

        // Trailing stars may match nothing at all.
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    private static bool Same(char a, char b) =>
        a == b || char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
}
