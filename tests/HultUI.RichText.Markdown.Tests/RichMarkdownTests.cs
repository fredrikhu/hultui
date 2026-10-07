using HultUI.Core.RichText;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.RichText.Markdown.Tests;

/// <summary>
/// Markdown in and out of the rich text document (docs/plans/16-rich-text-editor.md, stage 6).
/// Reading is compared block by block through <see cref="RichBlock.ToString"/>; writing against
/// the canonical text; and the two together by the property that matters — a document written
/// and read back is the same document.
/// </summary>
public class RichMarkdownTests(ITestOutputHelper output)
{
    private static readonly RichMarks Bold = new(InlineStyle.Bold);
    private static readonly RichMarks Italic = new(InlineStyle.Italic);

    private static string Read(string markdown) => string.Join(" | ", RichMarkdown.Parse(markdown).Blocks);

    // ── Reading ──

    [Theory]
    [InlineData("# Title", "h1 \"Title\"")]
    [InlineData("### Third", "h3 \"Third\"")]
    [InlineData("Setext\n======", "h1 \"Setext\"")]
    [InlineData("one\ntwo", "Paragraph \"one two\"")]                    // a soft break renders as a space
    [InlineData("one\\\ntwo", "Paragraph \"one\ntwo\"")]                 // a hard one breaks the line
    [InlineData("one  \ntwo", "Paragraph \"one\ntwo\"")]
    [InlineData("**b** *i* _i_ ~~s~~ `c`", "Paragraph \"b i i s c\" {0+1:Bold 2+1:Italic 4+1:Italic 6+1:Strike 8+1:Code}")]
    [InlineData("***both***", "Paragraph \"both\" {0+4:Bold, Italic}")]
    [InlineData("[docs](https://x.org/d)", "Paragraph \"docs\" {0+4:link=https://x.org/d}")]
    [InlineData("<https://x.org>", "Paragraph \"https://x.org\" {0+13:link=https://x.org}")]
    [InlineData("see https://x.org", "Paragraph \"see https://x.org\"")]   // a bare address stays text
    [InlineData("a &amp; b", "Paragraph \"a & b\"")]
    [InlineData("---", "Rule \"\"")]
    [InlineData("```cs\nvar x;\n```", "CodeBlock(cs) \"var x;\"")]
    [InlineData("    indented", "CodeBlock \"indented\"")]
    [InlineData("> quoted", "Quote \"quoted\"")]
    [InlineData("> > deeper", "Quote>1 \"deeper\"")]
    public void Reads(string markdown, string expected) => Assert.Equal(expected, Read(markdown));

    [Fact]
    public void Lists_NestByIndent_AndTasksKeepTheirBox()
    {
        const string md = "- a\n  - nested\n    1. deep\n- [x] done\n- [ ] open\n\n1. one\n2. two";
        Assert.Equal(
            "BulletItem \"a\" | BulletItem>1 \"nested\" | NumberedItem>2 \"deep\" | task[x] \"done\" | task[ ] \"open\""
            + " | NumberedItem \"one\" | NumberedItem \"two\"",
            Read(md));
    }

    [Fact]
    public void AnItemsSecondParagraph_ContinuesIt_OneLevelIn()
    {
        Assert.Equal("BulletItem \"item\" | Paragraph>1 \"more of it\" | BulletItem \"next\"",
            Read("- item\n\n  more of it\n- next"));
    }

    [Theory]
    [InlineData("| a | b |\n|---|---|\n| ![x](y.png) | 2 |")]                   // a picture in a cell
    [InlineData("<!-- a template's comment -->")]                               // HTML
    [InlineData("<details>\n<summary>More</summary>")]
    [InlineData("see ![logo](/img/logo.png) here")]                            // a picture in a line
    [InlineData("<img src=\"a.png\" height=\"40\">")]                          // a size the document has no field for
    [InlineData("text with <kbd>Ctrl</kbd> in it")]                              // inline HTML
    [InlineData("> - a list\n> - in a quote")]
    [InlineData("[ref]: https://x.org")]                                         // a link reference definition
    public void WhatTheDocumentCannotHold_IsKeptRaw_AndWrittenBackExactly(string markdown)
    {
        var doc = RichMarkdown.Parse(markdown);
        var raw = Assert.Single(doc.Blocks);
        Assert.Equal(BlockKind.Raw, raw.Kind);
        Assert.Equal(markdown, raw.Text);
        Assert.Equal(markdown, RichMarkdown.Write(doc));
    }

