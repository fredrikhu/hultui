using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
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


    /// <summary>The app over the temp collection, its clock at <paramref name="at"/> (local noon on
    /// a fixed day unless given; see <see cref="HistoryClock"/>).</summary>
    private void Hosted(Action<HeadlessSession, MeridianApp> body, HeadlessOptions? options = null, DateTimeOffset? at = null)
    {
        using var session = HeadlessSession.Create(1600, 900, options: options);
        HistoryClock.Set(session, at ?? HistoryClock.Noon);
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

    /// <summary>A key in History makes no row: they are item rows the list keeps (none of them
    /// looks its own place up in the log any more), and the list draws the selection under them,
    /// the row's own fill (at the 60 % a row older than today is drawn at), so a key made the two
    /// rows the selection left and reached (plan 17 round 7). The list holds the selection, so a
    /// reader hears which row it is; and a character typed into the search builds the window
    /// once, not twice.</summary>
    [Fact]
    public void AKeyMakesNoRow_TheListDrawsTheSelection_AndASearchCharacterBuildsOnce()
    {
        // Two sends two days old (at the end of the list, drawn at 60 %), then forty from today.
        for (int i = 0; i < 2; i++)
            HistoryLog.Append(_root, new HistoryEntry(HistoryClock.Noon.AddDays(-2).AddMinutes(i), "GET",
                $"https://api.x/v1/old/{i}", 200, 10, null, null));
        var start = HistoryClock.Noon.AddMinutes(-90);
        for (int i = 0; i < 40; i++)
            HistoryLog.Append(_root, new HistoryEntry(start.AddMinutes(i), i % 3 == 0 ? "POST" : "GET",
                $"https://api.x/v1/items/{i}", 200, 10 + i, null, null));
        var raised = Meridian.Theme.Azimuth.Raised;
        Hosted((session, app) =>
        {
            GoTo(session, KeyCode.Digit4);
            GoTo(session, KeyCode.Down);
            var list = (HultUI.Core.Elements.VirtualListElement<MeridianApp.HistoryRowItem>)
                session.Window.FindByAutomationId("history-rows")!.Element;
            for (int i = 0; i < 5; i++)
            {
                int made = app.HistoryRowsMade;
                GoTo(session, KeyCode.Down);
                Assert.Equal(0, app.HistoryRowsMade - made);
            }
            // Newest first: the sixth row is the 35th send.
            var selected = Walk(session.Window.AccessibilityTree).Single(n => n.IsSelected == true);
            Assert.EndsWith("/items/34 200", selected.Name);
            Assert.Equal(5, list.SelectedIndex);
            // The row's own fill, drawn by the list; no row draws one (none is hovered). The send is
            // today's: 56 minutes before the app's noon.
            Assert.True(HistoryLog.IsToday(app.FilteredHistory()[5], HistoryClock.Noon));
            Assert.Equal(raised, list.SelectionHighlight!.Value.Background);
            Assert.DoesNotContain(list.Children, r => r is HultUI.Core.Elements.BoxElement { BackgroundColor: var c } && c == raised);
            // On a send older than today, the fill at the row's 60 %.
            while (list.SelectedIndex < 41) GoTo(session, KeyCode.Down);
            Assert.EndsWith("/old/0 200", Walk(session.Window.AccessibilityTree).Single(n => n.IsSelected == true).Name);
            Assert.Equal(Meridian.Theme.Azimuth.Alpha(raised, 0.6), list.SelectionHighlight!.Value.Background);

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
            Assert.True(Contains(session, "11 of 42"), "the count should reflect the filter");
        });
    }

    /// <summary>A send older than today reads dimmer through its colours, not through its row: the
    /// row has no <c>Opacity</c> (one on an element with children draws it offscreen, so every
    /// visible row was a layer on every History frame: 29 at 1440×900, 20–32 % of a real frame,
    /// plan 17 round 8 step 9), and everything it draws (its texts, the method chip's tint and
    /// label, its hover tint) is today's row's colour at 60 % of its alpha.</summary>
    [Fact]
    public void AnOlderSendFadesItsColours_NotItsRow()
    {
        // The same send twice: two days old, then from today (newest first: today's is row 0).
        var now = HistoryClock.Noon;
        HistoryLog.Append(_root, new HistoryEntry(now.AddDays(-2), "POST", "https://api.x/v1/same", 201, 12, "ci", null));
        HistoryLog.Append(_root, new HistoryEntry(now.AddMinutes(-1), "POST", "https://api.x/v1/same", 201, 12, "ci", null));
        static Color Fade(Color c) => new(c.R, c.G, c.B, (byte)Math.Round(c.A * 0.6));
        Hosted((session, app) =>
        {
            GoTo(session, KeyCode.Digit4);
            var list = session.Window.FindByAutomationId("history-rows")!.Element;
            var todays = list.Children.Single(r => r.AutomationId == "hist-row-0");
            var older = list.Children.Single(r => r.AutomationId == "hist-row-1");
            Assert.True(HistoryLog.IsToday(app.FilteredHistory()[0], now));
            Assert.False(HistoryLog.IsToday(app.FilteredHistory()[1], now));

            var a = Descendants(todays).ToList();
            var b = Descendants(older).ToList();
            Assert.Equal(a.Count, b.Count);
            Assert.All(b, e => Assert.Equal(1f, e.Opacity));
            int texts = 0, fills = 0;
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].GetType(), b[i].GetType());
                if (a[i] is HultUI.Core.Elements.TextElement)
                {
                    Assert.NotNull(a[i].Inherited.TextColor);
                    Assert.Equal(Fade(a[i].Inherited.TextColor!.Value), b[i].Inherited.TextColor);
                    texts++;
                }
                else if (a[i] is HultUI.Core.Elements.BoxElement { BackgroundColor: { A: > 0 } fill })
                {
                    Assert.Equal(Fade(fill), ((HultUI.Core.Elements.BoxElement)b[i]).BackgroundColor);
                    fills++;
                }
            }
            Assert.Equal((6, 1), (texts, fills));   // five texts and the chip's label; the chip's tint

            // Hovered, the older row's tint is at its 60 % too, and the frame draws no layer.
            var log = session.Window.RenderLog;
            log.Clear();
            var box = older.WindowBorderBox;
            session.Window.MoveMouse(box.X + box.Width / 2, box.Y + box.Height / 2);
            session.PumpFrames(2);
            Assert.Equal(Fade(Meridian.Theme.Azimuth.Raised), ((HultUI.Core.Elements.BoxElement)older).BackgroundColor);
            Assert.Contains(log.Named("DrawText"), op => op.ToString().Contains("api.x/v1/same", StringComparison.Ordinal));
            Assert.Empty(log.Named("PushOpacity"));
        }, new HeadlessOptions { RecordDrawCalls = true });
    }

    /// <summary>Thirty seconds after midnight, by the app's clock, a send from a minute ago is
    /// yesterday's and one from twenty seconds ago is today's: in the row's time, in the "today"
    /// filter, and in the time a new send is stamped with. History read the machine's clock
    /// (DateTimeOffset.Now), so a test could not set the time of day, and one that sent "a minute
    /// ago" failed from 00:00 to 00:01 local (meridian-v1.8.0's installer run, at 00:00:41 UTC).</summary>
    [Fact]
    public void JustAfterMidnight_TodayIsTheAppClocks()
    {
        var midnight = HistoryClock.At(0, 0, 30);
        HistoryLog.Append(_root, new HistoryEntry(midnight.AddMinutes(-1), "GET", "https://api.x/v1/late", 200, 12, null, null));
        HistoryLog.Append(_root, new HistoryEntry(midnight.AddSeconds(-20), "GET", "https://api.x/v1/early", 200, 12, null, null));
        Hosted((session, app) =>
        {
            GoTo(session, KeyCode.Digit4);
            // Newest first: the send at 00:00:10 is row 0, the one at 23:59:30 row 1.
            var list = session.Window.FindByAutomationId("history-rows")!.Element;
            string TimeOf(string row) => Descendants(list.Children.Single(r => r.AutomationId == row))
                .OfType<HultUI.Core.Elements.TextElement>().First().Content;
            Assert.Equal("00:00:10", TimeOf("hist-row-0"));
            Assert.Equal("yesterday 23:59", TimeOf("hist-row-1"));

            Click(session, "filter-today");
            Assert.Equal(["https://api.x/v1/early"], app.FilteredHistory().Select(e => e.Url));
            Click(session, "filter-today");

            app.RecordHistory("GET", "https://api.x/v1/now", SendResult.Infra(
                new InfraFailure(InfraKind.Timeout, "timed out", "", TimeSpan.FromSeconds(1))));
            var stamped = app.History.Single(e => e.Url == "https://api.x/v1/now").At;
            Assert.InRange(stamped - midnight, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }, at: midnight);
    }

    /// <summary>A key in History builds the archived send beside the list and nothing else: the list
    /// draws the selection and is told through its ref, so the window does not build (plan 17
    /// round 8: 52 KB a key, the window built for a panel). The archive shows the send the key
    /// reached, and the reader hears it selected. A search character builds the view and no
    /// archive, whether its rows keep the selected send or not; a click on a row, which moves the
    /// selection with a build of the view, builds the archive with it; and what each leaves on
    /// screen is what a full build makes.</summary>
    [Fact]
    public void AKeyBuildsTheArchiveAlone_AndASearchCharacterBuildsNoArchive()
    {
        var start = HistoryClock.Noon.AddMinutes(-90);
        for (int i = 0; i < 40; i++)
            HistoryLog.Append(_root, new HistoryEntry(start.AddMinutes(i), i % 3 == 0 ? "POST" : "GET",
                $"https://api.x/v1/items/{i}", 200, 10 + i, null, null));
        Hosted((session, app) =>
        {
            GoTo(session, KeyCode.Digit4);
            GoTo(session, KeyCode.Down);   // the first selection places the archive: the view builds
            var list = (HultUI.Core.Elements.VirtualListElement<MeridianApp.HistoryRowItem>)
                session.Window.FindByAutomationId("history-rows")!.Element;
            int cursor = 0;
            void Key(KeyCode key, int archives)
            {
                int builds = app.Builds, parts = app.PartBuilds, archived = app.HistoryArchiveBuilds;
                GoTo(session, key);
                cursor = key == KeyCode.Down ? cursor + 1 : key == KeyCode.Up ? cursor - 1 : cursor;
                Assert.Equal(0, app.Builds - builds);
                Assert.Equal(0, app.PartBuilds - parts);
                Assert.Equal(archives, app.HistoryArchiveBuilds - archived);
                // Newest first: row n is the (39 - n)th send.
                string url = $"https://api.x/v1/items/{39 - cursor}";
                Assert.Equal(cursor, list.SelectedIndex);
                Assert.EndsWith($"/items/{39 - cursor} 200", Walk(session.Window.AccessibilityTree).Single(n => n.IsSelected == true).Name);
                Assert.Contains(url, ArchiveTexts(session));
            }
            for (int i = 0; i < 5; i++) Key(KeyCode.Down, archives: 1);
            Key(KeyCode.Up, archives: 1);
            Key(KeyCode.Up, archives: 1);
            Key(KeyCode.Enter, archives: 0);   // the send it selects is the one shown
            AssertTheArchiveIsAFullBuild(session, app);

            // The selected send is items/36; "items/3" keeps it, "items/31" drops it from the list.
            session.Window.FindByAutomationId("history-search")!.Click();
            session.PumpFrames(2);
            foreach (var ch in "items/31")
            {
                int builds = app.Builds, archived = app.HistoryArchiveBuilds;
                session.Window.TypeText(ch.ToString());
                session.PumpFrames(2);
                Assert.Equal(1, app.Builds - builds);
                Assert.Equal(0, app.HistoryArchiveBuilds - archived);
                Assert.Contains("https://api.x/v1/items/36", ArchiveTexts(session));
            }
            Assert.Single(app.FilteredHistory());
            AssertTheArchiveIsAFullBuild(session, app);

            // A click on a row selects its send with a build of the view, and the archive with it.
            for (int i = 0; i < "items/31".Length; i++) session.Window.PressKey(KeyCode.Backspace);
            session.PumpFrames(2);
            Assert.Equal(40, app.FilteredHistory().Count);
            session.Window.Window.ClearFocus();
            session.PumpFrames(2);
            var listNow = (HultUI.Core.Elements.VirtualListElement<MeridianApp.HistoryRowItem>)
                session.Window.FindByAutomationId("history-rows")!.Element;
            int before = app.HistoryArchiveBuilds;
            session.Window.Click(listNow.Children.Single(r => r.AutomationId == "hist-row-9"));
            session.PumpFrames(2);
            Assert.Equal(1, app.HistoryArchiveBuilds - before);
            Assert.Contains("https://api.x/v1/items/30", ArchiveTexts(session));
            AssertTheArchiveIsAFullBuild(session, app);

            // A send logged with no build since (the list on screen holds the rows before it): a
            // key builds the view, so the row the list selects is the send the archive shows.
            app.RecordHistory("GET", "https://api.x/v1/late", SendResult.Infra(
                new InfraFailure(InfraKind.Timeout, "timed out", "", TimeSpan.FromSeconds(1))));
            int buildsBefore = app.Builds;
            GoTo(session, KeyCode.Down);
            var selectedRow = Walk(session.Window.AccessibilityTree).Single(n => n.IsSelected == true).Name!;
            var shown = ArchiveTexts(session).Single(t => t.StartsWith("https://api.x/v1/", StringComparison.Ordinal));
            Assert.Contains(shown + " ", selectedRow);
            Assert.Equal(1, app.Builds - buildsBefore);
        });
    }

    /// <summary>Up after a search that left fewer rows than the cursor had walked selects the last
    /// row; it threw (the cursor, one above where it stood, was still past the rows).</summary>
    [Fact]
    public void UpAfterASearchThatShortenedTheRows_SelectsTheLastRow()
    {
        var start = HistoryClock.Noon.AddMinutes(-90);
        for (int i = 0; i < 40; i++)
            HistoryLog.Append(_root, new HistoryEntry(start.AddMinutes(i), "GET",
                $"https://api.x/v1/items/{i}", 200, 10 + i, null, null));
        Hosted((session, app) =>
        {
            GoTo(session, KeyCode.Digit4);
            for (int i = 0; i < 30; i++) GoTo(session, KeyCode.Down);
            session.Window.FindByAutomationId("history-search")!.Click();
            session.Window.TypeText("items/1");   // items/1 and items/10 … 19: eleven rows
            session.PumpFrames(2);
            session.Window.Window.ClearFocus();
            session.PumpFrames(2);
            Assert.Equal(11, app.FilteredHistory().Count);

            GoTo(session, KeyCode.Up);
            Assert.EndsWith("/items/1 200", Walk(session.Window.AccessibilityTree).Single(n => n.IsSelected == true).Name);
            Assert.Contains("https://api.x/v1/items/1", ArchiveTexts(session));
            GoTo(session, KeyCode.Up);
            Assert.EndsWith("/items/10 200", Walk(session.Window.AccessibilityTree).Single(n => n.IsSelected == true).Name);
        });
    }

    /// <summary>Another view lets History go (plan 17 round 11). Its list's ref, its find field's and the
    /// archived send panel's each held the whole History view through their parents behind every other
    /// view; a build that shows another view empties them. Back in History the selection is where it was,
    /// and a key moves it with no build of the window, the list told through its ref and the archive
    /// building alone, as before.</summary>
    [Fact]
    public void AnotherView_LetsHistoryGo_AndBackInHistoryAKeyBuildsTheArchiveAlone()
    {
        var start = HistoryClock.Noon.AddMinutes(-90);
        for (int i = 0; i < 40; i++)
            HistoryLog.Append(_root, new HistoryEntry(start.AddMinutes(i), "GET",
                $"https://api.x/v1/items/{i}", 200, 10 + i, null, null));
        Hosted((session, app) =>
        {
            GoTo(session, KeyCode.Digit4);
            for (int i = 0; i < 5; i++) GoTo(session, KeyCode.Down);   // cursor on row 4, items/35
            var history = WeakHistoryList(session);

            GoTo(session, KeyCode.Digit1);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(history.IsAlive, "the History view outlived its build behind Requests");

            GoTo(session, KeyCode.Digit4);
            Assert.EndsWith("/items/35 200", Walk(session.Window.AccessibilityTree).Single(n => n.IsSelected == true).Name);
            Assert.Contains("https://api.x/v1/items/35", ArchiveTexts(session));
            int builds = app.Builds, archived = app.HistoryArchiveBuilds;
            GoTo(session, KeyCode.Down);
            Assert.Equal(0, app.Builds - builds);
            Assert.Equal(1, app.HistoryArchiveBuilds - archived);
            Assert.EndsWith("/items/34 200", Walk(session.Window.AccessibilityTree).Single(n => n.IsSelected == true).Name);
            Assert.Contains("https://api.x/v1/items/34", ArchiveTexts(session));
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference WeakHistoryList(HeadlessSession session) =>
        new(session.Window.FindByAutomationId("history-rows")!.Element);

    private static List<string> ArchiveTexts(HeadlessSession session) =>
        Descendants(session.Window.FindByAutomationId("history-archive")!.Element)
            .OfType<HultUI.Core.Elements.TextElement>().Select(t => t.Content).ToList();

    /// <summary>The archive on screen is what a full build of the window (its parts with it) makes.</summary>
    private static void AssertTheArchiveIsAFullBuild(HeadlessSession session, MeridianApp app)
    {
        var kept = ArchiveTexts(session);
        int archived = app.HistoryArchiveBuilds;
        typeof(MeridianApp).GetMethod("MarkDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            Type.EmptyTypes)!.Invoke(app, null);
        session.PumpFrames(2);
        Assert.Equal(1, app.HistoryArchiveBuilds - archived);
        Assert.Equal(kept, ArchiveTexts(session));
    }

    private static IEnumerable<HultUI.Core.Elements.Element> Descendants(HultUI.Core.Elements.Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    /// <summary>The arrows walk the rows past the bottom of the list and back past its top, and the
    /// row they select stays on screen. The keys come to the app, not to the list, which is not
    /// focused and so did not follow them: a walk of 49 keys left the selection 21 rows below the
    /// screen (plan 17 round 6).</summary>
    [Fact]
    public void AWalkPastTheScreenKeepsTheSelectedRowInView()
    {
        var start = HistoryClock.Noon.AddMinutes(-200);
        for (int i = 0; i < 120; i++)
            HistoryLog.Append(_root, new HistoryEntry(start.AddMinutes(i), "GET",
                $"https://api.x/v1/items/{i}", 200, 10 + i, null, null));
        Hosted((session, app) =>
        {
            GoTo(session, KeyCode.Digit4);
            var list = (HultUI.Core.Elements.ScrollViewElement)session.Window.FindByAutomationId("history-rows")!.Element;
            float viewport = list.Layout.AbsoluteContentBox.Height;
            int onScreen = (int)(viewport / Meridian.Theme.Azimuth.RowRun);
            Assert.InRange(onScreen, 10, 60);

            int cursor = -1;
            void Keys(KeyCode key, int keys)
            {
                for (int i = 0; i < keys; i++)
                {
                    GoTo(session, key);
                    cursor += key == KeyCode.Down ? 1 : -1;
                    float top = cursor * Meridian.Theme.Azimuth.RowRun;
                    Assert.True(top >= list.ScrollOffsetY - 0.5f
                        && top + Meridian.Theme.Azimuth.RowRun <= list.ScrollOffsetY + viewport + 0.5f,
                        $"row {cursor} at {top} is off the screen ({list.ScrollOffsetY}..{list.ScrollOffsetY + viewport})");
                }
                // The row on screen is the selected one, inside the list's box. (A list's rows
                // are its items to assistive technology, so the row is found among its elements.)
                var row = list.Children.Single(r => r.AutomationId == $"hist-row-{cursor}");
                var box = list.Layout.AbsoluteContentBox;
                Assert.InRange(row.WindowBorderBox.Y, box.Y - 0.5f, box.Y + box.Height - row.WindowBorderBox.Height + 0.5f);
                Assert.True(Walk(session.Window.AccessibilityTree).Single(n => n.IsSelected == true).Name!
                    .EndsWith($"/items/{119 - cursor} 200"));
            }
            Keys(KeyCode.Down, onScreen + 20);
            Assert.True(list.ScrollOffsetY > 0, "the walk past the screen scrolled nothing");
            Keys(KeyCode.Up, onScreen + 10);
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
