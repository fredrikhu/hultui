namespace HultUI.Core.Builders;

/// <summary>
/// Subsequence matching, the command-palette convention: the query's letters in order,
/// anywhere, case ignored — "gtr" finds "Go to Runner".
/// </summary>
public static class FuzzyMatch
{
    /// <summary>
    /// How well <paramref name="needle"/> matches <paramref name="haystack"/>: lower is better,
    /// -1 is no match. The score is the span the match needed, so a contiguous run beats letters
    /// scattered over the text, and an earlier start breaks a tie. Spaces in the needle are
    /// skipped; an empty needle matches nothing.
    /// </summary>
    public static int Score(string haystack, string needle)
    {
        int first = -1, i = 0;
        foreach (char c in needle)
        {
            if (c == ' ') continue;
            int found = haystack.AsSpan(i).IndexOf([c], StringComparison.OrdinalIgnoreCase);
            if (found < 0) return -1;
            found += i;
            if (first < 0) first = found;
            i = found + 1;
        }
        return first < 0 ? -1 : (i - first) * 100 + first;
    }
}
