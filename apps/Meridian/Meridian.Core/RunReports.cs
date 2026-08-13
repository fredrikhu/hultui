using System.Globalization;
using System.Text;
using System.Xml;

namespace Meridian.Core;

/// <summary>
/// The run, in words — ONE set of them. The GUI's summary card and the CLI's
/// output both come from <see cref="TextLines"/>, so a developer reading a CI
/// log can picture the screen: same order, same glyphs, same wording. That
/// correspondence is asserted literally by the tests; change the words here
/// and both surfaces change together.
/// </summary>
public static class RunReports
{
    public const string PassGlyph = "✓";
    public const string FailGlyph = "✗";
    public const string InfraGlyph = "!";
    public const string SkipGlyph = "−";

    public static string Glyph(RunVerdict verdict) => verdict switch
    {
        RunVerdict.Pass => PassGlyph,
        RunVerdict.Fail => FailGlyph,
        RunVerdict.Infra => InfraGlyph,
        _ => SkipGlyph,
    };

    /// <summary>The header/counts/problem lines, in report order. Failures
    /// first, then infra — nobody should re-open the run to learn what broke.</summary>
    public static IReadOnlyList<string> TextLines(RunSummary summary, string scopeName, string? envName)
    {
        var lines = new List<string>
        {
            $"{scopeName} ({envName ?? "no environment"}) {summary.Results.Count} requests",
            CountsLine(summary),
        };
        foreach (var result in summary.Results.Where(r => r.Verdict == RunVerdict.Fail))
            lines.Add($"{FailGlyph} {result.DisplayName}  {result.Detail}");
        foreach (var result in summary.Results.Where(r => r.Verdict == RunVerdict.Infra))
            lines.Add($"{InfraGlyph} {result.DisplayName}  {result.Detail}");
        if (summary.Aborted)
            lines.Add($"{InfraGlyph} aborted — {summary.Skipped} requests never ran");
        lines.Add($"exit code {summary.ExitCode}");
        return lines;
    }

    public static string CountsLine(RunSummary summary) =>
        $"{PassGlyph} {summary.Passed}  {FailGlyph} {summary.Failed}  {InfraGlyph} {summary.Infra}  {SkipGlyph} {summary.Skipped}  wall {summary.Wall.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s";

    public static string Text(RunSummary summary, string scopeName, string? envName) =>
        string.Join("\n", TextLines(summary, scopeName, envName)) + "\n";

    /// <summary>JUnit XML, the CI lingua franca: a Fail is a &lt;failure&gt;,
    /// an Infra an &lt;error&gt; (infrastructure, not the code under test),
    /// a Skipped a &lt;skipped/&gt;.</summary>
    public static string JUnit(RunSummary summary, string scopeName)
    {
        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false });
        writer.WriteStartElement("testsuite");
        writer.WriteAttributeString("name", scopeName);
        writer.WriteAttributeString("tests", summary.Results.Count.ToString());
        writer.WriteAttributeString("failures", summary.Failed.ToString());
        writer.WriteAttributeString("errors", summary.Infra.ToString());
        writer.WriteAttributeString("skipped", summary.Skipped.ToString());
        writer.WriteAttributeString("time", summary.Wall.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));

        foreach (var result in summary.Results)
        {
            writer.WriteStartElement("testcase");
            writer.WriteAttributeString("classname", scopeName);
            writer.WriteAttributeString("name", result.DisplayName);
            writer.WriteAttributeString("time", result.Duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            switch (result.Verdict)
            {
                case RunVerdict.Fail:
                    writer.WriteStartElement("failure");
                    writer.WriteAttributeString("message", result.Detail);
                    writer.WriteEndElement();
                    break;
                case RunVerdict.Infra:
                    writer.WriteStartElement("error");
                    writer.WriteAttributeString("message", result.Detail);
                    writer.WriteEndElement();
                    break;
                case RunVerdict.Skipped:
                    writer.WriteStartElement("skipped");
                    writer.WriteEndElement();
                    break;
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.Flush();
        return sb.ToString();
    }
}
