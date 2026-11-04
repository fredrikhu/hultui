using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Meridian;

/// <summary>
/// The response pane's "pretty" for markup: XML re-indented through a real parse, HTML through a
/// forgiving indenter, since HTML is rarely well-formed XML (<c>&lt;!doctype html&gt;</c>, a
/// <c>&lt;meta&gt;</c> with no end tag, an optional <c>&lt;/p&gt;</c> left out).
///
/// <para>The indenter puts each tag on a line of its own, two spaces deeper per open element, and
/// collapses runs of whitespace in text. An element holding only a short text stays on one line
/// (<c>&lt;title&gt;Example Domain&lt;/title&gt;</c>). The content of <c>script</c>, <c>style</c>,
/// <c>pre</c> and <c>textarea</c> is kept as it came, and so are comments. It never throws: what it
/// can't make sense of comes out as text. Pretty is for reading — raw shows the bytes as sent.</para>
/// </summary>
internal static class MarkupPretty
{
    private const string Step = "  ";

    /// <summary>A start tag, its text and its end tag join on one line up to this long.</summary>
    private const int OneLineMax = 100;

    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    /// <summary>Elements whose content is not markup, or whose whitespace matters.</summary>
    private static readonly HashSet<string> Verbatim = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "pre", "textarea",
    };

    /// <summary>Elements whose end tag HTML lets an author leave out: a second one opening
    /// closes the first.</summary>
    private static readonly HashSet<string> SelfClosingSiblings = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "li", "dt", "dd", "tr", "td", "th", "option",
    };

    /// <summary>The XML re-indented, or null when it doesn't parse (the caller then tries
    /// <see cref="Html"/>, or shows it as received).</summary>
    public static string? Xml(string text)
    {
        try
        {
            var doc = XDocument.Parse(text);
            string body = doc.ToString(SaveOptions.None).ReplaceLineEndings("\n");   // the writer uses the OS's
            return doc.Declaration is { } declaration ? declaration + "\n" + body : body;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    public static string Html(string text) => Indent(text, html: true);

    /// <summary>XML re-indented through a parse, or — when it doesn't parse, as a cut-off body
    /// doesn't — through the indenter with none of HTML's rules.</summary>
    public static string XmlOrIndented(string text) => Xml(text) ?? Indent(text, html: false);

    private static string Indent(string text, bool html)
    {
        var tokens = Tokenize(text, html);
        var output = new StringBuilder(text.Length + text.Length / 4);
        var open = new List<string>();

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            switch (token.Kind)
            {
                case Kind.Text:
                    string collapsed = Collapse(token.Raw);
                    if (collapsed.Length > 0) Line(output, open.Count, collapsed);
                    break;

                case Kind.Other:   // comment, doctype, CDATA, processing instruction
                    Line(output, open.Count, token.Raw.Trim());
                    break;

                case Kind.Start:
                    if (html && SelfClosingSiblings.Contains(token.Name) && open.Count > 0
                        && string.Equals(open[^1], token.Name, StringComparison.OrdinalIgnoreCase))
                        open.RemoveAt(open.Count - 1);

                    if (token.SelfClosed || (html && Void.Contains(token.Name)))
                    {
                        Line(output, open.Count, token.Raw);
                        break;
                    }
                    if (html && Verbatim.Contains(token.Name))
                    {
                        // The tokenizer gave the content as one text token, verbatim, then the end tag.
                        Line(output, open.Count, token.Raw);
                        if (i + 1 < tokens.Count && tokens[i + 1].Kind == Kind.Text)
                        {
                            // Its own lines and indentation, without the blank ones around it.
                            string content = tokens[++i].Raw.TrimEnd().TrimStart('\r', '\n');
                            if (content.Length > 0) output.Append(content).Append('\n');
                        }
                        if (i + 1 < tokens.Count && tokens[i + 1].Kind == Kind.End
                            && string.Equals(tokens[i + 1].Name, token.Name, StringComparison.OrdinalIgnoreCase))
                            Line(output, open.Count, tokens[++i].Raw);
                        break;
                    }
                    // <h1>Example Domain</h1> on one line.
                    if (i + 2 < tokens.Count && tokens[i + 1].Kind == Kind.Text && tokens[i + 2].Kind == Kind.End
                        && string.Equals(tokens[i + 2].Name, token.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        string inner = Collapse(tokens[i + 1].Raw);
                        if (token.Raw.Length + inner.Length + tokens[i + 2].Raw.Length <= OneLineMax)
                        {
                            Line(output, open.Count, token.Raw + inner + tokens[i + 2].Raw);
                            i += 2;
                            break;
                        }
                    }
                    Line(output, open.Count, token.Raw);
                    open.Add(token.Name);
                    break;

                case Kind.End:
                    int at = open.FindLastIndex(n => string.Equals(n, token.Name, StringComparison.OrdinalIgnoreCase));
                    if (at >= 0) open.RemoveRange(at, open.Count - at);   // closes what was left open inside it
                    Line(output, open.Count, token.Raw);
                    break;
            }
        }
        if (output.Length > 0 && output[^1] == '\n') output.Length--;
        return output.ToString();
    }

    private static void Line(StringBuilder output, int depth, string text)
    {
        for (int d = 0; d < depth; d++) output.Append(Step);
        output.Append(text).Append('\n');
    }

    private static string Collapse(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private enum Kind { Text, Start, End, Other }

    private readonly record struct Token(Kind Kind, string Raw, string Name = "", bool SelfClosed = false);

    private static List<Token> Tokenize(string text, bool html)
    {
        var tokens = new List<Token>();
        int i = 0, textStart = 0;
        while (i < text.Length)
        {
            if (text[i] != '<' || i + 1 >= text.Length) { i++; continue; }
            char next = text[i + 1];
            bool tagLike = char.IsLetter(next) || next == '/' || next == '!' || next == '?';
            if (!tagLike) { i++; continue; }   // a stray "<" in text: "a < b"

            if (i > textStart) tokens.Add(new Token(Kind.Text, text[textStart..i]));
            int end;
            if (string.CompareOrdinal(text, i, "<!--", 0, 4) == 0)
                end = EndOf(text, i + 4, "-->");
            else if (string.CompareOrdinal(text, i, "<![CDATA[", 0, 9) == 0)
                end = EndOf(text, i + 9, "]]>");
            else if (next == '?')
                end = EndOf(text, i + 2, "?>");
            else if (next == '!')
                end = EndOf(text, i + 2, ">");
            else
                end = TagEnd(text, i + 1);

            string raw = text[i..end];
            if (next is '!' or '?')
                tokens.Add(new Token(Kind.Other, raw));
            else if (next == '/')
                tokens.Add(new Token(Kind.End, raw, NameOf(raw, 2)));
            else
            {
                string name = NameOf(raw, 1);
                tokens.Add(new Token(Kind.Start, raw, name, raw.EndsWith("/>", StringComparison.Ordinal)));
                if (html && Verbatim.Contains(name) && !raw.EndsWith("/>", StringComparison.Ordinal))
                {
                    // Everything up to the matching end tag is one text token, as it came.
                    int close = text.IndexOf("</" + name, end, StringComparison.OrdinalIgnoreCase);
                    if (close < 0) close = text.Length;
                    if (close > end) tokens.Add(new Token(Kind.Text, text[end..close]));
                    end = close;
                }
            }
            i = textStart = end;
        }
        if (textStart < text.Length) tokens.Add(new Token(Kind.Text, text[textStart..]));
        return tokens;
    }

    /// <summary>Just past <paramref name="terminator"/>, or the end of the text.</summary>
    private static int EndOf(string text, int from, string terminator)
    {
        int at = text.IndexOf(terminator, from, StringComparison.Ordinal);
        return at < 0 ? text.Length : at + terminator.Length;
    }

    /// <summary>Just past a tag's closing <c>&gt;</c>, skipping any inside quoted attribute values.</summary>
    private static int TagEnd(string text, int from)
    {
        char quote = '\0';
        for (int i = from; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '"' or '\'') quote = c;
            else if (c == '>') return i + 1;
        }
        return text.Length;
    }

    private static string NameOf(string raw, int from)
    {
        int i = from;
        while (i < raw.Length && (char.IsLetterOrDigit(raw[i]) || raw[i] is '-' or ':' or '_')) i++;
        return raw[from..i];
    }
}
