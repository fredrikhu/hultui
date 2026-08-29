using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
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


    private void Hosted(Action<HeadlessSession, MeridianApp> body,
        int viewerCap = 1024 * 1024, string? saveTo = null, HeadlessOptions? options = null,
        int width = 1440)
    {
        using var session = HeadlessSession.Create(width, 900, options: options);
        if (saveTo is not null)
            session.Window.FileDialogs.EnqueueResult(saveTo);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window, viewerCapBytes: viewerCap);
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

    /// <summary>
    /// A match below the fold in a body whose lines <i>wrap</i> is scrolled all the way to it.
    ///
    /// <para>The response body is one read-only TextArea and it wraps at the pane width, which
    /// for a JSON payload in a 560dip pane is most of the time. The reveal used to hand the
    /// search's <i>logical</i> line index to a scroll that counts <i>drawn rows</i>, so every
    /// wrap above the match cost one row of accuracy and the match stopped short of the
    /// viewport — further short the deeper in the body it sat.</para>
    ///
    /// <para>Asserted by comparison rather than against a pixel figure, because the right answer
    /// depends on the measured line height and the pane width and this has to hold on any font.
    /// Two bodies, same line count, same marker on the same line, one wrapping and one not: the
    /// wrapping one has strictly more rows above the marker and so must end up strictly further
    /// down. Under the old arithmetic both produced the same offset, which is the symptom.</para>
    /// </summary>
    [Fact]
    public void AMatchInAWrappedBodyIsScrolledToItsRealRowNotItsLineNumber()
    {
        float flat = ScrollAfterFindingTheMarker(wrapping: false);
        float wrapped = ScrollAfterFindingTheMarker(wrapping: true);

        Assert.True(flat > 0, "the unwrapped body's match is hundreds of lines down and it never scrolled");
        Assert.True(wrapped > flat,
            $"every line above the match wraps, so the match sits far further down than its line "
            + $"number suggests — the body stopped at {wrapped:F0}px, no further than the "
            + $"{flat:F0}px an unwrapped body of the same length needs");
    }

    /// <summary>
    /// A match deep inside a single very long line is scrolled to, not merely to the top of the
    /// line it sits on.
    ///
    /// <para>This is Meridian's own shape rather than an invented one: <c>raw</c> mode on a
    /// minified JSON payload is one logical line thousands of characters wide, wrapped into
    /// hundreds of rows. A line number cannot name a row inside it — every match in the body
    /// resolves to line 0, which is the top of the pane — so the reveal hands the body an
    /// absolute offset instead.</para>
    /// </summary>
    [Fact]
    public void AMatchInsideOneLongWrappedLineScrollsToTheMatchNotTheLineStart()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-ok");
            var words = Enumerable.Repeat("filler", 4000).Append("far-below");
            var text = string.Join(' ', words);
            app.ActiveTab!.Result = Meridian.Core.SendResult.Ok(TextResponse(text));
            MarkDirty(app);
            session.PumpFrames(2);

            session.Window.PressKey(KeyCode.F, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            session.Window.TypeText("far-below");
            session.PumpFrames(3);
            Assert.True(Contains(session, "1 of 1"), "the marker should be the only match");

            try { session.PumpUntil(() => ScrollY(session) > 0, TimeSpan.FromSeconds(15)); }
            catch (TimeoutException) { /* the assertion below reports it */ }

            Assert.True(ScrollY(session) > 0,
                "the whole body is one logical line, so scrolling to a line number can only ever "
                + "reach the top of the pane — the match is hundreds of wrapped rows below it");
        });
    }

    /// <summary>Where the body ended up after walking to the only match, for a body that wraps
    /// and one that does not. Both are the same number of lines with the marker on the last.</summary>
    private float ScrollAfterFindingTheMarker(bool wrapping)
    {
        float scroll = 0;
        Hosted((session, app) =>
        {
            Click(session, "req-ok");
            // Handed straight to the tab rather than sent: the shape under test is the reveal,
            // and a fabricated body is the only way to hold the line count fixed across the
            // wrapping and non-wrapping variants.
            app.ActiveTab!.Result = Meridian.Core.SendResult.Ok(LongTextResponse(wrapping));
            MarkDirty(app);
            session.PumpFrames(2);

            session.Window.PressKey(KeyCode.F, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            session.Window.TypeText("far-below");
            session.PumpFrames(3);
            Assert.True(Contains(session, "1 of 1"), "the marker should be the only match");

            // The reveal lands in a post-frame callback off a rebuild, so this waits for the
            // scroll rather than assuming a frame count. The wait can only turn a slow pass
            // into a pass; a body that never scrolls still fails, with the message intact.
            try { session.PumpUntil(() => ScrollY(session) > 0, TimeSpan.FromSeconds(15)); }
            catch (TimeoutException) { /* the caller's assertion reports it */ }
            scroll = ScrollY(session);
        });
        return scroll;
    }

    /// <summary>
    /// Narrowing the window re-wraps the body.
    /// <para>
    /// Meridian shows a response through a <see cref="TextAreaElement"/>, which owns its own
    /// wrapping and caches the line starts against the width it wrapped at. That is the whole
    /// reason this app is not exposed to the fault the other two were: their wrapped rows are
    /// measured in a build and handed to a virtualized list, so a resize that skipped the
    /// build left them stacked at the old width. Here the wrap is recomputed during layout.
    /// </para>
    /// <para>Pinned anyway, because it is a property of the control and not of this view: the
    /// cache is keyed on the width, and a cache that failed to notice a width change would
    /// look exactly like the bug the other apps had.</para>
    /// </summary>
    [Fact]
    public void NarrowingTheWindowReWrapsTheBody()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-ok");
            app.ActiveTab!.Result = Meridian.Core.SendResult.Ok(
                TextResponse(string.Join(' ', Enumerable.Repeat("wrap me around the pane", 60))));
            MarkDirty(app);
            session.PumpFrames(3);

            int wide = WrappedRowCount(session);
            Assert.True(wide > 1, $"the body laid out as {wide} row(s) — it did not wrap at all");

            // Both widths are below the orientation breakpoint, so the pane stays across the
            // bottom and narrowing the window really does narrow it. Crossing the breakpoint
            // instead moves the pane from a side column to the full width, which makes it
            // *wider* — the first draft of this test read that as a failure to re-wrap.
            session.Window.SetClientSize(760, 900);
            session.PumpFrames(4);

            int narrow = WrappedRowCount(session);
            Assert.True(narrow > wide,
                $"the body took {wide} rows at 1000px and {narrow} at 760px — narrowing did not re-wrap");
        }, width: 1000);
    }

    /// <summary>
    /// A run with nowhere to break is cut by character rather than run off the pane.
    /// <para>The body has no sideways scrolling, so a line that overflows is simply
    /// unreadable past the edge — a minified payload, a base64 blob or a long hash is exactly
    /// the shape of response that produces one.</para>
    /// </summary>
    [Fact]
    public void AnUnbreakableRunIsCutByCharacterRatherThanRunningOffThePane()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-ok");
            app.ActiveTab!.Result = Meridian.Core.SendResult.Ok(TextResponse(new string('x', 2000)));
            MarkDirty(app);
            session.PumpFrames(3);

            // One logical line, so any row count above one is the character-level break.
            Assert.True(WrappedRowCount(session) > 5,
                $"2000 unbreakable characters laid out as {WrappedRowCount(session)} row(s) — they did not break");
        });
    }

    /// <summary>How many rows the body wrapped into, asked of the control's own line-start
    /// cache — the same list it scrolls and hit-tests against.</summary>
    private static int WrappedRowCount(HeadlessSession session)
    {
        var el = session.Window.FindByAutomationId("response-body");
        Assert.True(el != null, "response body area not found");
        var area = (TextAreaElement)el!.Element;
        var starts = (System.Collections.Generic.List<int>?)typeof(TextAreaElement)
            .GetField("_lineStarts", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!.GetValue(area);
        Assert.True(starts is not null, "the body has not wrapped yet — pump a frame first");
        return starts!.Count;
    }

    /// <summary>400 filler lines then the marker, as text/plain so nothing re-indents it.
    /// <paramref name="wrapping"/> pads every line past any plausible pane width, so each one
    /// occupies several drawn rows while the line <i>count</i> stays the same.</summary>
    private static Meridian.Core.HttpResponseData LongTextResponse(bool wrapping)
    {
        var filler = wrapping ? " " + string.Join(' ', Enumerable.Repeat("filler", 70)) : "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 400; i++) sb.Append("old line ").Append(i).Append(filler).Append('\n');
        sb.Append("the far-below marker\n");
        return TextResponse(sb.ToString());
    }

    /// <summary>A 200 carrying <paramref name="text"/> as text/plain — plain so pretty mode
    /// leaves it exactly as written and the line count is the one the test intends.</summary>
    private static Meridian.Core.HttpResponseData TextResponse(string text) =>
        new(200, "OK", [("Content-Type", "text/plain")], "text/plain",
            new Meridian.Core.ResponseBody(Meridian.Core.BodyKind.Text, text, [], text.Length, false, null),
            new Meridian.Core.PhaseTimings(default, default, default, default, default, default, false, "HTTP/1.1", null),
            "http://127.0.0.1/long");

    private static float ScrollY(HeadlessSession session)
    {
        var el = session.Window.FindByAutomationId("response-body");
        Assert.True(el != null, "response body area not found");
        return (float)typeof(TextAreaElement).GetProperty("ScrollOffsetY",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(el!.Element)!;
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
