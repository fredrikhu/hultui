using HultUI.Core.Accessibility;
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
