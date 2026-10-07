using System.Text;
using System.Text.RegularExpressions;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.RichText;
using Xunit;

namespace HultUI.Core.Tests.RichText;

/// <summary>
/// A document written out for other programs (docs/plans/16-rich-text-editor.md, export): as a
/// web page, and as RTF for word processors. Neither is read back; what is checked is the output
/// itself, and that its structure holds — tags nest as HTML allows, groups close as RTF needs.
/// </summary>
public class RichExportTests
{
    private static RichBlock P(string text, params RichSpan[] spans) => RichBlock.Paragraph(text, spans);
    private static RichBlock Item(BlockKind kind, string text, int indent = 0) => RichBlock.Of(kind, text, indent);
    private static RichSpan Bold(int start, int length) => new(start, length, new RichMarks(InlineStyle.Bold));

    private static RichBlock[] Table(int columns, params string[] texts) =>
        [.. texts.Select((t, i) => RichBlock.Cell(i / columns, i % columns, t))];

    // A PNG's signature and header: all a writer reads of it, and enough to embed.
    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    // Everything at once, for the structure checks.
    private static RichDocument Everything() => new([
        RichBlock.Heading(1, "Release notes"),
        P("Bold, a link and code.", Bold(0, 4),
            new RichSpan(6, 6, new RichMarks(Link: "https://example.test/?a=1&b=\"2\"")),
            new RichSpan(17, 4, new RichMarks(InlineStyle.Code))),
        Item(BlockKind.BulletItem, "one"),
        Item(BlockKind.NumberedItem, "nested", 1),
        RichBlock.Of(BlockKind.Paragraph, "continues one", 1),
        Item(BlockKind.TaskItem, "done") with { Checked = true },
        RichBlock.Of(BlockKind.Quote, "quoted"),
        RichBlock.Of(BlockKind.Quote, "deeper", 1),
        RichBlock.Of(BlockKind.CodeBlock, "var x = 1;\nreturn x < 2;") with { Info = "csharp" },
        RichBlock.Of(BlockKind.Rule),
        RichBlock.Picture(new RichImage("shot.png", "A shot", Width: 120, Link: "https://example.test")),
        .. Table(2, "Name", "Days", "Ana", "2"),
        P("Centred") with { Align = BlockAlign.Center },
    ]);

    // ── HTML ──

    [Fact]
    public void Html_ListsNestInsideTheirItems_AndAContinuingParagraphStaysInIts()
    {
        var html = RichHtml.Write([
            Item(BlockKind.BulletItem, "a"),
            Item(BlockKind.NumberedItem, "x", 1),
            Item(BlockKind.NumberedItem, "y", 1),
            RichBlock.Of(BlockKind.Paragraph, "more", 1),
            Item(BlockKind.BulletItem, "b"),
            Item(BlockKind.TaskItem, "t") with { Checked = true },
        ]);
        Assert.Equal(
            "<ul><li>a<ol><li>x</li><li>y</li></ol><p>more</p></li><li>b</li>"
            + "<li><input type=\"checkbox\" checked disabled> t</li></ul>", html);
    }

    [Fact]
    public void Html_QuotesGroup_AndADeeperOneNests()
    {
        var html = RichHtml.Write([
            RichBlock.Of(BlockKind.Quote, "q1"),
            RichBlock.Of(BlockKind.Quote, "q2", 1),
            RichBlock.Of(BlockKind.Quote, "q3"),
            P("p"),
        ]);
        Assert.Equal("<blockquote><p>q1</p><blockquote><p>q2</p></blockquote><p>q3</p></blockquote><p>p</p>", html);
    }

