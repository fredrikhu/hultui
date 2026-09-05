using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace VesperCommander;

/// <summary>What a text can be re-indented as.</summary>
public enum PrettyKind { None, Json, Xml }

/// <summary>
/// Re-indents JSON and XML for the viewer. A minified API response or a one-line settings
/// file is unreadable as it is stored; this parses it and writes it back out with one thing
/// per line, so the viewer can show the shape of it.
///
/// <para>The parse is the real one — <see cref="JsonDocument"/> and <see cref="XDocument"/> —
/// rather than a bracket-counting reformatter, so what comes out is what the file means.
/// The cost is that a file with a stray comma is refused rather than half-formatted; the
/// error says where.</para>
///
/// <para>Detection only decides whether to <em>offer</em> the format. A press of the key
/// tries regardless, by sniffing the first non-blank character, so a JSON body saved as
/// <c>.txt</c> is one keypress away too.</para>
/// </summary>
public static class PrettyPrint
{
    // A byte-order mark decoded into the text. Neither parser wants it, and it is the one
    // "blank" a TrimStart leaves behind.
    private const char Bom = (char)0xFEFF;

    private static readonly HashSet<string> JsonExt = new(StringComparer.OrdinalIgnoreCase)
        { ".json", ".jsonc", ".geojson", ".webmanifest", ".har", ".ipynb" };

