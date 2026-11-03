using HultUI.Core.Rendering;
using HultUI.Syntax;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// Tokenizing one line costs the square of its length: with no budget, as on a headless clock,
/// a 32,000-character line took 17 s of JavaScript and 27 s of C#, and on the window's clock it
/// spent its whole 250 ms budget and got less colour the longer it was (173 characters of 32,000).
/// A line longer than <see cref="SyntaxHighlighter.LongLine"/> characters is coloured on those
/// first characters only now, and leaves the state it started in, as a line stopped at its
/// budget does.
/// </summary>
public class LongLineTests
{
    private static IReadOnlyList<TextSpan>[] Tokenize(string path, string text) =>
        SyntaxHighlighter.TokenizeLines(path, text, SyntaxHighlighter.DefaultMaxLines, default, SteppingClock.Standing)!;

    /// <summary>Strings and punctuation all the way along, as a minified bundle's line.</summary>
    private static string Dense(int chars)
    {
        var line = new System.Text.StringBuilder("var a = [");
        for (int i = 0; line.Length < chars; i++) line.Append('"').Append(i).Append("\",");
        return line.Append("];").ToString();
    }

    [Fact]
    public void ALongLine_IsColouredOnItsFirstCharacters_AndTheRestIsPlain()
    {
        string line = Dense(6_000);

        var spans = Tokenize("a.js", line)[0];

        Assert.Equal(Tokenize("a.js", line[..SyntaxHighlighter.LongLine])[0], spans);
        Assert.InRange(spans[^1].End, SyntaxHighlighter.LongLine - 8, SyntaxHighlighter.LongLine);
    }

    /// <summary>JSON tokenizes about 17 times cheaper than JavaScript: 8,000 of its characters
    /// cost what 2,000 of JavaScript's do.</summary>
    [Fact]
    public void ALongJsonLine_IsColouredFurther()
    {
        var line = new System.Text.StringBuilder("{");
        for (int i = 0; line.Length < 12_000; i++) line.Append("\"k").Append(i).Append("\":").Append(i).Append(',');
        string json = line.Append("\"z\":0}").ToString();

        var spans = Tokenize("a.json", json)[0];

        Assert.Equal(Tokenize("a.json", json[..SyntaxHighlighter.LongDataLine])[0], spans);
        Assert.InRange(spans[^1].End, SyntaxHighlighter.LongDataLine - 8, SyntaxHighlighter.LongDataLine);
    }

    /// <summary>A comment opened in the coloured part and left open: the next line starts from
    /// the state the long line started in, so it is code, not comment.</summary>
    [Fact]
    public void TheLineAfterALongLine_StartsFromTheStateTheLongLineStartedIn()
    {
        string code = "var b = 1;";
        var spans = Tokenize("a.js", "/* " + new string('x', 3_000) + "\n" + code);

        Assert.Equal(Tokenize("a.js", code)[0], spans[1]);
    }

    /// <summary>A line of exactly the limit is a whole line, and a comment it leaves open runs on.</summary>
    [Fact]
    public void ALineOfTheLimit_IsWhole()
    {
        string line = "var a = 1;" + new string(' ', SyntaxHighlighter.LongLine - 12) + "/*";
        var spans = Tokenize("a.js", line + "\nvar b = 1;");

        Assert.Equal(SyntaxHighlighter.LongLine, line.Length);
        Assert.Equal(spans[0][^1].Color, Assert.Single(spans[1]).Color);
    }

    /// <summary>The limit falls between the two halves of an emoji: the line is cut before it.</summary>
    [Fact]
    public void ACutNeverSplitsASurrogatePair()
    {
        string head = "var s = \"" + new string('x', SyntaxHighlighter.LongLine - 10);
        string line = head + "\U0001F600" + new string('y', 500) + "\";";
        Assert.True(char.IsHighSurrogate(line[SyntaxHighlighter.LongLine - 1]));

        Assert.Equal(Tokenize("a.js", head)[0], Tokenize("a.js", line)[0]);
    }

    /// <summary>A long line a diff walks only for its state leaves the state it started in, so
    /// it needs no tokenize at all: the walk costs what it would with a short line there, and
    /// the lines shown below it are coloured as a whole tokenize colours them.</summary>
    [Fact]
    public void ALongLineNobodyShows_IsNotTokenized()
    {
        static string Text(string line) => "/* c */\n" + line + "\nvar b = 1; // after\n";
        LineRange[] shown = [new(3, 3)];

        string comment = Text("/* " + new string('x', 3_000));
        Assert.Equal(Tokenize("a.js", comment)[2], Diff(comment).New![2]);

        string dense = Text(Dense(6_000));
        Diff(Text("x"));
        long @long = Bytes(() => Diff(dense)), @short = Bytes(() => Diff(Text("x")));
        Assert.True(@long < @short + 100_000, $"{@long:N0} bytes against {@short:N0}");

        TokenizedDiff Diff(string t) => SyntaxHighlighter.TokenizeDiff("a.js", null, [], t, shown,
            SyntaxHighlighter.DefaultMaxLines, default, SteppingClock.Standing);

        static long Bytes(Action tokenize)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            tokenize();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
