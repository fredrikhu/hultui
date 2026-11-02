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

    /// <summary>
    /// Where the body's content is held as one entry of <see cref="_lines"/>, or -1. The entry's
    /// text is the body's content, every line of it with the entry's terminator between them, and
    /// stands for those lines, each ended by that terminator: it serializes to the same bytes. Only
    /// the block that ends the document is held this way (<see cref="PackTrailingBody"/>, after a
    /// parse or an edit; <see cref="TrySpliceBody"/>), which is where the body editor's first edit
    /// puts it, so the entry is followed by the marker line alone and every line before it keeps
    /// its index. <see cref="Reparse"/> reads it as it is (it is no marker line, and the body's
    /// content is its text), and an edit that rewrites a line before it in place leaves it be; an
    /// edit that adds or removes lines goes through <see cref="Lines"/>, which splits it back first.
    /// </summary>
    /// <remarks>A keystroke in an 89,000-character body split the whole body into lines, trimmed
    /// each, and walked and joined them again to reparse the document: 1.2 MB of copies a key, and
    /// any other edit to such a request walked and joined it again (plan 17 round 6). Now a
    /// keystroke in the body replaces the block's content in place, and the rest copy none of it.</remarks>
    private int _packedBody = -1;

    /// <summary>The lines, each entry one line: <see cref="_lines"/> with the body's content split
    /// back into lines if it was held as one.</summary>
    private List<SourceLine> Lines
    {
        get
        {
            Unpack();
            return _lines;
        }
    }

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

    /// <summary>Whether <see cref="Serialize"/> would return <paramref name="text"/>, compared in
    /// place: the stale-preview banner asks on every build of the Assertions tab, and a whole new
    /// copy of the document a keystroke is what that cost.</summary>
    public bool SerializesTo(string? text)
    {
        if (text is null) return false;
        var rest = text.AsSpan();
        foreach (var line in _lines)
        {
            if (!rest.StartsWith(line.Raw, StringComparison.Ordinal)) return false;
            rest = rest[line.Raw.Length..];
            if (!rest.StartsWith(line.Terminator, StringComparison.Ordinal)) return false;
            rest = rest[line.Terminator.Length..];
        }
        return rest.IsEmpty;
    }

    /// <summary>True once any edit method has run; cleared by <see cref="MarkSaved"/>.
    /// This is what the tab dirty dot and the footer's "● unsaved" read.</summary>
    public bool Edited { get; private set; }

    public void MarkSaved() => Edited = false;

    public static ReqDocument Parse(string text)
    {
        var doc = new ReqDocument(LineBuffer.Split(text));
        doc.Reparse();
        doc.PackTrailingBody();
        return doc;
    }

    /// <summary>Re-derives every parsed field from <see cref="_lines"/>. Runs
    /// after each edit so line numbers and typed views stay consistent with the
    /// text — the lines are the single source of truth.</summary>
    private void Reparse()
    {
        Method = null; Url = null; RequestLineNumber = 0; Name = null;
        Body = null; Auth = null;
        _headers.Clear(); _asserts.Clear(); _extracts.Clear(); _errors.Clear();

        var doc = this;
        var lines = doc._lines;   // a packed body's content is read as one line (see _packedBody)

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
                        doc.Body = new ReqBody(type, marker, JoinContent(lines, i + 1, close), lineNo);
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
    }

    // ── Edits ────────────────────────────────────────────────────────────
    // Every edit rewrites only its own line(s), preserving comments, blanks,
    // unknown constructs, and each untouched line's own terminator, then
    // reparses so the typed views stay true — except a body edit to the block
    // that ends the document, which knows what the reparse would find
    // (TrySpliceBody). A line rewritten in place is one before the body
    // block (_packedBody), so its index is its line number less one.

    /// <summary>Replaces the request line, or inserts one at the top when the
    /// file has none.</summary>
    public void SetRequestLine(string method, string url)
    {
        string raw = $"{method} {url}";
        if (RequestLineNumber > 0)
            _lines[RequestLineNumber - 1] = _lines[RequestLineNumber - 1] with { Raw = raw };
        else
            Lines.Insert(0, new SourceLine(raw, DominantTerminator()));
        Touch();
    }

    /// <summary>Rewrites the header at <paramref name="index"/> (into
    /// <see cref="Headers"/>) in place.</summary>
    public void SetHeader(int index, string name, string value)
    {
        var line = _headers[index].Line;
        _lines[line - 1] = _lines[line - 1] with { Raw = $"header {name}: {value}" };
        Touch();
    }

    /// <summary>Appends a header after the last existing header, or after the
    /// request line when there are none yet.</summary>
    public void AddHeader(string name, string value)
    {
        int insertAfter = _headers.Count > 0 ? _headers[^1].Line
            : RequestLineNumber > 0 ? RequestLineNumber
            : 0;
        InsertLine(insertAfter, $"header {name}: {value}");
        Touch();
    }

    public void RemoveHeader(int index)
    {
        Lines.RemoveAt(_headers[index].Line - 1);
        Touch();
    }

    /// <summary>
    /// Replaces the body block's content (and type), or appends a new block at
    /// the end of the file. The writer picks the fence marker: <c>end</c>
    /// unless the content itself contains such a line, in which case a
    /// collision-free one is generated — the reader's <c>until</c> form.
    /// </summary>
    public void SetBody(string type, string content)
    {
        if (TrySpliceBody(type, content)) return;

        // Remove the existing block (fence line through marker line) first.
        if (Body is { } existing)
        {
            int fence = existing.Line - 1;
            int marker = FindMarker(Lines, fence + 1, existing.Marker);
            Lines.RemoveRange(fence, (marker < 0 ? Lines.Count : marker + 1) - fence);
        }
        if (content.Length == 0 && type.Length == 0) { Touch(); return; }

        string chosen = "end";
        string[] contentLines = content.Length == 0 ? [] : content.ReplaceLineEndings("\n").Split('\n');
        if (contentLines.Any(l => l.Trim() == chosen))
            chosen = NewMarker();

        string term = DominantTerminator();
        EnsureFinalTerminator(term);
        Lines.Add(new SourceLine(FenceLine(type, chosen), term));
        foreach (var line in contentLines)
            Lines.Add(new SourceLine(line, term));
        Lines.Add(new SourceLine(chosen, term));
        Touch();
    }

    /// <summary>
    /// <see cref="SetBody"/> where the body block already ends the document — every keystroke in
    /// the body editor but the first, which moves the block there. The result is the one the whole
    /// path makes: the block written again at the end in the lines' prevailing ending (with the
    /// content's own endings changed to it), and everything parsed from the lines before it as it
    /// was, since the lines before the block are the ones it parsed and nothing follows the block.
    /// Without the work: the content becomes one entry (<see cref="_packedBody"/>) instead of a line
    /// each, and is the body's content as it is, so nothing is split, trimmed, joined or reparsed.
    /// False, having changed nothing, where it does not apply.
    /// </summary>
    private bool TrySpliceBody(string type, string content)
    {
        if (Body is not { } existing || (content.Length == 0 && type.Length == 0)) return false;
        int fence = existing.Line - 1;
        int marker = _packedBody >= 0 ? _packedBody + 1 : FindMarker(_lines, fence + 1, existing.Marker);
        if (marker != _lines.Count - 1) return false;   // something follows the block

        // What the whole path counts once the block is out: the lines before it.
        string term = DominantTerminator(_lines, fence);
        // ReplaceLineEndings returns the content itself when every ending in it is already this one.
        string text = content.ReplaceLineEndings(term);
        string chosen = HasLine(text, "end") ? NewMarker() : "end";
        // A marker the content holds as a line would close the block early: the whole path, which
        // parses what it wrote, says what that means. (Not seen: a generated one.)
        if (chosen != "end" && HasLine(text, chosen)) return false;
        string fenceRaw = FenceLine(type, chosen);
        var (parsedType, parsedMarker) = ParseBodyFence(fenceRaw[4..].TrimStart());
        if (parsedMarker != chosen) return false;

        if (fence > 0 && _lines[fence - 1].Terminator.Length == 0)
            _lines[fence - 1] = _lines[fence - 1] with { Terminator = term };
        _lines.RemoveRange(fence, _lines.Count - fence);
        _lines.Add(new SourceLine(fenceRaw, term));
        _packedBody = -1;
        if (text.Length > 0)
        {
            _packedBody = _lines.Count;
            _lines.Add(new SourceLine(text, term));
        }
        _lines.Add(new SourceLine(chosen, term));
        Body = new ReqBody(parsedType, chosen, text, fence + 1);
        Edited = true;
        return true;
    }

    /// <summary>Splits the body's content back into one entry a line (see <see cref="_packedBody"/>).</summary>
    private void Unpack()
    {
        if (_packedBody < 0) return;
        var packed = _lines[_packedBody];
        var split = packed.Raw.Split(packed.Terminator);
        var lines = new SourceLine[split.Length];
        for (int i = 0; i < split.Length; i++) lines[i] = new SourceLine(split[i], packed.Terminator);
        _lines.RemoveAt(_packedBody);
        _lines.InsertRange(_packedBody, lines);
        _packedBody = -1;
    }

    private static string FenceLine(string type, string marker) => marker == "end"
        ? (type.Length > 0 ? $"body {type}" : "body")
        : (type.Length > 0 ? $"body {type} until {marker}" : $"body until {marker}");

    /// <summary>A fence marker for content that holds an <c>end</c> line.</summary>
    private static string NewMarker() => $"end-{Guid.NewGuid():N}"[..12];

    /// <summary>Whether a line of <paramref name="text"/>, its white space trimmed, is
    /// <paramref name="marker"/>: what <see cref="FindMarker"/> would stop at.</summary>
    private static bool HasLine(string text, string marker)
    {
        var rest = text.AsSpan();
        while (true)
        {
            int nl = rest.IndexOf('\n');
            if ((nl < 0 ? rest : rest[..nl]).Trim().SequenceEqual(marker)) return true;
            if (nl < 0) return false;
            rest = rest[(nl + 1)..];
        }
    }

    /// <summary>Sets the <c>auth</c> line, rewriting the existing one in place
    /// or appending after the headers. "none" removes it — the absence of a
    /// line is what lets a folder default apply again.</summary>
    public void SetAuth(string args)
    {
        if (Auth is { } existing)
        {
            if (args == "none") Lines.RemoveAt(existing.Line - 1);
            else _lines[existing.Line - 1] = _lines[existing.Line - 1] with { Raw = $"auth {args}" };
            Touch();
            return;
        }
        if (args == "none") { Touch(); return; }
        int after = _headers.Count > 0 ? _headers[^1].Line
            : RequestLineNumber > 0 ? RequestLineNumber
            : Lines.Count;
        InsertLine(after, $"auth {args}");
        Touch();
    }

    /// <summary>Rewrites the assert at <paramref name="index"/> in place.</summary>
    public void SetAssert(int index, string args)
    {
        var line = _asserts[index].Line;
        _lines[line - 1] = _lines[line - 1] with { Raw = $"assert {args}" };
        Touch();
    }

    /// <summary>Appends an assert after the last one, or at the end of the file.</summary>
    public void AddAssert(string args) => AddDirectiveLine("assert", args, _asserts);

    public void RemoveAssert(int index)
    {
        Lines.RemoveAt(_asserts[index].Line - 1);
        Touch();
    }

    /// <summary>Rewrites the extract at <paramref name="index"/> in place.</summary>
    public void SetExtract(int index, string args)
    {
        var line = _extracts[index].Line;
        _lines[line - 1] = _lines[line - 1] with { Raw = $"extract {args}" };
        Touch();
    }

    public void AddExtract(string args) => AddDirectiveLine("extract", args, _extracts);

    public void RemoveExtract(int index)
    {
        Lines.RemoveAt(_extracts[index].Line - 1);
        Touch();
    }

    private void AddDirectiveLine(string keyword, string args, List<ReqDirective> kin)
    {
        int insertAfter = kin.Count > 0 ? kin[^1].Line : Lines.Count;
        InsertLine(insertAfter, $"{keyword} {args}");
        Touch();
    }

    private void Touch()
    {
        Edited = true;
        Reparse();
        PackTrailingBody();
    }

    /// <summary>Holds the body's content as one entry (<see cref="_packedBody"/>) when its block
    /// ends the document and its lines all end alike, as the body editor writes them. The entry is
    /// the parsed content itself.</summary>
    private void PackTrailingBody()
    {
        if (_packedBody >= 0 || Body is not { } body) return;
        int first = body.Line, close = FindMarker(_lines, first, body.Marker);
        if (close != _lines.Count - 1 || close - first < 2) return;
        string term = _lines[first].Terminator;
        for (int i = first + 1; i < close; i++)
            if (_lines[i].Terminator != term) return;
        _lines.RemoveRange(first + 1, close - first - 1);
        _lines[first] = new SourceLine(body.Content, term);
        _packedBody = first;
    }

    private void InsertLine(int afterLineNumber, string raw)
    {
        string term = DominantTerminator();
        if (afterLineNumber >= Lines.Count) EnsureFinalTerminator(term);
        Lines.Insert(Math.Min(afterLineNumber, Lines.Count), new SourceLine(raw, term));
    }

    /// <summary>The file's prevailing line ending, for lines this edit adds.</summary>
    private string DominantTerminator() => DominantTerminator(Lines, Lines.Count);

    /// <summary>The prevailing line ending of the first <paramref name="count"/> lines.</summary>
    private static string DominantTerminator(List<SourceLine> lines, int count)
    {
        int crlf = 0, lf = 0;
        for (int i = 0; i < count; i++)
        {
            var terminator = lines[i].Terminator;
            if (terminator == "\r\n") crlf++;
            else if (terminator == "\n") lf++;
        }
        return crlf > lf ? "\r\n" : "\n";
    }

    /// <summary>A final line with no terminator gets one before anything is
    /// appended after it.</summary>
    private void EnsureFinalTerminator(string term)
    {
        if (Lines.Count > 0 && Lines[^1].Terminator.Length == 0)
            Lines[^1] = Lines[^1] with { Terminator = term };
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

    /// <summary>The first line from <paramref name="from"/> that is <paramref name="marker"/> with
    /// its white space trimmed, or -1. Trims in place: a trimmed copy of each indented line of a
    /// body was made on every reparse.</summary>
    private static int FindMarker(List<SourceLine> lines, int from, string marker)
    {
        for (int i = from; i < lines.Count; i++)
            if (lines[i].Raw.AsSpan().Trim().SequenceEqual(marker))
                return i;
        return -1;
    }

    /// <summary>The body's content: lines <paramref name="first"/> up to <paramref name="close"/>
    /// (the marker line) with their own terminators, the last one's left off, made in one copy — a
    /// single line (a packed body's content among them) is its own text.</summary>
    private static string JoinContent(List<SourceLine> lines, int first, int close)
    {
        int count = close - first;
        if (count <= 0) return "";
        if (count == 1) return lines[first].Raw;
        int length = -lines[close - 1].Terminator.Length;
        for (int i = first; i < close; i++) length += lines[i].Raw.Length + lines[i].Terminator.Length;
        return string.Create(length, (lines, first, close), static (span, at) =>
        {
            var (lines, first, close) = at;
            int o = 0;
            for (int i = first; i < close; i++)
            {
                lines[i].Raw.CopyTo(span[o..]);
                o += lines[i].Raw.Length;
                if (i == close - 1) break;
                lines[i].Terminator.CopyTo(span[o..]);
                o += lines[i].Terminator.Length;
            }
        });
    }
}
