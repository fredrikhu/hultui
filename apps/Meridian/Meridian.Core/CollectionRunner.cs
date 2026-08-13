using System.Diagnostics;

namespace Meridian.Core;

public enum RunVerdict
{
    /// <summary>Every assertion passed (or none, and the status was 2xx).</summary>
    Pass,
    /// <summary>An assertion failed — the API is wrong. Red.</summary>
    Fail,
    /// <summary>The network/config is wrong — unreachable, unresolved. Amber,
    /// never red: an unreachable host must not read as an API failure.</summary>
    Infra,
    /// <summary>Never ran: its producer didn't pass, the run was aborted or
    /// bailed, or cancellation got there first.</summary>
    Skipped,
}

/// <summary>One request's outcome in a run.</summary>
public sealed record RunRequestResult(
    string Path,
    string DisplayName,
    string? Method,
    RunVerdict Verdict,
    SendResult? Send,
    IReadOnlyList<(Directives.AssertionSpec Spec, Directives.AssertionOutcome Outcome)> Assertions,
    TimeSpan Duration,
    /// <summary>The one line the reports quote: assertion expected/got, infra
    /// detail, or why it was skipped. Empty for a pass.</summary>
    string Detail)
{
    public int AssertionsPassed => Assertions.Count(a => a.Outcome.Passed);
}

public sealed record RunSummary(
    IReadOnlyList<RunRequestResult> Results,
    TimeSpan Wall,
    bool Aborted)
{
    public int Passed => Results.Count(r => r.Verdict == RunVerdict.Pass);
    public int Failed => Results.Count(r => r.Verdict == RunVerdict.Fail);
    public int Infra => Results.Count(r => r.Verdict == RunVerdict.Infra);
    public int Skipped => Results.Count(r => r.Verdict == RunVerdict.Skipped);
    /// <summary>The CLI's exit code: anything not green is 1.</summary>
    public int ExitCode => Failed > 0 || Infra > 0 || Aborted ? 1 : 0;
}

/// <summary>
/// Executes a scope of requests in chain order — the same run the CLI performs,
/// which is the point: the GUI streams these results into a list, the CLI
/// prints them, and <see cref="RunReports"/> words both identically.
///
/// <para>Rules: extractions apply on any response; a request whose variables
/// were to be produced by a scope member that didn't pass is skipped, not
/// failed; infra failures don't stop the run; <c>stopOnFailure</c> stops on
/// the first red (assertion) failure; with no assertions, 2xx is the pass.</para>
/// </summary>
public sealed class CollectionRunner(HttpEngine engine)
{
    public sealed record Request(string Path, string DisplayName, ReqDocument Doc);

    public async Task<RunSummary> RunAsync(
        IReadOnlyList<Request> scope,
        IReadOnlyDictionary<string, string> environment,
        bool stopOnFailure,
        Action<RunRequestResult>? onResult,
        CancellationToken ct)
    {
        var wall = Stopwatch.StartNew();
        var byPath = scope.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
        var nodes = scope.Select(r => Chain.Analyze(r.Path, r.Doc)).ToList();
        var nodeByPath = nodes.ToDictionary(n => n.Path, StringComparer.OrdinalIgnoreCase);
        var (order, cycleError) = Chain.RunOrderAll(nodes);
        if (cycleError != null)
        {
            // A cycle is a config error on every member: nothing runs.
            var broken = scope.Select(r => new RunRequestResult(r.Path, r.DisplayName, r.Doc.Method,
                RunVerdict.Infra, null, [], TimeSpan.Zero, cycleError)).ToList();
            foreach (var b in broken) onResult?.Invoke(b);
            return new RunSummary(broken, wall.Elapsed, Aborted: false);
        }

        var producerOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in nodes)
            foreach (var variable in node.Produces)
                producerOf.TryAdd(variable, node.Path);

        var extracted = new Dictionary<string, string>(StringComparer.Ordinal);
        var verdictOf = new Dictionary<string, RunVerdict>(StringComparer.OrdinalIgnoreCase);
        var results = new List<RunRequestResult>();
        bool bailed = false;

        void Emit(RunRequestResult result)
        {
            results.Add(result);
            verdictOf[result.Path] = result.Verdict;
            onResult?.Invoke(result);
        }

