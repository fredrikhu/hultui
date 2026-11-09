using System.Text;
using HultUI.Syntax;
using TextMateSharp.Grammars;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// Nearly all of a tokenize's bytes are TextMateSharp's own: its tokens, scope lists, state stacks
/// and regular-expression results. Until plan 17 round 9 the highlighter asked it for token objects,
/// each with a list of its scope names, and matched every list against the theme: Theme.Match built a
/// new list of rules each call, 58-75 bytes a token, 12 % of an XML response's tokenize. Now it asks
/// for the encoded tokens, two numbers a token with the theme's colour in them, and adds only the
/// spans it returns.
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

    /// <summary>A JSON response as Meridian's pretty view shows it: an array of objects, one key a
    /// line.</summary>
    private static string Accounts()
    {
        var text = new StringBuilder("{\n  \"accounts\": [\n");
        for (int i = 0; i < 100; i++)
            text.Append("    {\n")
                .Append($"      \"id\": {i},\n      \"name\": \"Account {i}\",\n")
                .Append($"      \"active\": {(i % 3 == 0 ? "false" : "true")},\n      \"balance\": {i * 17 % 991}.25\n")
                .Append(i < 99 ? "    },\n" : "    }\n");
        return text.Append("  ]\n}\n").ToString();
    }

    public static TheoryData<string> Bodies => ["a.xml", "a.json"];

    private static string Body(string path) => path.EndsWith(".xml") ? Orders() : Accounts();

    /// <summary>The highlighter's own bytes a token, over what TextMateSharp allocates for the
    /// encoded tokens of the same lines with the same grammar: the spans it returns, 32 bytes a
    /// token in the XML and 20 in the JSON. With token objects and their scope lists it was 126
    /// and 167.</summary>
    [Theory]
    [MemberData(nameof(Bodies))]
    public void ATokenizeAllocatesLittleOverTextMateSharpsOwn(string path)
    {
        var (ours, encoded, _, tokens) = Measure(path);

        double perToken = (ours - encoded) / (double)tokens;
        Assert.True(perToken < 60, $"{perToken:0} bytes a token over TextMateSharp's ({ours:N0} against {encoded:N0}, {tokens:N0} tokens)");
    }

    /// <summary>What the encoded tokens save: the whole tokenize, spans and all, allocates less than
    /// TextMateSharp's token objects for the same lines alone (79 % of them in the JSON, 87 % in the
    /// XML); with the token objects coloured from their scope names it was 104 % and 107 %.</summary>
    [Theory]
    [MemberData(nameof(Bodies))]
    public void ATokenizeAllocatesLessThanTokenObjectsWould(string path)
    {
        var (ours, _, objects, _) = Measure(path);

        double share = ours / (double)objects;
        Assert.True(share < 0.95, $"{share:P0} of the token objects' bytes ({ours:N0} against {objects:N0})");
    }

    /// <summary>Bytes of one tokenize by the highlighter, of TextMateSharp's walk over the same lines
    /// with encoded tokens and with token objects, and the walk's token count.</summary>
    private static (long Ours, long Encoded, long Objects, long Tokens) Measure(string path)
    {
        string text = Body(path);
        IGrammar? grammar = null;
        for (int i = 0; i < 3; i++)   // the grammar's first use, and the colours' first lookup
            Tokenize(g => grammar = g);

        var lines = text.Split('\n').Select(l => l.AsMemory()).ToArray();
        long tokens = 0;
        Walk(encoded: true);
        Walk(encoded: false);
        long encoded = Bytes(() => Walk(encoded: true));
        long objects = Bytes(() => Walk(encoded: false));
        long ours = Bytes(() => Tokenize(null));
        return (ours, encoded, objects, tokens);

        void Tokenize(Func<IGrammar, IGrammar>? wrap) =>
            Assert.NotNull(SyntaxHighlighter.TokenizeLines(path, text, SyntaxHighlighter.DefaultMaxLines,
                CancellationToken.None, SteppingClock.Standing, wrap));

        void Walk(bool encoded)
        {
            tokens = 0;
            IStateStack? state = null;
            foreach (var line in lines)
            {
                if (encoded)
                {
                    var result = grammar!.TokenizeLine2(new LineText(line), state, TimeSpan.MaxValue);
                    state = result.RuleStack;
                    tokens += result.Tokens.Length / 2;
                }
                else
                {
                    var result = grammar!.TokenizeLine(new LineText(line), state, TimeSpan.MaxValue);
                    state = result.RuleStack;
                    tokens += result.Tokens.Length;
                }
            }
        }

        static long Bytes(Action run)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            run();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
