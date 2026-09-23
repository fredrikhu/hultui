using Junction.GitLab;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Merge request descriptions and review notes read as GitLab renders them: paragraphs join
/// their lines, the markup becomes styled ranges and links, the blocks keep their shapes,
/// and the HTML a template leaves behind neither shows raw nor swallows the text around it.
/// </summary>
public class MarkdownReaderTests
{
    [Fact]
    public void SoftBreaksJoinAParagraphAndHardBreaksKeepTheLine()
    {
        var doc = MarkdownReader.Read("one\ntwo  \nthree\r\n\r\nnext");
        Assert.Equal(2, doc.Blocks.Count);
        Assert.Equal("one two\nthree", Para(doc, 0).Text);
        Assert.Equal("next", Para(doc, 1).Text);
    }

    [Fact]
    public void EmphasisStrikeAndCodeBecomeRunsAndTheMarkupGoes()
    {
        var t = Para(MarkdownReader.Read("a **b** _c_ ~~d~~ `e()`"), 0);
        Assert.Equal("a b c d e()", t.Text);
        Assert.Equal(new[]
        {
            new MdRun(2, 1, MdStyle.Strong),
            new MdRun(4, 1, MdStyle.Emphasis),
            new MdRun(6, 1, MdStyle.Strike),
            new MdRun(8, 3, MdStyle.Code),
        }, t.Runs);
    }

    [Fact]
    public void NestedEmphasisCarriesBothStyles()
    {
        var t = Para(MarkdownReader.Read("**bold _both_**"), 0);
        Assert.Equal("bold both", t.Text);
        Assert.Equal(new[] { new MdRun(0, 5, MdStyle.Strong), new MdRun(5, 4, MdStyle.Strong | MdStyle.Emphasis) }, t.Runs);
    }

    [Fact]
    public void LinksKeepTheirLabelAndBareUrlsBecomeLinks()
    {
        var t = Para(MarkdownReader.Read("See [the docs](https://x.test/d) or https://y.test/z."), 0);
        Assert.Equal("See the docs or https://y.test/z.", t.Text);
        Assert.Equal(new[] { new MdLink(4, 8, "https://x.test/d"), new MdLink(16, 16, "https://y.test/z") }, t.Links);
    }

    [Fact]
    public void APictureSplitsItsParagraphAndKeepsItsSize()
    {
        var doc = MarkdownReader.Read("Before ![the shot](/uploads/abc/image.png){width=600 height=400} after");
        Assert.Collection(doc.Blocks,
            b => Assert.Equal("Before", Assert.IsType<MdParagraph>(b).Text.Text),
            b => Assert.Equal(new MdImage("/uploads/abc/image.png", "the shot", null, new MdLength(600, false), new MdLength(400, false)), b),
            b => Assert.Equal("after", Assert.IsType<MdParagraph>(b).Text.Text));
    }

    [Fact]
    public void ALinkedPictureGoesWhereItsLinkGoes()
    {
        var image = Assert.IsType<MdImage>(Assert.Single(MarkdownReader.Read("[![pipeline](https://x.test/badge.png)](https://x.test/pipelines)").Blocks));
        Assert.Equal(("https://x.test/badge.png", "https://x.test/pipelines"), (image.Url, image.Link));
    }

    [Fact]
    public void AnImgTagIsAPictureToo()
    {
        var doc = MarkdownReader.Read("<p>Look:</p>\n<img src=\"/uploads/abc/b.png\" width=\"300\" alt=\"the &amp; plan\">\n\ntext <img src='https://x.test/i.png'> more");
        Assert.Collection(doc.Blocks,
            b => Assert.Equal("Look:", Assert.IsType<MdParagraph>(b).Text.Text),
            b => Assert.Equal(new MdImage("/uploads/abc/b.png", "the & plan", null, new MdLength(300, false), null), b),
            b => Assert.Equal("text", Assert.IsType<MdParagraph>(b).Text.Text),
            b => Assert.Equal("https://x.test/i.png", Assert.IsType<MdImage>(b).Url),
            b => Assert.Equal("more", Assert.IsType<MdParagraph>(b).Text.Text));
    }

    [Fact]
    public void WhereThereIsNoRoomAPictureIsItsAltTextLinkingToIt()
    {
        var heading = Assert.IsType<MdHeading>(Assert.Single(MarkdownReader.Read("## See ![the shot](/uploads/abc/shot.png)").Blocks));
        Assert.Equal("See the shot", heading.Text.Text);
        Assert.Equal(new MdLink(4, 8, "/uploads/abc/shot.png"), Assert.Single(heading.Text.Links));
    }

