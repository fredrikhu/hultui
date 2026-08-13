using System.Text.Json;

namespace Meridian.Core;

/// <summary>
/// The OpenAPI reader — JSON only, by design (see the plan's risk note: a YAML
/// parser is reflection-heavy and AOT-hostile, and JSON specs plus live
/// probing cover v1). Reads what the map actually shows: paths, methods,
/// summaries, parameters, deprecation, and one example response per endpoint.
///
/// <para>A parse failure returns the position the reader stopped at, so the
/// error state can say <c>openapi.json:212:7</c> and offer to jump there.</para>
/// </summary>
public static class OpenApi
{
    public sealed record ParseFailure(string Message, int Line, int Column);

    /// <summary>Where a spec is conventionally served. Probed in order.</summary>
    public static readonly string[] WellKnownPaths =
    [
        "/openapi.json",
        "/.well-known/openapi.json",
        "/swagger.json",
        "/swagger/v1/swagger.json",
        "/api-docs",
        "/v3/api-docs",
    ];

    private static readonly string[] Methods =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    public static ApiMap? Parse(string json, string baseUrl, string source, out ParseFailure? failure)
    {
        failure = null;
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            failure = new ParseFailure(
                e.Message.Split(" LineNumber:")[0].TrimEnd('.'),
                (int)(e.LineNumber ?? 0) + 1,
                (int)(e.BytePositionInLine ?? 0) + 1);
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("paths", out var paths))
            {
                failure = new ParseFailure("not an OpenAPI document — no \"paths\" object", 1, 1);
                return null;
            }

            string? version = root.TryGetProperty("info", out var info)
                && info.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;

            var endpoints = new List<Endpoint>();
            foreach (var pathEntry in paths.EnumerateObject())
            {
                if (pathEntry.Value.ValueKind != JsonValueKind.Object) continue;
                // Parameters declared once for the whole path apply to each method.
                var shared = ReadParams(pathEntry.Value);

                foreach (var opEntry in pathEntry.Value.EnumerateObject())
                {
                    if (!Methods.Contains(opEntry.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (opEntry.Value.ValueKind != JsonValueKind.Object) continue;
                    var op = opEntry.Value;

                    var parameters = shared.Concat(ReadParams(op)).ToList();
                    endpoints.Add(new Endpoint(
                        opEntry.Name.ToUpperInvariant(),
                        pathEntry.Name,
                        Confidence.Declared,
                        Group: TagOf(op) ?? ApiMap.GroupOf(pathEntry.Name),
                        Summary: StringOr(op, "summary", ""),
                        Deprecated: op.TryGetProperty("deprecated", out var dep) && dep.ValueKind == JsonValueKind.True,
                        Parameters: parameters,
                        ExampleResponse: ExampleOf(op)));
                }
            }
            return new ApiMap(baseUrl, endpoints, source, version);
        }
    }

    private static List<EndpointParam> ReadParams(JsonElement owner)
    {
        var list = new List<EndpointParam>();
        if (!owner.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var p in parameters.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            string name = StringOr(p, "name", "");
            if (name.Length == 0) continue;
            list.Add(new EndpointParam(
                name,
                StringOr(p, "in", "query"),
                p.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.True,
                p.TryGetProperty("schema", out var schema) ? StringOr(schema, "type", "string") : "string"));
        }
        return list;
    }

    /// <summary>The first tag, which is how specs group endpoints for display.</summary>
    private static string? TagOf(JsonElement op) =>
        op.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
        && tags.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.String } first
            ? first.GetString()
            : null;

    /// <summary>An example body for the first 2xx response, from either the
    /// OpenAPI 3 <c>content</c> shape or the Swagger 2 <c>examples</c> one.</summary>
    private static string? ExampleOf(JsonElement op)
    {
        if (!op.TryGetProperty("responses", out var responses) || responses.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var response in responses.EnumerateObject())
        {
            if (!response.Name.StartsWith('2')) continue;
            if (response.Value.ValueKind != JsonValueKind.Object) continue;

            if (response.Value.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object)
                foreach (var media in content.EnumerateObject())
                    if (media.Value.ValueKind == JsonValueKind.Object
                        && media.Value.TryGetProperty("example", out var example))
                        return Pretty(example);

            if (response.Value.TryGetProperty("examples", out var examples) && examples.ValueKind == JsonValueKind.Object)
                foreach (var media in examples.EnumerateObject())
                    return Pretty(media.Value);
        }
        return null;
    }

    private static string Pretty(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            element.WriteTo(writer);
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string StringOr(JsonElement owner, string name, string fallback) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    // ── Promotion ────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>.req</c> file text for an endpoint. Path parameters become
    /// <c>{{var}}</c> templates and the base URL becomes <c>{{base_url}}</c>,
    /// so a promoted request is immediately environment-driven rather than
    /// hard-coded. Observed endpoints say so in a comment — the file carries
    /// the same honesty the map does.
    /// </summary>
    public static string ToRequestFile(Endpoint endpoint)
    {
        var lines = new List<string>();
        if (endpoint.Summary.Length > 0) lines.Add("# " + endpoint.Summary);
        if (endpoint.Confidence == Confidence.Observed)
            lines.Add($"# observed in traffic — not declared by a spec (seen {endpoint.SeenCount}×)");
        if (endpoint.Deprecated) lines.Add("# deprecated");

        string path = System.Text.RegularExpressions.Regex.Replace(
            endpoint.Path, @"\{([^}]+)\}", m => "{{" + Sanitize(m.Groups[1].Value) + "}}");
        lines.Add($"{endpoint.Method} {{{{base_url}}}}{path}");
        lines.Add($"name {NameOf(endpoint)}");

        foreach (var p in endpoint.Params.Where(p => p.In == "header"))
            lines.Add($"header {p.Name}: {{{{{Sanitize(p.Name)}}}}}");

        if (endpoint.Method is "POST" or "PUT" or "PATCH")
        {
            lines.Add("header Content-Type: application/json");
            lines.Add("body json");
            lines.Add("{}");
            lines.Add("end");
        }
        lines.Add("assert status equals 200");
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>"GET /v1/charges/{id}" → "Get charge" — a name a person would
    /// have typed, from the last literal segment.</summary>
    internal static string NameOf(Endpoint endpoint)
    {
        var segments = endpoint.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.StartsWith('{')).ToList();
        string noun = segments.Count > 0 ? segments[^1] : "request";
        if (noun.Length > 1 && noun.EndsWith('s') && endpoint.Path.EndsWith('}'))
            noun = noun[..^1];   // one of a collection
        string verb = endpoint.Method switch
        {
            "GET" => "Get",
            "POST" => "Create",
            "PUT" => "Replace",
            "PATCH" => "Update",
            "DELETE" => "Delete",
            _ => endpoint.Method.ToLowerInvariant(),
        };
        return $"{verb} {noun.Replace('-', ' ').Replace('_', ' ')}";
    }

    /// <summary>The file name a promoted endpoint gets.</summary>
    public static string FileNameFor(Endpoint endpoint) =>
        NameOf(endpoint).ToLowerInvariant().Replace(' ', '-') + ".req";

    private static string Sanitize(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
}