    [Fact]
    public void Html_ADocument_IsAPageOfItsOwn_WithTheEditorsProportions()
    {
        var html = RichHtml.WriteDocument(Everything(), new RichExportOptions { Title = "Notes & plans", FontFamily = "Segoe UI", FontSize = 15 });
        Assert.StartsWith("<!doctype html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n", html);
        Assert.Contains("<title>Notes &amp; plans</title>", html);
        Assert.Contains("font-family:'Segoe UI',sans-serif;font-size:15px;", html);
        Assert.Contains("h1{font-size:1.75em}", html);        // the theme's heading scale
        Assert.Contains("code,pre{font-family:monospace;font-size:0.92em}", html);
        Assert.Contains("<h1>Release notes</h1>\n", html);     // a top-level block to a line
        Assert.Contains("<a href=\"https://example.test/?a=1&amp;b=&quot;2&quot;\">a link</a>", html);
        Assert.Contains("<li class=\"task\"><input type=\"checkbox\" checked disabled> done</li>", html);
        Assert.Contains("<pre><code class=\"language-csharp\">var x = 1;\nreturn x &lt; 2;</code></pre>", html);
        Assert.Contains("<a href=\"https://example.test\"><img src=\"shot.png\" alt=\"A shot\" width=\"120\"></a>", html);
        Assert.Contains("<table><thead><tr><th>Name</th><th>Days</th></tr></thead><tbody><tr><td>Ana</td><td>2</td></tr></tbody></table>", html);
        Assert.Contains("<p style=\"text-align:center\">Centred</p>", html);
        Assert.EndsWith("</body>\n</html>\n", html);
        AssertNestsAsHtmlAllows(html);
    }

    [Fact]
    public void Html_APictureIsEmbedded_WhenTheAppHandsOverItsFile()
    {
        var png = Png(2, 1);
        var doc = new RichDocument([RichBlock.Picture(new RichImage("a.png", "A")), RichBlock.Picture(new RichImage("b.png", "B"))]);
        var html = RichHtml.WriteDocument(doc, new RichExportOptions { Pictures = i => i.Source == "a.png" ? png : null });
        Assert.Contains($"<img src=\"data:image/png;base64,{Convert.ToBase64String(png)}\" alt=\"A\">", html);
        Assert.Contains("<img src=\"b.png\" alt=\"B\">", html);   // not handed over: linked
    }

    [Fact]
    public void Html_ADepthTheDocumentSkips_IsAnUnmarkedItem()
    {
        var html = RichHtml.Write([Item(BlockKind.BulletItem, "deep", 2)]);
        Assert.Equal("<ul><li style=\"list-style:none\"><ul><li style=\"list-style:none\"><ul><li>deep</li></ul></li></ul></li></ul>", html);
        AssertNestsAsHtmlAllows(html);
    }

    // Every element closes in the order it opened, an item sits in a list, a row in a table's
    // head or body, and no paragraph holds another block.
    private static void AssertNestsAsHtmlAllows(string html)
    {
        var voids = new HashSet<string> { "meta", "br", "hr", "img", "input" };
        var blocks = new HashSet<string> { "p", "ul", "ol", "li", "blockquote", "pre", "table", "h1", "h2", "h3", "hr" };
        var open = new Stack<string>();
        foreach (Match m in Regex.Matches(html, "<(/?)([a-z][a-z0-9]*)[^>]*>"))
        {
            string name = m.Groups[2].Value;
            bool closing = m.Groups[1].Value == "/";
            if (closing)
            {
                Assert.True(open.Count > 0 && open.Peek() == name, $"</{name}> at {m.Index} closes <{(open.Count > 0 ? open.Peek() : "")}>");
                open.Pop();
                continue;
            }
            string parent = open.Count > 0 ? open.Peek() : "";
            if (name == "li") Assert.True(parent is "ul" or "ol", $"<li> in <{parent}> at {m.Index}");
            if (name == "tr") Assert.True(parent is "thead" or "tbody", $"<tr> in <{parent}> at {m.Index}");
            if (name is "td" or "th") Assert.Equal("tr", parent);
            if (blocks.Contains(name)) Assert.True(parent is not ("p" or "h1" or "h2" or "h3"), $"<{name}> in <{parent}> at {m.Index}");
            if (!voids.Contains(name)) open.Push(name);
        }
        Assert.Empty(open);
    }

    // ── RTF ──

    [Fact]
    public void Rtf_ADocument_IsHeadingsParagraphsAndMarkedItems()
    {
        var rtf = RichRtf.Write(new RichDocument([
            RichBlock.Heading(1, "Title"),
            P("Say hi", Bold(4, 2)),
            Item(BlockKind.BulletItem, "a"),
            Item(BlockKind.BulletItem, "b"),
        ]));
        // 14 units is 10.5 pt (21 half-points); a heading 1.75 of that, with 0.6 of its size
        // above less the paragraph space a word processor adds to it; 15 twips a unit.
        Assert.Equal(
            "{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033\n"
            + "{\\fonttbl{\\f0\\fswiss\\fcharset0 Arial;}{\\f1\\fmodern\\fcharset0 Courier New;}}\n"
            + "{\\colortbl ;}\n"
            + "\\viewkind4\\uc1\n"
            + "\\pard\\plain\\outlinelevel0\\keepn\\sb101\\sa120\\f0\\fs37\\b Title\\par\n"
            + "\\pard\\plain\\sa120\\f0\\fs21 Say {\\b hi}\\par\n"
            + "\\pard\\plain\\li360\\fi-360\\tx360\\sa45\\f0\\fs21 \\u8226?\\tab a\\par\n"
            + "\\pard\\plain\\li360\\fi-360\\tx360\\sa120\\f0\\fs21 \\u8226?\\tab b\\par\n"
            + "}", rtf);
    }

