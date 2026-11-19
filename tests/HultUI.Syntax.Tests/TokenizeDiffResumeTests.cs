using System.Reflection;
using System.Runtime.CompilerServices;
using HultUI.Core.Rendering;
using HultUI.Syntax;
using TextMateSharp.Grammars;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// A diff's walk cancelled part-way is kept with its texts, and a call handed the same strings goes
/// on from where it stopped. Junction's reader going through big files at a reading pace cancelled
/// every walk, and each visit to a file walked from its first line again: on 675 KB files, 32 MB a
/// key on all threads that bought nothing (plan 17 round 16).
///
/// <para>What a walk that went on gives must be what one uncancelled walk gives, span for span:
/// every test compares the two. Run alone: one test changes the process-wide theme.</para>
/// </summary>
[Collection(nameof(TokenizeDiffResumeTests))]
public class TokenizeDiffResumeTests
{
    private static string Csharp(string middle, string end) =>
        "using System;\n\n/* opens here\n"
        + string.Concat(Enumerable.Range(0, 599).Select(i => $"   comment {i}\n"))
        + "   " + middle + "\n   closes here */\nclass Program\n{\n    string Text = @\"opens\n"
        + string.Concat(Enumerable.Range(0, 595).Select(i => $"  string {i}\n"))
        + "  " + end + "\n\";\n}\n";

    private const int Changed = 603;    // the first line that differs, inside the block comment opened on line 3
    private const int Last = 1203;      // inside the verbatim string opened on line 607
    private const int Shared = Changed - 1;
    private const int Walk = Shared + 2 * (Last - Shared);   // the shared lines once, then each side's own

    private static readonly LineRange[] Shown = [new(Changed - 3, Changed + 3), new(Last - 3, Last)];

    /// <summary>Two versions of their own: the walks kept are kept with the strings, so each test
    /// makes its own.</summary>
    private static (string Old, string New) Texts() =>
        (Csharp(middle: "the old line", end: "old"), Csharp(middle: "the new line", end: "new"));

    private static TokenizedDiff Tokenize(string? old, string? @new, LineRange[]? shown = null, string path = "a.cs",
        Func<IGrammar, IGrammar>? wrap = null, CancellationToken cancellationToken = default, TimeProvider? clock = null) =>
        SyntaxHighlighter.TokenizeDiff(path, old, shown ?? Shown, @new, shown ?? Shown,
            SyntaxHighlighter.DefaultMaxLines, cancellationToken, clock ?? SteppingClock.Standing, wrap);

