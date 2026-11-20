using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HultUI.Core.Rendering;
using HultUI.Syntax;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Rules;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// The old version's walk takes the new version's lines where both have the same text and start in
/// the same state, instead of tokenizing them again: a diff of a few hunks shares the stretches
/// between them, and the old version used to walk each again (bigrepo's 675 KB file: 13,626 lines
/// walked, 7,453 with the take; plan 17 round 17). Every test compares each shown line with a whole
/// tokenize of its own version: what is taken must be what a walk would have made.
/// </summary>
public class TokenizeDiffSharedLinesTests
{
    private const int Lines = 1000;

    /// <summary>A class of one field a line (lines 5 to 999), with <paramref name="change"/>'s
    /// lines in place of the fields it names.</summary>
    private static string Code(Func<int, string?> change)
    {
        var text = new StringBuilder("using System;\n\nclass Program\n{\n");
        for (int line = 5; line < Lines; line++) text.Append(change(line) ?? $"    int Field{line} = {line};").Append('\n');
        return text.Append("}\n").ToString();
    }

    private static TokenizedDiff Tokenize(string old, string @new, LineRange[] shown, string path = "a.cs",
        Func<IGrammar, IGrammar>? wrap = null, CancellationToken cancellationToken = default) =>
        SyntaxHighlighter.TokenizeDiff(path, old, shown, @new, shown, SyntaxHighlighter.DefaultMaxLines,
            cancellationToken, SteppingClock.Standing, wrap);

    private static IReadOnlyList<TextSpan>[] Whole(string text, string path = "a.cs") =>
        SyntaxHighlighter.TokenizeLines(path, text, SyntaxHighlighter.DefaultMaxLines, default, SteppingClock.Standing)!;

    /// <summary>Each shown line of each side has the spans a whole tokenize of its version gives it.</summary>
    private static void AsWalked(string old, string @new, TokenizedDiff diff, LineRange[] shown, string path = "a.cs")
    {
        var (wholeOld, wholeNew) = (Whole(old, path), Whole(@new, path));
        foreach (var range in shown)
            for (int line = range.First; line <= range.Last; line++)
            {
                Assert.True(wholeOld[line - 1].SequenceEqual(diff.Old![line - 1]), $"old line {line}");
                Assert.True(wholeNew[line - 1].SequenceEqual(diff.New![line - 1]), $"new line {line}");
            }
    }

    private static readonly LineRange[] TwoHunks = [new(97, 103), new(597, 603)];

    private static (string Old, string New) FieldsChangedAt(params int[] lines) =>
        (Code(line => lines.Contains(line) ? $"    int Changed{line} = 1;" : null),
         Code(line => lines.Contains(line) ? $"    int Changed{line} = 2;" : null));

    [Fact]
    public void AMultiHunkDiff_WalksTheLinesTheVersionsShareOnce()
    {
        var (old, @new) = FieldsChangedAt(100, 600);

        var diff = Tokenize(old, @new, TwoHunks);

        // 99 lines both start with; the new version's 100 to 603; of the old version's, only the
        // two that differ. Walked again, the old version's would be 504.
        Assert.Equal(99 + 504 + 2, diff.LinesWalked);
        AsWalked(old, @new, diff, TwoHunks);
        Assert.NotEmpty(diff.Old![100]);   // the field's keyword and number, taken
    }

    /// <summary>A line starting in another state is walked, whatever its text: the old version opens
    /// a block comment at line 100 and closes it at 300, so its lines in between are comment and the
    /// new version's are code. From 301 on the states meet again, and the old version takes.</summary>
    [Fact]
    public void ALineStartingInAnotherState_IsWalked()
    {
        string old = Code(line => line == 100 ? "    /* opened" : line == 300 ? "    */" : null);
        string @new = Code(line => line == 100 ? "    int Changed = 2;" : line == 300 ? "    */" : null);
        LineRange[] shown = [new(97, 103), new(148, 152), new(297, 303), new(597, 603)];

        var diff = Tokenize(old, @new, shown);

        Assert.Equal(99 + 504 + 201, diff.LinesWalked);
        AsWalked(old, @new, diff, shown);
        Assert.False(diff.Old![149].SequenceEqual(diff.New![149]), "line 150 is a comment on one side only");
    }

    /// <summary>A state that differs only in what its rule's begin captured is another state: a
    /// fence of four backticks is not closed by three. The lines inside both fences have the same
    /// text and the same rule open, but the old version's fence ends at line 7, and its heading and
    /// inline code after it are coloured as Markdown, the new version's as the fence's text.</summary>
    [Fact]
    public void AStateThatDiffersOnlyInWhatItsBeginCaptured_IsWalked()
    {
        static string Markdown(string fence) =>
            "# Title\n\nSome text.\n\n" + fence + "\nint x = 1;\n```\n\n## After\n\nSome `code` here.\n"
            + string.Concat(Enumerable.Range(0, 40).Select(i => $"Line {i} with `code {i}`.\n"));
        string old = Markdown("```"), @new = Markdown("````");
        LineRange[] shown = [new(4, 12), new(40, 44)];

        var diff = Tokenize(old, @new, shown, path: "a.md");

        AsWalked(old, @new, diff, shown, path: "a.md");
        Assert.False(diff.Old![8].SequenceEqual(diff.New![8]), "the heading after the fence");
        Assert.Equal(4 + 2 * (44 - 4), diff.LinesWalked);   // the old version takes none of the new's
    }

