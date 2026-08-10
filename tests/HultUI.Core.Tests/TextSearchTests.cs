using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Core.Tests;

/// <summary>
/// The find-in-text model four views share. What is asserted here is the awkward part —
/// zero-length regex matches, a pattern that will not compile, one that will not finish, and
/// tinting that has to survive the syntax colouring underneath it.
/// </summary>
public class TextSearchTests
{
    private static readonly Color Hit = new(255, 200, 0, 60);
    private static readonly Color Current = new(255, 200, 0, 160);

    private static TextSearch Over(params string[] lines)
    {
        var s = new TextSearch(() => { });
        s.Bind(lines.Length, 1, (line, _) => lines[line]);
        return s;
    }

    [Fact]
    public void FindsEveryOccurrence_CaseInsensitiveByDefault()
    {
        var s = Over("Alpha beta", "gamma", "alpha alpha");
        s.SetQuery("alpha");

        Assert.Equal(3, s.Hits.Count);
        Assert.Equal(new[] { 0, 2, 2 }, s.Hits.Select(h => h.Line).ToArray());
        Assert.Equal(new[] { 0, 0, 6 }, s.Hits.Select(h => h.Start).ToArray());
        Assert.Equal(0, s.Index);
    }

    [Fact]
    public void MatchCaseNarrowsIt()
    {
        var s = Over("Alpha", "alpha");
        s.SetQuery("alpha");
        Assert.Equal(2, s.Hits.Count);

        s.SetMatchCase(true);
        Assert.Single(s.Hits);
        Assert.Equal(1, s.Hits[0].Line);
    }

    /// <summary>Overlapping occurrences are not counted twice — "aa" in "aaa" is one match,
    /// not two, or "next" would walk a character at a time over the same run.</summary>
    [Fact]
    public void OverlappingOccurrencesCountOnce()
    {
        var s = Over("aaaa");
        s.SetQuery("aa");

        Assert.Equal(2, s.Hits.Count);
        Assert.Equal(new[] { 0, 2 }, s.Hits.Select(h => h.Start).ToArray());
    }

    [Fact]
    public void MoveWrapsAtBothEnds()
    {
        var s = Over("hit", "no", "hit", "hit");
        s.SetQuery("hit");
        Assert.Equal(0, s.Index);

        s.Move(1); Assert.Equal(1, s.Index);
        s.Move(1); Assert.Equal(2, s.Index);
        s.Move(1); Assert.Equal(0, s.Index);      // past the end, back to the first
        s.Move(-1); Assert.Equal(2, s.Index);     // and before the first, to the last
    }

    [Fact]
    public void NoQueryMeansNoResults()
    {
        var s = Over("alpha");
        s.SetQuery("alpha");
        s.SetQuery("");

        Assert.Empty(s.Hits);
        Assert.Equal(-1, s.Index);
        Assert.Null(s.Error);         // an empty query is not an error, just nothing to find
    }

    // ── Lanes ────────────────────────────────────────────────────────────

    /// <summary>A comparison view has two columns over one set of line numbers. Hits come back
    /// in reading order — line first, then lane — and a lane with no line there is skipped
    /// rather than searched as an empty one.</summary>
    [Fact]
    public void TwoLanesAreSearchedInReadingOrder()
    {
        string?[,] grid =
        {
            { "hit here", "hit there" },
            { null, "hit only right" },
            { "hit only left", null },
        };
        var s = new TextSearch(() => { });
        s.Bind(3, 2, (line, lane) => grid[line, lane]);
        s.SetQuery("hit");

        Assert.Equal(4, s.Hits.Count);
        Assert.Equal(new[] { (0, 0), (0, 1), (1, 1), (2, 0) },
            s.Hits.Select(h => (h.Line, h.Lane)).ToArray());
    }

    [Fact]
    public void HitsOnIsPerLaneNotJustPerLine()
    {
        string?[,] grid = { { "left hit", "right hit hit" } };
        var s = new TextSearch(() => { });
        s.Bind(1, 2, (line, lane) => grid[line, lane]);
        s.SetQuery("hit");

        Assert.Single(s.HitsOn(0, lane: 0)!);
        Assert.Equal(2, s.HitsOn(0, lane: 1)!.Count);
    }

