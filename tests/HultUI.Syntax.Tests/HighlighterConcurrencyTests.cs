using System.Diagnostics;
using HultUI.Syntax;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// Tokenizing is meant to run in the background so the UI can keep drawing. It did not: the
/// tokenize held one global lock for its whole duration, and <see cref="SyntaxHighlighter.CanHighlight"/>
/// — which a file list asks once per visible row per frame — took the same lock. So a
/// "background" parse of a large file stalled the very frame that was meant to show the text
/// already read from disk, for as long as the parse ran (~90ms for 500 lines of C#).
/// </summary>
public class HighlighterConcurrencyTests
{
    private static string CSharp(int lines) =>
        string.Join("\n", Enumerable.Range(0, lines)
            .Select(i => $"    var result{i} = Compute(arg{i}, \"payload {i}\", flag: true);"));

    [Fact]
    public void AskingWhetherAFileCanBeHighlightedDoesNotWaitForARunningTokenize()
    {
        // Warm both the registry and the per-extension answer, so what is timed below is a
        // cache hit — the case the UI actually makes, every row of every frame.
        Assert.True(SyntaxHighlighter.CanHighlight("warm.cs"));

        var big = CSharp(3000);
        using var started = new ManualResetEventSlim();
        var tokenizeMs = 0L;
        var tokenize = Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            started.Set();
            SyntaxHighlighter.SpansForText("big.cs", big);
            tokenizeMs = sw.ElapsedMilliseconds;
        });

        started.Wait(TimeSpan.FromSeconds(5));
        Thread.Sleep(15);   // let it get well inside the parse

        // The UI thread's question, many times over, while that parse is running.
        var asked = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++)
            SyntaxHighlighter.CanHighlight($"file{i % 7}.cs");
        asked.Stop();

#pragma warning disable xUnit1031 // the point of the test is to observe the parse while it runs
        tokenize.Wait(TimeSpan.FromSeconds(30));
#pragma warning restore xUnit1031

        // Guard against a machine so fast the parse finished before we asked — that would
        // make the assertion below pass without proving anything.
        Assert.True(tokenizeMs > 20,
            $"the tokenize took only {tokenizeMs}ms, too short for this test to overlap it");
        Assert.True(asked.ElapsedMilliseconds < tokenizeMs / 2,
            $"200 cached CanHighlight calls took {asked.ElapsedMilliseconds}ms while a "
            + $"{tokenizeMs}ms tokenize ran — they are queueing behind it again");
    }

    [Fact]
    public void ConcurrentTokenizesStillProduceTheSameSpans()
    {
        var text = CSharp(200);
        int expected = SyntaxHighlighter.SpansForText("a.cs", text).Count;

        var counts = new int[8];
        Parallel.For(0, counts.Length, i => counts[i] = SyntaxHighlighter.SpansForText($"a{i}.cs", text).Count);

        Assert.All(counts, c => Assert.Equal(expected, c));
    }
}
