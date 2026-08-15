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
}
