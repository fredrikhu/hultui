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

            var ws = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                Encoding = new UTF8Encoding(false),
                // A file that had no declaration doesn't grow one.
                OmitXmlDeclaration = doc.Declaration is null,
            };
            using var ms = new MemoryStream();
            using (var w = XmlWriter.Create(ms, ws)) doc.Save(w);
            error = null;
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }
        catch (XmlException ex)
        {
            error = ex.LineNumber > 0 ? $"Not valid XML (line {ex.LineNumber})" : "Not valid XML";
            return null;
        }
        catch (Exception ex) { error = "Not valid XML: " + ex.Message; return null; }
    }
}