    /// <summary>A walk of <paramref name="old"/> and <paramref name="new"/> cancelled after its
    /// <paramref name="lines"/>th line.</summary>
    private static void Cancel(string old, string @new, int lines, LineRange[]? shown = null, string path = "a.cs",
        TimeProvider? clock = null, Action<int>? each = null)
    {
        using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => Tokenize(old, @new, shown, path,
            CancelsAfter.Around(lines, cancel, each), cancel.Token, clock));
    }

    /// <summary>What one walk gives, of copies: other strings, so nothing kept goes on.</summary>
    private static TokenizedDiff Whole(string old, string @new, LineRange[]? shown = null, string path = "a.cs") =>
        Tokenize(new string(old.AsSpan()), new string(@new.AsSpan()), shown, path);

    private static void Same(TokenizedDiff whole, TokenizedDiff resumed)
    {
        Assert.Equal(whole.Old!.Length, resumed.Old!.Length);
        Assert.Equal(whole.New!.Length, resumed.New!.Length);
        for (int i = 0; i < whole.Old.Length; i++) Assert.Equal(whole.Old[i], resumed.Old[i]);
        for (int i = 0; i < whole.New.Length; i++) Assert.Equal(whole.New[i], resumed.New[i]);
    }

    public static TheoryData<int> Stops => new()
    {
        1,                    // the first line
        300,                  // the lines both versions start with
        Shared,               // the last of them: the new version's own are next
        Shared + 1,           // the first of the new version's own
        Shared + 300,         // inside the block comment's end and the verbatim string
        Shared + (Last - Shared),   // the new version's last: the old version's own are next
        Walk - 1,             // one line to go
    };

    [Theory]
    [MemberData(nameof(Stops))]
    public void AWalkCancelledPartWay_GoesOnFromThere_WithTheSpansOfOneWalk(int stop)
    {
        var (old, @new) = Texts();
        var whole = Whole(old, @new);
        Assert.Equal(Walk, whole.LinesWalked);

        Cancel(old, @new, stop);
        var resumed = Tokenize(old, @new);

        Assert.Equal(Walk - stop, resumed.LinesWalked);
        Same(whole, resumed);
        Assert.NotEmpty(resumed.New![Changed - 1]);   // the comment's colour, not plain
        Assert.NotEmpty(resumed.Old![Last - 1]);      // the verbatim string's
    }

    [Fact]
    public void AWalkCancelledTwice_GoesOnFromTheSecondStop()
    {
        var (old, @new) = Texts();

        Cancel(old, @new, 300);
        Cancel(old, @new, 600);
        var resumed = Tokenize(old, @new);

        Assert.Equal(Walk - 900, resumed.LinesWalked);
        Same(Whole(old, @new), resumed);
    }

    /// <summary>A walk's arrays are the caller's once it finishes: nothing goes on with them, and
    /// the next call for the texts walks them all.</summary>
    [Fact]
    public void AFinishedWalk_KeepsNothing()
    {
        var (old, @new) = Texts();
        Cancel(old, @new, 300);
        var first = Tokenize(old, @new);

        var again = Tokenize(old, @new);

        Assert.Equal(Walk, again.LinesWalked);
        Assert.NotSame(first.New, again.New);
        Same(Whole(old, @new), first);
        Same(first, again);
    }

    /// <summary>The same characters in other strings may be another file's: they walk from the
    /// first line. A file changed on disk is read as other strings.</summary>
    [Fact]
    public void OtherStringsOfTheSameText_WalkFromTheStart()
    {
        var (old, @new) = Texts();
        Cancel(old, @new, 900);

        var other = Tokenize(old, new string(@new.AsSpan()));

        Assert.Equal(Walk, other.LinesWalked);
        Same(Whole(old, @new), other);
    }

    [Fact]
    public void AnotherVersion_WalksFromTheStart()
    {
        var (old, @new) = Texts();
        string changed = Csharp(middle: "the newer line", end: "new");
        Cancel(old, @new, 900);

        var other = Tokenize(old, changed);

        Assert.Equal(Walk, other.LinesWalked);
        Same(Whole(old, changed), other);
    }

    [Fact]
    public void OtherLinesShown_WalkFromTheStart()
    {
        var (old, @new) = Texts();
        LineRange[] fewer = [new(Changed - 3, Changed + 3)];
        Cancel(old, @new, 300);

        var other = Tokenize(old, @new, fewer);

        Assert.Equal(Shared + 2 * 4, other.LinesWalked);
        Same(Whole(old, @new, fewer), other);
        // and the walk for all of them is still there
        Assert.Equal(Walk - 300, Tokenize(old, @new).LinesWalked);
    }

    /// <summary>Another grammar: the same texts as JavaScript start again, in its colours.</summary>
    [Fact]
    public void AnotherGrammar_WalksFromTheStart()
    {
        var (old, @new) = Texts();
        Cancel(old, @new, 900);

        var other = Tokenize(old, @new, path: "a.js");

        Assert.Equal(Walk, other.LinesWalked);
        Same(Whole(old, @new, path: "a.js"), other);
    }

    /// <summary>A walk's states and colours are its theme's: after a theme change the texts walk
    /// from the first line, in the new theme's colours.</summary>
    [Fact]
    public void AThemeChange_WalksFromTheStart_InTheNewColours()
    {
        var (old, @new) = Texts();
        var dark = Whole(old, @new);
        Cancel(old, @new, 900);
        try
        {
            Assert.True(SyntaxHighlighter.SetTheme("LightPlus"));
            var light = Tokenize(old, @new);

            Assert.Equal(Walk, light.LinesWalked);
            Same(Whole(old, @new), light);
            Assert.NotEqual(dark.New![Changed - 1], light.New![Changed - 1]);
        }
        finally { SyntaxHighlighter.SetTheme("DarkPlus"); }
    }

    /// <summary>The three seconds a version may take are its walking time: a walk that went on
    /// has what the one before it left, not three seconds more, and not less for the time between
    /// the two calls. Here the first call spends 2.95 s, a minute goes by, and the call that goes
    /// on spends 0.1 s on its first line: the new version's lines after it are past the budget, and
    /// plain, as they are in one walk that spends the same.</summary>
    [Fact]
    public void TheBudget_GoesOnFromWhereTheWalkStopped()
    {
        var (old, @new) = Texts();
        var clock = new ManualClock();
        Cancel(old, @new, Shared + 10, clock: clock, each: line =>
        {
            if (line == Shared + 10) clock.Advance(TimeSpan.FromSeconds(2.95));
        });
        clock.Advance(TimeSpan.FromSeconds(60));

        int seen = 0;
        var resumed = Tokenize(old, @new, clock: clock, wrap: CancelsAfter.Around(int.MaxValue, null, _ =>
        {
            if (++seen == 1) clock.Advance(TimeSpan.FromSeconds(0.1));
        }));

        Assert.All(resumed.New![(Changed + 10)..], line => Assert.Empty(line));
        Assert.NotEmpty(resumed.Old![Last - 1]);   // the old version's own lines have their own budget
    }

    /// <summary>What a cancelled walk keeps goes when its texts do: it holds a string only while
    /// the caller does.</summary>
    [Fact]
    public void AKeptWalk_GoesWithItsTexts()
    {
        var (oldRef, newRef) = CancelOnTextsNobodyKeeps();
        for (int i = 0; i < 3 && (oldRef.IsAlive || newRef.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(newRef.IsAlive, "the walk kept its new text alive");
        Assert.False(oldRef.IsAlive, "the walk kept its old text alive");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Old, WeakReference New) CancelOnTextsNobodyKeeps()
    {
        var (old, @new) = Texts();
        Cancel(old, @new, 900);
        return (new WeakReference(old), new WeakReference(@new));
    }

    /// <summary>The grammar, except that a cancel follows its <c>lines</c>th line (the walk sees it
    /// before the next), and <c>each</c> hears of every line.</summary>
    public class CancelsAfter : DispatchProxy
    {
        private IGrammar _grammar = null!;
        private int _lines, _seen;
        private CancellationTokenSource? _cancel;
        private Action<int>? _each;

        public static Func<IGrammar, IGrammar> Around(int lines, CancellationTokenSource? cancel, Action<int>? each = null) =>
            grammar =>
            {
                var proxy = Create<IGrammar, CancelsAfter>();
                var self = (CancelsAfter)(object)proxy;
                (self._grammar, self._lines, self._cancel, self._each) = (grammar, lines, cancel, each);
                return proxy;
            };

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = method!.Invoke(_grammar, args);
            if (method.Name == nameof(IGrammar.TokenizeLine2))
            {
                _seen++;
                _each?.Invoke(_seen);
                if (_seen == _lines) _cancel?.Cancel();
            }
            return result;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _now;
        public void Advance(TimeSpan by) => _now += by.Ticks;
    }
}

[CollectionDefinition(nameof(TokenizeDiffResumeTests), DisableParallelization = true)]
public class TokenizeDiffResumeCollection;
