using System.Diagnostics;
using System.Text.Json;

namespace PerfSuite;

/// <summary>Console table, JSON capture, and the comparison of two captures.</summary>
static class Report
{
    public static void PrintTable(IReadOnlyList<Result> results)
    {
        Console.WriteLine();
        Console.WriteLine($"{"scenario",-44}{"elems",7}{"min",9}{"median",9}{"build",8}{"style",8}{"layout",8}{"render",8}{"other",8}{"alloc",10}");
        Console.WriteLine($"{"",-44}{"",7}{"ms",9}{"ms",9}{"ms",8}{"ms",8}{"ms",8}{"ms",8}{"ms",8}{"KB/op",10}");
        string? group = null;
        foreach (var r in results)
        {
            string g = r.Name[..r.Name.IndexOf('/')];
            if (g != group) { Console.WriteLine(); group = g; }
            string flag = r.UnrenderedSteps > 0 ? $"  ! {r.UnrenderedSteps}/{r.N} steps rendered nothing" : "";
            string figure = r.Figure is { } f ? $"  {f:0.0} {r.FigureUnit}" : "";
            Console.WriteLine($"{r.Name,-44}{r.Elements,7}{r.MinMs,9:0.000}{r.MedianMs,9:0.000}{r.BuildMs,8:0.00}{r.StyleMs,8:0.00}{r.LayoutMs,8:0.00}{r.RenderMs,8:0.00}{r.OtherMs,8:0.00}{r.AllocKB,10:N0}{figure}{flag}");
        }
        Console.WriteLine();
    }

    // ── JSON capture ─────────────────────────────────────────────────────────

