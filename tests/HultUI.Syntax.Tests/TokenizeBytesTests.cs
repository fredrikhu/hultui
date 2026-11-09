using System.Text;
using HultUI.Syntax;
using TextMateSharp.Grammars;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// Nearly all of a tokenize's bytes are TextMateSharp's own: its tokens, scope lists, state stacks
/// and regular-expression results. What the highlighter adds on top is the spans it returns and,
/// until plan 17 round 9, a theme match on every token: Theme.Match builds a new list of rules
/// each call, 58-75 bytes a token, 12 % of an XML response's tokenize. A token's colour depends on
/// its scope names alone, and a text has few different lists of them, so each is matched once.
/// </summary>
public class TokenizeBytesTests
{
    /// <summary>An XML response as Meridian's pretty view shows it: groups of orders, one element
    /// or attribute run a line.</summary>
    private static string Orders()
    {
        var text = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<orders>\n");
        for (int g = 0; g < 10; g++)
        {
            text.Append($"  <group id=\"g{g}\">\n");
            for (int o = 0; o < 10; o++)
                text.Append($"    <order id=\"{g * 10 + o}\">\n      <customer>Customer {g}-{o}</customer>\n")
                    .Append($"      <total currency=\"EUR\">{(g * 31 + o * 7) % 997}.50</total>\n    </order>\n");
            text.Append("  </group>\n");
        }
        return text.Append("</orders>\n").ToString();
    }

    /// <summary>The highlighter's own bytes a token, over what TextMateSharp allocates to tokenize
    /// the same lines with the same grammar: 104 with a theme match every token, 32 now (the spans
    /// it returns, 0.6 of one a token).</summary>
    [Fact]
    public void ATokenizeAllocatesLittleOverTextMateSharpsOwn()
    {
        string text = Orders();
        IGrammar? grammar = null;
        for (int i = 0; i < 3; i++)   // the grammar's first use, and the scope lists' first match
            Tokenize(g => grammar = g);

        var lines = text.Split('\n').Select(l => l.AsMemory()).ToArray();
        long tokens = 0;
        Walk();
        long theirs = Bytes(Walk);
        long ours = Bytes(() => Tokenize(null));

        double perToken = (ours - theirs) / (double)tokens;
        Assert.True(perToken < 60, $"{perToken:0} bytes a token over TextMateSharp's ({ours:N0} against {theirs:N0}, {tokens:N0} tokens)");

        void Tokenize(Func<IGrammar, IGrammar>? wrap) =>
            Assert.NotNull(SyntaxHighlighter.TokenizeLines("a.xml", text, SyntaxHighlighter.DefaultMaxLines,
                CancellationToken.None, SteppingClock.Standing, wrap));

        void Walk()
        {
            tokens = 0;
            IStateStack? state = null;
            foreach (var line in lines)
            {
                var result = grammar!.TokenizeLine(new LineText(line), state, TimeSpan.MaxValue);
                state = result.RuleStack;
                tokens += result.Tokens.Length;
            }
        }

        static long Bytes(Action run)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            run();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    /// <summary>A colour is found again only for the same names in the same order: lists that
    /// differ only far out (a tag inside a script inside a page, or inside the page itself) or
    /// in length are different lists.</summary>
    [Fact]
    public void ScopeListsAreTheSameOnlyWithTheSameNamesInOrder()
    {
        var comparer = SyntaxHighlighter.ScopeListComparer.Instance;
        List<string> tag = ["text.html.basic", "meta.tag.structure.div.start.html", "entity.name.tag.html"];
        string[] copy = [.. tag.Select(s => new string(s.AsSpan()))];   // equal names, other strings

        Assert.True(comparer.Equals(tag, copy));
        Assert.Equal(comparer.GetHashCode(tag), comparer.GetHashCode(copy));
        Assert.False(comparer.Equals(tag, ["source.js", .. tag.Skip(1)]));
        Assert.False(comparer.Equals(tag, tag.Take(2).ToList()));
        Assert.False(comparer.Equals(tag, [.. tag, "punctuation.definition.tag.html"]));
        Assert.False(comparer.Equals(tag, [.. tag.Reverse<string>()]));
    }
}
