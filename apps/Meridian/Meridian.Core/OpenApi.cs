using System.Text;
using System.Text.Json;
using System.Text.Unicode;

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

    /// <summary>
    /// What discovery tries for a pasted URL, and the API base it maps. The
    /// pasted URL itself goes first, so a spec URL works as pasted. A spec at a
    /// well-known path, or a Swagger UI page (<c>…/swagger/index.html</c>), maps
    /// the part before it; anything else is taken as the base.
    /// </summary>
    public static (string BaseUrl, IReadOnlyList<string> SpecUrls) ProbeCandidates(string pasted)
    {
        var url = pasted.Trim().TrimEnd('/');
        var baseUrl = url;
        // Longest first: …/swagger/v1/swagger.json also ends in /swagger.json.
        if (WellKnownPaths.Where(p => url.EndsWith(p, StringComparison.OrdinalIgnoreCase))
                .MaxBy(p => p.Length) is { } known)
            baseUrl = url[..^known.Length];
        else if (url.LastIndexOf("/swagger/", StringComparison.OrdinalIgnoreCase) is var swagger and >= 0)
            baseUrl = url[..swagger];

        var urls = new List<string> { url };
        foreach (var path in WellKnownPaths)
            if (!urls.Contains(baseUrl + path, StringComparer.OrdinalIgnoreCase))
                urls.Add(baseUrl + path);
        return (baseUrl, urls);
    }

    /// <summary>A response's whole text. Past the viewer cap the engine keeps
    /// only the head in memory and the full body on disk, and a spec cut at
    /// the cap is not JSON.</summary>
    public static string FullText(ResponseBody body)
    {
        if (body.Truncated && body.SpillPath is { } spill)
        {
            try { return File.ReadAllText(spill); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return body.Text;
    }

    /// <summary>
    /// <see cref="FullText"/> as UTF-8, for <see cref="Parse(ReadOnlyMemory{byte}, string, string, out ParseFailure?)"/>:
    /// the file a body past the viewer cap was spilled to, read as bytes (not as a string of 2 bytes
    /// a character), or the text in memory.
    /// </summary>
    internal static ReadOnlyMemory<byte> FullUtf8(ResponseBody body)
    {
        if (body.Truncated && body.SpillPath is { } spill)
        {
            try { return ReadUtf8(spill); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return Encoding.UTF8.GetBytes(body.Text);
    }

    /// <summary>
    /// A file's text as UTF-8, the text <see cref="File.ReadAllText(string)"/> reads: without a
    /// UTF-8 byte order mark, and decoded as that does when the file is not plain UTF-8 (another
    /// byte order mark, bytes that are not UTF-8). A plain UTF-8 file, as specs are, is its own
    /// bytes, with no string in between.
    /// </summary>
    internal static ReadOnlyMemory<byte> ReadUtf8(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ReadOnlySpan<byte> span = bytes;
        int start = span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        bool otherBom = span.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])
            || span.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]);
        if (!otherBom && Utf8.IsValid(span[start..]))
            return bytes.AsMemory(start);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Encoding.UTF8.GetBytes(reader.ReadToEnd());
    }

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
            failure = FailureOf(e);
            return null;
        }
        using (doc) return Read(doc, baseUrl, source, out failure);
    }

    /// <summary>
    /// <see cref="Parse(string, string, string, out ParseFailure?)"/> of UTF-8 text, a path at a time.
    /// A document of the whole spec rents a UTF-8 copy of the text and a table of its tokens that
    /// grows by doubling, and every array it rents stays in the shared pool once it is disposed: 9 MB
    /// after a spec of 2,000 operations. This reads the bytes where they are and makes a document of
    /// <c>info</c> and of one path item at a time, a few KB each, whose arrays the next one takes
    /// again. The map is the one the whole document gives (the last <c>paths</c> and <c>info</c>, as
    /// <c>TryGetProperty</c> finds them), and a failure is at the same line and column: the same
    /// reader reads the same text to its end.
    /// </summary>
    internal static ApiMap? Parse(ReadOnlyMemory<byte> utf8Json, string baseUrl, string source, out ParseFailure? failure)
    {
        failure = null;
        List<Endpoint>? endpoints = null;
        string? version = null;
        try
        {
            var reader = new Utf8JsonReader(utf8Json.Span);
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject) reader.Skip();
            else
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("paths"u8))
                    {
                        reader.Read();
                        endpoints = reader.TokenType == JsonTokenType.StartObject ? ReadPaths(ref reader) : null;
                        if (endpoints is null) reader.Skip();
                    }
                    else if (reader.ValueTextEquals("info"u8))
                    {
                        reader.Read();
                        using var info = JsonDocument.ParseValue(ref reader);
                        version = info.RootElement.ValueKind == JsonValueKind.Object ? VersionOf(info.RootElement) : null;
                    }
                    else
                    {
                        reader.Read();
                        reader.Skip();
                    }
                }
            // To the end, as a document reads it: anything after the root fails here.
            while (reader.Read()) { }
        }
        catch (JsonException e)
        {
            failure = FailureOf(e);
            return null;
        }
        if (endpoints is null)
        {
            failure = NotASpec();
            return null;
        }
        return new ApiMap(baseUrl, endpoints, source, version);
    }

    /// <summary>The path items of a <c>paths</c> object, the reader on its start: each a document of
    /// its own while its endpoints are read.</summary>
    private static List<Endpoint> ReadPaths(ref Utf8JsonReader reader)
    {
        var endpoints = new List<Endpoint>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string path = reader.GetString()!;
            reader.Read();
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }
            using var item = JsonDocument.ParseValue(ref reader);
            AddPathItem(path, item.RootElement, endpoints);
        }
        return endpoints;
    }

    private static ParseFailure FailureOf(JsonException e) => new(
        e.Message.Split(" LineNumber:")[0].TrimEnd('.'),
        (int)(e.LineNumber ?? 0) + 1,
        (int)(e.BytePositionInLine ?? 0) + 1);

    private static ApiMap? Read(JsonDocument doc, string baseUrl, string source, out ParseFailure? failure)
    {
        failure = null;
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("paths", out var paths))
        {
            failure = NotASpec();
            return null;
        }

        string? version = root.TryGetProperty("info", out var info) ? VersionOf(info) : null;

        var endpoints = new List<Endpoint>();
        foreach (var pathEntry in paths.EnumerateObject())
        {
            if (pathEntry.Value.ValueKind != JsonValueKind.Object) continue;
            AddPathItem(pathEntry.Name, pathEntry.Value, endpoints);
        }
        return new ApiMap(baseUrl, endpoints, source, version);
    }

    private static ParseFailure NotASpec() => new("not an OpenAPI document — no \"paths\" object", 1, 1);

    private static string? VersionOf(JsonElement info) =>
        info.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The endpoints of one path item, an object: one a method it declares.</summary>
    private static void AddPathItem(string path, JsonElement item, List<Endpoint> endpoints)
    {
        // Parameters declared once for the whole path apply to each method.
        var shared = ReadParams(item);

        foreach (var opEntry in item.EnumerateObject())
        {
            if (!Methods.Contains(opEntry.Name, StringComparer.OrdinalIgnoreCase)) continue;
            if (opEntry.Value.ValueKind != JsonValueKind.Object) continue;
            var op = opEntry.Value;

            var parameters = shared.Concat(ReadParams(op)).ToList();
            endpoints.Add(new Endpoint(
                opEntry.Name.ToUpperInvariant(),
                path,
                Confidence.Declared,
                Group: TagOf(op) ?? ApiMap.GroupOf(path),
                Summary: StringOr(op, "summary", ""),
                Deprecated: op.TryGetProperty("deprecated", out var dep) && dep.ValueKind == JsonValueKind.True,
                Parameters: parameters,
                ExampleResponse: ExampleOf(op)));
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
