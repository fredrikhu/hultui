using System.Diagnostics;
using PerfSuite;

// ─────────────────────────────────────────────────────────────────────────────
// Scenario performance suite: the framework's features one at a time — grid, wrap,
// text, scrolling, sticky, virtual lists, tables, components, input, focus, overlays,
// themes, effects, animation, accessibility — each built in a headless session and
// timed from the mutation to the rendered frame, with the frame split into build /
// style / layout / render. The js-framework-benchmark sample covers one workload in
// depth on a real window; this covers the breadth, so a change to one feature can be
// shown not to cost the others anything. See README.md.
//
//   dotnet run -c Release --project samples/HultUI.Samples.PerfSuite
//   … -- --filter grid,text --json captures/mine.json --label "my change"
//   … -- --compare captures/before.json captures/after.json
// ─────────────────────────────────────────────────────────────────────────────

System.Globalization.CultureInfo.DefaultThreadCurrentCulture =
    System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        HultUI scenario performance suite

          --list                      list scenarios and exit
          --verify                    run each scenario's self-check (untimed) and exit
          --filter a,b                run scenarios whose name contains any of the terms
          --rounds N                  round-robin rounds (default 5)
          --samples N                 timed steps per round (default 5)
          --quick                     2 rounds × 3 steps: a smoke run, not a capture
          --verbose                   print every timed step (total, passes, gen-0 GCs)
          --no-pin                    don't pin to the performance cores of a hybrid CPU
          --json PATH                 write the results as a capture
          --label TEXT                label stored in the capture
          --compare BASE CURRENT      compare two captures and exit
          --threshold PCT             compare: change that counts (default 10)
        """);
    return 0;
}

int compareAt = Array.IndexOf(args, "--compare");
if (compareAt >= 0)
{
    if (compareAt + 2 >= args.Length) { Console.Error.WriteLine("--compare needs two capture paths"); return 2; }
    double threshold = double.TryParse(Arg("--threshold"), out var t) ? t / 100 : 0.10;
    Report.Compare(args[compareAt + 1], args[compareAt + 2], threshold);
    return 0;
}

var all = Scenarios.All().ToList();
var duplicate = all.GroupBy(s => s.Name).FirstOrDefault(g => g.Count() > 1);
if (duplicate is not null) throw new InvalidOperationException($"Two scenarios are called {duplicate.Key}.");

if (args.Contains("--list"))
{
    foreach (var s in all) Console.WriteLine($"{s.Name,-44}{s.Description}");
    return 0;
}

var selected = all;
if (Arg("--filter") is { } filter)
{
    // Split on commas, but not one between digits: "grid,text" is two terms, "2,000" one.
    var terms = System.Text.RegularExpressions.Regex.Split(filter, @"(?<!\d),|,(?!\d)")
        .Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
    selected = all.Where(s => terms.Any(t => s.Name.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
    if (selected.Count == 0) { Console.Error.WriteLine($"No scenario matches '{filter}'. Try --list."); return 2; }
}

if (args.Contains("--verify"))
{
    var failures = Runner.Verify(selected);
    foreach (var f in failures) Console.WriteLine($"FAIL  {f}");
    Console.WriteLine(failures.Count == 0
        ? $"verify: all {selected.Count} scenarios do what they say"
        : $"verify: {failures.Count} failure(s) across {selected.Count} scenarios");
    return failures.Count == 0 ? 0 : 1;
}

bool quick = args.Contains("--quick");
Runner.Verbose = args.Contains("--verbose");
var options = new Options
{
    Rounds = int.TryParse(Arg("--rounds"), out var rounds) ? rounds : quick ? 2 : 5,
    PerRound = int.TryParse(Arg("--samples"), out var perRound) ? perRound : quick ? 3 : 5,
};

if (!Report.IsRelease())
    Console.WriteLine("WARNING: Debug build. Debug compiles in element origins and strict-lifetime checks; capture with -c Release.");

// Everything else the machine does lands on the samples; raising the priority and reading
// the minimum is what makes two captures comparable (TextBench README).
try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High; }
catch (Exception) { /* not permitted here; the numbers are just noisier */ }
string? pinning = args.Contains("--no-pin") ? null : CorePinning.PinToFastestCores();
if (pinning is not null) Console.WriteLine(pinning);

Console.WriteLine($"=== HultUI scenario suite: {selected.Count} scenarios, {options.Rounds} rounds × {options.PerRound} steps ===");
var sw = Stopwatch.StartNew();
var results = Runner.Run(selected, options, msg => Console.WriteLine($"  {msg}  ({sw.Elapsed.TotalSeconds:0}s)"));
Report.PrintTable(results);
Console.WriteLine($"=== done in {sw.Elapsed.TotalSeconds:0} s. min = best sample; build = the mutation itself; other = a11y, overlays, frame bookkeeping ===");

if (Arg("--json") is { } json)
{
    Report.WriteJson(json, results, Arg("--label"), options, pinning);
    Console.WriteLine($"capture written to {json}");
}
foreach (var f in Runner.Failures.Distinct()) Console.WriteLine($"CHECK FAILED  {f}");
return results.Any(r => r.UnrenderedSteps > 0) || Runner.Failures.Count > 0 ? 1 : 0;