    [Fact]
    public void Rtf_EscapesItsSpecials_AndWritesEverythingPastAsciiAsUnicode()
    {
        var rtf = RichRtf.Write([P("a{b}\\c é 😀\tz")]);
        Assert.Contains("a\\{b\\}\\\\c \\u233? \\u-10179?\\u-8704?\\tab z\\par", rtf);
        Assert.All(rtf, c => Assert.True(c < 0x80, $"'{c}' is not ASCII"));
    }

    [Fact]
    public void Rtf_NumbersItemsAsTheEditorDoes()
    {
        var rtf = RichRtf.Write([
            Item(BlockKind.NumberedItem, "a"),
            Item(BlockKind.NumberedItem, "b"),
            RichBlock.Of(BlockKind.Paragraph, "c", 1),
            Item(BlockKind.NumberedItem, "d"),
            P("e"),
            Item(BlockKind.NumberedItem, "f"),
            Item(BlockKind.BulletItem, "g", 1),
            Item(BlockKind.TaskItem, "h") with { Checked = true },
        ]);
        Assert.Contains("1.\\tab a", rtf);
        Assert.Contains("2.\\tab b", rtf);
        Assert.Contains("\\li360\\sa45\\f0\\fs21 c", rtf);    // continuing b, under its text, as close as an item
        Assert.Contains("3.\\tab d", rtf);
        Assert.Contains("1.\\tab f", rtf);                    // a paragraph ended the list
        Assert.Contains("\\li720\\fi-360\\tx720\\sa45\\f0\\fs21 \\u9702?\\tab g", rtf);   // a circle one deeper
        Assert.Contains("\\u9745?\\tab h", rtf);              // a ticked box
    }

    [Fact]
    public void Rtf_MarksAreGroups_ALinkAField_AndColoursComeFromTheTable()
    {
        var rtf = RichRtf.Write([P("red link mark code",
            new RichSpan(0, 3, new RichMarks(Color: Color.FromHex("#FF0000"))),
            new RichSpan(4, 4, new RichMarks(Link: "https://x.test/\"q\"")),
            new RichSpan(9, 4, new RichMarks(InlineStyle.Italic | InlineStyle.Underline, Highlight: Color.FromHex("#FFFF0080"), Size: 20))),
            P("x", new RichSpan(0, 1, new RichMarks(InlineStyle.Code))),
        ]);
        Assert.Contains("{\\colortbl ;\\red255\\green0\\blue0;\\red9\\green105\\blue218;\\red255\\green255\\blue127;}", rtf);
        Assert.Contains("{\\cf1 red} ", rtf);
        // A link with nothing else on it: no stray space opening its text.
        Assert.Contains("{\\field{\\*\\fldinst{HYPERLINK \"https://x.test/%22q%22\"}}{\\fldrslt{\\ul\\cf2 {link}}}} ", rtf);
        // Half-transparent yellow laid over the page.
        Assert.Contains("{\\i\\ul\\fs30\\highlight3 mark}", rtf);
        Assert.Contains("{\\f1\\fs19 x}", rtf);   // code: the code face at 0.92 of the body
    }

