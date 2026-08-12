namespace Meridian.Core;

/// <summary>
/// A parsed <c>envs/*.env</c> file. Same lossless, never-throwing contract as
/// <see cref="ReqDocument"/>. The grammar:
/// <code>
/// # comment
/// base_url=https://staging.acme-pay.dev
/// timeout_ms=30000
/// secret client_secret
/// </code>
/// <para>A <c>secret</c> line declares that a key's value lives in the OS
/// keyring, never in this file. The key may or may not also carry a
/// <c>key=</c> line (a placeholder for ordering/diff purposes); its file value
/// is ignored at resolution time once flagged secret.</para>
/// <para>Values are everything after the first <c>=</c>, verbatim — spaces and
/// further <c>=</c> included. A duplicate key keeps the later value.</para>
/// </summary>
public sealed class EnvDocument
{
    private readonly List<SourceLine> _lines;

    /// <summary>True once any edit method has run; cleared by <see cref="MarkSaved"/>.</summary>
    public bool Edited { get; private set; }

    public void MarkSaved() => Edited = false;

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly HashSet<string> _secrets = new(StringComparer.Ordinal);
    private readonly List<ParseError> _errors = [];

    /// <summary>Key → value, later duplicate wins. Secret keys may appear here
    /// with their placeholder file value; check <see cref="IsSecret"/>.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;
    /// <summary>Keys in first-appearance order (a <c>secret</c> line counts as
    /// an appearance), for stable display.</summary>
    public IReadOnlyList<string> Keys => _order;
    public bool IsSecret(string key) => _secrets.Contains(key);
    public IReadOnlyList<ParseError> Errors => _errors;

    private EnvDocument(List<SourceLine> lines) => _lines = lines;

    public string Serialize() => LineBuffer.Join(_lines);

    public static EnvDocument Parse(string text)
    {
        var doc = new EnvDocument(LineBuffer.Split(text));
        doc.Reparse();
        return doc;
    }

    private void Note(string key)
    {
        if (!_order.Contains(key)) _order.Add(key);
    }

    // ── Edits — the ReqDocument contract: rewrite only your own line, keep
    // every other byte, reparse after ─────────────────────────────────────

    /// <summary>Sets a key's value: rewrites its <c>key=value</c> line in place
    /// (the last one, which is the one that wins), or appends one at the end.
    /// Creates the key when absent.</summary>
    public void SetValue(string key, string value)
    {
        int line = LastValueLine(key);
        if (line > 0)
            _lines[line - 1] = _lines[line - 1] with { Raw = $"{key}={value}" };
        else
        {
            EnsureFinalTerminator();
            _lines.Add(new SourceLine($"{key}={value}", DominantTerminator()));
        }
        Touch();
    }

    /// <summary>Flags a key as secret (appends the <c>secret</c> line when it
    /// isn't already flagged). The value lives in the keyring, not here.</summary>
    public void MarkSecret(string key)
    {
        if (_secrets.Contains(key)) return;
        EnsureFinalTerminator();
        _lines.Add(new SourceLine($"secret {key}", DominantTerminator()));
        Touch();
    }

    private int LastValueLine(string key)
    {
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            string line = _lines[i].Raw.TrimStart();
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].TrimEnd() == key) return i + 1;
        }
        return 0;
    }

    private void Touch()
    {
        Edited = true;
        _values.Clear(); _order.Clear(); _secrets.Clear(); _errors.Clear();
        Reparse();
    }

    private void Reparse()
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            int lineNo = i + 1;
            string line = _lines[i].Raw.TrimStart();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith("secret ", StringComparison.Ordinal))
            {
                string key = line["secret ".Length..].Trim();
                if (key.Length == 0) { _errors.Add(new ParseError(lineNo, "secret needs a key name")); continue; }
                _secrets.Add(key);
                Note(key);
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                _errors.Add(new ParseError(lineNo, "expected \"key=value\" or \"secret key\""));
                continue;
            }

            string k = line[..eq].TrimEnd();
            _values[k] = line[(eq + 1)..];
            Note(k);
        }
    }

    private string DominantTerminator()
    {
        int crlf = 0, lf = 0;
        foreach (var line in _lines)
        {
            if (line.Terminator == "\r\n") crlf++;
            else if (line.Terminator == "\n") lf++;
        }
        return crlf > lf ? "\r\n" : "\n";
    }

    private void EnsureFinalTerminator()
    {
        if (_lines.Count > 0 && _lines[^1].Terminator.Length == 0)
            _lines[^1] = _lines[^1] with { Terminator = DominantTerminator() };
    }
}
