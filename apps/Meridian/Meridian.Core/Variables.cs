namespace Meridian.Core;

/// <summary>One piece of a template: literal text, or a <c>{{name}}</c> variable.</summary>
public readonly record struct TemplateSegment(string Text, bool IsVariable);

/// <summary>The outcome of resolving a template: the resolved text (unresolved
/// variables stay as their <c>{{name}}</c> literal so the failure is visible),
/// plus which names could not be resolved.</summary>
public sealed record Resolution(string Text, IReadOnlyList<string> Unresolved)
{
    public bool FullyResolved => Unresolved.Count == 0;
}

/// <summary>
/// The <c>{{var}}</c> engine. Precedence, by design: the <b>environment wins</b>
/// over extracted values — what's written in a file on disk beats what a run
/// produced at runtime, so resolution is predictable from reading the collection.
/// Built-ins are a separate namespace entirely: every <c>$</c>-prefixed name
/// (<c>{{$uuid}}</c>, <c>{{$timestamp}}</c>) resolves only through them, and
/// plain names never do.
/// </summary>
public sealed class VariableResolver(
    IReadOnlyDictionary<string, string>? environment = null,
    IReadOnlyDictionary<string, string>? extracted = null,
    Func<string, string?>? builtins = null)
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();
    private readonly IReadOnlyDictionary<string, string> _environment = environment ?? Empty;
    private readonly IReadOnlyDictionary<string, string> _extracted = extracted ?? Empty;
    private readonly Func<string, string?> _builtins = builtins ?? DefaultBuiltins;

    /// <summary>The shipped built-ins. Injectable in tests (and in the runner,
    /// where a run wants one timestamp, not one per reference).</summary>
    public static string? DefaultBuiltins(string name) => name switch
    {
        "$uuid" => Guid.NewGuid().ToString(),
        "$timestamp" => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
        _ => null,
    };

    public string? Lookup(string name)
    {
        if (name.StartsWith('$')) return _builtins(name);
        if (_environment.TryGetValue(name, out var env)) return env;
        if (_extracted.TryGetValue(name, out var ext)) return ext;
        return null;
    }

    public Resolution Resolve(string template)
    {
        var sb = new System.Text.StringBuilder(template.Length);
        List<string>? unresolved = null;
        foreach (var seg in Tokenize(template))
        {
            if (!seg.IsVariable) { sb.Append(seg.Text); continue; }
            var value = Lookup(seg.Text);
            if (value != null) sb.Append(value);
            else
            {
                sb.Append("{{").Append(seg.Text).Append("}}");
                (unresolved ??= []).Add(seg.Text);
            }
        }
        return new Resolution(sb.ToString(), (IReadOnlyList<string>?)unresolved ?? []);
    }

    /// <summary>
    /// Splits a template into literal and variable segments. A variable is
    /// <c>{{name}}</c> where the name is non-empty and contains no braces or
    /// whitespace; anything else — unclosed braces, <c>{{ }}</c>, nesting — is
    /// literal text, not an error. Adjacent literals are merged.
    /// </summary>
    /// <remarks>A literal is the text between two variables, so each is a piece of the template
    /// (the template itself when it holds no variable): a request body's build copied it twice a
    /// keystroke through a builder, 350 KB at 89,000 characters (plan 17 round 6).</remarks>
    public static IReadOnlyList<TemplateSegment> Tokenize(string template)
    {
        var segments = new List<TemplateSegment>();
        int literal = 0;
        while (NextVariable(template, literal, out int open, out int close))
        {
            if (open > literal) segments.Add(new TemplateSegment(template[literal..open], false));
            segments.Add(new TemplateSegment(template[(open + 2)..close].Trim(), true));
            literal = close + 2;
        }
        if (literal < template.Length) segments.Add(new TemplateSegment(template[literal..], false));
        return segments;
    }

    /// <summary>The names of the variables in <paramref name="template"/>, in order, as
    /// <see cref="Tokenize"/> finds them, without the literal text between them.</summary>
    public static IEnumerable<string> VariableNames(string template)
    {
        int from = 0;
        while (NextVariable(template, from, out int open, out int close))
        {
            yield return template[(open + 2)..close].Trim();
            from = close + 2;
        }
    }

    /// <summary>The next <c>{{name}}</c> at or after <paramref name="from"/>: its braces at
    /// <paramref name="open"/> and <paramref name="close"/>. A <c>{{</c> that opens no variable is
    /// literal text, and the search goes on after it.</summary>
    private static bool NextVariable(string template, int from, out int open, out int close)
    {
        int i = from;
        while (i < template.Length)
        {
            open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0) break;
            close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0) break;   // no "}}" after this "{{", so none after any later one either
            if (IsName(template.AsSpan(open + 2, close - open - 2).Trim())) return true;
            i = open + 2;
        }
        open = close = -1;
        return false;
    }

    /// <summary>A variable's name: not empty, with no braces and no white space.</summary>
    private static bool IsName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty) return false;
        foreach (char c in name)
            if (c is '{' or '}' || char.IsWhiteSpace(c)) return false;
        return true;
    }
}
