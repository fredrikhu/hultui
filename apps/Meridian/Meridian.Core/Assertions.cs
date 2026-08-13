using System.Globalization;

namespace Meridian.Core;

/// <summary>
/// The typed side of the <c>assert</c> and <c>extract</c> directives P1 has
/// been carrying as raw text. The grammar, one line each:
/// <code>
/// assert status equals 200
/// assert time under 500 ms
/// assert header content-type contains application/json
/// assert body $.status equals "succeeded"
/// extract body $.auth.access_token -> token
/// extract header x-request-id -> rid
/// </code>
/// Parsing returns null + an error message rather than throwing — a half-typed
/// row in the editor is a state, not a crash.
/// </summary>
public static class Directives
{
    // ── Assertions ───────────────────────────────────────────────────────

    /// <summary>Source is status/time/header/body; Qualifier is the header
    /// name or the JSONPath; Expected has its surrounding quotes removed.</summary>
    public sealed record AssertionSpec(string Source, string Qualifier, string Op, string Expected);

    public sealed record AssertionOutcome(bool Passed, string Actual);

    public static readonly string[] AssertSources = ["status", "time", "header", "body"];
    public static readonly string[] AssertOps = ["equals", "contains", "under"];

    public static AssertionSpec? ParseAssert(string args, out string? error)
    {
        error = null;
        var parts = SplitArgs(args);
        if (parts.Count < 3) { error = "assert needs: source [name] operator expected"; return null; }

        string source = parts[0];
        string qualifier = "";
        int opIndex = 1;
        if (source is "header" or "body")
        {
            if (parts.Count < 4) { error = $"assert {source} needs: {(source == "header" ? "name" : "$.path")} operator expected"; return null; }
            qualifier = parts[1];
            opIndex = 2;
        }
        if (!AssertSources.Contains(source)) { error = $"unknown source \"{source}\""; return null; }
        string op = parts[opIndex];
        if (!AssertOps.Contains(op)) { error = $"unknown operator \"{op}\""; return null; }
        if (source == "body" && JsonPath.Validate(qualifier) is { } pathError) { error = pathError; return null; }

        string expected = Unquote(string.Join(' ', parts.Skip(opIndex + 1)));
        return new AssertionSpec(source, qualifier, op, expected);
    }

    public static string FormatAssert(AssertionSpec spec) =>
        spec.Qualifier.Length > 0
            ? $"{spec.Source} {spec.Qualifier} {spec.Op} {spec.Expected}"
            : $"{spec.Source} {spec.Op} {spec.Expected}";

    public static AssertionOutcome Evaluate(AssertionSpec spec, HttpResponseData response)
    {
        string? actual = spec.Source switch
        {
            "status" => response.StatusCode.ToString(),
            "time" => response.Timings.Total.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture),
            "header" => response.Headers
                .Where(h => h.Name.Equals(spec.Qualifier, StringComparison.OrdinalIgnoreCase))
                .Select(h => h.Value).FirstOrDefault(),
            _ => response.Body.Kind == BodyKind.Text
                ? JsonPath.Evaluate(spec.Qualifier, response.Body.Text).First
                : null,
        };
        if (actual == null)
            return new AssertionOutcome(false, spec.Source == "header" ? "header absent" : "no match");

        bool passed = spec.Op switch
        {
            "contains" => actual.Contains(spec.Expected, StringComparison.OrdinalIgnoreCase),
            "under" => TryNumber(actual, out var a) && TryNumber(spec.Expected, out var limit) && a < limit,
            _ => actual == spec.Expected,
        };
        return new AssertionOutcome(passed, actual);
    }

    private static bool TryNumber(string text, out double value)
    {
        // "500 ms" → 500: the unit is display sugar, the number is the value.
        int end = 0;
        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] is '.' or '-')) end++;
        return double.TryParse(text[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // ── Extractions ──────────────────────────────────────────────────────

    /// <summary>Source is header/body; Selector the header name or JSONPath;
    /// Variable the plain name the value lands in.</summary>
    public sealed record ExtractionSpec(string Source, string Selector, string Variable);

    public static ExtractionSpec? ParseExtract(string args, out string? error)
    {
        error = null;
        var parts = SplitArgs(args);
        int arrow = parts.IndexOf("->");
        if (arrow < 0 || arrow != parts.Count - 2)
        {
            error = "extract needs: source selector -> variable";
            return null;
        }
        var left = parts[..arrow];
        if (left.Count != 2 || left[0] is not ("body" or "header"))
        {
            error = "extract needs: body $.path | header name, then -> variable";
            return null;
        }
        string variable = parts[^1];
        if (variable.Length == 0 || variable.StartsWith('$'))
        {
            error = "the variable is a plain name ($-names are built-ins)";
            return null;
        }
        if (left[0] == "body" && JsonPath.Validate(left[1]) is { } pathError) { error = pathError; return null; }
        return new ExtractionSpec(left[0], left[1], variable);
    }

    public static string FormatExtract(ExtractionSpec spec) =>
        $"{spec.Source} {spec.Selector} -> {spec.Variable}";

    /// <summary>The selected value, or null when the response has no such
    /// header / the path selects nothing / the body isn't text.</summary>
    public static string? EvaluateExtract(ExtractionSpec spec, HttpResponseData response) => spec.Source switch
    {
        "header" => response.Headers
            .Where(h => h.Name.Equals(spec.Selector, StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Value).FirstOrDefault(),
        _ => response.Body.Kind == BodyKind.Text
            ? JsonPath.Evaluate(spec.Selector, response.Body.Text).First
            : null,
    };

    private static List<string> SplitArgs(string args) =>
        args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Unquote(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
}