    public static void WriteJson(string path, IReadOnlyList<Result> results, string? label, Options o, string? pinning)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        using var w = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",   // the repository stores LF; Windows would otherwise write CRLF
            // Captures are committed and read by people: keep "+", "×" and "—" as themselves.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        w.WriteStartObject();
        w.WriteString("label", label ?? "");
        w.WriteString("date", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm zzz"));
        w.WriteString("commit", GitCommit() ?? "");
        w.WriteString("machine", Environment.MachineName);
        w.WriteString("cpu", Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "");
        w.WriteNumber("cores", Environment.ProcessorCount);
        w.WriteString("os", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        w.WriteString("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        w.WriteString("configuration", IsRelease() ? "Release" : "Debug");
        w.WriteNumber("rounds", o.Rounds);
        w.WriteNumber("perRound", o.PerRound);
        w.WriteString("pinning", pinning ?? "");
        w.WriteStartArray("results");
        foreach (var r in results)
        {
            w.WriteStartObject();
            w.WriteString("name", r.Name);
            w.WriteString("description", r.Description);
            w.WriteNumber("elements", r.Elements);
            w.WriteNumber("n", r.N);
            w.WriteNumber("minMs", Math.Round(r.MinMs, 4));
            w.WriteNumber("medianMs", Math.Round(r.MedianMs, 4));
            w.WriteNumber("buildMs", Math.Round(r.BuildMs, 4));
            w.WriteNumber("styleMs", Math.Round(r.StyleMs, 4));
            w.WriteNumber("layoutMs", Math.Round(r.LayoutMs, 4));
            w.WriteNumber("renderMs", Math.Round(r.RenderMs, 4));
            w.WriteNumber("otherMs", Math.Round(r.OtherMs, 4));
            w.WriteNumber("allocKB", Math.Round(r.AllocKB, 1));
            w.WriteNumber("unrenderedSteps", r.UnrenderedSteps);
            if (r.Figure is { } figure)
            {
                w.WriteNumber("figure", Math.Round(figure, 2));
                w.WriteString("figureUnit", r.FigureUnit ?? "");
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private sealed record Capture(string Label, string Commit, string Date, Dictionary<string, Result> Results, List<string> Order);

    private static Capture ReadJson(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var results = new Dictionary<string, Result>();
        var order = new List<string>();
        foreach (var e in root.GetProperty("results").EnumerateArray())
        {
            var r = new Result(
                e.GetProperty("name").GetString()!, e.GetProperty("description").GetString() ?? "",
                e.GetProperty("elements").GetInt32(), e.GetProperty("n").GetInt32(),
                e.GetProperty("minMs").GetDouble(), e.GetProperty("medianMs").GetDouble(),
                e.GetProperty("buildMs").GetDouble(), e.GetProperty("styleMs").GetDouble(),
                e.GetProperty("layoutMs").GetDouble(), e.GetProperty("renderMs").GetDouble(),
                e.GetProperty("otherMs").GetDouble(), e.GetProperty("allocKB").GetDouble(),
                e.GetProperty("unrenderedSteps").GetInt32(),
                e.TryGetProperty("figure", out var figure) ? figure.GetDouble() : null,
                e.TryGetProperty("figureUnit", out var unit) ? unit.GetString() : null);
            results[r.Name] = r;
            order.Add(r.Name);
        }
        string Str(string name) => root.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
        return new Capture(Str("label"), Str("commit"), Str("date"), results, order);
    }

    // ── Compare ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Two captures side by side. A row is called slower or faster only when the minimum
    /// AND the median moved past the threshold in the same direction: the minimum is the
    /// cost when nothing interfered, the median says it was not one lucky sample, and
    /// either alone moves with the machine's mood.
    /// </summary>
    public static void Compare(string basePath, string currentPath, double threshold)
    {
        var a = ReadJson(basePath);
        var b = ReadJson(currentPath);
        Console.WriteLine();
        Console.WriteLine($"base:    {Describe(a)}  ({basePath})");
        Console.WriteLine($"current: {Describe(b)}  ({currentPath})");
        Console.WriteLine();
        Console.WriteLine($"{"scenario",-44}{"base min",10}{"cur min",10}{"Δmin",8}{"base med",10}{"cur med",10}{"Δmed",8}{"Δalloc",9}");

        int slower = 0, faster = 0;
        string? group = null;
        foreach (var name in b.Order.Concat(a.Order.Where(n => !b.Results.ContainsKey(n))))
        {
            string g = name[..name.IndexOf('/')];
            if (g != group) { Console.WriteLine(); group = g; }

            if (!a.Results.TryGetValue(name, out var x)) { Console.WriteLine($"{name,-44}{"—",10}{b.Results[name].MinMs,10:0.000}   (new)"); continue; }
            if (!b.Results.TryGetValue(name, out var y)) { Console.WriteLine($"{name,-44}{x.MinMs,10:0.000}{"—",10}   (gone)"); continue; }

            double dMin = Delta(x.MinMs, y.MinMs), dMed = Delta(x.MedianMs, y.MedianMs);
            double dAlloc = Delta(x.AllocKB, y.AllocKB);
            string verdict = dMin > threshold && dMed > threshold ? "  slower"
                : dMin < -threshold && dMed < -threshold ? "  faster" : "";
            if (verdict == "  slower") slower++;
            if (verdict == "  faster") faster++;
            string figures = x.Figure is { } fx && y.Figure is { } fy ? $"  {fx:0.0} → {fy:0.0} {y.FigureUnit}" : "";
            Console.WriteLine($"{name,-44}{x.MinMs,10:0.000}{y.MinMs,10:0.000}{Pct(dMin),8}{x.MedianMs,10:0.000}{y.MedianMs,10:0.000}{Pct(dMed),8}{Pct(dAlloc),9}{verdict}{figures}");
        }
        Console.WriteLine();
        Console.WriteLine($"{slower} slower, {faster} faster (both min and median past ±{threshold:P0}).");
        Console.WriteLine("Only compare captures from the same machine, same configuration, same power plan.");
    }

    private static string Describe(Capture c)
        => string.Join(" ", new[] { c.Label, c.Commit, c.Date }.Where(s => s.Length > 0));

    private static double Delta(double from, double to) => from == 0 ? 0 : to / from - 1;
    private static string Pct(double d) => $"{d * 100:+0;-0;0}%";

    // ── Provenance ───────────────────────────────────────────────────────────

    private static string? GitCommit()
    {
        try
        {
            // No tag matches, so describe falls back to the short hash — plus "-dirty" when
            // the capture ran on uncommitted changes, which a baseline must not hide.
            using var p = Process.Start(new ProcessStartInfo("git", "describe --always --dirty --match=__no_tag__")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            });
            if (p is null) return null;
            string sha = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return p.ExitCode == 0 ? sha : null;
        }
        catch (Exception) { return null; }
    }

    public static bool IsRelease()
    {
        var attr = typeof(Report).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false)
            .OfType<DebuggableAttribute>().FirstOrDefault();
        return attr is null || !attr.IsJITOptimizerDisabled;
    }
}
