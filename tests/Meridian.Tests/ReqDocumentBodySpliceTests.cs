using System.Text;
using System.Text.RegularExpressions;
using HultUI.Testing;
using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// A keystroke in the body editor replaces the body block in place (ReqDocument.TrySpliceBody)
/// instead of splitting the body into lines and reparsing the whole document. These hold it to what
/// the whole path made, byte for byte and field for field, and hold it to copying nothing of the
/// body (plan 17 round 6, step 7a).
/// </summary>
public class ReqDocumentBodySpliceTests
{
    // ── The same document as before ──────────────────────────────────────

    public static TheoryData<string, string> Corpus => new()
    {
        { "lf", "POST https://x/y\nname LF\nheader A: b\nbody json\n{\n  \"a\": 1\n}\nend\n" },
        { "crlf", "POST https://x/y\r\nname CRLF\r\nheader A: b\r\nbody json\r\n{\r\n  \"a\": 1\r\n}\r\nend\r\n" },
        { "crlf body in an lf file", "POST https://x/y\nheader A: b\nbody json\r\n{\r\n  \"a\": 1\r\n}\r\nend\r\n" },
        { "the block mid-file", "# a comment\nPOST https://x/y\nname Middle\n\nbody json\n{\n    \"deep\": {\n        \"x\": [1, 2]\n    }\n}\nend\n\nauth bearer {{t}}\nassert status equals 200\nextract body $.id -> id\n" },
        { "trailing newline and blank lines", "POST https://x/y\nbody text\nline one\n\n\nend\n" },
        { "### in the body", "POST https://x/y\nname Hashes\nbody text\n### not a separator\n# nor a comment\nend\n" },
        { "no final newline", "POST https://x/y\nname NoFinal\nbody json\n{}\nend" },
        { "no body", "POST https://x/y\nname NoBody\nheader A: b" },
        { "an empty body", "POST https://x/y\nbody json\nend\n" },
        { "a custom marker", "POST https://x\nbody text until DONE-7f3a\nfirst\nend\nlast\nDONE-7f3a\n" },
        { "mixed endings", "GET https://x\r\nheader A: b\nheader C: d\r\nbody\r\nraw \r\n mixed\nend\nassert status equals 200\r\n" },
        { "an unclosed body", "POST https://x\nbody json\n{ never closed\n" },
        { "two bodies", "POST https://x\nbody json\n1\nend\nbody text\n2\nend\n" },
        { "nothing", "" },
        { "the full example", """
            # Get one charge by id.
            GET {{base_url}}/v1/charges/{{charge_id}}
            name Get charge

            header Authorization: Bearer {{token}}
            header Accept: application/json

            body json
            {
              "expand": ["refunds"]
            }
            end

            auth bearer {{token}}
            assert status equals 200
            assert body $.status equals "succeeded"
            extract body $.id -> charge_id
            """ },
    };

