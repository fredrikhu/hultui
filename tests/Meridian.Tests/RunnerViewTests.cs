using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Core;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>The Runner view over a real collection: pre-flight, streaming,
/// the summary/report correspondence, abort/resume, re-run failures.</summary>
public class RunnerViewTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-run-").FullName;

    public RunnerViewTests(TestHttpServer server)
    {
        _server = server;
        Write("api/login.req", $"GET {_server.Origin}/ok\nname Login\nextract body $.ok -> flag\n");
        Write("api/consume.req", $"POST {_server.Origin}/echo\nname Consume\nbody text\nf={{{{flag}}}}\nend\n");
        Write("api/teapot.req", $"GET {_server.Origin}/teapot\nname Teapot\nassert status equals 200\n");
        Write("envs/local.env", "unused=1\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }


    private void Hosted(Action<HeadlessSession, MeridianApp> body)
    {
        using var session = HeadlessSession.Create(1440, 900);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrame();
            session.Window.PressKey(KeyCode.Digit2);
            session.PumpFrames(2);
            body(session, app);
        }
        finally
        {
            app?.Dispose();
        }
    }

    private static bool Contains(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true
            || n.Value?.Contains(text, StringComparison.Ordinal) == true);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    private static void Click(HeadlessSession session, string automationId)
    {
        var el = session.Window.FindByAutomationId(automationId);
        Assert.True(el != null, $"#{automationId} not found");
        el!.Click();
        session.PumpFrames(2);
    }

    private static void RunAndWait(HeadlessSession session, MeridianApp app)
    {
        Click(session, "run-start");
        session.PumpUntil(() => !app.RunnerRunning, TimeSpan.FromSeconds(15));
        session.PumpFrames(2);
    }

    [Fact]
    public void ThePreFlightShowsScopeOptionsAndTheCiHint()
    {
        Hosted((session, app) =>
        {
            Assert.NotNull(session.Window.FindByAutomationId("scope-all"));
            Assert.NotNull(session.Window.FindByAutomationId("scope-api"));
            Assert.NotNull(session.Window.FindByAutomationId("run-bail"));
            Assert.True(Contains(session, "3 req"), "scope counts missing");
            Assert.True(Contains(session, "meridian run"), "CI hint missing");
            Assert.True(Contains(session, "--env local"), "CI hint should carry the env");
        });
    }

    [Fact]
    public void ARunStreamsRowsInChainOrderAndSummarizes()
    {
        Hosted((session, app) =>
        {
            RunAndWait(session, app);

            // Chain order: Login before Consume (Teapot anywhere after its deps).
            Assert.True(Contains(session, "✓ Login"), "Login row missing");
            Assert.True(Contains(session, "✓ Consume"), "Consume row missing");
            Assert.True(Contains(session, "✗ Teapot"), "Teapot row missing");

            // The summary card IS the text report — literally, line for line.
            var summary = new RunSummary([], TimeSpan.Zero, false);
            Assert.NotNull(session.Window.FindByAutomationId("run-summary"));
            foreach (var line in LastReportLines(app))
                Assert.True(Contains(session, line), $"summary line missing: \"{line}\"");
        });
    }

    private static IReadOnlyList<string> LastReportLines(MeridianApp app) =>
        RunReports.TextLines(app.LastRunSummaryForTests!, app.LastScopeNameForTests, app.ActiveEnvName);

    [Fact]
    public void ExpandingAFailureShowsExpectedVersusActual()
    {
        Hosted((session, app) =>
        {
            RunAndWait(session, app);
            // Find the Teapot row and click it.
            int index = IndexOfRow(session, "Teapot");
            Click(session, $"run-row-{index}");

            Assert.NotNull(session.Window.FindByAutomationId("run-detail"));
            Assert.True(Contains(session, "expected \"200\" · actual \"418\""));
        });
    }

    private static int IndexOfRow(HeadlessSession session, string name)
    {
        for (int i = 0; i < 20; i++)
        {
            var el = session.Window.FindByAutomationId($"run-row-{i}");
            if (el == null) break;
            if (el.Element.AccessibleName?.Contains(name) == true) return i;
        }
        throw new Xunit.Sdk.XunitException($"no run row named {name}");
    }

    [Fact]
    public void RerunFailuresOnlyRunsJustTheProblems()
    {
        Hosted((session, app) =>
        {
            RunAndWait(session, app);
            Assert.True(Contains(session, "Re-run failures only"));

            Click(session, "run-rerun-failures");
            session.PumpUntil(() => !app.RunnerRunning, TimeSpan.FromSeconds(15));
            session.PumpFrames(2);

            // Only Teapot ran this time.
            Assert.Equal(1, app.LastRunSummaryForTests!.Results.Count);
            Assert.Equal("Teapot", app.LastRunSummaryForTests.Results[0].DisplayName);
        });
    }

    [Fact]
    public void AbortKeepsPartialResultsAndOffersResume()
    {
        Write("api/stall.req", $"GET {_server.Origin}/stall\nname Stall\n");
        Hosted((session, app) =>
        {
            Click(session, "run-start");
            // Wait until the stall request is in flight, then abort.
            session.PumpUntil(() => session.Window.FindByAutomationId("run-abort") != null, TimeSpan.FromSeconds(5));
            session.PumpUntil(() => Contains(session, "✓ Login"), TimeSpan.FromSeconds(10));
            session.Window.PressKey(KeyCode.Escape);
            session.PumpUntil(() => !app.RunnerRunning, TimeSpan.FromSeconds(10));
            session.PumpFrames(2);

            var summary = app.LastRunSummaryForTests!;
            Assert.True(summary.Aborted);
            Assert.True(summary.Skipped > 0, "something should have been skipped");
            Assert.True(Contains(session, "aborted —"), "the aborted line is part of the report");
            Assert.NotNull(session.Window.FindByAutomationId("run-resume"));

            // Resume starts executing the skipped remainder (the stalling
            // request would take its full 30 s timeout, so abort again rather
            // than wait it out) — what ran before is kept, not re-run.
            int kept = summary.Results.Count - summary.Skipped;
            Click(session, "run-resume");
            session.PumpUntil(() => app.RunnerRunning, TimeSpan.FromSeconds(5));
            session.Window.PressKey(KeyCode.Escape);
            session.PumpUntil(() => !app.RunnerRunning, TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            Assert.True(app.LastRunSummaryForTests!.Results.Count(r => r.Verdict == RunVerdict.Pass) >= kept - 1,
                "resume must keep prior results rather than discarding them");
        });
    }

    [Fact]
    public void TheRunWritesItsLogIntoTheCollection()
    {
        Hosted((session, app) =>
        {
            RunAndWait(session, app);

            var runsDir = Path.Combine(_root, "runs");
            Assert.True(Directory.Exists(runsDir), "runs/ directory missing");
            var log = Directory.GetFiles(runsDir, "*.run.log").Single();
            var text = File.ReadAllText(log);
            Assert.Contains("exit code 1", text);
            Assert.Contains("✗ Teapot", text);
        });
    }

    /// <summary>The rail counts each scope from the scanned collection, with no file read
    /// (MeridianApp.Runner.cs): what a count of the files in it finds. A file that cannot be read
    /// is counted, and the run leaves it out, as it always has.</summary>
    [Fact]
    public void TheRailCountsEachScopeFromTheCollection_AnUnreadableFileIncluded()
    {
        Write("other/lone.req", $"GET {_server.Origin}/ok\nname Lone\n");
        Write("other/deep/two.req", $"GET {_server.Origin}/ok\nname Deep two\n");
        Write("other/_folder.req", "header X-Folder: 1\n");
        Write("api/locked.req", $"GET {_server.Origin}/ok\nname Locked\n");
        // Held with no sharing, a read of it fails: on Windows, and on Linux, where .NET takes
        // an exclusive lock for FileShare.None.
        using var held = new FileStream(Path.Combine(_root, "api", "locked.req"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        int readable = Requests(_root).Count(Readable);
        if (OperatingSystem.IsWindows()) Assert.Equal(Requests(_root).Count() - 1, readable);
        Hosted((session, app) =>
        {
            foreach (var (id, folder) in new[] { ("scope-all", _root), ("scope-api", Path.Combine(_root, "api")), ("scope-other", Path.Combine(_root, "other")) })
                Assert.Equal($"{Requests(folder).Count()} req", ScopeCount(session, id));
            Assert.Equal("Run 6 requests", session.Window.FindByAutomationId("run-start")!.Element.AccessibleName);

            RunAndWait(session, app);
            var ran = app.LastRunSummaryForTests!.Results;
            Assert.Equal(readable, ran.Count);
            Assert.Equal(readable < 6, ran.All(r => r.DisplayName != "Locked"));
            Assert.True(Contains(session, $"{readable} / {readable}"), "the progress line counts what ran");
        });
    }

    /// <summary>The requests of a scope on disk: its .req files and its folders', a folder's
    /// defaults file and dot-folders left out, as the collection's scan does.</summary>
    private static IEnumerable<string> Requests(string folder) =>
        Directory.EnumerateFiles(folder, "*.req")
            .Where(f => !Path.GetFileName(f).Equals("_folder.req", StringComparison.OrdinalIgnoreCase))
            .Concat(Directory.EnumerateDirectories(folder)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .SelectMany(Requests));

    private static bool Readable(string file)
    {
        try { File.ReadAllText(file); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>The "N req" a scope row of the rail shows.</summary>
    private static string? ScopeCount(HeadlessSession session, string automationId)
    {
        var row = session.Window.FindByAutomationId(automationId);
        Assert.True(row != null, $"#{automationId} not found");
        return row!.Element.Children.OfType<TextElement>()
            .Select(t => t.Content).LastOrDefault(c => c.EndsWith(" req", StringComparison.Ordinal));
    }

    /// <summary>A run hands back the result rows it made before (MeridianApp.Runner.cs): a run of n
    /// results builds the results once a result and makes n rows, and the list on screen is what a
    /// build that keeps no row makes. Expanding a row makes none; a run that starts over makes its
    /// rows again.</summary>
    [Fact]
    public void ARunMakesOneRowAResult_AndKeepsTheRest()
    {
        for (int i = 0; i < 12; i++)
            Write($"bulk/b{i:00}.req", $"GET {_server.Origin}/ok\nname Bulk {i}\n");
        Hosted((session, app) =>
        {
            int made = app.RunRowsMade, results = app.RunResultsBuilds;
            RunAndWait(session, app);
            int n = app.LastRunSummaryForTests!.Results.Count;
            Assert.Equal(15, n);
            Assert.True(app.RunResultsBuilds - results >= n, $"{app.RunResultsBuilds - results} builds of the results for {n} results");
            Assert.Equal(n, app.RunRowsMade - made);
            AssertRunListIsAFullBuild(session, app, "after the run");

            Click(session, "run-row-3");
            Assert.NotNull(session.Window.FindByAutomationId("run-detail"));
            AssertRunListIsAFullBuild(session, app, "a row expanded");
            made = app.RunRowsMade;
            Click(session, "run-row-3");
            Assert.Null(session.Window.FindByAutomationId("run-detail"));
            Assert.Equal(0, app.RunRowsMade - made);

            made = app.RunRowsMade;
            Click(session, "run-rerun-all");
            session.PumpUntil(() => !app.RunnerRunning, TimeSpan.FromSeconds(15));
            session.PumpFrames(2);
            Assert.Equal(n, app.RunRowsMade - made);
            AssertRunListIsAFullBuild(session, app, "after a second run");
        });
    }

    /// <summary>A result builds the Runner's results alone (MeridianApp.Runner.cs): the window, the
    /// title bar and the rail of views build when the run starts and when it ends, not once a
    /// result; and a run going on behind another view builds nothing there, its results all shown
    /// when the Runner is shown again.</summary>
    [Fact]
    public void AResultBuildsTheResultsAlone_AndNothingBehindAnotherView()
    {
        for (int i = 0; i < 12; i++)
            Write($"bulk/b{i:00}.req", $"GET {_server.Origin}/ok\nname Bulk {i}\n");
        Hosted((session, app) =>
        {
            int builds = app.Builds, parts = app.PartBuilds, results = app.RunResultsBuilds;
            RunAndWait(session, app);
            int n = app.LastRunSummaryForTests!.Results.Count;
            Assert.True(app.RunResultsBuilds - results >= n, $"{app.RunResultsBuilds - results} builds of the results for {n} results");
            Assert.InRange(app.Builds - builds, 2, 4);      // the run's start and its end (and a click)
            Assert.InRange(app.PartBuilds - parts, 4, 8);   // the title bar and the rail, as often
            AssertRunListIsAFullBuild(session, app, "after the run");

            // Another run, and the Requests view at once, before any result lands: the run goes on
            // behind it.
            app.StartRun();
            session.Window.PressKey(KeyCode.Digit1);
            builds = app.Builds; results = app.RunResultsBuilds;
            int made = app.RunRowsMade;
            session.PumpUntil(() => !app.RunnerRunning, TimeSpan.FromSeconds(15));
            session.PumpFrames(2);
            Assert.InRange(app.Builds - builds, 1, 3);      // its end (and the run log's rescan)
            Assert.Equal(0, app.RunResultsBuilds - results);
            Assert.Equal(0, app.RunRowsMade - made);

            session.Window.PressKey(KeyCode.Digit2);
            session.PumpFrames(2);
            Assert.Equal(n, app.RunRowsMade - made);
            for (int i = 0; i < n; i++)
                Assert.NotNull(session.Window.FindByAutomationId($"run-row-{i}"));
            Assert.True(Contains(session, $"{n} / {n}"), "the progress line counts the run");
            AssertRunListIsAFullBuild(session, app, "shown again after a run behind Requests");
        });
    }

    /// <summary>The Runner's progress line, result list and summary on screen equal what a build
    /// that keeps no result row makes.</summary>
    private static void AssertRunListIsAFullBuild(HeadlessSession session, MeridianApp app, string when)
    {
        string kept = PartTests.Sig(RunnerMain(session));
        app.ForgetRunRows();
        typeof(MeridianApp).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!
            .Invoke(app, null);
        session.PumpFrames(2);
        string full = PartTests.Sig(RunnerMain(session));
        Assert.True(kept == full, $"the kept result list is not what a full build makes ({when})");
    }

    private static Element RunnerMain(HeadlessSession session) =>
        PartTests.Descendants(PartTests.Root(session)).First(e => e.Key == "runner-main");

    [Fact]
    public void FolderScopeRunsOnlyThatFolder()
    {
        Write("other/lone.req", $"GET {_server.Origin}/ok\nname Lone\n");
        Hosted((session, app) =>
        {
            Click(session, "scope-other");
            RunAndWait(session, app);

            Assert.Equal(["Lone"], app.LastRunSummaryForTests!.Results.Select(r => r.DisplayName));
        });
    }
}
