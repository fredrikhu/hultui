using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Core;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>P10's view half — and, more importantly, what auth actually puts
/// on the wire, proved against the loopback server's /whoami mirror.</summary>
public class AuthViewTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-authv-").FullName;

    public AuthViewTests(TestHttpServer server)
    {
        _server = server;
        Write("charges/_folder.req", "auth bearer {{token}}\n");
        Write("charges/who.req", $"GET {_server.Origin}/whoami\nname Who\n");
        Write("lone.req", $"GET {_server.Origin}/whoami\nname Lone\n");
        Write("envs/local.env", "token=inherited-tok\nuser=bot\npassword=hunter2\napi_key=k-123\n");
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

    private static string SendAndRead(HeadlessSession session, MeridianApp app)
    {
        Click(session, "send");
        session.PumpUntil(() => app.ActiveTab!.Result != null && app.ActiveTab.Sending == null,
            TimeSpan.FromSeconds(10));
        return app.ActiveTab!.Result!.Response!.Body.Text;
    }

    // ── What lands on the wire ───────────────────────────────────────────

    [Fact]
    public void AnInheritedBearerReachesTheServer()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-who");

            Assert.Equal("Bearer inherited-tok", SendAndRead(session, app));
        });
    }

    [Fact]
    public void NoAuthMeansNoAuthorizationHeader()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-lone");   // outside charges/, no folder default

            Assert.Equal("none", SendAndRead(session, app));
        });
    }

    [Fact]
    public void BasicIsEncodedOnTheWire()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-lone");
            app.ActiveTab!.Doc.SetAuth("basic {{user}} {{password}}");
            session.PumpFrames(2);

            var expected = "Basic " + Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes("bot:hunter2"));
            Assert.Equal(expected, SendAndRead(session, app));
        });
    }

    [Fact]
    public void AnExplicitAuthorizationHeaderWinsOverTheAuthLine()
    {
        // Someone who wrote the header meant it — auth must not clobber it.
        Hosted((session, app) =>
        {
            Click(session, "req-who");   // inherits bearer {{token}}
            app.ActiveTab!.Doc.AddHeader("Authorization", "Bearer hand-written");
            session.PumpFrames(2);

            Assert.Equal("Bearer hand-written", SendAndRead(session, app));
        });
    }

    [Fact]
    public void AnApiKeyInTheQueryLandsInTheUrl()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-lone");
            app.ActiveTab!.Doc.SetAuth("apikey query api_key {{api_key}}");
            session.PumpFrames(2);
            SendAndRead(session, app);

            Assert.Contains("api_key=k-123", _server.LastRequestLine);
        });
    }

    [Fact]
    public void AnUnresolvedCredentialBlocksTheSend()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-lone");
            app.ActiveTab!.Doc.SetAuth("bearer {{nowhere}}");
            session.PumpFrames(2);
            Click(session, "send");
            session.PumpFrames(4);

            Assert.Null(app.ActiveTab.Result);
            Assert.True(Contains(session, "auth needs {{nowhere}}"),
                "the toast must name the missing credential");
        });
    }

    [Fact]
    public void OAuth2FetchesATokenAndSendsIt()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-lone");
            app.ActiveTab!.Doc.SetAuth($"oauth2 client_credentials {_server.Origin}/token id secret");
            session.PumpFrames(2);

            Assert.Equal("Bearer tok-abc123", SendAndRead(session, app));
        });
    }

    [Fact]
    public void AnUnreachableTokenEndpointStopsTheSend()
    {
        int deadPort = FreePort();
        Hosted((session, app) =>
        {
            Click(session, "req-lone");
            app.ActiveTab!.Doc.SetAuth($"oauth2 client_credentials http://127.0.0.1:{deadPort}/token id secret");
            session.PumpFrames(2);
            Click(session, "send");
            session.PumpUntil(() => Contains(session, "token endpoint unreachable"), TimeSpan.FromSeconds(10));

            Assert.Null(app.ActiveTab.Result);
        });
    }

    // ── The Auth tab ─────────────────────────────────────────────────────

    [Fact]
    public void TheInheritanceBarNamesTheFolderAndOverridingCopiesItDown()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-who");
            Click(session, "subtab-auth");

            Assert.NotNull(session.Window.FindByAutomationId("auth-inheritance"));
            Assert.True(Contains(session, "inherited from charges/ — Bearer"));

            Click(session, "auth-override");

            // The request now owns the config, seeded from what was in effect.
            Assert.Equal("bearer {{token}}", app.ActiveTab!.Doc.Auth!.Args);
            Assert.Null(session.Window.FindByAutomationId("auth-inheritance"));
        });
    }

    [Fact]
    public void EditingInheritedAuthWritesTheFolderFile()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-who");
            Click(session, "subtab-auth");
            Assert.True(Contains(session, "editing writes charges/_folder.req"),
                "the footer must say where the edit lands");

            Click(session, "auth-kind-basic");

            var folderFile = File.ReadAllText(Path.Combine(_root, "charges", "_folder.req"));
            Assert.Contains("auth basic", folderFile);
            // …and the request file itself stays clean.
            Assert.DoesNotContain("auth", File.ReadAllText(Path.Combine(_root, "charges", "who.req")));
        });
    }

    [Fact]
    public void PickingATypeShowsOnlyItsFields()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-lone");
            Click(session, "subtab-auth");

            Click(session, "auth-kind-basic");
            Assert.NotNull(session.Window.FindByAutomationId("auth-user"));
            Assert.Null(session.Window.FindByAutomationId("auth-token-url"));

            Click(session, "auth-kind-oauth2");
            Assert.NotNull(session.Window.FindByAutomationId("auth-token-url"));
            Assert.NotNull(session.Window.FindByAutomationId("auth-client-secret"));
            Assert.Null(session.Window.FindByAutomationId("auth-user"));
        });
    }

    [Fact]
    public void NewAuthConfigsDefaultToVariableChipsNotLiterals()
    {
        // The nudge that keeps credentials out of the repo.
        Hosted((session, app) =>
        {
            Click(session, "req-lone");
            Click(session, "subtab-auth");
            Click(session, "auth-kind-bearer");

            Assert.Equal("bearer {{token}}", app.ActiveTab!.Doc.Auth!.Args);
        });
    }

    [Fact]
    public void TheTokenChipTracksTheGrant()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-lone");
            app.ActiveTab!.Doc.SetAuth($"oauth2 client_credentials {_server.Origin}/token id secret");
            Click(session, "subtab-auth");
            Assert.True(Contains(session, "no token yet"), "before any send there is no token");

            SendAndRead(session, app);
            session.PumpFrames(2);

            Assert.True(Contains(session, "token valid"), "after a send the chip should show a live token");
            Click(session, "auth-clear-token");
            Assert.True(Contains(session, "no token yet"));
        });
    }

    [Fact]
    public void SettingAuthToNoneRestoresTheFolderDefault()
    {
        Hosted((session, app) =>
        {
            Click(session, "req-who");
            Click(session, "subtab-auth");
            Click(session, "auth-override");           // now owns "bearer {{token}}"
            Assert.NotNull(app.ActiveTab!.Doc.Auth);

            Click(session, "auth-kind-none");

            // The line is gone, so the folder default applies again — which is
            // what "none" has to mean for inheritance to be usable.
            Assert.Null(app.ActiveTab.Doc.Auth);
            Assert.NotNull(session.Window.FindByAutomationId("auth-inheritance"));
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
