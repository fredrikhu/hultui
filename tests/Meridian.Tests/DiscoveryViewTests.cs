using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Core;
using Meridian.Platform;
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

    private sealed class NoPicker : IFolderPicker
    {
        public bool IsAvailable => true;
        public Task<string?> PickFolderAsync(string? d = null, CancellationToken c = default) => Task.FromResult<string?>(null);
    }

    private void Hosted(Action<HeadlessSession, MeridianApp> body)
    {
        using var session = HeadlessSession.Create(1600, 900);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window, new NoPicker());
            session.Application.SetRoot(app.Mount());
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
            Assert.NotNull(session.Window.FindByAutomationId("disc-observed-header"));
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
    public void SelectingAnEndpointShowsItsDetail()
    {
        Hosted((session, app) =>
        {
            ImportSpec(session, app);
            Click(session, "ep-GET-v1-charges-id");

            Assert.NotNull(session.Window.FindByAutomationId("disc-detail"));
            Assert.True(Contains(session, "Retrieve a charge"));
            Assert.True(Contains(session, "spec-declared"));
        });
    }

    [Fact]
    public void GenerateWritesARealRequestFileIntoTheCollection()
    {
        Hosted((session, app) =>
        {
            ImportSpec(session, app);
            Click(session, "ep-GET-v1-charges-id");
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
            Click(session, "disc-promote");

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
}