        foreach (var path in order)
        {
            var request = byPath[path];
            if (bailed || ct.IsCancellationRequested)
            {
                Emit(new RunRequestResult(path, request.DisplayName, request.Doc.Method,
                    RunVerdict.Skipped, null, [], TimeSpan.Zero,
                    bailed ? "skipped — stopped on failure" : "skipped — aborted"));
                continue;
            }

            // A producer in scope that didn't pass ⇒ its consumers skip.
            var node = nodeByPath[path];
            string? deadProducer = node.Uses
                .Select(use => producerOf.GetValueOrDefault(use))
                .FirstOrDefault(p => p != null && !p.Equals(path, StringComparison.OrdinalIgnoreCase)
                    && verdictOf.TryGetValue(p, out var v) && v != RunVerdict.Pass);
            if (deadProducer != null)
            {
                Emit(new RunRequestResult(path, request.DisplayName, request.Doc.Method,
                    RunVerdict.Skipped, null, [], TimeSpan.Zero,
                    $"skipped — {System.IO.Path.GetFileNameWithoutExtension(deadProducer)} did not pass"));
                continue;
            }

            Emit(await RunOneAsync(request, environment, extracted, ct));
            if (results[^1].Verdict == RunVerdict.Fail && stopOnFailure) bailed = true;
        }

        wall.Stop();
        return new RunSummary(results, wall.Elapsed, Aborted: ct.IsCancellationRequested);
    }

    private async Task<RunRequestResult> RunOneAsync(
        Request request,
        IReadOnlyDictionary<string, string> environment,
        Dictionary<string, string> extracted,
        CancellationToken ct)
    {
        var doc = request.Doc;
        var sw = Stopwatch.StartNew();
        RunRequestResult Done(RunVerdict verdict, SendResult? send,
            IReadOnlyList<(Directives.AssertionSpec, Directives.AssertionOutcome)> assertions, string detail) =>
            new(request.Path, request.DisplayName, doc.Method, verdict, send, assertions, sw.Elapsed, detail);

        if (doc.Method is not { } method || doc.Url is not { } urlTemplate)
            return Done(RunVerdict.Infra, null, [], "no request line");

        var resolver = new VariableResolver(environment, extracted);
        var url = resolver.Resolve(urlTemplate);
        var unresolved = new List<string>(url.Unresolved);
        var headers = new List<(string, string)>();
        foreach (var header in doc.Headers)
        {
            var value = resolver.Resolve(header.Value);
            unresolved.AddRange(value.Unresolved);
            headers.Add((header.Name, value.Text));
        }
        string? bodyText = null;
        if (doc.Body is { Content.Length: > 0 } body)
        {
            var resolved = resolver.Resolve(body.Content);
            unresolved.AddRange(resolved.Unresolved);
            bodyText = resolved.Text;
        }
        if (unresolved.Count > 0)
            return Done(RunVerdict.Infra, null, [],
                $"unresolved {string.Join(", ", unresolved.Distinct().Select(n => "{{" + n + "}}"))}");

        SendResult send;
        try
        {
            send = await engine.SendAsync(new RequestSpec(method, url.Text, headers, bodyText,
                bodyText != null && doc.Body!.Type == "json" ? "application/json" : null), ct);
        }
        catch (OperationCanceledException)
        {
            return Done(RunVerdict.Skipped, null, [], "skipped — aborted");
        }
        catch (Exception e) when (e is UriFormatException or InvalidOperationException or ArgumentException)
        {
            return Done(RunVerdict.Infra, null, [], $"bad request: {e.Message}");
        }

        if (send.Failure is { } failure)
            return Done(RunVerdict.Infra, send, [], $"{failure.Headline} · {failure.Detail}");

        var response = send.Response!;
        foreach (var directive in doc.Extracts)
            if (Directives.ParseExtract(directive.Args, out _) is { } spec
                && Directives.EvaluateExtract(spec, response) is { } value)
                extracted[spec.Variable] = value;

        var outcomes = new List<(Directives.AssertionSpec, Directives.AssertionOutcome)>();
        foreach (var directive in doc.Asserts)
            if (Directives.ParseAssert(directive.Args, out _) is { } spec)
                outcomes.Add((spec, Directives.Evaluate(spec, response)));

        if (outcomes.Count == 0)
        {
            // The documented default: no assertions ⇒ 2xx is the bar.
            bool ok = response.StatusCode is >= 200 and < 300;
            return Done(ok ? RunVerdict.Pass : RunVerdict.Fail, send, [],
                ok ? "" : $"expected 2xx, got {response.StatusCode}");
        }

        var firstFail = outcomes.FirstOrDefault(o => !o.Item2.Passed);
        return firstFail.Item1 == null
            ? Done(RunVerdict.Pass, send, outcomes, "")
            : Done(RunVerdict.Fail, send, outcomes,
                $"{FailLabel(firstFail.Item1)} expected \"{firstFail.Item1.Expected}\", got \"{firstFail.Item2.Actual}\"");
    }

    private static string FailLabel(Directives.AssertionSpec spec) =>
        spec.Qualifier.Length > 0 ? spec.Qualifier : spec.Source;
}
