using System.Reflection;
using HultUI.Syntax;
using TextMateSharp.Grammars;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// A tokenize runs on a budget: 250 ms a line, measured by TextMateSharp on the wall clock, and
/// three seconds a text. Both used to make a text's colour depend on how busy the machine was.
/// Meridian's response tests tokenized the same JSON twice and, on a loaded CI runner, compared
/// 7,223 spans with 3,003, and 11,157 with 17,183 of its 26,002.
///
/// <para>Run alone: the line stopped here holds the highlighter's gate for 300 ms, and
/// <see cref="HighlighterConcurrencyTests"/> times tokenizes that would wait for it.</para>
/// </summary>
[Collection(nameof(TokenizeBudgetTests))]
public class TokenizeBudgetTests
{
    private static readonly string Json = "{\n" + string.Join(",\n",
        Enumerable.Range(0, 40).Select(i => $"  \"key{i}\": \"value {i}\", \"n{i}\": {i}")) + "\n}";

    /// <summary>The whole text's colour, on a clock that stands still: no budget runs out.</summary>
    private static IReadOnlyList<HultUI.Core.Rendering.TextSpan>[] Whole() =>
        SyntaxHighlighter.TokenizeLines("a.json", Json, int.MaxValue, CancellationToken.None,
            new SteppingClock(TimeSpan.Zero))!;

    /// <summary>A line stopped part-way hands back the state it stopped in, here inside a key's
    /// quotes, and the lines after it were tokenized from there: keys and values swapped colours
    /// to the end of the text. They start from the state the stopped line started in now.</summary>
    [Fact]
    public void ALineThatRunsOutOfItsBudgetDoesNotRecolourTheLinesAfterIt()
    {
        var whole = Whole();

        var stopped = SyntaxHighlighter.TokenizeLines("a.json", Json, int.MaxValue, CancellationToken.None,
            clock: null, wrap: g => StopsPartWay.Around(g, line: 3, at: 5))!;

        Assert.Equal(whole.Length, stopped.Length);
        for (int i = 4; i < whole.Length; i++)
            Assert.Equal(whole[i], stopped[i]);
    }

    /// <summary>The text's budget runs on the caller's clock: one that has moved four seconds by
    /// the first line leaves every line plain. A line's budget is the system clock's alone, since
    /// TextMateSharp measures it on the wall clock; on any other, a line has none.</summary>
    [Fact]
    public void TheBudgetRunsOnTheCallersClock()
    {
        var late = SyntaxHighlighter.TokenizeLines("a.json", Json, int.MaxValue, CancellationToken.None,
            new SteppingClock(TimeSpan.FromSeconds(4)))!;
        Assert.All(late, line => Assert.Empty(line));

        var limits = new List<TimeSpan>();
        SyntaxHighlighter.TokenizeLines("a.json", Json, int.MaxValue, CancellationToken.None,
            clock: null, wrap: g => StopsPartWay.Around(g, line: -1, at: 0, limits));
        Assert.NotEmpty(limits);
        Assert.All(limits, limit => Assert.Equal(SyntaxHighlighter.LineBudget, limit));

        limits.Clear();
        SyntaxHighlighter.TokenizeLines("a.json", Json, int.MaxValue, CancellationToken.None,
            new SteppingClock(TimeSpan.Zero), wrap: g => StopsPartWay.Around(g, line: -1, at: 0, limits));
        Assert.NotEmpty(limits);
        Assert.All(limits, limit => Assert.Equal(TimeSpan.MaxValue, limit));
    }

    /// <summary>Moves <c>step</c> on every reading.</summary>
    private sealed class SteppingClock(TimeSpan step) : TimeProvider
    {
        private long _now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Add(ref _now, step.Ticks);
    }

    /// <summary>
    /// The grammar, except that line <c>line</c> takes longer than its budget and stops at
    /// character <c>at</c>, as a descheduled worker's does: what comes back is the tokenize of
    /// the line's first <c>at</c> characters, and the state they end in. Records the line
    /// budget each line is given, and hands the grammar none, so that only the line stopped
    /// here stops however busy the machine is.
    /// </summary>
    public class StopsPartWay : DispatchProxy
    {
        private IGrammar _grammar = null!;
        private int _line, _at, _seen;
        private List<TimeSpan>? _limits;

        public static IGrammar Around(IGrammar grammar, int line, int at, List<TimeSpan>? limits = null)
        {
            var proxy = Create<IGrammar, StopsPartWay>();
            var self = (StopsPartWay)(object)proxy;
            (self._grammar, self._line, self._at, self._limits) = (grammar, line, at, limits);
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != nameof(IGrammar.TokenizeLine) || args is not [LineText text, var state, TimeSpan limit])
                return method.Invoke(_grammar, args);

            _limits?.Add(limit);
            if (_seen++ != _line)
                return _grammar.TokenizeLine(text, (IStateStack?)state, TimeSpan.MaxValue);
            Thread.Sleep(SyntaxHighlighter.LineBudget + TimeSpan.FromMilliseconds(50));
            return _grammar.TokenizeLine(new LineText(text.Memory[.._at]), (IStateStack?)state, TimeSpan.MaxValue);
        }
    }
}

[CollectionDefinition(nameof(TokenizeBudgetTests), DisableParallelization = true)]
public class TokenizeBudgetCollection;