    [Fact]
    public void RawInsideAList_KeepsItsLines_AtTheItemsColumn()
    {
        const string md = "- item\n\n  | a | b |\n  |---|---|\n  | 1 | 2 |";
        var doc = RichMarkdown.Parse(md);
        Assert.Equal(BlockKind.Raw, doc[1].Kind);
        Assert.Equal("| a | b |\n|---|---|\n| 1 | 2 |", doc[1].Text);
        Assert.Equal(md, RichMarkdown.Write(doc));
    }

    // ── Writing ──

    [Fact]
    public void Writing_IsCanonical()
    {
        var doc = new RichDocument(
        [
            RichBlock.Heading(2, "Summary"),
            RichBlock.Paragraph("Some bold and italic text.", new RichSpan(5, 4, Bold), new RichSpan(14, 6, Italic)),
            RichBlock.Of(BlockKind.BulletItem, "one"),
            RichBlock.Of(BlockKind.BulletItem, "two", indent: 1),
            RichBlock.Of(BlockKind.NumberedItem, "first"),
            RichBlock.Of(BlockKind.NumberedItem, "second"),
            RichBlock.Of(BlockKind.TaskItem, "todo"),
            RichBlock.Of(BlockKind.Quote, "said"),
            RichBlock.Of(BlockKind.CodeBlock, "x = 1") with { Info = "py" },
            RichBlock.Of(BlockKind.Rule),
        ]);

        Assert.Equal(
            "## Summary\n\n"
            + "Some **bold** and *italic* text.\n\n"
            + "- one\n  - two\n1. first\n2. second\n- [ ] todo\n\n"
            + "> said\n\n"
            + "```py\nx = 1\n```\n\n"
            + "---",
            RichMarkdown.Write(doc));
    }

    [Theory]
    [InlineData("Fixes #123 for @fred, see ~bug and !45")]   // GitLab references stay as typed
    [InlineData("snake_case_name and 2 * 3")]
    [InlineData("C:\\Users\\fred")]
    [InlineData("a - b + c = d")]
    public void PlainText_IsNotEscapedWhereItReadsAsText(string text)
    {
        var md = RichMarkdown.Write(new RichDocument([RichBlock.Paragraph(text)]));
        Assert.Equal(text, md);
    }

    [Theory]
    [InlineData("# not a heading", "\\# not a heading")]
    [InlineData("- not a bullet", "\\- not a bullet")]
    [InlineData("1. not a number", "1\\. not a number")]
    [InlineData("> not a quote", "\\> not a quote")]
    [InlineData("*stars* and `ticks` and [brackets]", "\\*stars\\* and \\`ticks\\` and \\[brackets\\]")]
    [InlineData("<b> & &amp;", "\\<b> & \\&amp;")]
    [InlineData("~~not struck~~", "\\~\\~not struck\\~\\~")]
    public void MarkupInText_IsEscaped(string text, string expected)
    {
        var doc = new RichDocument([RichBlock.Paragraph(text)]);
        string md = RichMarkdown.Write(doc);
        Assert.Equal(expected, md);
        Assert.Equal(doc, RichMarkdown.Parse(md));
    }

    [Fact]
    public void ALineBreak_IsABackslash_AndInAListItLinesUpUnderTheText()
    {
        var doc = new RichDocument([RichBlock.Of(BlockKind.BulletItem, "one\ntwo")]);
        string md = RichMarkdown.Write(doc);
        Assert.Equal("- one\\\n  two", md);
        Assert.Equal(doc, RichMarkdown.Parse(md));
    }

    [Fact]
    public void WhatMarkdownCannotSay_IsDropped()
    {
        var doc = new RichDocument(
        [
            RichBlock.Paragraph("u", new RichSpan(0, 1, new RichMarks(InlineStyle.Underline, Color: new(255, 0, 0, 255))))
                with { Align = BlockAlign.Center },
            RichBlock.Paragraph(""),
            RichBlock.Paragraph("after"),
        ]);
        Assert.Equal("u\n\nafter", RichMarkdown.Write(doc));
    }

    // ── Round trips ──

    public static TheoryData<string> Documents() =>
    [
        "plain",
        "bold",
        "nested",
        "links",
        "code",
        "lists",
        "quotes",
        "everything",
    ];

