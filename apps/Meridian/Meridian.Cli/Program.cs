using System.Text;
using Meridian.Cli;
using Meridian.Core;

// The report's glyphs (✓ ✗ ! −) are the whole point of the wording being
// shared with the app. Windows hands a console app the OEM codepage by
// default, which transliterates them ("√" for "✓") in terminals and in
// redirected CI logs alike — so say UTF-8 explicitly. Guarded: a process with
// no usable stdout handle throws here, and that must not fail the run.
try { Console.OutputEncoding = Encoding.UTF8; }
catch (IOException) { }

// `meridian run collection/ --env ci --report junit:reports/run.xml`
//
// The same engine and the same words as the app's Runner view: this binary
// parses a command line, hands the scope to CollectionRunner, and prints
// RunReports. Nothing about a run is decided here.

if (CliOptions.Parse(args, out var parseError) is not { } options)
{
    Console.Error.WriteLine($"meridian: {parseError}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.Usage);
    return CliOptions.ExitUsage;
}

if (CollectionLoader.Load(options.Path, options.Env, new DefaultKeyring(), out var loadError) is not { } loaded)
{
    Console.Error.WriteLine($"meridian: {loadError}");
    return CliOptions.ExitUsage;
}

if (options.Command == "list")
{
    foreach (var request in loaded.Requests)
        Console.WriteLine($"{request.Doc.Method ?? "?",-7} {request.DisplayName}");
    Console.WriteLine($"{loaded.Requests.Count} requests in {loaded.ScopeName}");
    return 0;
}

if (loaded.Requests.Count == 0)
{
    Console.Error.WriteLine($"meridian: no requests in {loaded.ScopeName}");
    return CliOptions.ExitUsage;
}

// A secret this machine lacks is named before the run rather than surfacing as
// a puzzling unresolved-variable failure on every request that uses it.
foreach (var key in loaded.MissingSecrets)
    Console.Error.WriteLine($"meridian: secret {key} is not set on this machine (keyring or ${key})");

using var engine = new HttpEngine();
var runner = new CollectionRunner(engine);

// Ctrl+C aborts the run the way Esc does in the app: what ran is reported.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var summary = await runner.RunAsync(
    loaded.Requests,
    loaded.Environment,
    options.Bail,
    options.Quiet ? null : PrintResult,
    cts.Token);

foreach (var line in RunReports.TextLines(summary, loaded.ScopeName, options.Env))
    Console.WriteLine(line);

if (options.JUnitPath is { } junitPath)
{
    try
    {
        if (Path.GetDirectoryName(junitPath) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);
        File.WriteAllText(junitPath, RunReports.JUnit(summary, loaded.ScopeName));
        Console.WriteLine($"junit: {junitPath}");
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"meridian: could not write {junitPath}: {e.Message}");
        return 1;
    }
}

return summary.ExitCode;

// Streamed per request, so a long run shows progress instead of going quiet.
static void PrintResult(RunRequestResult result)
{
    string status = result.Send?.Response is { } response
        ? $"{response.StatusCode} {response.Timings.Total.TotalMilliseconds:0}ms"
        : result.Verdict == RunVerdict.Skipped ? "skipped" : "—";
    string assertions = result.Assertions.Count > 0
        ? $"  {result.AssertionsPassed}/{result.Assertions.Count} {RunReports.PassGlyph}"
        : "";
    Console.WriteLine($"{RunReports.Glyph(result.Verdict)} {result.DisplayName,-28} {status}{assertions}");
}
