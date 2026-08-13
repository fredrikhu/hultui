using System.Text.Json;

namespace Meridian.Core;

/// <summary>
/// The JSONPath subset assertions and extractions speak: <c>$.a.b</c>,
/// <c>$.items[0].id</c>, <c>$.items[*].id</c>. Filters and recursive descent
/// are deliberately out — every path a v1 file can express is statically
/// checkable and cheap to evaluate on a keystroke.
///
/// <para>Values stringify the way a person would write them: strings lose
/// their quotes, numbers/booleans/null are their JSON text, objects and arrays
/// are their raw JSON. A path that selects nothing is not an error — "matches
/// nothing" is a state the UI renders; an unparsable path or non-JSON input is.</para>
/// </summary>
public static class JsonPath
{
    public sealed record Result(IReadOnlyList<string> Matches, string? Error)
    {
        public bool HasMatch => Matches.Count > 0;
        public string? First => Matches.Count > 0 ? Matches[0] : null;
    }

    public static Result Evaluate(string path, string json)
    {
        var (segments, parseError) = ParsePath(path);
        if (parseError != null) return new Result([], parseError);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return new Result([], "the response body is not JSON"); }

        using (doc)
        {
            var current = new List<JsonElement> { doc.RootElement };
            foreach (var segment in segments)
            {
                var next = new List<JsonElement>();
                foreach (var element in current)
                    segment.Apply(element, next);
                current = next;
                if (current.Count == 0) break;
            }
            return new Result(current.Select(Stringify).ToList(), null);
        }
    }

    /// <summary>Parse-time validation only — for flagging a bad path while it
    /// is being typed, without a response to evaluate against.</summary>
    public static string? Validate(string path) => ParsePath(path).Error;

    private static string Stringify(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        _ => element.GetRawText(),
    };

    // ── Path parsing ─────────────────────────────────────────────────────

    private abstract record Segment
    {
        public abstract void Apply(JsonElement element, List<JsonElement> into);
    }

    private sealed record Property(string Name) : Segment
    {
        public override void Apply(JsonElement element, List<JsonElement> into)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(Name, out var value))
                into.Add(value);
        }
    }

    private sealed record Index(int At) : Segment
    {
        public override void Apply(JsonElement element, List<JsonElement> into)
        {
            if (element.ValueKind == JsonValueKind.Array && At >= 0 && At < element.GetArrayLength())
                into.Add(element[At]);
        }
    }

    private sealed record AllElements : Segment
    {
        public override void Apply(JsonElement element, List<JsonElement> into)
        {
            if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray())
                    into.Add(item);
        }
    }

    private static (List<Segment> Segments, string? Error) ParsePath(string path)
    {
        var segments = new List<Segment>();
        if (path.Length == 0 || path[0] != '$')
            return (segments, "a path starts with $");

        int i = 1;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                i++;
                int start = i;
                while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                if (i == start) return (segments, $"empty name after . at {start}");
                segments.Add(new Property(path[start..i]));
            }
            else if (path[i] == '[')
            {
                int close = path.IndexOf(']', i);
                if (close < 0) return (segments, "unclosed [");
                string inner = path[(i + 1)..close].Trim();
                if (inner == "*")
                    segments.Add(new AllElements());
                else if (int.TryParse(inner, out int index) && index >= 0)
                    segments.Add(new Index(index));
                else if (inner.Length >= 2 && (inner[0] == '\'' || inner[0] == '"') && inner[^1] == inner[0])
                    segments.Add(new Property(inner[1..^1]));
                else
                    return (segments, $"[{inner}] is not an index, *, or 'name'");
                i = close + 1;
            }
            else
            {
                return (segments, $"unexpected '{path[i]}' at {i}");
            }
        }
        return (segments, null);
    }
}