    private static RichDocument Sample(string name) => name switch
    {
        "plain" => new([RichBlock.Paragraph("Just a line of text.")]),
        "bold" => new([RichBlock.Paragraph("a bold word, and bold, italic.",
            new RichSpan(2, 4, Bold), new RichSpan(17, 4, Bold), new RichSpan(23, 6, Italic))]),
        "nested" => new([RichBlock.Paragraph("outer inner outer",
            new RichSpan(0, 6, Bold), new RichSpan(6, 5, new RichMarks(InlineStyle.Bold | InlineStyle.Italic)),
            new RichSpan(11, 6, Bold))]),
        "links" => new([RichBlock.Paragraph("see the docs, or https://x.org/a_(b)",
            new RichSpan(4, 8, new RichMarks(Link: "https://x.org/d?q=1&r=(2)")),
            new RichSpan(17, 19, new RichMarks(Link: "https://x.org/a_(b)")))]),
        "code" => new([
            RichBlock.Paragraph("call `this` and ``that``",
                new RichSpan(5, 6, new RichMarks(InlineStyle.Code)), new RichSpan(16, 8, new RichMarks(InlineStyle.Code))),
            RichBlock.Of(BlockKind.CodeBlock, "has ``` inside\n\nand a blank line") with { Info = "md" },
        ]),
        "lists" => new([
            RichBlock.Of(BlockKind.NumberedItem, "one"),
            RichBlock.Of(BlockKind.NumberedItem, "two"),
            RichBlock.Of(BlockKind.BulletItem, "under two", indent: 1),
            RichBlock.Paragraph("more under two") with { Indent = 2 },
            RichBlock.Of(BlockKind.TaskItem, "done", indent: 1) with { Checked = true },
            RichBlock.Of(BlockKind.CodeBlock, "code in the list", indent: 2),
            RichBlock.Of(BlockKind.NumberedItem, "three"),
        ]),
        "quotes" => new([
            RichBlock.Of(BlockKind.Quote, "first"),
            RichBlock.Of(BlockKind.Quote, "second paragraph"),
            RichBlock.Of(BlockKind.Quote, "nested", indent: 1),
            RichBlock.Of(BlockKind.Quote, "back out"),
        ]),
        _ => new([
            RichBlock.Heading(1, "Title #1"),
            RichBlock.Paragraph("Intro with a strike and a line\nbreak.",
                new RichSpan(13, 6, new RichMarks(InlineStyle.Strike))),
            RichBlock.Of(BlockKind.Raw, "<details>\n<summary>More</summary>"),
            RichBlock.Cell(0, 0, "t") , RichBlock.Cell(0, 1, "a") with { Align = BlockAlign.End },
            RichBlock.Cell(1, 0, "x | y"), RichBlock.Cell(1, 1, "two\nlines", new RichSpan(0, 9, Bold)) with { Align = BlockAlign.End },
            RichBlock.Picture(new RichImage("img/a b.png", "A [bracketed] picture", "Its title")),
            RichBlock.Picture(new RichImage("w.png", "Wide \"one\"", Width: 320, Link: "https://x.org/?a=1&b=2")),
            RichBlock.Of(BlockKind.Rule),
            RichBlock.Heading(3, "End"),
        ]),
    };

    // ── Pictures and tables ──

    [Theory]
    [InlineData("![A cat](cat.png)", "cat.png", "A cat", null, null, null)]
    [InlineData("![](a.png \"Title\")", "a.png", "", "Title", null, null)]
    [InlineData("[![logo](l.png)](https://x.org)", "l.png", "logo", null, null, "https://x.org")]
    [InlineData("<img src=\"s.png\" alt=\"S &amp; T\" width=\"300\">", "s.png", "S & T", null, 300f, null)]
    [InlineData("<a href=\"https://x.org\">\n<img src=\"s.png\" alt=\"S\" width=\"64\" />\n</a>", "s.png", "S", null, 64f, "https://x.org")]
    public void APictureAloneOnItsLine_IsAPicture(string markdown, string source, string alt, string? title, float? width,
        string? link)
    {
        var block = Assert.Single(RichMarkdown.Parse(markdown).Blocks);
        Assert.Equal(new RichImage(source, alt, title, width, link), block.Image);
    }

    [Fact]
    public void ATable_IsARunOfCells_AlignedByColumn()
    {
        var doc = RichMarkdown.Parse("| Name | Days |\n| :-- | --: |\n| **Ana** | 2 |\n| Bo<br>and Cy | 3 |\n| Di |");
        Assert.Equal(
            "cell[0,0] \"Name\" | cell[0,1]@End \"Days\" | cell[1,0] \"Ana\" {0+3:Bold} | cell[1,1]@End \"2\""
            + " | cell[2,0] \"Bo\nand Cy\" | cell[2,1]@End \"3\" | cell[3,0] \"Di\" | cell[3,1]@End \"\"",
            doc.ToString());
        Assert.Equal(
            "| Name | Days |\n| --- | ---: |\n| **Ana** | 2 |\n| Bo<br>and Cy | 3 |\n| Di |  |",
            RichMarkdown.Write(doc));
    }

