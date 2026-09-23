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
    public void AnImageReadsAsItsAltTextLinkingToThePicture()
    {
        var t = Para(MarkdownReader.Read("![before and after](/uploads/abc/shot.png)"), 0);
        Assert.Equal("before and after", t.Text);
        Assert.Equal(new MdLink(0, 16, "/uploads/abc/shot.png"), Assert.Single(t.Links));
    }

    [Fact]
    public void AnImagesSizeIsNotReadAsText()
    {
        var t = Para(MarkdownReader.Read("![image](/uploads/abc/image.png){width=600 height=400} after"), 0);
        Assert.Equal("image after", t.Text);
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
            "<!-- Describe the change -->\n\nReal text<br>next line\n\n<details><summary>Logs</summary>\n\nbody\n\n</details>");
        Assert.Equal(new[] { "Real text\nnext line", "Logs", "body" }, doc.Blocks.Select(b => ((MdParagraph)b).Text.Text));
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

    private static MdText Para(MdDocument doc, int i) => Assert.IsType<MdParagraph>(doc.Blocks[i]).Text;
}
