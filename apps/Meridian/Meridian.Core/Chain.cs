namespace Meridian.Core;

/// <summary>
/// Chain resolution: which requests feed which. A request <b>uses</b> the
/// plain variables its templates reference; it <b>produces</b> the variables
/// its extractions name. From those, the run order for executing one request
/// "alone" — dependencies first, each once. A list, not a graph canvas.
/// </summary>
public static class Chain
{
    public sealed record Node(
        string Path,
        IReadOnlyList<string> Uses,
        IReadOnlyList<string> Produces);

    /// <summary>What a request uses (every <c>{{var}}</c> in its request line,
    /// headers and body that isn't a $-builtin) and produces (its extractions'
    /// variables). Environment definitions are the caller's business — the
    /// chain doesn't know which uses an env satisfies.</summary>
    public static Node Analyze(string path, ReqDocument doc)
    {
        var uses = new List<string>();
        // The names only: the sub-tabs ask on every build of the editor, and a request body's
        // literal text is most of the document.
        void Collect(string? template)
        {
            if (template == null) return;
            foreach (var name in VariableResolver.VariableNames(template))
                if (!name.StartsWith('$') && !uses.Contains(name))
                    uses.Add(name);
        }
        Collect(doc.Url);
        foreach (var header in doc.Headers) Collect(header.Value);
        Collect(doc.Body?.Content);

        var produces = new List<string>();
        foreach (var extract in doc.Extracts)
            if (Directives.ParseExtract(extract.Args, out _) is { } spec && !produces.Contains(spec.Variable))
                produces.Add(spec.Variable);

        return new Node(path, uses, produces);
    }

    /// <summary>
    /// The order to run <paramref name="targetPath"/> and everything it
    /// transitively depends on: dependencies first, target last. A variable no
    /// request produces is simply not a dependency (the environment or the
    /// extracted store answers for it, or the send blocks — not this class's
    /// call). A dependency cycle is an error naming its members.
    /// </summary>
    public static (IReadOnlyList<string> Order, string? Error) RunOrderFor(
        string targetPath, IReadOnlyList<Node> nodes)
    {
        var byPath = nodes.ToDictionary(n => n.Path, StringComparer.OrdinalIgnoreCase);
        // First producer wins, matching the resolver's determinism.
        var producerOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in nodes)
            foreach (var variable in node.Produces)
                producerOf.TryAdd(variable, node.Path);

        var order = new List<string>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? cycle = null;

        void Visit(string path)
        {
            if (cycle != null || done.Contains(path)) return;
            if (!visiting.Add(path))
            {
                cycle = $"dependency cycle through {System.IO.Path.GetFileName(path)}";
                return;
            }
            if (byPath.TryGetValue(path, out var node))
                foreach (var use in node.Uses)
                    if (producerOf.TryGetValue(use, out var producer) && !producer.Equals(path, StringComparison.OrdinalIgnoreCase))
                        Visit(producer);
            visiting.Remove(path);
            done.Add(path);
            order.Add(path);
        }

        Visit(targetPath);
        return cycle != null ? ([], cycle) : (order, null);
    }

    /// <summary>The order to run <b>every</b> node: dependencies first, each
    /// once, scope order otherwise preserved. The runner's spine.</summary>
    public static (IReadOnlyList<string> Order, string? Error) RunOrderAll(IReadOnlyList<Node> nodes)
    {
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? error = null;
        foreach (var node in nodes)
        {
            var (partial, cycleError) = RunOrderFor(node.Path, nodes);
            if (cycleError != null) { error = cycleError; break; }
            foreach (var path in partial)
                if (seen.Add(path))
                    order.Add(path);
        }
        return error != null ? ([], error) : (order, null);
    }

    /// <summary>Which requests consume <paramref name="variable"/> — the
    /// "→ used by" line on a produces row.</summary>
    public static IReadOnlyList<string> ConsumersOf(string variable, IReadOnlyList<Node> nodes) =>
        nodes.Where(n => n.Uses.Contains(variable)).Select(n => n.Path).ToList();
}
