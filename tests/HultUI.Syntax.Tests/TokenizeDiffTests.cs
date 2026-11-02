using HultUI.Core.Rendering;
using HultUI.Syntax;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// Two versions of a file tokenized for a diff: a line shown has the spans a whole tokenize gives
/// it, and the work stops at the last line shown. <c>LinesWalked</c> is the win: History's
/// prefetch tokenized both whole files of every commit passed, about 10 MB a key on a worker,
/// to show the few lines of a diff.
/// </summary>
public class TokenizeDiffTests
{
    private static readonly string Old = Csharp(middle: "the old line", end: "old");
    private static readonly string New = Csharp(middle: "the new line", end: "new");

    private const int Lines = 1205;
    private const int Changed = 603;    // inside the block comment opened on line 3
    private const int Last = 1203;      // inside the verbatim string opened on line 607

    /// <summary>A block comment open across the first change, and a verbatim string open across
    /// the second: neither line can be coloured without the lines above it.</summary>
    private static string Csharp(string middle, string end) =>
        "using System;\n\n/* opens here\n"
        + string.Concat(Enumerable.Range(0, 599).Select(i => $"   comment {i}\n"))
        + "   " + middle + "\n   closes here */\nclass Program\n{\n    string Text = @\"opens\n"
        + string.Concat(Enumerable.Range(0, 595).Select(i => $"  string {i}\n"))
        + "  " + end + "\n\";\n}\n";

    private static TokenizedDiff Tokenize(string? old, LineRange[] oldShown, string? @new, LineRange[] newShown,
        int maxLines = SyntaxHighlighter.DefaultMaxLines, CancellationToken cancellationToken = default,
        string path = "a.cs") =>
        SyntaxHighlighter.TokenizeDiff(path, old, oldShown, @new, newShown, maxLines, cancellationToken,
            SteppingClock.Standing);

    private static IReadOnlyList<TextSpan>[] Whole(string text) =>
        SyntaxHighlighter.TokenizeLines("a.cs", text, SyntaxHighlighter.DefaultMaxLines, default, SteppingClock.Standing)!;

    private static void Same(IReadOnlyList<TextSpan>[] whole, IReadOnlyList<TextSpan>[] partial, LineRange[] shown)
    {
        foreach (var range in shown)
            for (int line = range.First; line <= range.Last && line <= whole.Length; line++)
                Assert.Equal(whole[line - 1], partial[line - 1]);
    }

    [Fact]
    public void ALineShown_HasTheSpansOfAWholeTokenize_WithWhatIsOpenAboveIt()
    {
        LineRange[] shown = [new(Changed - 3, Changed + 3), new(Last - 3, Last)];
        var result = Tokenize(Old, shown, New, shown);

        Same(Whole(Old), result.Old!, shown);
        Same(Whole(New), result.New!, shown);
        Assert.NotEmpty(result.New![Changed - 1]);   // the comment's colour, not plain
        Assert.Single(result.New[Changed - 1]);
    }

    [Fact]
    public void EachSide_StopsAtItsLastLineShown()
    {
        var result = Tokenize(Old, [new(Changed, Changed)], New, [new(Changed + 4, Changed + 4)]);

        Assert.Equal(Changed, result.Old!.Length);
        Assert.Equal(Changed + 4, result.New!.Length);
    }

    [Fact]
    public void TheLinesBothVersionsStartWith_AreWalkedOnce()
    {
        var result = Tokenize(Old, [new(Changed, Changed)], New, [new(Changed, Changed)]);

        // Line 603 is the first that differs: 602 shared, then one line of each.
        Assert.Equal(Changed + 1, result.LinesWalked);
    }

    [Fact]
    public void WhatTheVersionsShare_IsShownOnBothSides()
    {
        LineRange[] shown = [new(10, 12)];
        var result = Tokenize(Old, shown, New, shown);

        Same(Whole(Old), result.Old!, shown);
        Same(Whole(New), result.New!, shown);
        Assert.Equal(12, result.LinesWalked);
    }

    [Fact]
    public void TwoVersionsWithNothingInCommon_WalkAlone()
    {
        string other = "\n" + Old;
        LineRange[] shown = [new(20, 20)];
        var result = Tokenize(other, shown, New, shown);

        Assert.Equal(40, result.LinesWalked);
        Same(Whole(other), result.Old!, shown);
        Same(Whole(New), result.New!, shown);
    }