    /// <summary>Typing in the body editor, with other edits between the keys: each body edit makes
    /// the document the whole path made from the same text.</summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void ABodyEditMakesWhatTheWholePathMade(string name, string text)
    {
        _ = name;
        var doc = ReqDocument.Parse(text);
        string expected = text;
        string content = doc.Body?.Content ?? "";
        string type = doc.Body?.Type ?? "json";

        void Body(string next, string? nextType = null)
        {
            content = next;
            type = nextType ?? type;
            doc.SetBody(type, content);
            expected = WholePathSetBody(expected, type, content);
            Same(expected, doc);
        }
        void Other(Action<ReqDocument> edit)
        {
            edit(doc);
            var reference = ReqDocument.Parse(expected);
            edit(reference);
            expected = reference.Serialize();
            Same(expected, doc);
        }

        // Keys at the end, then a newline, an indented line, a CRLF and lone CR, blank lines, a
        // "###" line, a trailing newline.
        Body(content + "x");
        Body(content + "\n");
        Body(content + "  \"k\": \"v\"");
        Body(content + "\r\n\r\n");
        Body(content + "\rlone");
        Body(content + "\n### x\n");
        Body(content + "\n\n");
        // An "end" line for a moment: a marker of its own, then "end" again.
        Body(content + "end");
        Body(content + "\n  end  \n");
        Body(content[..^"\n  end  \n".Length]);
        Body(content[..^"end".Length]);
        // Other edits between keys: an assert appended after the block, a header, the URL.
        Other(d => d.AddAssert("status equals 201"));
        Body("\t" + content);
        Body(content + "y");
        Other(d => d.AddHeader("X-Key", "1"));
        Body(content + "z");
        Other(d => d.SetRequestLine("PUT", "https://x/z"));
        Body(content + "w");
        Other(d => d.SetAuth("bearer {{t2}}"));
        Body(content + "v");
        // A change of type, an empty body, then no body at all, then one again.
        Body(content, "text");
        Body("", "json");
        Body("", "");
        Body("{}", "json");
        Body("{}\n");
        Body(content + "!");
        // Saved: the document reads back as itself.
        doc.MarkSaved();
        Assert.False(doc.Edited);
        Assert.Equal(Describe(ReqDocument.Parse(doc.Serialize())), Describe(doc));
    }

    /// <summary>A keystroke when the block is not the last thing in the file moves it to the end as
    /// before; the next one is spliced, and both say the document is edited.</summary>
    [Fact]
    public void TheFirstEditMovesTheBlockToTheEnd_TheNextReplacesItThere()
    {
        var doc = ReqDocument.Parse("POST https://x\nbody json\nold\nend\nassert status equals 200\n");
        doc.SetBody("json", "new");
        Assert.Equal("POST https://x\nassert status equals 200\nbody json\nnew\nend\n", doc.Serialize());
        doc.MarkSaved();
        doc.SetBody("json", "newer\nstill");
        Assert.True(doc.Edited);
        Assert.Equal("POST https://x\nassert status equals 200\nbody json\nnewer\nstill\nend\n", doc.Serialize());
        Assert.Equal(new ReqBody("json", "end", "newer\nstill", 3), doc.Body);
        Assert.Equal(2, doc.Asserts.Single().Line);
        // An edit after it sees the body's lines one by one: the assert lands after the block.
        doc.AddAssert("time under 500 ms");
        Assert.Equal("POST https://x\nassert status equals 200\nassert time under 500 ms\nbody json\nnewer\nstill\nend\n", doc.Serialize());
        Assert.Equal(new ReqBody("json", "end", "newer\nstill", 4), doc.Body);
    }

    // ── Copies of the body ───────────────────────────────────────────────

    /// <summary>A keystroke in an 89,000-character body: the edit copies nothing of it (the text
    /// area's own new text is the edit's input). It cost 1.2 MB a key, about seven copies.</summary>
    [Fact]
    public void AKeystrokeInABigBodyCopiesNothingOfIt()
    {
        string body = BigJson(89_000);
        var doc = ReqDocument.Parse($"POST https://x/y\nname Big\nheader Content-Type: application/json\nbody json\n{body}end\n");
        string[] keys = [body + "a", body];   // what the text area hands over, key after key
        doc.SetBody("json", keys[1]);
        int k = 0;
        long bytes = Allocations.Least(() => doc.SetBody("json", keys[k++ % 2]), goal: 1024);
        Assert.True(bytes < 4096, $"a body keystroke allocated {bytes:N0} B");
        Assert.Same(keys[(k - 1) % 2], doc.Body!.Content);
    }

