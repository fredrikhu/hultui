using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Core;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>P9: the append-only log and the History view over it.</summary>
public class HistoryTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-hist-").FullName;

    public HistoryTests(TestHttpServer server)
    {
        _server = server;
        Write("ok.req", $"GET {_server.Origin}/ok\nname Ok\n");
        Write("teapot.req", $"GET {_server.Origin}/teapot\nname Teapot\n");
        Write("envs/local.env", "unused=1\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ── The log ──────────────────────────────────────────────────────────

    [Fact]
    public void EntriesRoundTripThroughTheLog()
    {
        var entry = new HistoryEntry(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000),
            "POST", "https://api.x/v1/charges?a=b", 201, 142.7, "ci", @"C:\c\create.req");

        Assert.True(HistoryLog.TryParse(HistoryLog.Format(entry), out var read));
        Assert.Equal(entry.At.ToUnixTimeMilliseconds(), read.At.ToUnixTimeMilliseconds());
        Assert.Equal((entry.Method, entry.Url, entry.StatusCode, entry.EnvName, entry.RequestPath),
            (read.Method, read.Url, read.StatusCode, read.EnvName, read.RequestPath));
        Assert.Equal(143, read.DurationMs, 0);
    }

    [Fact]
    public void AnInfraFailureIsHistoryToo()
    {
        var entry = new HistoryEntry(DateTimeOffset.Now, "GET", "https://nope.invalid", 0, 30, null, null,
            "could not resolve host");

        Assert.True(HistoryLog.TryParse(HistoryLog.Format(entry), out var read));
        Assert.False(read.Reached);
        Assert.Equal("could not resolve host", read.Detail);
        Assert.Null(read.EnvName);
    }

    [Fact]
    public void ACorruptLineCostsOneEntryNotTheFile()
    {
        var good = new HistoryEntry(DateTimeOffset.Now, "GET", "https://x", 200, 5, null, null);
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryLog.PathFor(_root))!);
        File.WriteAllText(HistoryLog.PathFor(_root),
            "garbage\n" + HistoryLog.Format(good) + "\nalso\tgarbage\n");

        var entries = HistoryLog.Read(_root);
        Assert.Single(entries);
        Assert.Equal("https://x", entries[0].Url);
    }

    [Fact]
    public void ReadingReturnsNewestFirst()
    {
        var now = DateTimeOffset.Now;
        HistoryLog.Append(_root, new HistoryEntry(now.AddMinutes(-5), "GET", "https://old", 200, 1, null, null));
        HistoryLog.Append(_root, new HistoryEntry(now, "GET", "https://new", 200, 1, null, null));

        var entries = HistoryLog.Read(_root);
        Assert.Equal(["https://new", "https://old"], entries.Select(e => e.Url));
    }

    [Fact]
    public void AMissingLogIsAnEmptyHistory()
    {
        Assert.Empty(HistoryLog.Read(Path.Combine(_root, "no-such-workspace")));
    }

    [Fact]
    public void TodaysSendsShowAClockTime()
    {
        // Built in local time on purpose: the label is for a person reading it
        // on this machine, and mixing UTC in here would test the conversion
        // rather than the formatting.
        var now = new DateTimeOffset(2026, 8, 13, 20, 0, 0, TimeSpan.Zero).ToLocalTime();
        var at = now.Date.AddHours(14).AddMinutes(3).AddSeconds(12);

        var label = HistoryLog.TimeLabel(
            new HistoryEntry(new DateTimeOffset(at), "GET", "u", 200, 1, null, null), now);

        Assert.Equal("14:03:12", label);
    }

    [Fact]
    public void OlderSendsGetARelativeDay()
    {
        var now = DateTimeOffset.Now;
        string Label(int daysAgo) => HistoryLog.TimeLabel(
            new HistoryEntry(now.AddDays(-daysAgo), "GET", "u", 200, 1, null, null), now);

        Assert.StartsWith("yesterday", Label(1));
        Assert.DoesNotContain("yesterday", Label(0));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", Label(30));
    }

    [Fact]
    public void TodayIsTodayOnlyForToday()
    {
        var now = DateTimeOffset.Now;
        Assert.True(HistoryLog.IsToday(new HistoryEntry(now, "GET", "u", 200, 1, null, null), now));
        Assert.False(HistoryLog.IsToday(new HistoryEntry(now.AddDays(-1), "GET", "u", 200, 1, null, null), now));
    }

    // ── The view ─────────────────────────────────────────────────────────


    private void Hosted(Action<HeadlessSession, MeridianApp> body)
    {
        using var session = HeadlessSession.Create(1600, 900);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrame();
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

    private static void GoTo(HeadlessSession session, KeyCode key)
    {
        session.Window.PressKey(key);
        session.PumpFrames(2);
    }

    private void SendBoth(HeadlessSession session, MeridianApp app)
    {
        foreach (var name in new[] { "ok", "teapot" })
        {
            Click(session, "req-" + name);
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result != null && app.ActiveTab.Sending == null,
                TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public void SendsLandInTheViewAndOnDisk()
    {
        Hosted((session, app) =>
        {
            SendBoth(session, app);
            GoTo(session, KeyCode.Digit4);

            Assert.Equal(2, app.History.Count);
            // Newest first: teapot was sent last.
            Assert.Equal("/teapot", new Uri(app.History[0].Url).AbsolutePath);
            Assert.True(Contains(session, "/ok"), "the ok row is missing");
            Assert.True(Contains(session, "✗ 418") || Contains(session, "! 418") || Contains(session, "418"),
                "the teapot's status is missing");

            // And the log is on disk, one line per send.
            var lines = File.ReadAllLines(HistoryLog.PathFor(_root));
            Assert.Equal(2, lines.Length);
        });
    }

    [Fact]
    public void AnUnreachableSendIsRecordedWithItsReason()
    {
        int deadPort = FreePort();
        Write("dead.req", $"GET http://127.0.0.1:{deadPort}/x\nname Dead\n");
        Hosted((session, app) =>
        {
            Click(session, "req-dead");
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result != null && app.ActiveTab.Sending == null,
                TimeSpan.FromSeconds(10));
            GoTo(session, KeyCode.Digit4);

            var entry = app.History.Single();
            Assert.False(entry.Reached);
            Assert.Contains("refused", entry.Detail);
            Assert.True(Contains(session, "connection refused"), "the row should say why it never landed");
        });
    }

    [Fact]
    public void TheSearchFiltersRows()
    {
        Hosted((session, app) =>
        {
            SendBoth(session, app);
            GoTo(session, KeyCode.Digit4);

            var search = session.Window.FindByAutomationId("history-search");
            search!.Click();
            session.Window.TypeText("teapot");
            session.PumpFrames(3);

            Assert.Single(app.FilteredHistory());
            Assert.True(Contains(session, "1 of 2"), "the count should reflect the filter");
        });
    }

    [Fact]
    public void FilteringToNothingOffersAWayBack()
    {
        Hosted((session, app) =>
        {
            SendBoth(session, app);
            GoTo(session, KeyCode.Digit4);

            var search = session.Window.FindByAutomationId("history-search");
            search!.Click();
            session.Window.TypeText("grpc");
            session.PumpFrames(3);

            Assert.NotNull(session.Window.FindByAutomationId("history-no-matches"));
            Assert.True(Contains(session, "0 matches for \"grpc\""));
            Click(session, "history-clear-search");

            Assert.Equal(2, app.FilteredHistory().Count);
        });
    }

    [Fact]
    public void MethodAndFailureFiltersNarrowTheList()
    {
        Hosted((session, app) =>
        {
            SendBoth(session, app);
            GoTo(session, KeyCode.Digit4);

            Click(session, "filter-failures");
            Assert.Single(app.FilteredHistory());          // only the 418
            Assert.Equal(418, app.FilteredHistory()[0].StatusCode);

            Click(session, "filter-failures");             // toggles off
            Assert.Equal(2, app.FilteredHistory().Count);

            Click(session, "filter-POST");                 // nothing was a POST
            Assert.Empty(app.FilteredHistory());
        });
    }

    [Fact]
    public void SelectingARowShowsTheReadOnlyArchive()
    {
        Hosted((session, app) =>
        {
            SendBoth(session, app);
            GoTo(session, KeyCode.Digit4);
            // Rows are virtualized (unreachable by automation id), and the mock
            // specifies the keyboard path anyway: ↑↓ walk, enter opens.
            GoTo(session, KeyCode.Down);

            Assert.NotNull(session.Window.FindByAutomationId("history-archive"));
            Assert.True(Contains(session, "archived · read-only"));
            Assert.NotNull(session.Window.FindByAutomationId("history-restore"));
        });
    }

    [Fact]
    public void RestoreOpensTheOriginalFileWithoutOverwritingIt()
    {
        Hosted((session, app) =>
        {
            SendBoth(session, app);

            // The file has MOVED ON since that send — a later edit points it
            // somewhere else. Restoring the old send must not quietly write the
            // old URL back over it; that would lose work.
            var file = Path.Combine(_root, "teapot.req");
            File.WriteAllText(file, $"GET {_server.Origin}/edited-since\nname Teapot\n");
            var onDisk = File.ReadAllText(file);

            GoTo(session, KeyCode.Digit4);
            GoTo(session, KeyCode.Down);
            Click(session, "history-restore");

            Assert.Equal(MeridianView.Requests, app.View);
            Assert.EndsWith("teapot.req", app.ActiveTab!.Path);
            // The tab shows what was sent…
            Assert.EndsWith("/teapot", app.ActiveTab.Doc.Url);
            Assert.True(app.ActiveTab.Dirty, "a restored send is an unsaved draft");
            // …while the file keeps the newer edit until someone saves.
            Assert.Equal(onDisk, File.ReadAllText(file));
        });
    }

    [Fact]
    public void HistorySurvivesAReopen()
    {
        Hosted((session, app) =>
        {
            SendBoth(session, app);
        });
        // A second session reads the same log back.
        Hosted((session, app) =>
        {
            GoTo(session, KeyCode.Digit4);
            Assert.Equal(2, app.History.Count);
        });
    }

    /// <summary>A key in History makes the two rows the selection left and reached, not every
    /// row on screen (they are item rows the list keeps, and none of them looks its own place up
    /// in the log any more); the list holds the selection, so a reader hears which row it is;
    /// and a character typed into the search builds the window once, not twice.</summary>
    [Fact]
    public void AKeyMakesTheTwoRowsItMovesBetween_AndASearchCharacterBuildsOnce()
    {
        var start = DateTimeOffset.Now.AddMinutes(-90);
        for (int i = 0; i < 40; i++)
            HistoryLog.Append(_root, new HistoryEntry(start.AddMinutes(i), i % 3 == 0 ? "POST" : "GET",
                $"https://api.x/v1/items/{i}", 200, 10 + i, null, null));
        Hosted((session, app) =>
        {
            GoTo(session, KeyCode.Digit4);
            GoTo(session, KeyCode.Down);
            for (int i = 0; i < 5; i++)
            {
                int made = app.HistoryRowsMade, builds = app.Builds;
                GoTo(session, KeyCode.Down);
                Assert.Equal(1, app.Builds - builds);
                Assert.Equal(2, app.HistoryRowsMade - made);
            }
            // Newest first: the sixth row is the 35th send.
            var selected = Walk(session.Window.AccessibilityTree).Single(n => n.IsSelected == true);
            Assert.EndsWith("/items/34 200", selected.Name);

            session.Window.FindByAutomationId("history-search")!.Click();
            session.PumpFrames(2);
            foreach (var ch in "items/3")
            {
                int builds = app.Builds;
                session.Window.TypeText(ch.ToString());
                Assert.Equal(1, app.Builds - builds);
                session.PumpFrames(2);
            }
            Assert.Equal(11, app.FilteredHistory().Count);   // items/3 and items/30 … 39
            Assert.True(Contains(session, "11 of 40"), "the count should reflect the filter");
        });
    }

    [Fact]
    public void AnEmptyHistorySaysSo()
    {
        Hosted((session, app) =>
        {
            GoTo(session, KeyCode.Digit4);
            Assert.NotNull(session.Window.FindByAutomationId("history-empty"));
        });
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