    [Fact]
    public void ALineNobodyShows_KeepsNoSpans()
    {
        var result = Tokenize(null, [], New, [new(Changed, Changed)]);

        Assert.All(result.New!.Take(Changed - 1), spans => Assert.Empty(spans));
        Assert.NotEmpty(result.New![Changed - 1]);
    }

    /// <summary>What walking a line costs less than colouring it: no token's scope list, no
    /// theme match, no spans. Bytes, not time: nearly all of the time is the grammar's regular
    /// expressions, which a line needs either way.</summary>
    [Fact]
    public void ALineNobodyShows_IsWalkedForLess()
    {
        Tokenize(null, [], New, [new(1, Lines)]);   // the grammar's first use costs what it costs

        long shown = Bytes(() => Tokenize(null, [], New, [new(1, Lines)]));
        long walked = Bytes(() => Tokenize(null, [], New, [new(Lines, Lines)]));

        Assert.True(walked < shown * 0.9, $"{walked:N0} bytes against {shown:N0}");

        static long Bytes(Action tokenize)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            tokenize();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    [Fact]
    public void ASideWithNothingShown_IsNotWalked()
    {
        var result = Tokenize(Old, [], New, [new(Changed, Changed)]);

        Assert.Null(result.Old);
        Assert.Equal(Changed, result.New!.Length);
        Assert.Equal(Changed, result.LinesWalked);
    }

    [Fact]
    public void ASideWithNoText_IsNull()
    {
        var result = Tokenize(null, [new(1, 1)], New, [new(1, 1)]);

        Assert.Null(result.Old);
        Assert.Equal(1, result.LinesWalked);
        Assert.Equal(Whole(New)[0], result.New![0]);
    }

    [Fact]
    public void AVersionWithNoNewSide_IsColoured()
    {
        LineRange[] shown = [new(Changed, Changed)];
        var result = Tokenize(Old, shown, null, []);

        Assert.Null(result.New);
        Same(Whole(Old), result.Old!, shown);
        Assert.NotEmpty(result.Old![Changed - 1]);
    }

    [Fact]
    public void LinesPastTheEndOfTheText_AreNotThere()
    {
        var result = Tokenize(Old, [new(Lines + 5, Lines + 9)], New, [new(1, Lines + 9)]);

        Assert.Equal(Lines, result.Old!.Length);
        Assert.Equal(Lines, result.New!.Length);
    }

    [Fact]
    public void ATextWhoseLinesEndInCarriageReturns_HasTheSpansOfAWholeTokenize()
    {
        string crlf = Old.Replace("\n", "\r\n");
        LineRange[] shown = [new(Changed, Changed)];
        var result = Tokenize(crlf, shown, New, shown);

        Same(Whole(crlf), result.Old!, shown);
        Same(Whole(New), result.New!, shown);
        Assert.Equal(Changed * 2, result.LinesWalked);   // not one line in common
    }

    [Fact]
    public void ATextTooLongForAWholeTokenize_GetsNoneHereEither()
    {
        Assert.Null(SyntaxHighlighter.TokenizeLines("a.cs", Old, Lines - 1, default, SteppingClock.Standing));

        var result = Tokenize(Old, [new(1, 1)], New, [new(1, 1)], maxLines: Lines - 1);

        Assert.Null(result.Old);
        Assert.Null(result.New);
        Assert.Equal(0, result.LinesWalked);
    }

    [Fact]
    public void ATextOfTwoMillionCharacters_GetsNoneHereEither()
    {
        string huge = new string('/', 2_000_001);
        Assert.Null(SyntaxHighlighter.TokenizeLines("a.cs", huge));

        Assert.Null(Tokenize(huge, [new(1, 1)], "class A { }\n", [new(1, 1)]).Old);
    }

    [Fact]
    public void AFileWithNoGrammar_HasNoSpans()
    {
        var result = Tokenize(Old, [new(1, 1)], New, [new(1, 1)], path: "a.unknown");

        Assert.Null(result.Old);
        Assert.Null(result.New);
    }

    [Fact]
    public void ACancelledTokenize_Stops()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            Tokenize(Old, [new(1, 1)], New, [new(1, 1)], cancellationToken: cancel.Token));
    }
}
