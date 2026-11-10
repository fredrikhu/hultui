using System.Text;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Core;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>P8's view: the map with both confidence levels, the detail pane,
/// promotion writing real files, spec import and its error state.</summary>
public class DiscoveryViewTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-disc-").FullName;

    public DiscoveryViewTests(TestHttpServer server)
    {
        _server = server;
        Write("probe.req", $"GET {_server.Origin}/ok\nname Probe\n");
        Write("envs/local.env", $"base_url={_server.Origin}\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private const string Spec = """
        {
          "openapi": "3.0.0",
          "info": { "version": "1.2.0" },
          "paths": {
            "/v1/charges": { "get": { "tags": ["charges"], "summary": "List charges" } },
            "/v1/charges/{id}": { "get": { "tags": ["charges"], "summary": "Retrieve a charge" } },
            "/v1/legacy": { "get": { "tags": ["legacy"], "deprecated": true } }
          }
        }
        """;


    private MeridianApp _app = null!;
    private Element _mounted = null!;

    private void Hosted(Action<HeadlessSession, MeridianApp> body)
    {
        using var session = HeadlessSession.Create(1600, 900);
        MeridianApp? app = null;
        try
        {
            app = _app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(_mounted = app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrame();
            session.Window.PressKey(KeyCode.Digit3);
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

    /// <summary>An element of the map's rows the list has made: only the rows on screen are elements,
    /// and a reader reads the list's items, not them, so they are found among the elements.</summary>
    private Element? RowElement(string automationId)
    {
        static Element? Find(Element element, string id)
        {
            if (element.AutomationId == id) return element;
            var children = element.Children;
            for (int i = 0; i < children.Count; i++)
                if (Find(children[i], id) is { } found) return found;
            return null;
        }
        return Find(_mounted, automationId);
    }

    /// <summary>The elements of the map's rows the list has made.</summary>
    private List<Element> RowElements()
    {
        var rows = new List<Element>();
        void Walk(Element element)
        {
            if (element.AutomationId?.StartsWith("ep-", StringComparison.Ordinal) == true) { rows.Add(element); return; }
            var children = element.Children;
            for (int i = 0; i < children.Count; i++) Walk(children[i]);
        }
        Walk(_mounted);
        return rows;
    }

    private string ImportSpec(HeadlessSession session, MeridianApp app)
    {
        var specPath = Path.Combine(_root, "openapi.json");
        File.WriteAllText(specPath, Spec);
        app.ImportSpecFile(specPath);
        session.PumpFrames(3);
        return specPath;
    }

    // ── Empty + import ───────────────────────────────────────────────────

    [Fact]
    public void TheEmptyMapSaysWhatToDo()
    {
        Hosted((session, app) =>
        {
            Assert.NotNull(session.Window.FindByAutomationId("disc-empty"));
            Assert.NotNull(session.Window.FindByAutomationId("disc-start"));
            Assert.NotNull(session.Window.FindByAutomationId("disc-import"));
        });
    }

    [Fact]
    public void ImportingASpecFillsTheMap()
    {
        Hosted((session, app) =>
        {
            ImportSpec(session, app);

            Assert.Equal(3, app.Map!.DeclaredCount);
            Assert.True(Contains(session, "/v1/charges"), "the map should list the spec's paths");
            Assert.True(Contains(session, "CHARGES"), "endpoints group by tag");
            Assert.True(Contains(session, "deprecated"), "the deprecated flag is part of the map");
        });
    }

    [Fact]
    public void ABrokenSpecShowsFileLineColumnAndOffersToOpenIt()
    {
        Hosted((session, app) =>
        {
            var path = Path.Combine(_root, "broken.json");
            File.WriteAllText(path, "{\n  \"paths\": {\n    oops\n  }\n}");
            app.ImportSpecFile(path);
            session.PumpFrames(3);

            var error = session.Window.FindByAutomationId("disc-parse-error");
            Assert.True(error != null, "the parse-error pane is missing");
            Assert.True(Contains(session, "broken.json:3:"), "the error must name file:line:column");
            Assert.NotNull(session.Window.FindByAutomationId("disc-open-at-line"));
        });
    }

    // ── Confidence ───────────────────────────────────────────────────────

    [Fact]
    public void ObservedTrafficJoinsTheMapUnderItsOwnHeading()
    {
        Hosted((session, app) =>
        {
            app.StartDiscovery(_server.Origin);
            session.PumpUntil(() => app.Map != null, TimeSpan.FromSeconds(10));

            // Send by hand from the Requests view; the map records it.
            session.Window.PressKey(KeyCode.Digit1);
            session.PumpFrames(2);
            Click(session, "req-probe");
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result?.Response != null, TimeSpan.FromSeconds(10));

            session.Window.PressKey(KeyCode.Digit3);
            session.PumpFrames(2);

            Assert.Equal(1, app.Map!.ObservedCount);
            Assert.NotNull(RowElement("disc-observed-header"));
            Assert.True(Contains(session, "OBSERVED — NOT IN SPEC"), "a reader hears the observed heading");
            Assert.True(Contains(session, "seen 1×"), "observed rows carry their evidence");
        });
    }

    [Fact]
    public void ASpecImportKeepsTrafficAlreadySeen()
    {
        Hosted((session, app) =>
        {
            // Observe /v1/charges first, then import a spec that declares it.
            app.StartDiscovery("https://api.example");
            session.PumpFrames(2);
            var observed = ApiMap.Empty("https://api.example")
                .Record("GET", "https://api.example/v1/charges", 200, null);
            var merged = MeridianApp.Merge(
                OpenApi.Parse(Spec, "https://api.example", "openapi.json", out _)!, observed);

            var endpoint = merged.Endpoints.Single(e => e.Method == "GET" && e.Path == "/v1/charges");
            Assert.Equal(Confidence.Declared, endpoint.Confidence);   // the spec wins on identity
            Assert.Equal(1, endpoint.SeenCount);                      // but evidence persists
        });
    }

    [Fact]
    public void AnObservedEndpointTheSpecDoesNotMentionSurvivesTheImport()
    {
        var observed = ApiMap.Empty("https://api.example")
            .Record("GET", "https://api.example/internal/health", 200, null);
        var merged = MeridianApp.Merge(
            OpenApi.Parse(Spec, "https://api.example", "openapi.json", out _)!, observed);

        // That gap is exactly what discovery is for — it must not be dropped.
        Assert.Contains(merged.Endpoints, e => e.Path == "/internal/health" && e.Confidence == Confidence.Observed);
    }

    // ── Detail + promotion ───────────────────────────────────────────────

    [Fact]
    public void AViewSwitchedUnderAStillPointer_LetsTheMapGo()
    {
        // A row clicked, then 1 for the Requests view with the pointer at rest. The element under the
        // pointer used to stay hovered after the build removed it, and through its parents it kept the
        // whole map alive until the pointer moved: 7.8 of 28 MB with a 2,000-endpoint spec.
        Hosted((session, app) =>
        {
            ImportSpec(session, app);
            ClickRow(session, "ep-GET-v1-charges-id");
            var row = Weak("ep-GET-v1-charges-id");

            session.Window.PressKey(KeyCode.Digit1);
            session.PumpFrames(3);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(row.IsAlive, "the Discovery view outlived its build under a still pointer");
        });
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private WeakReference Weak(string automationId) => new(RowElement(automationId)!);

    [Fact]
    public void SelectingAnEndpointShowsItsDetail()
    {
        Hosted((session, app) =>
        {
            ImportSpec(session, app);
            ClickRow(session, "ep-GET-v1-charges-id");

            Assert.NotNull(session.Window.FindByAutomationId("disc-detail"));
            Assert.True(Contains(session, "Retrieve a charge"));
            Assert.True(Contains(session, "spec-declared"));
        });
    }

    /// <summary>A reader is told which row the detail shows: the list's item of the selected row says
    /// so, and the rest say they are not, so a click moves "selected" from one item to the other. The
    /// items are named as the rows are, a heading by its text.</summary>
    [Fact]
    public void TheSelectedRowIsToldToAReader()
    {
        Hosted((session, app) =>
        {
            ImportSpec(session, app);
            Assert.Equal(["CHARGES", "GET /v1/charges, spec-declared", "GET /v1/charges/{id}, spec-declared",
                "LEGACY", "GET /v1/legacy, spec-declared, deprecated"], MapItems(session).Select(n => n.Name));
            Assert.All(MapItems(session), n => Assert.False(n.IsSelected));

            ClickRow(session, "ep-GET-v1-charges-id");
            Assert.Equal(["GET /v1/charges/{id}, spec-declared"], MapItems(session).Where(n => n.IsSelected == true).Select(n => n.Name));
            Assert.All(MapItems(session), n => Assert.NotNull(n.IsSelected));

            ClickRow(session, "ep-GET-v1-legacy");
            Assert.Equal(["GET /v1/legacy, spec-declared, deprecated"], MapItems(session).Where(n => n.IsSelected == true).Select(n => n.Name));
        });
    }

    /// <summary>The map's items as a reader has them: the children of the map's list node.</summary>
    private static List<AccessibilityNode> MapItems(HeadlessSession session) =>
        Walk(session.Window.AccessibilityTree).Single(n => n.Role == AccessibilityRole.List).Children.ToList();

    [Fact]
    public void GenerateWritesARealRequestFileIntoTheCollection()
    {
        Hosted((session, app) =>
        {
            ImportSpec(session, app);
            ClickRow(session, "ep-GET-v1-charges-id");
            Click(session, "disc-generate");

            var all = Directory.GetFiles(_root, "*.req", SearchOption.AllDirectories);
            var file = Path.Combine(_root, "charges", "get-charge.req");
            Assert.True(File.Exists(file),
                $"{file} was not written; have: {string.Join(" | ", all)}");
            var doc = ReqDocument.Parse(File.ReadAllText(file));
            Assert.Empty(doc.Errors);
            Assert.Equal("GET", doc.Method);
            Assert.Contains("{{base_url}}", doc.Url);

            // And it is in the tree now.
            session.Window.PressKey(KeyCode.Digit1);
            session.PumpFrames(2);
            Assert.True(Contains(session, "Get charge"), "the promoted request should appear in the tree");
        });
    }

    [Fact]
    public void PromoteSaysHowManyFilesItWillWriteAndWritesThem()
    {
        Hosted((session, app) =>
        {
            ImportSpec(session, app);

            // The button names the real action: three declared endpoints.
            Assert.True(Contains(session, "Promote 3 to collection"), "the promote button must say what it does");
            int builds = app.Builds;
            Click(session, "disc-promote");
            // Once, by the toast that says so: the promote's own rebuild made it twice.
            Assert.Equal(1, app.Builds - builds);
            Assert.True(Contains(session, "Wrote 3 files."), "the toast should say what was written");

            var written = Directory.GetFiles(_root, "*.req", SearchOption.AllDirectories)
                .Select(Path.GetFileName).ToList();
            Assert.Contains("get-charges.req", written);
            Assert.Contains("get-charge.req", written);
            Assert.Contains("get-legacy.req", written);
        });
    }

    [Fact]
    public void ObservedEndpointsOptInToPromotionAndStayMarked()
    {
        Hosted((session, app) =>
        {
            app.StartDiscovery(_server.Origin);
            session.PumpUntil(() => app.Map != null, TimeSpan.FromSeconds(10));
            session.Window.PressKey(KeyCode.Digit1);
            session.PumpFrames(2);
            Click(session, "req-probe");
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result?.Response != null, TimeSpan.FromSeconds(10));
            session.Window.PressKey(KeyCode.Digit3);
            session.PumpFrames(2);

            // Off by default: promoting writes nothing.
            Click(session, "disc-promote");
            Assert.Empty(Directory.GetFiles(_root, "get-ok.req", SearchOption.AllDirectories));

            Click(session, "disc-include-observed");
            Click(session, "disc-promote");

            var file = Directory.GetFiles(_root, "*.req", SearchOption.AllDirectories)
                .FirstOrDefault(f => File.ReadAllText(f).Contains("observed in traffic"));
            Assert.True(file != null, "an opted-in observed endpoint should be written, marked as inferred");
        });
    }

    // ── A click at scale ─────────────────────────────────────────────────
    //
    // The map is a virtual list of its lines: only the rows on screen are elements. A click moves the
    // selection, which only the map and the detail show: it builds the view, and the list keeps every
    // row but the two it moves between (MeridianApp.Discovery.cs). The oracle is a build that keeps
    // nothing.

    /// <summary>A click builds the view alone and makes the row it leaves and the row it reaches:
    /// the same elements at 30 endpoints as at 300. It made every row again, 2.8 MB a click at 600.</summary>
    [Fact]
    public void AClickMakesTheTwoRowsItMovesBetween_HoweverLongTheMap()
    {
        var made = new List<ulong>();
        foreach (int paths in new[] { 15, 150 })
            Hosted((session, app) =>
            {
                ImportWalkSpec(session, app, paths);
                Assert.Equal(paths * 2, app.Map!.DeclaredCount);
                var first = RowElement("ep-GET-v1-group00-items000-id")!;
                var second = RowElement("ep-PUT-v1-group00-items000-id")!;
                session.Window.Click(first);
                session.PumpFrames(2);

                int rows = app.EndpointRowsMade, builds = app.Builds, parts = app.PartBuilds;
                ulong before = ElementId.New().Value;
                session.Window.Click(second);
                session.PumpFrames(2);
                made.Add(ElementId.New().Value - before - 1);

                Assert.Equal(2, app.EndpointRowsMade - rows);   // the row it left and the row it reached
                Assert.Equal(1, app.Builds - builds);
                Assert.Equal(0, app.PartBuilds - parts);         // the title bar and the rail show no selection
                Assert.True(Selected(session, "ep-PUT-v1-group00-items000-id"));
            });
        Assert.True(made[0] == made[1], $"a click made {made[0]} elements at 30 endpoints and {made[1]} at 300");
    }

    /// <summary>At 2,000 endpoints the map is the rows on screen: the list made about a screen of
    /// rows, and a reader is still told every line, the list's items served as a reader asks. As a
    /// scroll view over a column the map made every row, 8,109 elements.</summary>
    [Fact]
    public void TheMapMakesTheRowsOnScreen_HoweverLongTheMap()
    {
        Hosted((session, app) =>
        {
            ImportWalkSpec(session, app, 1000);
            Assert.Equal(2000, app.Map!.DeclaredCount);
            int rows = RowElements().Count;
            Assert.True(rows is > 20 and < 60, $"the map made {rows} rows of 2,000 endpoints");
            Assert.True(app.EndpointRowsMade < 60, $"the map has made {app.EndpointRowsMade} rows");

            // 2,000 rows and 12 headings: past what the tree builds eagerly, so the list's node serves them.
            var list = Walk(session.Window.AccessibilityTree).Single(n => n.Role == AccessibilityRole.List);
            var items = list.ItemSource!;
            Assert.Equal(2012, items.ItemCount);
            Assert.Equal("GROUP00", items.GetItemLabel(0));
            Assert.Equal("PUT /v1/group11/items995/{id}, spec-declared", items.GetItemLabel(2011));
        });
    }

    /// <summary>A reader reaches a row off screen: the list's last item is in the reader's tree, told
    /// off screen; the reader's scroll brings its row onto the screen where its item stands, and its
    /// invoke selects it as a click would. The map has no keys of its own (its rows are not Tab
    /// stops), so a reader's route is how a row off screen is reached without the pointer.</summary>
    [Fact]
    public void AReaderReachesARowOffScreen()
    {
        Hosted((session, app) =>
        {
            var reader = session.Window.InstallActionRouter();
            ImportWalkSpec(session, app, 150);
            const string Last = "PUT /v1/group11/items143/{id}, spec-declared";
            var item = MapItems(session)[^1];
            Assert.Equal(Last, item.Name);
            Assert.True(item.IsOffscreen, "the last row is on screen already");
            Assert.Null(RowElement("ep-PUT-v1-group11-items143-id"));

            reader.ScrollElementIntoView(item.ElementId);
            session.PumpFrames(3);
            var row = RowElement("ep-PUT-v1-group11-items143-id");
            Assert.NotNull(row);
            Element? map = row;
            while (map is not null and not ScrollViewElement) map = map.Parent;
            var list = map!.ScreenBorderBox;
            var box = row!.ScreenBorderBox;
            Assert.True(box.Y >= list.Y && box.Y + box.Height <= list.Y + list.Height, $"the row is at {box}, the map at {list}");
            item = MapItems(session)[^1];
            Assert.False(item.IsOffscreen);

            reader.InvokeElement(item.ElementId);
            session.PumpFrames(3);
            Assert.True(Selected(session, "ep-PUT-v1-group11-items143-id"), "the reader's invoke did not select the row");
            Assert.True(Contains(session, "Write item 143"), "the detail does not show the endpoint the reader invoked");
        });
    }

    /// <summary>Over a walk of clicks (the same row twice, one with the pointer moved over another row
    /// first, one after a scroll, a send folded into the map with a new observed endpoint, the
    /// observed toggle, a view and back) the window shows what a build that keeps no row shows: the
    /// pixels, the reader's tree and the focus; and a click leaves the map where it was scrolled.</summary>
    [Fact]
    public void AClickWalk_ShowsWhatAFullBuildShows()
    {
        Hosted((session, app) =>
        {
            // Track the server's base, so a send joins the map, then import a spec over it.
            app.StartDiscovery(_server.Origin);
            session.PumpUntil(() => session.Window.FindByAutomationId("disc-probing") is null, TimeSpan.FromSeconds(10));
            ImportWalkSpec(session, app, 20);
            Record(session, app, "/internal/health");   // observed, not in the spec
            AssertAFullBuildShowsTheSame(session, app, "imported, one observed");

            ClickRow(session, "ep-GET-v1-group00-items000-id");
            AssertAFullBuildShowsTheSame(session, app, "a first click");
            ClickRow(session, "ep-PUT-v1-group01-items001-id");
            AssertAFullBuildShowsTheSame(session, app, "a second");
            ClickRow(session, "ep-PUT-v1-group01-items001-id");
            AssertAFullBuildShowsTheSame(session, app, "the same row again");

            var over = RowElement("ep-GET-v1-group01-items013-id")!;
            var at = CenterOf(over);
            session.Window.MoveMouse(at.X, at.Y);
            session.PumpFrames(2);
            ClickRow(session, "ep-GET-v1-group00-items012-id");
            AssertAFullBuildShowsTheSame(session, app, "a click after the pointer was over another row");

            Element? up = RowElement("ep-GET-v1-group00-items000-id");
            while (up is not null and not ScrollViewElement) up = up.Parent;
            var map = (ScrollViewElement)up!;
            var mapAt = CenterOf(map);
            session.Window.Wheel(mapAt.X, mapAt.Y, 0, -6);
            session.PumpFrames(3);
            float scrolled = map.ScrollOffsetY;
            Assert.True(scrolled > 0, "the map did not scroll");
            ClickRow(session, "ep-PUT-v1-group02-items014-id");
            Assert.Equal(scrolled, map.ScrollOffsetY);
            AssertAFullBuildShowsTheSame(session, app, "a click in the scrolled map");

            Record(session, app, "/v1/group00/items000/abc_12");   // seen on a declared endpoint
            Record(session, app, "/v2/other/xyz_9");               // a second observed one
            Record(session, app, "/internal/health");              // the first seen again: "seen 2×"
            AssertAFullBuildShowsTheSame(session, app, "sends folded in");
            session.Window.Wheel(mapAt.X, mapAt.Y, 0, -100);   // the observed rows are at the end
            session.PumpFrames(3);
            ClickRow(session, "ep-GET-internal-health");
            AssertAFullBuildShowsTheSame(session, app, "an observed row");
            session.Window.Wheel(mapAt.X, mapAt.Y, 0, 100);
            session.PumpFrames(3);
            ClickRow(session, "ep-GET-v1-group00-items000-id");
            // The row's click selects the endpoint as the map has it now, with what the send saw.
            Assert.True(Contains(session, "200 ×1"), "the detail does not show the send the endpoint was seen in");
            AssertAFullBuildShowsTheSame(session, app, "the declared row the send was seen on");

            Click(session, "disc-include-observed");
            AssertAFullBuildShowsTheSame(session, app, "observed included");

            session.Window.PressKey(KeyCode.Digit1);
            session.PumpFrames(2);
            session.Window.PressKey(KeyCode.Digit3);
            session.PumpFrames(2);
            AssertAFullBuildShowsTheSame(session, app, "Requests and back");
            ClickRow(session, "ep-PUT-v1-group00-items000-id");
            AssertAFullBuildShowsTheSame(session, app, "a click after the view came back");
        });
    }

    // ── The map cache ────────────────────────────────────────────────────

    /// <summary>The cache file says what it said when it was one string joined on the UI thread:
    /// the base, the spec and its version, then an endpoint a line.</summary>
    [Fact]
    public void TheMapCacheIsWrittenAsBefore()
    {
        var full = new ApiMap("https://api.example", [
            new Endpoint("GET", "/v1/charges/{id}", Confidence.Declared, "charges", Summary: "Retrieve\ta charge",
                SeenCodes: new Dictionary<int, int> { [200] = 3, [404] = 1 }, SeenCount: 4),
            new Endpoint("POST", "/v1/charges", Confidence.Declared, "charges", Deprecated: true),
            new Endpoint("GET", "/internal/health", Confidence.Observed, "internal",
                SeenCodes: new Dictionary<int, int> { [503] = 1 }, SeenCount: 1, InferredShape: "{ ok: boolean }"),
        ], "openapi.json", "1.2.0");
        var bare = ApiMap.Empty("https://api.example").Record("GET", "https://api.example/v1/ping", 204, null);
        var path = Path.Combine(_root, ".meridian", "discovery", "x.map");
        foreach (var map in new[] { full, bare, ApiMap.Empty("https://api.example") })
        {
            MeridianApp.WriteMap(map, path);
            var lines = map.Endpoints.Select(e =>
                string.Join('\t', e.Method, e.Path, e.Confidence, e.Group, e.SeenCount,
                    string.Join(',', e.Codes.Select(c => $"{c.Key}={c.Value}")), e.Summary.Replace('\t', ' ')));
            string before = $"{map.BaseUrl}\t{map.SpecSource}\t{map.SpecVersion}\n" + string.Join('\n', lines);
            Assert.Equal(new UTF8Encoding(false).GetBytes(before), File.ReadAllBytes(path));
        }
    }

    /// <summary>A send into a tracked base folds into the map and writes the cache off the UI thread:
    /// at 600 endpoints the cache was 272 KB of strings on the UI thread a send.</summary>
    [Fact]
    public void ASendIntoATrackedBase_WritesTheCacheOffTheUiThread()
    {
        Hosted((session, app) =>
        {
            app.StartDiscovery(_server.Origin);
            session.PumpUntil(() => session.Window.FindByAutomationId("disc-probing") is null, TimeSpan.FromSeconds(10));
            ImportWalkSpec(session, app, 300);
            var body = new ResponseBody(BodyKind.Text, """{"id":"x_1"}""", [], 12, false, null);
            HttpResponseData Response(string url) => new(200, "OK", [], "application/json", body, null!, url);
            for (int i = 0; i < 3; i++)
                app.RecordInMap("GET", $"{_server.Origin}/v1/group05/items005/ab_{i}", Response($"{_server.Origin}/v1/group05/items005/ab_{i}"));
            app.FlushMapCache();

            long before = GC.GetAllocatedBytesForCurrentThread();
            app.RecordInMap("GET", _server.Origin + "/internal/health", Response(_server.Origin + "/internal/health"));
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            app.FlushMapCache();

            var cache = Directory.GetFiles(Path.Combine(_root, ".meridian", "discovery"), "*.map").Single();
            var text = File.ReadAllText(cache);
            Assert.Contains("GET\t/internal/health\tObserved\tinternal\t1\t200=1\t", text);
            Assert.Contains("GET\t/v1/group05/items005/{id}\tDeclared\tgroup05\t3\t200=3\tRead item 5", text);
            Assert.True(bytes < 96 * 1024, $"a send allocated {bytes / 1024} KB on the UI thread");
        });
    }

    private void ImportWalkSpec(HeadlessSession session, MeridianApp app, int paths)
    {
        var specPath = Path.Combine(_root, "walk-openapi.json");
        File.WriteAllText(specPath, WalkSpec(paths));
        app.ImportSpecFile(specPath);
        // Past the toast that says so (2.6 s), which draws differently from frame to frame.
        for (int i = 0; i < 200; i++) session.PumpFrame();
    }

    /// <summary>A spec of <paramref name="paths"/> item paths, each read and written (GET and PUT), in
    /// twelve tags, as the round 9 walks' fixtures are.</summary>
    internal static string WalkSpec(int paths)
    {
        var sb = new StringBuilder("{\n \"openapi\": \"3.0.3\",\n \"info\": { \"title\": \"Walk API\", \"version\": \"1.0\" },\n \"paths\": {\n");
        for (int i = 0; i < paths; i++)
        {
            string tag = $"group{i % 12:00}";
            string op(string verb, string summary) =>
                $"\"{verb}\": {{ \"tags\": [\"{tag}\"], \"summary\": \"{summary} item {i}\", \"operationId\": \"{verb}{i}\", "
                + "\"parameters\": [{ \"name\": \"id\", \"in\": \"path\", \"required\": true, \"schema\": { \"type\": \"string\" } }], "
                + "\"responses\": { \"200\": { \"description\": \"ok\" }, \"404\": { \"description\": \"missing\" } } }";
            sb.Append($"  \"/v1/{tag}/items{i:000}/{{id}}\": {{ {op("get", "Read")}, {op("put", "Write")} }}");
            sb.Append(i < paths - 1 ? ",\n" : "\n");
        }
        return sb.Append(" }\n}\n").ToString();
    }

    /// <summary>A send to the tracked base folded into the map, and the build a send's end makes.</summary>
    private void Record(HeadlessSession session, MeridianApp app, string path)
    {
        var body = new ResponseBody(BodyKind.Text, """{"id":"x_1","ok":true}""", [], 22, false, null);
        app.RecordInMap("GET", _server.Origin + path,
            new HttpResponseData(200, "OK", [], "application/json", body, null!, _server.Origin + path));
        FullRebuild(app);
        session.PumpFrames(2);
    }

    private void ClickRow(HeadlessSession session, string automationId)
    {
        var row = RowElement(automationId);
        Assert.True(row != null, $"#{automationId} not found");
        session.Window.Click(row!);
        session.PumpFrames(2);
        Assert.True(Selected(session, automationId), $"#{automationId} is not selected after its click");
    }

    /// <summary>Whether a reader is told the row is selected: its item, by the row's name.</summary>
    private bool Selected(HeadlessSession session, string automationId)
    {
        string name = RowElement(automationId)!.AccessibleName!;
        return MapItems(session).Single(n => n.Name == name).IsSelected == true;
    }

    private static HultUI.Core.Primitives.Point CenterOf(Element element)
    {
        var box = element.ScreenBorderBox;
        return new(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    /// <summary>The window as it is equals the window a build that keeps no map row makes: the pixels,
    /// the reader's tree and the focus.</summary>
    private static void AssertAFullBuildShowsTheSame(HeadlessSession session, MeridianApp app, string when)
    {
        var (pixels, tree, focus) = Scene(session);
        app.ForgetMapRows();
        FullRebuild(app);
        session.PumpFrames(2);
        var (fullPixels, fullTree, fullFocus) = Scene(session);
        Assert.True(tree == fullTree, $"the reader's tree differs from a full build's ({when}):\n{FirstDifference(tree, fullTree)}");
        Assert.True(focus == fullFocus, $"focus on {focus}, a full build's on {fullFocus} ({when})");
        Assert.True(pixels.AsSpan().SequenceEqual(fullPixels), $"the pixels differ from a full build's ({when})");
    }

    private static (byte[] Pixels, string Tree, string? Focus) Scene(HeadlessSession session)
    {
        session.PumpFrames(6);   // past any hover fade
        using var image = session.Window.CapturePixels();
        using var pixmap = image.PeekPixels();
        var focused = session.Window.Window.FocusedElement;
        return (pixmap.GetPixelSpan().ToArray(), session.Window.DumpAccessibilityTree(),
            focused is null ? null : $"{focused.GetType().Name} {focused.AutomationId} {focused.Key}");
    }

    private static string FirstDifference(string a, string b)
    {
        var x = a.Split('\n'); var y = b.Split('\n');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            if (i >= x.Length || i >= y.Length || x[i] != y[i])
                return $"line {i}:\n  kept: {(i < x.Length ? x[i] : "<none>")}\n  full: {(i < y.Length ? y[i] : "<none>")}";
        return "(none)";
    }

    private static void FullRebuild(MeridianApp app) =>
        typeof(MeridianApp).GetMethod("MarkDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, Type.EmptyTypes)!
            .Invoke(app, null);
}
