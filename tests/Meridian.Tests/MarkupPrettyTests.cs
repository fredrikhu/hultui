using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

/// <summary>"pretty" for HTML and XML bodies: until 2026-11-04 it re-indented JSON alone, and
/// an HTML page (www.example.com's) showed exactly as raw did.</summary>
public class MarkupPrettyTests
{
    private static string Lf(string s) => s.ReplaceLineEndings("\n");

    [Fact]
    public void AnHtmlPageIsIndentedWithShortElementsOnOneLine()
    {
        const string page = """
            <!doctype html><html><head><title>Example Domain</title>
            <meta charset="utf-8" /><meta name="viewport" content="width=device-width">
            <style type="text/css">
                body { margin: 0; }
            </style></head><body><div><h1>Example Domain</h1>
            <p>This domain is for use in illustrative examples in documents.</p>
            <p><a href="https://www.iana.org/domains/example">More information...</a></p>
            </div></body></html>
            """;

        Assert.Equal(Lf("""
            <!doctype html>
            <html>
              <head>
                <title>Example Domain</title>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width">
                <style type="text/css">
                body { margin: 0; }
                </style>
              </head>
              <body>
                <div>
                  <h1>Example Domain</h1>
                  <p>This domain is for use in illustrative examples in documents.</p>
                  <p>
                    <a href="https://www.iana.org/domains/example">More information...</a>
                  </p>
                </div>
              </body>
            </html>
            """), MarkupPretty.Html(Lf(page)));
    }

    [Fact]
    public void LeftOutEndTagsAndMismatchesDoNotDriftTheIndent()
    {
        // <li> closes the <li> before it; </div> closes the <span> left open inside it.
        Assert.Equal(Lf("""
            <ul>
              <li>
                one
              <li>
                two
            </ul>
            <div>
              <span>
                x
            </div>
            <p>after</p>
            """), MarkupPretty.Html("<ul><li>one<li>two</ul><div><span>x</div><p>after</p>"));
    }

    [Fact]
    public void ScriptAndPreKeepTheirContentAndCommentsStay()
    {
        string html = "<body><!-- keep  me --><pre>a\n  b</pre><script>if (a < b) { go(); }</script></body>";
        Assert.Equal(Lf("""
            <body>
              <!-- keep  me -->
              <pre>
            a
              b
              </pre>
              <script>
            if (a < b) { go(); }
              </script>
            </body>
            """), MarkupPretty.Html(html));
    }

    [Fact]
    public void QuotedGreaterThanAndStrayLessThanAreText()
    {
        Assert.Equal(Lf("""
            <p>
              <a title="1 > 0" href="#">x</a>
              a < b
            </p>
            """), MarkupPretty.Html("""<p><a title="1 > 0" href="#">x</a> a < b</p>"""));
    }

    [Fact]
    public void XmlIsReIndentedThroughAParseAndKeepsItsDeclaration()
    {
        Assert.Equal(Lf("""
            <?xml version="1.0" encoding="utf-8"?>
            <feed>
              <link href="https://a.example/" />
              <entry>
                <id>1</id>
              </entry>
            </feed>
            """), MarkupPretty.XmlOrIndented("""<?xml version="1.0" encoding="utf-8"?><feed><link href="https://a.example/"/><entry><id>1</id></entry></feed>"""));
    }

    [Fact]
    public void CutOffXmlIsIndentedWithoutHtmlsRules()
    {
        // <link> holds content in XML (an RSS item's), so it is no void element here.
        Assert.Equal(Lf("""
            <rss>
              <item>
                <link>https://a.example/1</link>
                <title>
            """), MarkupPretty.XmlOrIndented("<rss><item><link>https://a.example/1</link><title>"));
    }

    [Fact]
    public void PrettyPicksTheFormatterByContentType()
    {
        static HttpResponseData Response(string contentType, string body) => new(200, "OK", [], contentType,
            new ResponseBody(BodyKind.Text, body, [], body.Length, false, null),
            new PhaseTimings(default, default, default, default, default, default, false, "HTTP/1.1", null), "https://a.example/");

        Assert.Equal("<p>\n  <b>x</b>\n</p>", Lf(ResponseView.Prettify(Response("text/html; charset=utf-8", "<p><b>x</b></p>"))));
        Assert.Equal("<a>\n  <b />\n</a>", Lf(ResponseView.Prettify(Response("application/xml", "<a><b/></a>"))));
        Assert.Equal("{\n  \"a\": 1\n}", Lf(ResponseView.Prettify(Response("application/json", "{\"a\":1}"))));
        Assert.Equal("<p><b>x</b></p>", Lf(ResponseView.Prettify(Response("text/plain", "<p><b>x</b></p>"))));
    }
}