    private static readonly HashSet<string> XmlExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".xaml", ".svg", ".csproj", ".vbproj", ".fsproj", ".props", ".targets",
        ".config", ".resx", ".nuspec", ".plist", ".xsd", ".xsl", ".xslt", ".wsdl", ".rss", ".atom",
        ".opml", ".gpx", ".kml", ".manifest", ".vsix", ".xlf", ".pom", ".xib", ".storyboard",
    };

    // Extensions that say nothing about the content, so the first character decides.
    private static readonly HashSet<string> Opaque = new(StringComparer.OrdinalIgnoreCase)
        { "", ".txt", ".log", ".tmp", ".bak", ".dat", ".out", ".resp", ".body", ".dump" };

    /// <summary>What the viewer should offer to format <paramref name="path"/> as, from its
    /// extension first and — for an extension that says nothing — from its first character.
    /// <see cref="PrettyKind.None"/> for a file the key would only refuse.</summary>
    public static PrettyKind Detect(string path, IReadOnlyList<string> lines)
    {
        string ext = Path.GetExtension(path);
        if (JsonExt.Contains(ext)) return PrettyKind.Json;
        if (XmlExt.Contains(ext)) return PrettyKind.Xml;
        return Opaque.Contains(ext) ? Sniff(lines) : PrettyKind.None;
    }

    /// <summary>The kind the first non-blank character suggests. <c>{</c> and <c>[</c> open a
    /// JSON value; <c>&lt;</c> opens a tag. Anything else is neither.</summary>
    public static PrettyKind Sniff(IReadOnlyList<string> lines)
    {
        for (int i = 0; i < lines.Count && i < 64; i++)
        {
            var t = lines[i].AsSpan().TrimStart();
            if (t.Length == 0) continue;
            if (t[0] == Bom) t = t[1..];
            if (t.Length == 0) continue;
            return t[0] switch
            {
                '{' or '[' => PrettyKind.Json,
                '<' => PrettyKind.Xml,
                _ => PrettyKind.None,
            };
        }
        return PrettyKind.None;
    }

    /// <summary>Format <paramref name="lines"/> as <paramref name="kind"/>. Returns the
    /// re-indented lines, or null with <paramref name="error"/> saying why not — a short
    /// sentence with a line number, fit for a footer.</summary>
    public static string[]? Format(PrettyKind kind, IReadOnlyList<string> lines, out string? error)
    {
        string text = string.Join('\n', lines);
        string? formatted = kind switch
        {
            PrettyKind.Json => FormatJson(text, out error),
            PrettyKind.Xml => FormatXml(text, out error),
            _ => Fail("Nothing to format", out error),
        };
        return formatted?.Split('\n');
    }

    private static string? Fail(string message, out string? error) { error = message; return null; }

    private static string? FormatJson(string text, out string? error)
    {
        try
        {
            // Trailing commas and comments are accepted on the way in — a jsonc settings file
            // is exactly the sort of thing this is for — and dropped on the way out, since
            // JsonDocument has nowhere to keep a comment.
            using var doc = JsonDocument.Parse(text.TrimStart(Bom), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 512,
            });
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
            {
                Indented = true,
                // The default encoder escapes every non-ASCII character as \uXXXX, which turns
                // "Göteborg" into line noise. Relaxed keeps text as text; this is a display,
                // not something sent to a browser.
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
                doc.WriteTo(w);
            error = null;
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length).Replace("\r\n", "\n");
        }
        catch (JsonException ex)
        {
            error = ex.LineNumber is { } line
                ? $"Not valid JSON (line {line + 1})"
                : "Not valid JSON";
            return null;
        }
        catch (Exception ex) { error = "Not valid JSON: " + ex.Message; return null; }
    }

    private static string? FormatXml(string text, out string? error)
    {
        try
        {
            // No DTD processing and no resolver: a viewer must not fetch anything a file
            // points at, and a billion-laughs entity expansion is a parse, not a display.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreWhitespace = true,
                IgnoreComments = false,
                IgnoreProcessingInstructions = false,
            };
            XDocument doc;
            using (var reader = XmlReader.Create(new StringReader(text.TrimStart(Bom)), settings))
                doc = XDocument.Load(reader, LoadOptions.None);

            ReindentText(doc);

            var ws = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = IndentUnit,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                Encoding = new UTF8Encoding(false),
                // The writer's own declaration is generated from its settings — it would say
                // utf-8 whatever the file said, and add an encoding to one that had none. The
                // file's is written back as parsed instead; a file without one doesn't grow one.
                OmitXmlDeclaration = true,
            };
            using var ms = new MemoryStream();
            using (var w = XmlWriter.Create(ms, ws)) doc.Save(w);
            error = null;
            var body = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            return doc.Declaration is { } decl ? decl.ToString() + "\n" + body : body;
        }
        catch (XmlException ex)
        {
            error = ex.LineNumber > 0 ? $"Not valid XML (line {ex.LineNumber})" : "Not valid XML";
            return null;
        }
        catch (Exception ex) { error = "Not valid XML: " + ex.Message; return null; }
    }

    private const string IndentUnit = "  ";

    /// <summary>
    /// Re-indent the text <em>inside</em> elements. The writer indents between elements and
    /// leaves a text node byte for byte — whitespace in text is significant to XML — so a
    /// multi-line text node keeps the indentation it had in the file, and the closing tag
    /// lands wherever the text's trailing whitespace put it. A doc comment's
    /// <c>&lt;summary&gt;</c> came out with its lines twelve spaces in and its closing tag
    /// eight, whatever depth the element was written at.
    ///
    /// <para>So every text node that spans lines is rewritten: each line trimmed and set one
    /// level deeper than its element, and — when the node is the element's last child — the
    /// closing tag put on a line of its own at the element's depth. Inline elements in the
    /// flow (<c>&lt;see cref/&gt;</c> between two runs of text) stay in the flow, since the
    /// text on either side of them keeps its own leading and trailing space. Single-line
    /// text (<c>&lt;item&gt;x&lt;/item&gt;</c>) is not touched, and nothing under
    /// <c>xml:space="preserve"</c> is.</para>
    /// </summary>
    private static void ReindentText(XDocument doc)
    {
        var preserve = XNamespace.Xml + "space";
        var touched = new HashSet<XElement>();
        foreach (var node in doc.DescendantNodes().OfType<XText>().ToList())
        {
            if (node is XCData || node.Parent is not { } parent) continue;
            if (!node.Value.Contains('\n')) continue;
            if (parent.AncestorsAndSelf().Any(a => (string?)a.Attribute(preserve) == "preserve")) continue;
            touched.Add(parent);

            int depth = parent.Ancestors().Count();
            string inner = Indent(depth + 1);
            bool first = node.PreviousNode is null;
            bool last = node.NextNode is null;

            var lines = node.Value.Split('\n');
            // Indentation is the file's, not the text's: strip it from every line after the
            // first, and the trailing whitespace before every newline. The first line's leading
            // space and the last line's trailing space are the gaps around an inline element
            // ("when <see/> drains"), kept unless this node opens or closes the element.
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) lines[i] = lines[i].TrimStart();
                if (i < lines.Length - 1) lines[i] = lines[i].TrimEnd();
            }
            int start = 0, end = lines.Length;
            if (first && lines[0].Length == 0) start++;                       // the newline after the open tag
            if (last && end > start && lines[end - 1].Trim().Length == 0) end--; // the indent before the close tag

            var sb = new StringBuilder();
            if (first) sb.Append('\n').Append(inner);
            for (int i = start; i < end; i++)
            {
                if (i > start)
                {
                    sb.Append('\n');
                    // A blank line stays blank; the line that positions a following element
                    // (the last one, empty) carries the indent that element will sit at.
                    if (lines[i].Length > 0 || (i == end - 1 && !last)) sb.Append(inner);
                }
                sb.Append(lines[i]);
            }
            if (last) sb.Append('\n').Append(Indent(depth));
            node.Value = sb.ToString();
        }

        // Once an element has mixed content the writer stops indenting inside it, including
        // its own closing tag. Where the last child is an element, the text above put that
        // element on a line of its own; the closing tag still needs one.
        foreach (var parent in touched)
            if (parent.LastNode is XElement)
                parent.Add(new XText("\n" + Indent(parent.Ancestors().Count())));
    }

    private static string Indent(int depth) => string.Concat(Enumerable.Repeat(IndentUnit, depth));
}
