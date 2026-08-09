namespace Junction.Git;

/// <summary>
/// Word-level diff between a paired deletion/addition line: the character
/// ranges on each side that actually changed, so the diff viewer can highlight
/// tokens instead of whole lines. Tokens are word runs ([A-Za-z0-9_]+),
/// whitespace runs, or single punctuation chars; matching is longest common
/// subsequence over tokens (O(n·m), capped for pathological lines).
/// </summary>
public static class IntralineDiff
{
    private const int MaxTokens = 256;
    /// <summary>Unchanged gaps this short get absorbed into the surrounding
    /// highlight so "a,b" → "x,y" reads as one range, not two slivers.</summary>
    private const int MergeGap = 2;

    /// <summary>Returns the changed ranges for each side (start, end) in char
    /// offsets, or null for a side with no changes. Both null when the lines
    /// are identical, too token-heavy, or share no non-whitespace token (then
    /// whole-line coloring reads better than highlighting everything).</summary>
    public static (IReadOnlyList<(int Start, int End)>? Old, IReadOnlyList<(int Start, int End)>? New)
        Compute(string oldText, string newText)
    {
        if (oldText == newText) return (null, null);
        var a = Tokenize(oldText);
        var b = Tokenize(newText);
        if (a.Count == 0 || b.Count == 0) return (null, null);
        if (a.Count > MaxTokens || b.Count > MaxTokens) return (null, null);

        var (aMatched, bMatched) = LcsMatch(oldText, a, newText, b);

        bool anchored = false;
        for (int i = 0; i < a.Count && !anchored; i++)
            anchored = aMatched[i] && !IsWhitespaceToken(oldText, a[i]);
        if (!anchored) return (null, null);

        return (ChangedRanges(a, aMatched), ChangedRanges(b, bMatched));
    }

    private static List<(int Start, int Length)> Tokenize(string text)
    {
        var tokens = new List<(int, int)>();
        int i = 0;
        while (i < text.Length)
        {
            int start = i;
            char c = text[i];
            if (IsWordChar(c))
                while (i < text.Length && IsWordChar(text[i])) i++;
            else if (char.IsWhiteSpace(c))
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            else
                i++;
            tokens.Add((start, i - start));
        }
        return tokens;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsWhitespaceToken(string text, (int Start, int Length) t) =>
        char.IsWhiteSpace(text[t.Start]);

    private static (bool[] A, bool[] B) LcsMatch(
        string aText, List<(int Start, int Length)> a,
        string bText, List<(int Start, int Length)> b)
    {
        int n = a.Count, m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = TokenEquals(aText, a[i], bText, b[j])
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var aMatched = new bool[n];
        var bMatched = new bool[m];
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (TokenEquals(aText, a[x], bText, b[y]))
            {
                aMatched[x++] = true;
                bMatched[y++] = true;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1]) x++;
            else y++;
        }
        return (aMatched, bMatched);
    }

    private static bool TokenEquals(string aText, (int Start, int Length) ta,
        string bText, (int Start, int Length) tb) =>
        ta.Length == tb.Length
        && aText.AsSpan(ta.Start, ta.Length).SequenceEqual(bText.AsSpan(tb.Start, tb.Length));

    private static IReadOnlyList<(int Start, int End)>? ChangedRanges(
        List<(int Start, int Length)> tokens, bool[] matched)
    {
        List<(int Start, int End)>? ranges = null;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (matched[i]) continue;
            int start = tokens[i].Start;
            int end = start + tokens[i].Length;
            while (i + 1 < tokens.Count && !matched[i + 1])
                end = tokens[++i].Start + tokens[i].Length;

            ranges ??= new List<(int, int)>();
            if (ranges.Count > 0 && start - ranges[^1].End <= MergeGap)
                ranges[^1] = (ranges[^1].Start, end);
            else
                ranges.Add((start, end));
        }
        return ranges;
    }
}