    // ── Regex ────────────────────────────────────────────────────────────

    [Fact]
    public void RegexMatchesPatternsNotLiterals()
    {
        var s = Over("value = 42", "value = abc", "value = 7");
        s.SetQuery(@"\d+");
        Assert.Empty(s.Hits);                 // literal backslash-d appears nowhere

        s.SetRegex(true);
        Assert.Equal(2, s.Hits.Count);
        Assert.Equal(new[] { 0, 2 }, s.Hits.Select(h => h.Line).ToArray());
        Assert.Equal(2, s.Hits[0].Length);    // "42"
        Assert.Equal(1, s.Hits[1].Length);    // "7"
    }

    [Fact]
    public void RegexHonoursMatchCase()
    {
        var s = Over("Alpha", "alpha");
        s.SetRegex(true);
        s.SetQuery("^a");
        Assert.Equal(2, s.Hits.Count);

        s.SetMatchCase(true);
        Assert.Single(s.Hits);
        Assert.Equal(1, s.Hits[0].Line);
    }

    /// <summary>
    /// A pattern that matches the empty string is kept as a navigable match but tints nothing,
    /// and above all does not loop. <c>^\s*$</c> is how anyone finds blank lines, so dropping
    /// zero-length matches outright would lose a search people actually run.
    /// </summary>
    [Fact]
    public void ZeroLengthMatchesAreKeptWithoutLooping()
    {
        var s = Over("alpha", "", "   ", "beta");
        s.SetRegex(true);
        s.SetQuery(@"^\s*$");

        Assert.Equal(2, s.Hits.Count);
        Assert.Equal(new[] { 1, 2 }, s.Hits.Select(h => h.Line).ToArray());
        Assert.Equal(0, s.Hits[0].Length);
        // Nothing to tint, and asking for it must not throw or invent a span.
        Assert.Null(s.Decorate(null, 1, 0, 0, Hit, Current));
    }

    [Fact]
    public void AStarPatternDoesNotMatchForeverAtOnePosition()
    {
        var s = Over("aaa");
        s.SetRegex(true);
        s.SetQuery("a*");

        // One match per position it can start at, not an unbounded run: the scan steps past a
        // zero-length match rather than retrying it.
        Assert.True(s.Hits.Count < TextSearch.MaxHits, "the scan ran to the cap on a 3-character line");
        Assert.False(s.Capped);
    }

    [Fact]
    public void AnUnparseablePatternReportsRatherThanThrows()
    {
        var s = Over("alpha");
        s.SetRegex(true);
        s.SetQuery("([unclosed");

        Assert.Empty(s.Hits);
        Assert.Equal("bad pattern", s.Error);

        s.SetQuery("alpha");
        Assert.Null(s.Error);          // and it recovers on the next keystroke
        Assert.Single(s.Hits);
    }

    /// <summary>
    /// A catastrophically backtracking pattern is abandoned, not endured. This search runs on
    /// the UI thread, so without a bound one typed character could hang the window.
    /// </summary>
    [Fact]
    public void ARunawayPatternGivesUpInsteadOfHangingTheUi()
    {
        var s = Over(new string('a', 40) + "!");
        s.SetRegex(true);
        s.SetQuery("(a+)+$");

        Assert.Equal("pattern too slow", s.Error);
        Assert.Empty(s.Hits);
    }

    /// <summary>The toggle changes what the same query means: as a pattern <c>a+b</c> matches
    /// "aab" and not the literal text "a+b"; switched off it matches exactly the other one.</summary>
    [Fact]
    public void TurningRegexOffTreatsThePatternAsLiteralAgain()
    {
        var s = Over(@"a+b", "aab");
        s.SetRegex(true);
        s.SetQuery(@"a+b");
        Assert.Single(s.Hits);
        Assert.Equal(1, s.Hits[0].Line);

        s.SetRegex(false);
        Assert.Single(s.Hits);
        Assert.Equal(0, s.Hits[0].Line);
    }

    // ── Decoration ───────────────────────────────────────────────────────