    [Theory]
    [InlineData("600", 600f, false)]
    [InlineData("600px", 600f, false)]
    [InlineData(" 50% ", 50f, true)]
    [InlineData("auto", null, false)]
    [InlineData("-4", null, false)]
    [InlineData("", null, false)]
    public void ASizeIsPixelsOrAPercentage(string value, float? expected, bool percent) =>
        Assert.Equal(expected is { } e ? new MdLength(e, percent) : null, MarkdownReader.ParseLength(value));

    [Fact]
    public void APipeTableKeepsItsColumnsAlignmentsAndStyles()
    {
        var table = Assert.IsType<MdTable>(Assert.Single(MarkdownReader.Read("""
            | Tender | Amount | Note |
            |:-------|-------:|:----:|
            | card   | **12.50** |   |
            | cash   | 3 |
            """).Blocks));
        Assert.Equal(new[] { MdAlign.Start, MdAlign.End, MdAlign.Center }, table.Align);
        Assert.Equal(new[] { true, false, false }, table.Rows.Select(r => r.Header));
        Assert.Equal(new[] { "Tender", "Amount", "Note" }, table.Rows[0].Cells.Select(c => c.Text));
        Assert.Equal("12.50", table.Rows[1].Cells[1].Text);
        Assert.Equal(MdStyle.Strong, Assert.Single(table.Rows[1].Cells[1].Runs).Style);
        Assert.Equal("", table.Rows[1].Cells[2].Text);
    }

    [Fact]
    public void ADetailsSectionHoldsWhatFollowsUntilItCloses()
    {
        var doc = MarkdownReader.Read("<details><summary>Test <b>logs</b></summary>\n\n- one\n- two\n\n</details>\n\nafter");
        Assert.Collection(doc.Blocks,
            b =>
            {
                var details = Assert.IsType<MdDetails>(b);
                Assert.Equal("Test logs", details.Summary.Text);
                Assert.False(details.Open);
                Assert.IsType<MdList>(Assert.Single(details.Blocks));
            },
            b => Assert.Equal("after", Assert.IsType<MdParagraph>(b).Text.Text));
    }

