using System.Reflection;
using System.Runtime.CompilerServices;
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

    /// <summary>What walking a line costs less than colouring it: no spans (10 % of these lines.
    /// bytes; a shown line's tokens are the same encoded ones since plan 17 round 9, where they
    /// used to be token objects with their scope lists, and a walked line saved a fifth). Bytes,
    /// not time: nearly all of the time is the grammar's regular expressions, which a line needs
    /// either way.</summary>
    [Fact]
    public void ALineNobodyShows_IsWalkedForLess()
    {
        Tokenize(null, [], New, [new(1, Lines)]);   // the grammar's first use costs what it costs

        long shown = Bytes(() => Tokenize(null, [], New, [new(1, Lines)]));
        long walked = Bytes(() => Tokenize(null, [], New, [new(Lines, Lines)]));

        Assert.True(walked < shown * 0.95, $"{walked:N0} bytes against {shown:N0}");

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

    // ── The public surface (plan 17 round 11; Junction was a friend assembly until then) ──

    /// <summary>This project sees the library's internals, so only reflection shows that another
    /// diff view can call it: the three are public, immutable, and take and give what their docs
    /// say.</summary>
    [Fact]
    public void TheDiffApi_IsPublic_AsDocumented()
    {
        var method = typeof(SyntaxHighlighter).GetMethod(nameof(SyntaxHighlighter.TokenizeDiff),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(typeof(TokenizedDiff), method.ReturnType);

        var nullability = new NullabilityInfoContext();
        Assert.Equal(
            [
                "string path", "string? oldText", "IReadOnlyList<LineRange> oldShown",
                "string? newText", "IReadOnlyList<LineRange> newShown", "int maxLines",
                "CancellationToken cancellationToken", "TimeProvider? clock = null",
            ],
            method.GetParameters().Select(p =>
                $"{Name(p.ParameterType)}{(nullability.Create(p).ReadState == NullabilityState.Nullable ? "?" : "")} "
                + $"{p.Name}{(p.HasDefaultValue ? $" = {p.DefaultValue ?? "null"}" : "")}"));

        Assert.Equal(["int First", "int Last"], Properties(typeof(LineRange)));
        Assert.Equal(["IReadOnlyList<TextSpan>[]? Old", "IReadOnlyList<TextSpan>[]? New", "int LinesWalked"],
            Properties(typeof(TokenizedDiff)));

        IEnumerable<string> Properties(Type type)
        {
            Assert.True(type.IsPublic, type.Name);
            Assert.True(type.IsValueType && type.IsDefined(typeof(IsReadOnlyAttribute)), $"{type.Name} is a readonly struct");
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.DeclaringType == type && p.GetIndexParameters().Length == 0)
                .Select(p =>
                {
                    // Set only in a constructor or an initializer (`with`), never after.
                    Assert.Contains(typeof(IsExternalInit), p.SetMethod!.ReturnParameter.GetRequiredCustomModifiers());
                    string nullable = nullability.Create(p).ReadState == NullabilityState.Nullable ? "?" : "";
                    return $"{Name(p.PropertyType)}{nullable} {p.Name}";
                });
        }

        static string Name(Type type) =>
            type == typeof(string) ? "string"
            : type == typeof(int) ? "int"
            : type.IsArray ? Name(type.GetElementType()!) + "[]"
            : type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GenericTypeArguments.Select(Name))}>"
            : type.Name;
    }

    [Fact]
    public void TheClock_IsTheSystemsWhenNoneIsGiven()
    {
        var result = SyntaxHighlighter.TokenizeDiff("a.cs", null, [], New, [new(Changed, Changed)],
            SyntaxHighlighter.DefaultMaxLines, default);

        Same(Whole(New), result.New!, [new(Changed, Changed)]);
    }

    public static TheoryData<int[], string> WrongRanges => new()
    {
        { [0, 3], "starts before line 1" },
        { [-2, -1], "starts before line 1" },
        { [5, 4], "ends before it starts" },
        { [10, 12, 3, 4], "does not start after range 0, which ends at line 12" },
        { [3, 6, 6, 8], "does not start after range 0, which ends at line 6" },
        { [1, 1, 2, 9, 4, 5], "does not start after range 1, which ends at line 9" },
    };

    /// <summary>A side's last line is read from its last range, and the ranges are stepped through
    /// once in order, so ranges out of order or overlapping would colour the wrong lines or stop
    /// early. They throw instead, for either version, with or without its text.</summary>
    [Theory]
    [MemberData(nameof(WrongRanges))]
    public void LinesShownOutOfOrder_OrNotNumberedFromOne_Throw(int[] pairs, string why)
    {
        LineRange[] wrong = [.. pairs.Chunk(2).Select(p => new LineRange(p[0], p[1]))];

        var old = Assert.Throws<ArgumentException>(() =>
            SyntaxHighlighter.TokenizeDiff("a.cs", Old, wrong, New, [new(1, 1)], SyntaxHighlighter.DefaultMaxLines, default));
        Assert.Equal("oldShown", old.ParamName);
        Assert.Contains(why, old.Message);

        var @new = Assert.Throws<ArgumentException>(() =>
            SyntaxHighlighter.TokenizeDiff("a.cs", Old, [new(1, 1)], null, wrong, SyntaxHighlighter.DefaultMaxLines, default));
        Assert.Equal("newShown", @new.ParamName);
        Assert.Contains(why, @new.Message);
    }

    [Fact]
    public void NoListOfLinesShown_Throws()
    {
        var thrown = Assert.Throws<ArgumentNullException>(() =>
            SyntaxHighlighter.TokenizeDiff("a.cs", Old, null!, New, [], SyntaxHighlighter.DefaultMaxLines, default));
        Assert.Equal("oldShown", thrown.ParamName);
    }

    [Fact]
    public void RangesThatTouch_AreOneRangeOfLines()
    {
        var split = Tokenize(Old, [new(Changed - 3, Changed), new(Changed + 1, Changed + 3)], New, []);
        var joined = Tokenize(Old, [new(Changed - 3, Changed + 3)], New, []);

        Assert.Equal(joined.Old!.Length, split.Old!.Length);
        for (int i = 0; i < joined.Old.Length; i++) Assert.Equal(joined.Old[i], split.Old[i]);
    }
}
