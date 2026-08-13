namespace Meridian.Core;

/// <summary>How much Meridian actually knows about an endpoint.</summary>
public enum Confidence
{
    /// <summary>A spec said so — solid chip.</summary>
    Declared,
    /// <summary>Only traffic said so — dashed, hollow chip, and any shape it
    /// carries is labelled as inference, never dressed up as spec truth.</summary>
    Observed,
}

public sealed record EndpointParam(string Name, string In, bool Required, string Type);

/// <summary>One endpoint in the map. Spec facts and traffic facts live side by
/// side — the detail pane merges them, so a declared endpoint can still carry
/// the status codes it was really seen returning.</summary>
public sealed record Endpoint(
    string Method,
    string Path,
    Confidence Confidence,
    string Group,
    string Summary = "",
    bool Deprecated = false,
    IReadOnlyList<EndpointParam>? Parameters = null,
    string? ExampleResponse = null,
    IReadOnlyDictionary<int, int>? SeenCodes = null,
    int SeenCount = 0,
    string? InferredShape = null)
{
    public IReadOnlyList<EndpointParam> Params => Parameters ?? [];
    public IReadOnlyDictionary<int, int> Codes => SeenCodes ?? new Dictionary<int, int>();
    public string Key => $"{Method} {Path}";
}

/// <summary>
/// What discovery knows about one base URL: the endpoints, and where they came
/// from. Immutable — recording traffic or importing a spec returns a new map,
/// which keeps the UI's rebuild story simple.
/// </summary>
public sealed record ApiMap(
    string BaseUrl,
    IReadOnlyList<Endpoint> Endpoints,
    string? SpecSource = null,
    string? SpecVersion = null)
{
    public static ApiMap Empty(string baseUrl) => new(baseUrl, []);

    public int DeclaredCount => Endpoints.Count(e => e.Confidence == Confidence.Declared);
    public int ObservedCount => Endpoints.Count(e => e.Confidence == Confidence.Observed);

    /// <summary>
    /// Folds one real send into the map. A send that matches a known endpoint
    /// (after templating concrete path segments back to <c>{param}</c>) updates
    /// its counts and keeps its confidence; anything else joins as Observed.
    /// </summary>
    public ApiMap Record(string method, string url, int statusCode, string? bodyJson)
    {
        string path;
        try { path = new Uri(url, UriKind.Absolute).AbsolutePath; }
        catch (UriFormatException) { return this; }

        var endpoints = Endpoints.ToList();
        int index = endpoints.FindIndex(e =>
            e.Method.Equals(method, StringComparison.OrdinalIgnoreCase) && PathMatches(e.Path, path));

        if (index < 0)
        {
            // Not known: template the concrete path so /v1/charges/ch_1 and
            // /v1/charges/ch_2 are one endpoint, not two.
            string templated = Templatize(path, endpoints.Select(e => e.Path));
            index = endpoints.FindIndex(e =>
                e.Method.Equals(method, StringComparison.OrdinalIgnoreCase) && e.Path == templated);
            if (index < 0)
            {
                endpoints.Add(new Endpoint(method.ToUpperInvariant(), templated, Confidence.Observed,
                    GroupOf(templated), SeenCodes: new Dictionary<int, int> { [statusCode] = 1 }, SeenCount: 1,
                    InferredShape: InferShape(bodyJson)));
                return this with { Endpoints = endpoints };
            }
        }

        var existing = endpoints[index];
        var codes = new Dictionary<int, int>(existing.Codes);
        codes[statusCode] = codes.GetValueOrDefault(statusCode) + 1;
        endpoints[index] = existing with
        {
            SeenCodes = codes,
            SeenCount = existing.SeenCount + 1,
            InferredShape = existing.InferredShape ?? InferShape(bodyJson),
        };
        return this with { Endpoints = endpoints };
    }

    /// <summary>A concrete path against a template: segment counts must match,
    /// and a <c>{param}</c> segment matches anything.</summary>
    internal static bool PathMatches(string template, string concrete)
    {
        var t = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var c = concrete.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (t.Length != c.Length) return false;
        for (int i = 0; i < t.Length; i++)
            if (!(t[i].StartsWith('{') && t[i].EndsWith('}')) && t[i] != c[i])
                return false;
        return true;
    }

    /// <summary>
    /// Turns identifier-looking segments into <c>{id}</c>. "Identifier-looking"
    /// means: long, or containing a digit, or prefixed like <c>ch_9f2</c> —
    /// the shapes real APIs use. A segment that another endpoint already uses
    /// literally is left alone, so <c>/v1/charges</c> never becomes a parameter.
    /// </summary>
    internal static string Templatize(string path, IEnumerable<string> knownPaths)
    {
        var literal = new HashSet<string>(
            knownPaths.SelectMany(p => p.Split('/', StringSplitOptions.RemoveEmptyEntries))
                      .Where(s => !s.StartsWith('{')),
            StringComparer.OrdinalIgnoreCase);

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            var s = segments[i];
            if (literal.Contains(s)) continue;
            if (LooksLikeId(s)) segments[i] = "{id}";
        }
        return "/" + string.Join('/', segments);
    }

    private static bool LooksLikeId(string segment)
    {
        if (segment.Length == 0) return false;
        if (segment.Contains('_') && segment.Any(char.IsAsciiDigit)) return true;   // ch_9f2
        if (Guid.TryParse(segment, out _)) return true;
        if (segment.All(char.IsAsciiDigit)) return true;                            // 42
        return segment.Length >= 16 && segment.Any(char.IsAsciiDigit);              // opaque token
    }

    /// <summary>The first path segment, which is how APIs group themselves
    /// ("/v1/charges/{id}" → charges). Version prefixes don't count.</summary>
    internal static string GroupOf(string path)
    {
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.StartsWith('{')) continue;
            if (segment.Length >= 2 && segment[0] is 'v' or 'V' && segment[1..].All(char.IsAsciiDigit)) continue;
            return segment;
        }
        return "other";
    }

    /// <summary>A one-line shape summary of a JSON object — inference, and
    /// labelled as such wherever it is shown.</summary>
    internal static string? InferShape(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            var fields = doc.RootElement.EnumerateObject()
                .Take(12)
                .Select(p => $"{p.Name}: {TypeName(p.Value.ValueKind)}");
            return "{ " + string.Join(", ", fields) + " }";
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string TypeName(System.Text.Json.JsonValueKind kind) => kind switch
    {
        System.Text.Json.JsonValueKind.String => "string",
        System.Text.Json.JsonValueKind.Number => "number",
        System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => "boolean",
        System.Text.Json.JsonValueKind.Array => "array",
        System.Text.Json.JsonValueKind.Object => "object",
        _ => "null",
    };
}