    [Fact]
    public void MatchesAreTintedAndTheCurrentOneDiffers()
    {
        var s = Over("needle and needle");
        s.SetQuery("needle");

        var spans = s.Decorate(null, 0, 0, "needle and needle".Length, Hit, Current)!;
        var tinted = spans.Where(x => x.Background != default).ToList();
        Assert.Equal(2, tinted.Count);
        Assert.Equal(Current, tinted[0].Background);   // the first is where the cursor starts
        Assert.Equal(Hit, tinted[1].Background);

        s.Move(1);
        var after = s.Decorate(null, 0, 0, "needle and needle".Length, Hit, Current)!
            .Where(x => x.Background != default).ToList();
        Assert.Equal(Hit, after[0].Background);
        Assert.Equal(Current, after[1].Background);
    }

    /// <summary>The syntax colour under a match survives it. A hit takes a background and
    /// never a foreground, so a matched keyword keeps the colour its grammar gave it.</summary>
    [Fact]
    public void SyntaxColourSurvivesATint()
    {
        var keyword = new Color(200, 120, 255, 255);
        IReadOnlyList<TextSpan> syntax = [new TextSpan(0, 5, keyword, default)];

        var s = Over("class Alpha");
        s.SetQuery("class");

        var spans = s.Decorate(syntax, 0, 0, "class Alpha".Length, Hit, Current)!;
        var over = spans.Single(x => x.Start == 0);
        Assert.Equal(keyword, over.Color);         // still the grammar's colour
        Assert.Equal(Current, over.Background);    // and tinted
    }

    /// <summary>A line with no matches is handed straight back, allocating nothing — which is
    /// almost every line of a file, once per realized row per frame.</summary>
    [Fact]
    public void ALineWithNoMatchesKeepsItsOwnSpans()
    {
        IReadOnlyList<TextSpan> syntax = [new TextSpan(0, 3, new Color(1, 2, 3, 255), default)];
        var s = Over("alpha", "beta");
        s.SetQuery("alpha");

        Assert.Same(syntax, s.Decorate(syntax, 1, 0, "beta".Length, Hit, Current));
    }

    // ── Content changes ──────────────────────────────────────────────────

    [Fact]
    public void RebindingSearchesTheNewContentForTheSameThing()
    {
        var s = Over("alpha", "alpha");
        s.SetQuery("alpha");
        Assert.Equal(2, s.Hits.Count);

        string[] next = ["beta", "alpha", "gamma"];
        s.Bind(next.Length, 1, (line, _) => next[line]);

        Assert.Single(s.Hits);
        Assert.Equal(1, s.Hits[0].Line);
        Assert.Equal("alpha", s.Query);
    }

    [Fact]
    public void UnbindingDropsTheResultsButKeepsTheQuery()
    {
        var s = Over("alpha");
        s.SetQuery("alpha");
        s.Unbind();

        Assert.Empty(s.Hits);
        Assert.Equal(-1, s.Index);
        Assert.Equal("alpha", s.Query);
    }

    [Fact]
    public void TheScanStopsAtTheCapAndSaysSo()
    {
        var lines = Enumerable.Repeat("x", TextSearch.MaxHits + 500).ToArray();
        var s = new TextSearch(() => { });
        s.Bind(lines.Length, 1, (line, _) => lines[line]);
        s.SetQuery("x");

        Assert.Equal(TextSearch.MaxHits, s.Hits.Count);
        Assert.True(s.Capped, "the count was cut without saying so");
    }

    [Fact]
    public void EveryChangeReportsToTheOwner()
    {
        int changes = 0;
        var lines = new[] { "alpha" };
        var s = new TextSearch(() => changes++);
        s.Bind(1, 1, (line, _) => lines[line]);
        Assert.Equal(1, changes);

        s.SetQuery("alpha"); Assert.Equal(2, changes);
        s.SetQuery("alpha"); Assert.Equal(2, changes);   // no change, no repaint
        s.ToggleMatchCase(); Assert.Equal(3, changes);
        s.ToggleRegex(); Assert.Equal(4, changes);
        s.Move(1); Assert.Equal(5, changes);
    }
}
