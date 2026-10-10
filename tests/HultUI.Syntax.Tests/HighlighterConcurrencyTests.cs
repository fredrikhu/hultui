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

    /// <summary>A tokenize nobody wants any more stops within a line of being told, and gives
    /// the gate to the next: Meridian stops a large response's tokenize when a newer response
    /// arrives or its tab closes, and the newer one must not wait for the stale one to finish.</summary>
    [Fact]
    public async Task ACancelledTokenizeStopsAndTheNextOneRuns()
    {
        var big = CSharp(3000);
        Assert.NotNull(SyntaxHighlighter.TokenizeLines("warm.cs", CSharp(10)));

        var whole = Stopwatch.StartNew();
        Assert.NotNull(SyntaxHighlighter.TokenizeLines("big.cs", big, int.MaxValue, CancellationToken.None));
        whole.Stop();

        using var stop = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        var stopped = new Stopwatch();
        var run = Task.Run(() =>
        {
            started.Set();
            return SyntaxHighlighter.TokenizeLines("big.cs", big, int.MaxValue, stop.Token);
        });
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Thread.Sleep(15);   // well inside the tokenize
        stopped.Start();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        stopped.Stop();

        Assert.True(whole.ElapsedMilliseconds > 60,
            $"the whole tokenize took only {whole.ElapsedMilliseconds}ms, too short for this test to cancel inside it");
        Assert.True(stopped.ElapsedMilliseconds < whole.ElapsedMilliseconds / 4,
            $"the tokenize ran on for {stopped.ElapsedMilliseconds}ms after it was cancelled; the whole one takes {whole.ElapsedMilliseconds}ms");

        // The gate is free: the next tokenize runs.
        Assert.NotNull(SyntaxHighlighter.TokenizeLines("after.cs", CSharp(10), int.MaxValue, CancellationToken.None));
        // And one cancelled before it starts never takes it.
        Assert.ThrowsAny<OperationCanceledException>(() =>
            SyntaxHighlighter.TokenizeLines("never.cs", big, int.MaxValue, new CancellationToken(canceled: true)));
    }

    /// <summary>The line cap is the caller's; the two-argument overload keeps the default.</summary>
    [Fact]
    public void TheLineCapIsTheCallers()
    {
        var text = CSharp(30);
        Assert.Null(SyntaxHighlighter.TokenizeLines("a.cs", text, 29, CancellationToken.None));
        Assert.Equal(30, SyntaxHighlighter.TokenizeLines("a.cs", text, 30, CancellationToken.None)!.Length);

        var overDefault = string.Join("\n", Enumerable.Repeat("1,", SyntaxHighlighter.DefaultMaxLines + 1));
        Assert.Null(SyntaxHighlighter.TokenizeLines("a.json", overDefault));
        Assert.Equal(SyntaxHighlighter.DefaultMaxLines + 1,
            SyntaxHighlighter.TokenizeLines("a.json", overDefault, SyntaxHighlighter.DefaultMaxLines + 1, CancellationToken.None)!.Length);
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
