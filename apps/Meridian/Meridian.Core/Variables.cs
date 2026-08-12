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
    public static IReadOnlyList<TemplateSegment> Tokenize(string template)
    {
        var segments = new List<TemplateSegment>();
        var literal = new System.Text.StringBuilder();
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0) { literal.Append(template, i, template.Length - i); break; }
            int close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            string? name = close < 0 ? null : template[(open + 2)..close].Trim();
            if (name is not { Length: > 0 } || name.Any(c => c is '{' or '}' || char.IsWhiteSpace(c)))
            {
                // Not a variable — the "{{" is literal; rescan after it.
                literal.Append(template, i, open + 2 - i);
                i = open + 2;
                continue;
            }
            literal.Append(template, i, open - i);
            if (literal.Length > 0) { segments.Add(new TemplateSegment(literal.ToString(), false)); literal.Clear(); }
            segments.Add(new TemplateSegment(name, true));
            i = close + 2;
        }
        if (literal.Length > 0) segments.Add(new TemplateSegment(literal.ToString(), false));
        return segments;
    }
}