    /// <summary>A keystroke in the URL, a header or an assertion of a request with an
    /// 89,000-character body rewrites its line and reparses the document: the body's content is
    /// read as it is, not joined again (about 600 KB a key before, three copies of it).</summary>
    [Fact]
    public void AnEditBeforeABigBodyCopiesNoneOfIt()
    {
        string body = BigJson(89_000);
        var doc = ReqDocument.Parse($"POST https://x/y\nname Big\nheader Content-Type: application/json\nassert status equals 200\nbody json\n{body}end\n");
        string content = doc.Body!.Content;
        string[] urls = ["https://x/ya", "https://x/y"];
        int k = 0;
        long url = Allocations.Least(() => doc.SetRequestLine("POST", urls[k++ % 2]), goal: 1024);
        long header = Allocations.Least(() => doc.SetHeader(0, "Content-Type", urls[k++ % 2]), goal: 1024);
        long assert = Allocations.Least(() => doc.SetAssert(0, urls[k++ % 2]), goal: 1024);
        Assert.True(url < 4096, $"a URL keystroke allocated {url:N0} B");
        Assert.True(header < 4096, $"a header keystroke allocated {header:N0} B");
        Assert.True(assert < 4096, $"an assertion keystroke allocated {assert:N0} B");
        Assert.Same(content, doc.Body!.Content);
    }

