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

        for (int i = 0; i < doc._lines.Count; i++)
        {
            int lineNo = i + 1;
            string line = doc._lines[i].Raw.TrimStart();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith("secret ", StringComparison.Ordinal))
            {
                string key = line["secret ".Length..].Trim();
                if (key.Length == 0) { doc._errors.Add(new ParseError(lineNo, "secret needs a key name")); continue; }
                doc._secrets.Add(key);
                doc.Note(key);
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                doc._errors.Add(new ParseError(lineNo, "expected \"key=value\" or \"secret key\""));
                continue;
            }

            string k = line[..eq].TrimEnd();
            doc._values[k] = line[(eq + 1)..];
            doc.Note(k);
        }

        return doc;
    }

    private void Note(string key)
    {
        if (!_order.Contains(key)) _order.Add(key);
    }
}
