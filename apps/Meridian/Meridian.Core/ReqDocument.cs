namespace Meridian.Core;

/// <summary>A line-numbered problem in a document. Lines are 1-based — these are
/// shown to people and jumped to in editors.</summary>
public sealed record ParseError(int Line, string Message);

public sealed record ReqHeader(string Name, string Value, int Line);

/// <summary>The body block: its declared type (may be empty), the fence marker
/// that closed it, and the verbatim content.</summary>
public sealed record ReqBody(string Type, string Marker, string Content, int Line);

/// <summary>An <c>assert</c>/<c>extract</c>/<c>auth</c> line, kept as its raw
/// argument string. The typed semantics land with their features (P5, P10); the
/// format carries them losslessly from day one.</summary>
public sealed record ReqDirective(string Args, int Line);

/// <summary>
/// A parsed <c>.req</c> file (or <c>_folder.req</c> — same grammar, request line
/// optional). Line-oriented and lossless: every line of the source, including
/// comments, blanks, and even malformed lines, is preserved verbatim, so
/// <see cref="Serialize"/> reproduces the input byte for byte. Parsing never
/// throws — problems become <see cref="Errors"/> with 1-based line numbers.
///
/// <para>The grammar, one directive per line:</para>
/// <code>
/// # comment
/// GET {{base_url}}/v1/charges/{{charge_id}}
/// name Get charge
/// header Authorization: Bearer {{token}}
/// body json
/// { "amount": 2499 }
/// end
/// auth bearer {{token}}
/// assert status equals 200
/// extract body $.id -> charge_id
/// </code>
/// <para>A body fence closes at a line equal to its marker — <c>end</c> by
/// default, or a custom one via <c>body json until MARK</c> for content that
/// itself contains an <c>end</c> line.</para>
/// </summary>
public sealed class ReqDocument
{
    private readonly List<SourceLine> _lines;

    public string? Method { get; private set; }
    public string? Url { get; private set; }
    /// <summary>1-based line of the request line, 0 when absent.</summary>
    public int RequestLineNumber { get; private set; }
    /// <summary>The <c>name</c> directive's value, or null.</summary>
    public string? Name { get; private set; }
    public IReadOnlyList<ReqHeader> Headers => _headers;
    public ReqBody? Body { get; private set; }
    public ReqDirective? Auth { get; private set; }
    public IReadOnlyList<ReqDirective> Asserts => _asserts;
    public IReadOnlyList<ReqDirective> Extracts => _extracts;
    public IReadOnlyList<ParseError> Errors => _errors;

    private readonly List<ReqHeader> _headers = [];
    private readonly List<ReqDirective> _asserts = [];
    private readonly List<ReqDirective> _extracts = [];
    private readonly List<ParseError> _errors = [];

    private ReqDocument(List<SourceLine> lines) => _lines = lines;

    public string Serialize() => LineBuffer.Join(_lines);

    public static ReqDocument Parse(string text)
    {
        var doc = new ReqDocument(LineBuffer.Split(text));
        var lines = doc._lines;

        for (int i = 0; i < lines.Count; i++)
        {
            int lineNo = i + 1;
            string raw = lines[i].Raw;
            string line = raw.TrimStart();

            if (line.Length == 0 || line.StartsWith('#')) continue;

            string keyword = FirstWord(line);
            string rest = line[keyword.Length..].TrimStart();

            switch (keyword)
            {
                case "name":
                    if (rest.Length == 0) doc.Error(lineNo, "name needs a value");
                    else doc.Name = rest;
                    continue;

                case "header":
                {
                    int colon = rest.IndexOf(':');
                    if (colon <= 0)
                        doc.Error(lineNo, "header needs \"Name: value\"");
                    else
                        doc._headers.Add(new ReqHeader(rest[..colon].TrimEnd(), rest[(colon + 1)..].TrimStart(), lineNo));
                    continue;
                }

                case "body":
                {
                    if (doc.Body != null) { doc.Error(lineNo, "a second body block"); }
                    var (type, marker) = ParseBodyFence(rest);
                    int close = FindMarker(lines, i + 1, marker);
                    if (close < 0)
                    {
                        doc.Error(lineNo, $"body never closed — no \"{marker}\" line follows");
                        i = lines.Count;   // rest of the file is body content
                        continue;
                    }
                    if (doc.Body == null)
                    {
                        var content = LineBuffer.Join(lines.Skip(i + 1).Take(close - i - 1));
                        // Content excludes the closing marker line; strip the final
                        // content line's terminator so the value is the text alone.
                        doc.Body = new ReqBody(type, marker, TrimFinalTerminator(content, lines, i + 1, close), lineNo);
                    }
                    i = close;   // skip content + marker
                    continue;
                }

                case "auth":
                    if (rest.Length == 0) doc.Error(lineNo, "auth needs a value");
                    else if (doc.Auth != null) doc.Error(lineNo, "a second auth line");
                    else doc.Auth = new ReqDirective(rest, lineNo);
                    continue;

                case "assert":
                    if (rest.Length == 0) doc.Error(lineNo, "assert needs a condition");
                    else doc._asserts.Add(new ReqDirective(rest, lineNo));
                    continue;

                case "extract":
                    if (rest.Length == 0) doc.Error(lineNo, "extract needs a source and a variable");
                    else doc._extracts.Add(new ReqDirective(rest, lineNo));
                    continue;
            }

            // A request line: an ALL-CAPS method followed by a URL.
            if (IsMethodToken(keyword) && rest.Length > 0)
            {
                if (doc.Method != null)
                    doc.Error(lineNo, "a second request line — one request per file");
                else
                {
                    doc.Method = keyword;
                    doc.Url = rest;
                    doc.RequestLineNumber = lineNo;
                }
                continue;
            }

            doc.Error(lineNo, IsMethodToken(keyword)
                ? $"{keyword} needs a URL"
                : $"unknown directive \"{keyword}\"");
        }

        return doc;
    }

    private void Error(int line, string message) => _errors.Add(new ParseError(line, message));

    private static string FirstWord(string s)
    {
        int i = 0;
        while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
        return s[..i];
    }

    private static bool IsMethodToken(string word) =>
        word.Length is >= 3 and <= 16 && word.All(c => c is >= 'A' and <= 'Z' or '-');

    private static (string Type, string Marker) ParseBodyFence(string rest)
    {
        // "json", "json until MARK", "until MARK", or nothing.
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts switch
        {
            [] => ("", "end"),
            ["until", var marker] => ("", marker),
            [var type] => (type, "end"),
            [var type, "until", var marker] => (type, marker),
            _ => (parts[0], "end"),
        };
    }

    private static int FindMarker(List<SourceLine> lines, int from, string marker)
    {
        for (int i = from; i < lines.Count; i++)
            if (lines[i].Raw.Trim() == marker)
                return i;
        return -1;
    }

    private static string TrimFinalTerminator(string content, List<SourceLine> lines, int first, int close)
    {
        if (close - first <= 0) return "";
        var last = lines[close - 1];
        return last.Terminator.Length > 0 ? content[..^last.Terminator.Length] : content;
    }
}