    /// <summary>What the editor's build asks of the document on a keystroke: the variables the
    /// request uses (the Chain tab's count) and, on the Assertions tab, whether the document is
    /// still what was sent. Neither copies the body: 350 KB a build at 89,000 characters before.</summary>
    [Fact]
    public void TheEditorsQuestionsCopyNothingOfABigBody()
    {
        string body = BigJson(89_000) + "{ \"id\": \"{{order_id}}\", \"tag\": \"{{$uuid}}\" }\n";
        var doc = ReqDocument.Parse($"POST https://x/{{{{host}}}}\nheader A: {{{{token}}}}\nbody json\n{body}end\n");
        doc.SetBody("json", body.TrimEnd('\n'));
        string snapshot = doc.Serialize();

        Chain.Node? node = null;
        long analyze = Allocations.Least(() => node = Chain.Analyze("big.req", doc), goal: 1024);
        Assert.Equal(new[] { "host", "token", "order_id" }, node!.Uses);
        Assert.True(analyze < 4096, $"Chain.Analyze allocated {analyze:N0} B");

        bool same = false;
        long compare = Allocations.Least(() => same = doc.SerializesTo(snapshot));
        Assert.True(same);
        Assert.Equal(0, compare);
        Assert.False(doc.SerializesTo(snapshot + " "));
        Assert.False(doc.SerializesTo(snapshot[..^1]));
        Assert.False(doc.SerializesTo(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text, no variables")]
    [InlineData("{{a}}")]
    [InlineData("x{{a}}y{{ b }}z")]
    [InlineData("{{{a}}")]
    [InlineData("{{a {{b}}")]
    [InlineData("{{ }} {{}} {{x y}} {{ok}} {{unclosed")]
    [InlineData("}}{{a}}{{")]
    [InlineData("{{$uuid}}-{{a}}{{a}}")]
    public void TokenizeSplitsAsItDid(string template)
    {
        Assert.Equal(TokenizeAsItWas(template), VariableResolver.Tokenize(template));
        Assert.Equal(TokenizeAsItWas(template).Where(s => s.IsVariable).Select(s => s.Text), VariableResolver.VariableNames(template));
    }

    [Fact]
    public void ALiteralWithNoVariableIsTheTemplateItself()
    {
        string template = BigJson(10_000);
        Assert.Same(template, VariableResolver.Tokenize(template).Single().Text);
    }

    // ── The whole path, as it was ────────────────────────────────────────

    /// <summary>ReqDocument.SetBody before plan 17 round 6, on a document's text: the block out,
    /// then written again at the end, every line in the lines' prevailing ending.</summary>
    private static string WholePathSetBody(string text, string type, string content)
    {
        var lines = LineBuffer.Split(text);
        if (ReqDocument.Parse(text).Body is { } existing)
        {
            int fence = existing.Line - 1;
            int marker = fence + 1;
            while (marker < lines.Count && lines[marker].Raw.Trim() != existing.Marker) marker++;
            lines.RemoveRange(fence, Math.Min(marker + 1, lines.Count) - fence);
        }
        if (content.Length == 0 && type.Length == 0) return LineBuffer.Join(lines);

        string chosen = "end";
        string[] contentLines = content.Length == 0 ? [] : content.ReplaceLineEndings("\n").Split('\n');
        if (contentLines.Any(l => l.Trim() == chosen))
            chosen = $"end-{Guid.NewGuid():N}"[..12];
        int crlf = lines.Count(l => l.Terminator == "\r\n"), lf = lines.Count(l => l.Terminator == "\n");
        string term = crlf > lf ? "\r\n" : "\n";
        if (lines.Count > 0 && lines[^1].Terminator.Length == 0) lines[^1] = lines[^1] with { Terminator = term };
        string fenceRaw = chosen == "end"
            ? (type.Length > 0 ? $"body {type}" : "body")
            : (type.Length > 0 ? $"body {type} until {chosen}" : $"body until {chosen}");
        lines.Add(new SourceLine(fenceRaw, term));
        foreach (var line in contentLines) lines.Add(new SourceLine(line, term));
        lines.Add(new SourceLine(chosen, term));
        return LineBuffer.Join(lines);
    }

    private static List<TemplateSegment> TokenizeAsItWas(string template)
    {
        var segments = new List<TemplateSegment>();
        var literal = new StringBuilder();
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0) { literal.Append(template, i, template.Length - i); break; }
            int close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            string? name = close < 0 ? null : template[(open + 2)..close].Trim();
            if (name is not { Length: > 0 } || name.Any(c => c is '{' or '}' || char.IsWhiteSpace(c)))
            {
                literal.Append(template, i, open + 2 - i);
                i = open + 2;
                continue;
            }
            literal.Append(template, i, open - i);
            if (literal.Length > 0) { segments.Add(new TemplateSegment(literal.ToString(), false)); literal.Clear(); }
            segments.Add(new TemplateSegment(name, true));
            i = close + 2;
        }
        if (literal.Length > 0) segments.Add(new TemplateSegment(literal.ToString(), false));
        return segments;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>The text, and everything parsed from it, the same as a document parsed from the
    /// expected text (a generated marker masked: it is new on every edit).</summary>
    private static void Same(string expected, ReqDocument doc)
    {
        Assert.Equal(Mask(expected), Mask(doc.Serialize()));
        Assert.Equal(Describe(ReqDocument.Parse(expected)), Describe(doc));
        Assert.True(doc.Edited);
    }

    private static string Mask(string text) => Regex.Replace(text, "end-[0-9a-f]{8}", "end-XXXXXXXX");

    private static string Describe(ReqDocument d)
    {
        var sb = new StringBuilder();
        sb.Append($"method={d.Method} url={d.Url} line={d.RequestLineNumber} name={d.Name}\n");
        sb.Append($"body={(d.Body is { } b ? $"{b.Type}|{b.Marker}|{b.Line}|{b.Content.Replace("\r", "\\r").Replace("\n", "\\n")}" : "none")}\n");
        sb.Append($"auth={d.Auth}\n");
        foreach (var h in d.Headers) sb.Append($"header {h}\n");
        foreach (var a in d.Asserts) sb.Append($"assert {a}\n");
        foreach (var e in d.Extracts) sb.Append($"extract {e}\n");
        foreach (var e in d.Errors) sb.Append($"error {e}\n");
        return Mask(sb.ToString());
    }

    private static string BigJson(int length)
    {
        var big = new StringBuilder("{\n  \"items\": [\n");
        for (int i = 0; big.Length < length; i++)
            big.Append("    {\"id\": ").Append(i).Append(", \"name\": \"customer ").Append(i).Append("\", \"note\": \"lorem ipsum dolor\"},\n");
        return big.Append("    {}\n  ]\n}\n").ToString();
    }
}