    [Fact]
    public void ARowWiderThanItsHeader_KeepsTheTableRaw()
    {
        const string md = "| a | b |\n|---|---|\n| 1 | 2 | 3 |";
        var raw = Assert.Single(RichMarkdown.Parse(md).Blocks);
        Assert.Equal(BlockKind.Raw, raw.Kind);
        Assert.Equal(md, raw.Text);
    }

    [Fact]
    public void APipeInACell_IsEscaped_InCodeToo()
    {
        var doc = new RichDocument([
            RichBlock.Cell(0, 0, "a|b"), RichBlock.Cell(0, 1, "c|d", new RichSpan(0, 3, new RichMarks(InlineStyle.Code))),
        ]);
        string md = RichMarkdown.Write(doc);
        Assert.Equal("| a\\|b | `c\\|d` |\n| --- | --- |", md);
        Assert.Equal(doc, RichMarkdown.Parse(md));
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void ADocumentWrittenAndReadBack_IsTheSameDocument(string name)
    {
        var doc = Sample(name);
        string md = RichMarkdown.Write(doc);
        output.WriteLine(md);
        var back = RichMarkdown.Parse(md);
        Assert.Equal(doc.ToString(), back.ToString());
        // What ToString leaves out too: a picture's words, title, width and link.
        Assert.Equal(doc, back);
    }

    /// <summary>
    /// Random documents in an alphabet heavy with Markdown's own characters, under random marks:
    /// every one must come back from Markdown as it went in. The generator leaves out what
    /// Markdown has no spelling for — spaces at a line's ends, code over a line break.
    /// <para>One thing it can produce is changed on purpose, and is held to a weaker promise: a
    /// style that starts or ends with a space. A delimiter against a space neither opens nor
    /// closes, so the writer moves the space out of the style; what is written must still read
    /// back to something that writes the same.</para>
    /// <para>Everything else — styles starting between a letter and a punctuation mark, struck
    /// text inside a word — must come back exactly: where a delimiter cannot work the writer
    /// uses an HTML tag, and the reader reads it back.</para>
    /// </summary>
    [Theory]
    [InlineData(20261006)]
    [InlineData(1)]
    [InlineData(77)]
    public void RandomDocuments_SurviveTheRoundTrip(int seed)
    {
        const int count = 4000;
        var rng = new Random(seed);
        int failures = 0, hazards = 0;
        for (int n = 0; n < count; n++)
        {
            var doc = RandomDocument(rng);
            string md = RichMarkdown.Write(doc);
            var back = RichMarkdown.Parse(md);
            bool ok;
            if (doc.Blocks.Any(FlankingHazard))
            {
                hazards++;
                ok = RichMarkdown.Write(back) == RichMarkdown.Write(RichMarkdown.Parse(RichMarkdown.Write(back)));
            }
            else
                ok = back.Equals(doc);
            if (ok) continue;
            if (++failures <= 5)
                output.WriteLine($"--- #{n}\n{doc}\n--- markdown\n{md}\n--- read back\n{back}\n");
        }
        output.WriteLine($"{hazards} of {count} had a style starting or ending with a space");
        Assert.Equal(0, failures);
    }

    /// <summary>A style that starts or ends with a space, which the writer moves out of the style
    /// so its delimiter can sit against text.</summary>
    private static bool FlankingHazard(RichBlock block)
    {
        const InlineStyle emphasis = InlineStyle.Bold | InlineStyle.Italic | InlineStyle.Strike;
        string t = block.Text;
        for (int i = 1; i < t.Length; i++)
        {
            var a = block.MarksAt(i - 1);
            var b = block.MarksAt(i);
            var ea = a.Style & emphasis;
            var eb = b.Style & emphasis;
            bool linkEdge = a.Link != b.Link || a.Has(InlineStyle.Code) != b.Has(InlineStyle.Code);
            if (ea == eb && !(linkEdge && (ea | eb) != 0)) continue;

            char l = t[i - 1], r = t[i];
            if ((ea & ~eb) != 0 && char.IsWhiteSpace(l)) return true;
            if ((eb & ~ea) != 0 && char.IsWhiteSpace(r)) return true;
        }
        return false;
    }

    private const string Alphabet = "abcdefghij ABC 0123 *_`~[]()<>&#!\\|-+=.:;'\"?/@";

    private static RichDocument RandomDocument(Random rng)
    {
        var blocks = new List<RichBlock>();
        int count = rng.Next(1, 5);
        for (int b = 0; b < count; b++)
        {
            int kind = rng.Next(8);
            if (kind == 6) { blocks.AddRange(RandomTable(rng)); continue; }
            if (kind == 7) { blocks.Add(RandomPicture(rng)); continue; }
            string text = RandomText(rng);
            var spans = RandomSpans(rng, text);
            var block = kind switch
            {
                0 => RichBlock.Heading(rng.Next(1, 4), text.Replace('\n', ' '), spans),
                1 => RichBlock.Paragraph(text, spans) with { Kind = BlockKind.BulletItem },
                2 => RichBlock.Paragraph(text, spans) with { Kind = BlockKind.NumberedItem },
                3 => RichBlock.Paragraph(text, spans) with { Kind = BlockKind.Quote },
                _ => RichBlock.Paragraph(text, spans),
            };
            if (block.Kind == BlockKind.Heading && block.Text.Trim().Length == 0) continue;
            blocks.Add(block);
        }
        return new RichDocument(blocks.Count == 0 ? [RichBlock.Paragraph("x")] : blocks);
    }

    // A table of random text — line breaks, pipes and backslashes among it — with each column
    // aligned one way, as Markdown aligns them.
    private static IEnumerable<RichBlock> RandomTable(Random rng)
    {
        int rows = rng.Next(1, 4), columns = rng.Next(1, 4);
        var aligns = Enumerable.Range(0, columns).Select(_ => (BlockAlign)rng.Next(3)).ToArray();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                string text = rng.Next(5) == 0 ? "" : RandomText(rng);
                yield return RichBlock.Cell(r, c, text, RandomSpans(rng, text)) with { Align = aligns[c] };
            }
    }