    [Fact]
    public void SectionsSideBySideNestedOrInOneBlockEachCloseInTheirPlace()
    {
        // No blank line between one section's close and the next one's open: one HTML block.
        var doc = MarkdownReader.Read(string.Join("\n", [
            "<details open><summary>A</summary>", "", "a", "", "</details>",
            "<details><summary>B</summary>", "", "b", "",
            "<details><summary>B.1</summary>inner, all one block</details>", "", "</details>",
        ]));
        Assert.Collection(doc.Blocks,
            b =>
            {
                var a = Assert.IsType<MdDetails>(b);
                Assert.Equal(("A", true), (a.Summary.Text, a.Open));
                Assert.Equal("a", Assert.IsType<MdParagraph>(Assert.Single(a.Blocks)).Text.Text);
            },
            b =>
            {
                var bee = Assert.IsType<MdDetails>(b);
                Assert.Equal("B", bee.Summary.Text);
                Assert.Collection(bee.Blocks,
                    x => Assert.Equal("b", Assert.IsType<MdParagraph>(x).Text.Text),
                    x =>
                    {
                        var inner = Assert.IsType<MdDetails>(x);
                        Assert.Equal("B.1", inner.Summary.Text);
                        Assert.Equal("inner, all one block", Assert.IsType<MdParagraph>(Assert.Single(inner.Blocks)).Text.Text);
                    });
            });
        // Keys are distinct within a body and the same on every read of it.
        var keys = doc.Blocks.OfType<MdDetails>().Select(d => d.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void EmojiShortcodesBecomeEmojiAndSmileysStayAsTyped()
    {
        Assert.Equal("🎉 shipped :) :notashortcode:", Para(MarkdownReader.Read(":tada: shipped :) :notashortcode:"), 0).Text);
    }

    [Fact]
    public void BlocksKeepTheirShapes()
    {
        var doc = MarkdownReader.Read("""
            ## Summary

            > quoted

            ```csharp title="x"
            var x = 1;

            return x;
            ```

            ---

            3. three
            4. four
               - nested
            """);
        Assert.Collection(doc.Blocks,
            b => Assert.Equal((2, "Summary"), (Assert.IsType<MdHeading>(b).Level, ((MdHeading)b).Text.Text)),
            b => Assert.Equal("quoted", Assert.IsType<MdParagraph>(Assert.IsType<MdQuote>(b).Blocks[0]).Text.Text),
            b =>
            {
                var code = Assert.IsType<MdCode>(b);
                Assert.Equal("csharp", code.Language);
                Assert.Equal("var x = 1;\n\nreturn x;", code.Code.ReplaceLineEndings("\n"));
            },
            b => Assert.IsType<MdRule>(b),
            b =>
            {
                var list = Assert.IsType<MdList>(b);
                Assert.True(list.Ordered);
                Assert.Equal(3, list.Start);
                Assert.False(list.Loose);
                Assert.Equal(2, list.Items.Count);
                Assert.Equal("four", Assert.IsType<MdParagraph>(list.Items[1].Blocks[0]).Text.Text);
                Assert.False(Assert.IsType<MdList>(list.Items[1].Blocks[1]).Ordered);
            });
    }

    [Fact]
    public void TaskItemsCarryTheirBoxAndNotItsMarkup()
    {
        var list = Assert.IsType<MdList>(Assert.Single(MarkdownReader.Read("- [x] done\n- [ ] todo\n- plain").Blocks));
        Assert.Equal(new bool?[] { true, false, null }, list.Items.Select(i => i.Checked));
        Assert.Equal(new[] { "done", "todo", "plain" }, list.Items.Select(i => ((MdParagraph)i.Blocks[0]).Text.Text));
    }

    [Fact]
    public void TemplateCommentsVanishAndOtherHtmlLeavesItsText()
    {
        var doc = MarkdownReader.Read(
            "<!-- Describe the change -->\n\nReal text<br>next line\n\n<div class=\"note\">Kept <em>text</em></div>\n\n<!-- <details> -->");
        Assert.Equal(new[] { "Real text\nnext line", "Kept text" }, doc.Blocks.Select(b => ((MdParagraph)b).Text.Text));
    }

    [Fact]
    public void ABodyOfOnlyCommentsIsEmpty()
    {
        Assert.True(MarkdownReader.Read("<!-- template -->\n\n<!-- more\nof it -->").IsEmpty);
        Assert.True(MarkdownReader.Read("").IsEmpty);
        Assert.True(MarkdownReader.Read(null).IsEmpty);
    }

    [Theory]
    [InlineData("**Nit:** trailing whitespace", "Nit: trailing whitespace")]
    [InlineData("<!-- template -->\n\n## Why\nBecause", "Why")]
    [InlineData("- [ ] first\n- second", "first")]
    [InlineData("> said\n> twice", "said twice")]
    [InlineData("", "")]
    public void TheFirstLineIsTheTextWithoutItsMarkup(string body, string expected) =>
        Assert.Equal(expected, MarkdownReader.FirstLine(body));

    [Theory]
    [InlineData("https://example.test/a", "https://example.test/a")]
    [InlineData("mailto:a@b.test", "mailto:a@b.test")]
    [InlineData("/uploads/abc123/shot.png", "https://gitlab.example/acme/core-api/uploads/abc123/shot.png")]
    [InlineData("/acme/other/-/issues/3", "https://gitlab.example/acme/other/-/issues/3")]
    [InlineData("docs/readme.md", null)]
    [InlineData("#section", null)]
    [InlineData("//elsewhere.test/x", null)]
    [InlineData("javascript:alert(1)", null)]
    public void LinksResolveAgainstTheProject(string url, string? expected)
    {
        var remote = new GitLabRemote("gitlab.example", "https", null, "acme/core-api");
        Assert.Equal(expected, JunctionApp.ResolveMarkdownUrl(url, remote));
    }

    [Theory]
    [InlineData("https://gitlab.example/acme/core-api/uploads/0a1b2c/shot%20one.png", null, "0a1b2c", "shot%20one.png")]
    [InlineData("https://gitlab.example/-/project/7781/uploads/0a1b2c/shot.png", 7781L, "0a1b2c", "shot.png")]
    [InlineData("https://gitlab.example/acme/other/uploads/0a1b2c/shot.png", null, null, null)]     // another project's
    [InlineData("https://elsewhere.test/acme/core-api/uploads/0a1b2c/shot.png", null, null, null)]  // another host
    [InlineData("https://gitlab.example/acme/core-api/uploads/not-hex/shot.png", null, null, null)]
    [InlineData("https://gitlab.example/acme/core-api/uploads/0a1b2c/", null, null, null)]
    public void AnUploadIsNamedByItsProjectSecretAndFile(string url, long? project, string? secret, string? file)
    {
        var remote = new GitLabRemote("gitlab.example", "https", null, "acme/core-api");
        var upload = JunctionApp.UploadOf(new Uri(url), remote);
        if (secret is null) Assert.Null(upload);
        else Assert.Equal((project, secret, file!), upload);
    }

    private static MdText Para(MdDocument doc, int i) => Assert.IsType<MdParagraph>(doc.Blocks[i]).Text;
}
