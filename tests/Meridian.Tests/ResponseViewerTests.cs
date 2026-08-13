using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Platform;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>The P3b response viewer: tabs, waterfall, pretty/raw, find, pane
/// flip, and the failure states with their actions — against the real loopback
/// servers.</summary>
public class ResponseViewerTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-resp-").FullName;

    public ResponseViewerTests(TestHttpServer server)
    {
        _server = server;
        Write("ok.req", $"GET {_server.Origin}/ok\n");
        Write("two.req", $"GET {_server.Origin}/two\n");
        Write("big.req", $"GET {_server.Origin}/big\n");
        Write("binary.req", $"GET {_server.Origin}/binary\n");
        Write("cookie.req", $"GET {_server.Origin}/setcookie\n");
        Write("stall.req", $"GET {_server.Origin}/stall\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class NoPicker : IFolderPicker
    {
        public bool IsAvailable => true;
        public Task<string?> PickFolderAsync(string? d = null, CancellationToken c = default) => Task.FromResult<string?>(null);
    }

    private sealed class FakeSavePicker(string? destination) : ISaveFilePicker
    {
        public bool IsAvailable => true;
        public Task<string?> PickSaveFileAsync(string suggestedFileName, string? initialDirectory = null,
            string? filterLabel = null, string? extension = null, CancellationToken cancellationToken = default)
            => Task.FromResult(destination);
    }

    private void Hosted(Action<HeadlessSession, MeridianApp> body,
        int viewerCap = 1024 * 1024, string? saveTo = null, HeadlessOptions? options = null,
        int width = 1440)
    {
        using var session = HeadlessSession.Create(width, 900, options: options);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window,
                new NoPicker(), new FakeSavePicker(saveTo), viewerCap);
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

    private void SendAndWait(HeadlessSession session, MeridianApp app, string req)
    {
        Click(session, "req-" + req);
        Click(session, "send");
        session.PumpUntil(() => app.ActiveTab!.Result != null && app.ActiveTab.Sending == null, TimeSpan.FromSeconds(10));
        session.PumpFrames(2);
    }

    private static string BodyText(HeadlessSession session)
    {
        var el = session.Window.FindByAutomationId("response-body");
        Assert.True(el != null, "response body area not found");
        return ((TextAreaElement)el!.Element).Text;
    }

    // ── Orientation ──────────────────────────────────────────────────────

    [Fact]
    public void TheResponsePaneSitsRightAndCtrlBackslashFlipsIt()
    {
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "ok");
            Assert.False(app.ResponseAtBottom);
            Assert.NotNull(session.Window.FindByAutomationId("response-right"));

            session.Window.PressKey(KeyCode.Backslash, KeyModifiers.Ctrl);
            session.PumpFrames(2);

            Assert.True(app.ResponseAtBottom);
            Assert.NotNull(session.Window.FindByAutomationId("response-bottom"));
            Assert.Null(session.Window.FindByAutomationId("response-right"));
        });
    }

    [Fact]
    public void ANarrowWindowForcesTheBottomOrientation()
    {
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "ok");
            Assert.True(app.ResponseAtBottom);
            Assert.NotNull(session.Window.FindByAutomationId("response-bottom"));
            // And there is no flip affordance — it would be a dead control.
            Assert.Null(session.Window.FindByAutomationId("flip-pane"));
        }, width: 1000);
    }

    [Fact]
    public void CrossingTheBreakpointByResizeForcesBottom()
    {
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "ok");
            Assert.False(app.ResponseAtBottom);

            session.Window.SetClientSize(980, 700);
            session.PumpFrames(3);
            Assert.True(app.ResponseAtBottom);

            session.Window.SetClientSize(1440, 900);
            session.PumpFrames(3);
            Assert.False(app.ResponseAtBottom);
        });
    }

    // ── Tabs ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheHeadersTabListsResponseHeaders()
    {
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "ok");
            Click(session, "resptab-headers");

            Assert.True(Contains(session, "Content-Type"), "Content-Type row missing");
            Assert.True(Contains(session, "application/json"));
        });
    }

    [Fact]
    public void TheCookiesTabShowsTheJarForTheHost()
    {
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "cookie");
            Click(session, "resptab-cookies");

            Assert.True(Contains(session, "sid"), "cookie name missing");
            Assert.True(Contains(session, "session only"), "the session-only note is part of the design");
        });
    }

    [Fact]
    public void TheTimelineShowsPhasesAndConnectionFacts()
    {
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "ok");
            Click(session, "resptab-timeline");

            foreach (var phase in new[] { "DNS", "TCP", "TLS", "TTFB", "Download" })
                Assert.True(Contains(session, phase), $"{phase} row missing");
            Assert.True(Contains(session, "reused connection: no"));
            Assert.True(Contains(session, "HTTP/1.1"));
        });
    }

    // ── Body: pretty/raw, syntax, find ───────────────────────────────────

    [Fact]
    public void PrettyIndentsJsonAndRawShowsItAsReceived()
    {
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "ok");   // {"ok":true} on one line

            // Pretty is the default: parsed and re-indented.
            Assert.Contains("\n", BodyText(session));
            Assert.Contains("\"ok\": true", BodyText(session));

            Click(session, "body-raw");
            Assert.Equal("""{"ok":true}""", BodyText(session));
        });
    }

    [Fact]
    public void TheBodyGetsSyntaxSpans()
    {
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "ok");
            var area = (TextAreaElement)session.Window.FindByAutomationId("response-body")!.Element;
            Assert.True(area.Spans is { Count: > 0 }, "JSON body should carry syntax spans");
        });
    }

    [Fact]
    public void FindTintsMatchesInTheRenderedOutput()
    {
        // Render-level, not element-level — the FilesFindTests lesson: assert
        // what the renderer is handed, not what the element property says.
        // The body is two-property JSON ON PURPOSE: the match sits on the FIRST
        // line while later lines carry syntax spans, which is exactly the shape
        // where an unsorted span merge silently drops the tint (the TextArea
        // slices per line and stops at the first span past the line end).
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "two");   // {"alpha":"xray","beta":"yankee"}

            session.Window.PressKey(KeyCode.F, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Assert.NotNull(session.Window.FindByAutomationId("find-input"));

            session.Window.TypeText("xray");
            session.PumpFrames(3);

            Assert.True(Contains(session, "1 of 1"), "match count missing");

            var log = session.Window.RenderLog!;
            log.Clear();
            MarkDirty(app);
            // The wait is on the body line being drawn at all — which happens
            // with or without the tint — so it cannot mask the assertion. The
            // filter is the body line's own text, quote-escaped as the log
            // renders it; the find input draws a plain "xray" that never matches.
            session.PumpUntil(() => log.Named("DrawText")
                    .Any(op => op.ToString().Contains("\\\"alpha\\\": \\\"xray\\\"", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(10));

            var matchLines = log.Named("DrawText")
                .Select(op => op.ToString())
                .Where(t => t.Contains("\\\"alpha\\\": \\\"xray\\\"", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(matchLines);
            Assert.All(matchLines, line => Assert.Contains("/bg:", line, StringComparison.Ordinal));
        }, options: HeadlessOptions.DeterministicRecording());
    }

    private static void MarkDirty(MeridianApp app) =>
        typeof(MeridianApp).GetMethod("MarkDirty",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
            .Invoke(app, null);

    // ── Truncation, binary, save ─────────────────────────────────────────

    [Fact]
    public void ATruncatedBodyShowsTheBannerAndSavesTheFullSpill()
    {
        var dest = Path.Combine(Path.GetTempPath(), $"meridian-save-{Guid.NewGuid():N}.txt");
        try
        {
            Hosted((session, app) =>
            {
                SendAndWait(session, app, "big");   // 5000 bytes vs cap 1000

                Assert.NotNull(session.Window.FindByAutomationId("truncated-banner"));
                Assert.NotNull(session.Window.FindByAutomationId("open-full"));

                Click(session, "save-body");
                session.PumpUntil(() => File.Exists(dest), TimeSpan.FromSeconds(5));

                Assert.Equal(5000, new FileInfo(dest).Length);
            }, viewerCap: 1000, saveTo: dest);
        }
        finally
        {
            File.Delete(dest);
        }
    }

    [Fact]
    public void ABinaryBodyShowsTheHexStub()
    {
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "binary");

            var el = session.Window.FindByAutomationId("response-hex");
            Assert.True(el != null, "hex stub missing");
            var text = ((TextAreaElement)el!.Element).Text;
            Assert.Contains("89 50 4e 47", text);   // PNG magic
            Assert.Contains(".PNG", text);          // the ascii column
            // No pretty/raw toggle for binary — it would be a dead control.
            Assert.Null(session.Window.FindByAutomationId("body-pretty"));
        });
    }

    // ── Failure states with their actions ────────────────────────────────

    [Fact]
    public void ATimeoutOffersRetry()
    {
        Write("quick-stall.req", $"GET {_server.Origin}/stall\n");
        Hosted((session, app) =>
        {
            Click(session, "req-quick-stall");
            app.ActiveTab!.Doc.SetRequestLine("GET", $"{_server.Origin}/stall");
            // Shorten the wait by cancelling via timeout: use a tiny engine timeout
            // by sending with the spec default — instead, just cancel-free stall with
            // 400ms via direct doc? The spec timeout is fixed; drive the real path:
            Click(session, "send");
            session.PumpUntil(() => session.Window.FindByAutomationId("cancel") != null, TimeSpan.FromSeconds(5));
            session.Window.PressKey(KeyCode.Escape);
            session.PumpUntil(() => app.ActiveTab.Sending == null, TimeSpan.FromSeconds(5));
            // Cancelled ≠ timeout; the retry action is for the Timeout state —
            // simulate the state the engine produces for it.
            app.ActiveTab.Result = Meridian.Core.SendResult.Infra(new Meridian.Core.InfraFailure(
                Meridian.Core.InfraKind.Timeout, "timed out after 30 s", "connected · no response bytes",
                TimeSpan.FromSeconds(30)));
            MarkDirty(app);
            session.PumpFrames(2);

            Assert.NotNull(session.Window.FindByAutomationId("retry-send"));
            Assert.True(Contains(session, "timed out"));
        });
    }

    [Fact]
    public void TlsTrustForWorkspaceRoundTrips()
    {
        using var tls = new TestTlsServer();
        Write("tls.req", $"GET https://127.0.0.1:{tls.Port}/x\n");
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "tls");

            // First send: rejected, named, with the trust action.
            Assert.Equal(Meridian.Core.InfraKind.TlsFailure, app.ActiveTab!.Result!.Failure!.Kind);
            Assert.True(Contains(session, TestTlsServer.CertCn));
            Assert.NotNull(session.Window.FindByAutomationId("trust-workspace"));

            // Trust → persists to the workspace → resends → 200 over TLS.
            Click(session, "trust-workspace");
            session.PumpUntil(() => app.ActiveTab.Result?.Response != null, TimeSpan.FromSeconds(10));

            Assert.Equal(200, app.ActiveTab.Result!.Response!.StatusCode);
            Assert.Equal("trusted", app.ActiveTab.Result.Response.Body.Text);
            var trustFile = Path.Combine(_root, ".meridian", "trusted.certs");
            Assert.True(File.Exists(trustFile), "the trust file should serialize into the workspace");
            Assert.Contains(tls.Thumbprint, File.ReadAllText(trustFile));
        });
    }

    [Fact]
    public void TheEditorAndResponseCoexistWhileSending()
    {
        Hosted((session, app) =>
        {
            SendAndWait(session, app, "ok");
            Click(session, "req-stall");
            Click(session, "send");
            session.PumpUntil(() => session.Window.FindByAutomationId("cancel") != null, TimeSpan.FromSeconds(5));

            // The stall tab has no previous result: the pane shows the sending state.
            Assert.True(Contains(session, "sending…"));

            session.Window.PressKey(KeyCode.Escape);
            session.PumpUntil(() => app.ActiveTab!.Sending == null, TimeSpan.FromSeconds(5));
        });
    }
}