    private static readonly string[] Sources = ["a.png", "img/b c.png", "x(1).png", "https://x.org/p?q=1&r=2", "<odd>.png"];

    private static RichBlock RandomPicture(Random rng) => RichBlock.Picture(new RichImage(
        Sources[rng.Next(Sources.Length)],
        rng.Next(3) == 0 ? "" : RandomText(rng),
        rng.Next(3) == 0 ? RandomText(rng).Replace('\n', ' ') : null,
        rng.Next(3) == 0 ? 120 : null,
        rng.Next(3) == 0 ? "https://x.org/l?" + rng.Next(3) : null));

    private static string RandomText(Random rng)
    {
        int length = rng.Next(1, 24);
        var chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = Alphabet[rng.Next(Alphabet.Length)];
        string text = new string(chars);
        if (rng.Next(5) == 0)
        {
            int at = rng.Next(text.Length);
            text = text[..at] + "\n" + text[at..];
        }
        // Spaces at a line's ends have no spelling; nor has an empty line.
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        string joined = string.Join("\n", lines);
        return joined.Length == 0 ? "x" : joined;
    }

    private static RichSpan[] RandomSpans(Random rng, string text)
    {
        var spans = new List<RichSpan>();
        int at = 0;
        while (at < text.Length)
        {
            int length = rng.Next(1, Math.Max(2, text.Length - at + 1));
            length = Math.Min(length, text.Length - at);
            var marks = rng.Next(4) switch
            {
                0 => RichMarks.None,
                1 => new RichMarks((InlineStyle)rng.Next(1, 16) & (InlineStyle.Bold | InlineStyle.Italic | InlineStyle.Strike)),
                2 => new RichMarks(InlineStyle.Code),
                _ => new RichMarks(Link: "https://x.org/" + rng.Next(3)),
            };
            // A line break cannot be code in Markdown: a code span ends at it.
            if (marks.Has(InlineStyle.Code) && text.AsSpan(at, length).Contains('\n'))
                marks = RichMarks.None;
            spans.Add(new RichSpan(at, length, marks));
            at += length;
        }
        return [.. spans];
    }

    [Fact]
    public void AGitLabTemplate_ReadsAndWritesStably()
    {
        const string template = """
            ## What does this MR do?

            <!-- Describe in detail what your merge request does and why. -->

            Closes #123. Uses `RichDocument` and **does not** break ~"type::bug".

            ## Screenshots

            | Before | After |
            |--------|-------|
            | ![b](/uploads/b.png) | ![a](/uploads/a.png) |

            ## Checklist

            - [x] Tests added
            - [ ] Docs updated
              - including the plan
            1. step one
            2. step two

            > A note from review.

            /label ~"workflow::in review"
            """;
        var first = RichMarkdown.Parse(template);
        string written = RichMarkdown.Write(first);
        output.WriteLine(written);
        Assert.Equal(first.ToString(), RichMarkdown.Parse(written).ToString());
        Assert.Equal(written, RichMarkdown.Write(RichMarkdown.Parse(written)));
        Assert.Contains("<!-- Describe in detail what your merge request does and why. -->", written);
        Assert.Contains("| Before | After |\n|--------|-------|", written);
    }
}
