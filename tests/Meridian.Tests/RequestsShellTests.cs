using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// The editor side of the shell, hosted headless over a real temp collection
/// and a real loopback server. Assertions go through the accessibility tree
/// and automation ids.
/// </summary>
public class RequestsShellTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-shell-").FullName;

    public RequestsShellTests(TestHttpServer server)
    {
        _server = server;
        Write("auth/login.req", "POST {{base_url}}/echo\nname Login\nheader Accept: application/json\n");
        Write("get-ok.req", $"GET {_server.Origin}/ok\n");
        Write("stall.req", $"GET {_server.Origin}/stall\n");
        Write("missing-var.req", "GET {{nowhere}}/x\n");
        Write("envs/local.env", $"base_url={_server.Origin}\n");
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

    // ── Tree ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheTreeShowsRequestsWithMethodAndName()
    {
        Hosted((session, app) =>
        {
            Assert.True(Contains(session, "POST Login"), "Login row missing");
            Assert.True(Contains(session, "GET Get ok"), "Get ok row missing");
            // The env auto-picked and shown in the titlebar.
            Assert.True(Contains(session, "env: local") || session.Window.FindByAutomationId("env-name") != null);
        });
    }

    [Fact]
    public void ClickingARowOpensATabWithTheUrl()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-login");

            Assert.NotNull(app.ActiveTab);
            Assert.EndsWith("login.req", app.ActiveTab!.Path);
            Assert.True(Contains(session, "{{base_url}}"), "URL display with the var chip is missing");
            Assert.True(Contains(session, "Login tab"), "tab is missing");
        });
    }

    // ── Editing + saving ─────────────────────────────────────────────────

    [Fact]
    public void PickingAMethodMarksDirtyAndCtrlSWritesTheFile()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-get-ok");
            Click(session, "method-chip");
            Click(session, "popover-PUT");

            Assert.Equal("PUT", app.ActiveTab!.Doc.Method);
            Assert.True(app.ActiveTab.Dirty);

            session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            session.PumpFrames(2);

            Assert.False(app.ActiveTab.Dirty);
            Assert.StartsWith("PUT ", File.ReadAllText(Path.Combine(_root, "get-ok.req")));
        });
    }

    [Fact]
    public void EditingTheUrlThroughTheInputUpdatesTheDocument()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-get-ok");
            Click(session, "url-display");           // switches to the input, focuses it
            var input = session.Window.FindByAutomationId("url-input");
            Assert.True(input != null, "url input did not appear");

            session.Window.TypeText("?a=1");
            session.PumpFrames(2);

            Assert.EndsWith("?a=1", app.ActiveTab!.Doc.Url);
            Assert.True(app.ActiveTab.Dirty);
            // The Params tab now counts one pair.
            Assert.Equal(("a", "1"), MeridianApp.QueryParams(app.ActiveTab).Single());
        });
    }

    /// <summary>On the Headers, Body and Auth sub-tabs a URL character builds the window only when
    /// it moves the Params or the Chain count, or makes the request unsaved: the box holds the text,
    /// and nothing else there reads the URL. Each character built the whole editor, 64 KB (plan 17
    /// round 7). The Params sub-tab, whose rows are the URL's, still builds for every character.</summary>
    [Fact]
    public void AUrlCharacterBuildsOnlyWhenItMovesWhatTheEditorShows()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-get-ok");
            int Typed(string text)
            {
                int builds = app.Builds;
                foreach (var ch in text)
                {
                    session.Window.TypeText(ch.ToString());
                    session.PumpFrames(2);
                }
                return app.Builds - builds;
            }

            foreach (var sub in new[] { "headers", "body", "auth" })
            {
                Click(session, "subtab-" + sub);
                Click(session, "url-display");
                Assert.Equal("url-input", session.Window.Window.FocusedElement?.AutomationId);
                if (sub == "headers") Assert.Equal(1, Typed("/"));   // the request turns unsaved
                Assert.Equal(0, Typed("v" + sub));
                Assert.Equal(0, Typed("?"));
                Assert.Equal(1, Typed("a"));                        // Params 1
                Assert.Equal(0, Typed("=1&"));
                Assert.Equal(1, Typed("b"));                        // Params 2
                Assert.Equal(1, Typed("{{base_url}}"));             // Chain 1, at the closing braces
                Assert.True(Contains(session, "Params 2"), "the Params count");
                Assert.True(Contains(session, "Chain 1"), "the Chain count");
                Assert.Equal(0, Typed("xyz"));
                Assert.Equal("url-input", session.Window.Window.FocusedElement?.AutomationId);

                // Off the box, the URL shows as typed.
                session.Window.PressKey(KeyCode.Tab);
                session.PumpFrames(2);
                Assert.EndsWith($"/ok/v{sub}?a=1&b{{{{base_url}}}}xyz", app.ActiveTab!.Doc.Url);
                Assert.True(Contains(session, $"v{sub}?a=1&b"), "the URL as typed");
                app.ActiveTab.Doc.SetRequestLine("GET", $"{_server.Origin}/ok/");
            }

            Click(session, "subtab-params");
            Click(session, "url-display");
            Assert.Equal(3, Typed("xyz"));
        });
    }

    /// <summary>A header value and the body are the URL box's case: the field holds the text, and the
    /// rest of the editor reads it only for the Chain count, so a character builds the window only
    /// when it moves that count or makes the request unsaved, and in the body when its first
    /// character makes the body block the sub-tab names ("Body json"). Each character built the
    /// whole editor, 65 KB (plan 17 round 9).</summary>
    [Fact]
    public void AHeaderValueOrBodyCharacterBuildsOnlyWhenItMovesWhatTheEditorShows()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-login");
            int Typed(string text)
            {
                int builds = app.Builds;
                foreach (var ch in text)
                {
                    session.Window.TypeText(ch.ToString());
                    session.PumpFrames(2);
                }
                return app.Builds - builds;
            }
            string? Focused() => session.Window.Window.FocusedElement?.AutomationId;

            Click(session, "subtab-headers");
            Click(session, "header-value-0");
            session.Window.PressKey(KeyCode.End);
            session.PumpFrames(1);
            Assert.Equal(1, Typed(";"));                     // the request turns unsaved
            Assert.True(Contains(session, "Login tab, unsaved"), "the tab's unsaved mark");
            Assert.Equal(0, Typed(" q=0.9 "));
            Assert.True(Contains(session, "Chain 1"), "the URL's base_url");
            Assert.Equal(1, Typed("{{token}}"));             // Chain 2, at the closing braces
            Assert.True(Contains(session, "Chain 2"), "the Chain count");
            Assert.Equal(0, Typed("x"));
            Assert.Equal("header-value-0", Focused());
            Assert.Equal("application/json; q=0.9 {{token}}x", app.ActiveTab!.Doc.Headers[0].Value);

            Click(session, "subtab-body");
            Assert.False(Contains(session, "Body json"), "no body block yet");
            Click(session, "body-editor");
            Assert.Equal(1, Typed("{"));                     // the block, and its type on the sub-tab
            Assert.True(Contains(session, "Body json"), "the body's type");
            Assert.Equal(0, Typed("\"a\": 1, "));
            Assert.Equal(1, Typed("{{user}}"));              // Chain 3
            Assert.True(Contains(session, "Chain 3"), "the Chain count");
            Assert.Equal(0, Typed(" "));
            Assert.Equal("body-editor", Focused());
            Assert.Equal("{\"a\": 1, {{user}} ", app.ActiveTab.Doc.Body!.Content);

            session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Assert.False(app.ActiveTab.Dirty);
            Assert.Equal(1, Typed("2"));                     // unsaved again
            Assert.True(Contains(session, "Login tab, unsaved"), "the tab's unsaved mark");
        });
    }

    [Fact]
    public void AddingAHeaderRowEditsTheDocument()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-get-ok");
            Click(session, "subtab-headers");
            Click(session, "add-header");

            Assert.Single(app.ActiveTab!.Doc.Headers);
            Assert.True(app.ActiveTab.Dirty);
            // The new row's inputs exist.
            Assert.NotNull(session.Window.FindByAutomationId("header-name-0"));
        });
    }

    // ── Send / cancel ────────────────────────────────────────────────────

    [Fact]
    public void SendingShowsTheStatusAndBody()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-get-ok");
            Click(session, "send");
            session.PumpUntil(() => Contains(session, "200 OK"), TimeSpan.FromSeconds(10));

            Assert.True(Contains(session, "✓ 200 OK"));
            Assert.NotNull(app.ActiveTab!.Result?.Response);
        });
    }

    [Fact]
    public void AVariableFromTheEnvironmentResolvesOnSend()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-login");   // POST {{base_url}}/echo
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result != null, TimeSpan.FromSeconds(10));

            Assert.Equal(200, app.ActiveTab!.Result!.Response!.StatusCode);
            Assert.Contains("POST|", app.ActiveTab.Result.Response.Body.Text);
        });
    }

    [Fact]
    public void AnUnresolvedVariableBlocksTheSendAndSaysWhich()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-missing-var");
            Click(session, "send");
            session.PumpFrames(3);

            Assert.Null(app.ActiveTab!.Result);
            Assert.Null(app.ActiveTab.Sending);
            // The toast's own copy, not the URL bar's chip (which always shows
            // the name): the block must SAY it blocked, and name the variable.
            Assert.True(Contains(session, "Unresolved in env local: {{nowhere}}"),
                "the unresolved-variable toast is missing");
        });
    }

    [Fact]
    public void EscCancelsAnInFlightSend()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-stall");
            Click(session, "send");
            session.PumpUntil(() => session.Window.FindByAutomationId("cancel") != null, TimeSpan.FromSeconds(5));

            session.Window.PressKey(KeyCode.Escape);
            session.PumpUntil(() => app.ActiveTab!.Sending == null, TimeSpan.FromSeconds(5));

            Assert.Null(app.ActiveTab!.Result);   // cancelled, not failed
            Assert.NotNull(session.Window.FindByAutomationId("send"));
        });
    }

    // ── Tabs ─────────────────────────────────────────────────────────────

    [Fact]
    public void CtrlWClosesTheActiveTab()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-get-ok");
            Click(session, "req-login");
            Assert.Equal(2, TabCount(app));

            session.Window.PressKey(KeyCode.W, KeyModifiers.Ctrl);
            session.PumpFrames(2);

            Assert.Equal(1, TabCount(app));
            Assert.EndsWith("get-ok.req", app.ActiveTab!.Path);
        });
    }

    private static int TabCount(MeridianApp app) => app.OpenTabCount;

    // ── External changes ─────────────────────────────────────────────────

    [Fact]
    public void ACleanTabReloadsSilentlyWhenTheFileChangesOnDisk()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-get-ok");
            Write("get-ok.req", $"GET {_server.Origin}/teapot\n");

            session.PumpUntil(() => app.ActiveTab!.Doc.Url!.EndsWith("/teapot"), TimeSpan.FromSeconds(10));
            Assert.False(app.ActiveTab!.ExternalChanged);
            Assert.Null(session.Window.FindByAutomationId("reload-external"));
        });
    }

    [Fact]
    public void ADirtyTabGetsTheBarAndReloadTakesTheDiskVersion()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-get-ok");
            app.ActiveTab!.Doc.SetRequestLine("GET", $"{_server.Origin}/mine");   // dirty
            Write("get-ok.req", $"GET {_server.Origin}/theirs\n");

            session.PumpUntil(() => session.Window.FindByAutomationId("reload-external") != null,
                TimeSpan.FromSeconds(10));

            Click(session, "reload-external");

            Assert.EndsWith("/theirs", app.ActiveTab.Doc.Url);
            Assert.False(app.ActiveTab.Dirty);
            Assert.Null(session.Window.FindByAutomationId("reload-external"));
        });
    }

    [Fact]
    public void KeepMineDismissesTheBarAndKeepsTheEdit()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-get-ok");
            app.ActiveTab!.Doc.SetRequestLine("GET", $"{_server.Origin}/mine");
            Write("get-ok.req", $"GET {_server.Origin}/theirs\n");

            session.PumpUntil(() => session.Window.FindByAutomationId("reload-external") != null,
                TimeSpan.FromSeconds(10));

            Click(session, "keep-mine");

            Assert.EndsWith("/mine", app.ActiveTab.Doc.Url);
            Assert.True(app.ActiveTab.Dirty);
            Assert.Null(session.Window.FindByAutomationId("reload-external"));
        });
    }

    /// <summary>
    /// Both editors are controlled: each keystroke goes into the document, and the rebuild hands
    /// the field its value back from there. Undo depends on that value coming back exactly — a
    /// document that normalised it would look, to the field, like a value set from code, and the
    /// history would be forgotten one key after it was written. Typed a frame per key, the way
    /// a person types, so every keystroke goes through that round trip.
    /// </summary>
    [Fact]
    public void CtrlZUndoesTypingInTheUrlAndTheBody()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-get-ok");
            string url = app.ActiveTab!.Doc.Url!;

            Click(session, "url-display");
            TypeSlowly(session, "?a=1");
            Assert.Equal(url + "?a=1", app.ActiveTab.Doc.Url);
            session.Window.PressKey(KeyCode.Z, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Assert.Equal(url, app.ActiveTab.Doc.Url);

            Click(session, "subtab-body");
            Click(session, "body-editor");
            TypeSlowly(session, "hello world");
            Assert.Equal("hello world", app.ActiveTab.Doc.Body?.Content);
            session.Window.PressKey(KeyCode.Z, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Assert.Equal("hello ", app.ActiveTab.Doc.Body?.Content);
        });
    }

    private static void TypeSlowly(HeadlessSession session, string text)
    {
        foreach (char c in text)
        {
            session.Window.TypeText(c.ToString());
            session.PumpFrame();
        }
    }
}