    [Fact]
    public void Rtf_ATable_IsRowsOfCells_TheHeaderShadedAndRepeated()
    {
        var rtf = RichRtf.Write([.. Table(2, "h1", "h2", "a", "b")]);
        string edge = "\\brdrs\\brdrw15\\brdrcf1";
        string cell = $"\\clbrdrt{edge}\\clbrdrl{edge}\\clbrdrb{edge}\\clbrdrr{edge}";
        Assert.Contains($"\\trowd\\trgaph150\\trleft0\\trpaddt75\\trpaddft3\\trpaddb75\\trpaddfb3\\trhdr{cell}\\clcbpat2\\cellx4680{cell}\\clcbpat2\\cellx9360\n", rtf);
        Assert.Contains("\\pard\\plain\\intbl\\f0\\fs21\\b h1\\cell\n\\pard\\plain\\intbl\\f0\\fs21\\b h2\\cell\n\\row\n", rtf);
        Assert.Contains($"\\trowd\\trgaph150\\trleft0\\trpaddt75\\trpaddft3\\trpaddb75\\trpaddfb3{cell}\\cellx4680{cell}\\cellx9360\n", rtf);
        Assert.Contains("\\pard\\plain\\intbl\\f0\\fs21 a\\cell\n", rtf);
    }

    [Fact]
    public void Rtf_APictureIsAPngPict_WhenGiven_AndItsAltTextWhenNot()
    {
        var png = Png(4, 2);
        var rtf = RichRtf.Write([
            RichBlock.Picture(new RichImage("a.png", "A")),
            RichBlock.Picture(new RichImage("b.gif", "Bee", Width: 1000)),
            RichBlock.Picture(new RichImage("c.png", "", Width: 1000)),
        ], new RichExportOptions { Pictures = i => i.Source == "b.gif" ? "GIF89a\u0002\0\u0001\0"u8.ToArray() : png });
        // Its own size at 15 twips a pixel.
        Assert.Contains($"{{\\pict\\pngblip\\picw4\\pich2\\picwgoal60\\pichgoal30\n{Convert.ToHexString(png)}\n}}", rtf);
        Assert.Contains("[Bee]", rtf);   // RTF shows no GIF
        // No wider than the page, the height kept in proportion.
        Assert.Contains("\\picwgoal9360\\pichgoal4680\n", rtf);
    }

    [Fact]
    public void Rtf_EveryGroupCloses_AndTheTitleIsInTheProperties()
    {
        var rtf = RichRtf.Write(Everything(), new RichExportOptions { Title = "Notes {1}" });
        Assert.Contains("{\\info{\\title Notes \\{1\\}}}\n", rtf);
        int depth = 0;
        for (int i = 0; i < rtf.Length; i++)
        {
            if (rtf[i] == '\\') { i++; continue; }   // an escaped brace is text
            if (rtf[i] == '{') depth++;
            else if (rtf[i] == '}') { depth--; Assert.True(depth > 0 || i == rtf.Length - 1, $"the document closes at {i}"); }
        }
        Assert.Equal(0, depth);
        Assert.Contains("\\outlinelevel0", rtf);
        Assert.Contains("{\\field{\\*\\fldinst{HYPERLINK \"https://example.test\"}}{\\fldrslt{\\ul\\cf", rtf);   // the linked picture
        Assert.Contains("[A shot]", rtf);
        Assert.Contains("\\fs19\\f1 var x = 1;\\line return x < 2;\\par", rtf);
        Assert.Contains("\\qc\\f0\\fs21 Centred\\par", rtf);
    }

    // ── The clipboard and the pictures' headers ──

    [Fact]
    public void Copying_OffersRtf()
    {
        var formats = RichClipboard.Formats(new RichFragment([P("Say hi", Bold(4, 2))]));
        var rtf = Encoding.ASCII.GetString(formats[ClipboardFormats.Rtf]);
        Assert.StartsWith("{\\rtf1", rtf);
        Assert.Contains("Say {\\b hi}\\par", rtf);
    }

    [Fact]
    public void APicturesFormatAndSize_AreReadFromItsHeader()
    {
        Assert.Equal(new RichPictureFile("image/png", 640, 480), RichPictureFile.Read(Png(640, 480)));
        Assert.Equal(new RichPictureFile("image/gif", 2, 1), RichPictureFile.Read("GIF89a\u0002\0\u0001\0"u8));
        // A JPEG's size is in its frame header, after whatever segments come first.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xC0, 0, 17, 8, 0x01, 0xE0, 0x02, 0x80, 3];
        Assert.Equal(new RichPictureFile("image/jpeg", 640, 480), RichPictureFile.Read(jpeg));
        Assert.Equal("image/svg+xml", RichPictureFile.Read("﻿<?xml version=\"1.0\"?><svg/>"u8.ToArray())?.MediaType);
        Assert.Null(RichPictureFile.Read("not a picture"u8));
    }
}