    /// <summary>TextMateSharp's <c>Equals</c> leaves out whether a rule's begin matched to the end of
    /// its line, which decides whether <c>\G</c> matches at the start of the next; the take compares
    /// it. Also pins the internal member it reads.</summary>
    [Fact]
    public void TwoStatesThatDifferInWhereTheirBeginEnded_AreNotTheSame()
    {
        var ctor = typeof(StateStack).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 8);
        StateStack State(StateStack? parent, bool beginCapturedEol) =>
            (StateStack)ctor.Invoke([parent, RuleId.Of(7), -1, -1, beginCapturedEol, "\\)", null, null]);
        var root = State(null, false);

        var (a, b, c) = (State(State(root, true), false), State(State(root, false), false), State(State(root, true), false));

        Assert.True(a.Equals((object)b));   // TextMateSharp's comparison
        Assert.False(SyntaxHighlighter.SameState(a, b));
        Assert.True(SyntaxHighlighter.SameState(a, c));
    }

    /// <summary>States of another kind are compared by their own <c>Equals</c>: here only by
    /// reference, and they say nothing of the rules open. The old version takes a line only where
    /// its state is the very one the new version's line started in; TextMateSharp hands back the
    /// state a line started in when the line opens and closes nothing, and a closed comment's
    /// parent, so here that is every line the full comparison takes.</summary>
    [Fact]
    public void StatesThatCompareByReference_AreTakenOnlyWhenTheyAreTheSameState()
    {
        string old = Code(line => line == 100 ? "    /* opened" : line == 300 ? "    */" : null);
        string @new = Code(line => line == 100 ? "    int Changed = 2;" : line == 300 ? "    */" : null);
        LineRange[] shown = [new(97, 103), new(148, 152), new(297, 303), new(597, 603)];

        var diff = Tokenize(old, @new, shown, wrap: ByReference.Around);

        AsWalked(old, @new, diff, shown);
        Assert.Equal(99 + 504 + 201, diff.LinesWalked);
    }

    public static TheoryData<int> Stops => new()
    {
        50,     // the lines both versions start with
        300,    // the new version's own
        603,    // its last: the old version's own are next
        604,    // the first of the old version's own that differs
        605,    // the second
    };

    /// <summary>A walk cancelled part-way goes on with what the new version's lines left, kept with
    /// it, and gives the spans of one walk.</summary>
    [Theory]
    [MemberData(nameof(Stops))]
    public void AWalkCancelledPartWay_GoesOnTakingTheSharedLines(int stop)
    {
        var (old, @new) = FieldsChangedAt(100, 400, 600);
        const int walk = 99 + 504 + 3;
        Assert.Equal(walk, Tokenize(new string(old.AsSpan()), new string(@new.AsSpan()), TwoHunks).LinesWalked);

        using (var cancel = new CancellationTokenSource())
            Assert.Throws<OperationCanceledException>(() =>
                Tokenize(old, @new, TwoHunks, wrap: TokenizeDiffResumeTests.CancelsAfter.Around(stop, cancel), cancellationToken: cancel.Token));
        var resumed = Tokenize(old, @new, TwoHunks);

        Assert.Equal(walk - stop, resumed.LinesWalked);
        AsWalked(old, @new, resumed, TwoHunks);
    }

    /// <summary>The grammar, with each state it hands out wrapped in one that compares by reference
    /// and shows no rule: the same state of the grammar gets the same wrapper.</summary>
    public class ByReference : DispatchProxy
    {
        private IGrammar _grammar = null!;
        private readonly ConditionalWeakTable<IStateStack, Ref> _refs = new();

        public static IGrammar Around(IGrammar grammar)
        {
            var proxy = Create<IGrammar, ByReference>();
            ((ByReference)(object)proxy)._grammar = grammar;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IGrammar.TokenizeLine2) && args!.Length == 3)
            {
                var result = _grammar.TokenizeLine2((LineText)args[0]!, (args[1] as Ref)?.Inner, (TimeSpan)args[2]!);
                return new Result(result.Tokens, _refs.GetValue(result.RuleStack, inner => new Ref(inner)));
            }
            return method.Invoke(_grammar, args);
        }

        private sealed class Ref(IStateStack inner) : IStateStack
        {
            public IStateStack Inner => inner;
            public int Depth => 0;
            public RuleId RuleId => null!;
            public string EndRule => null!;
        }

        private sealed class Result(int[] tokens, IStateStack ruleStack) : ITokenizeLineResult2
        {
            public int[] Tokens => tokens;
            public IStateStack RuleStack => ruleStack;
        }
    }
}
