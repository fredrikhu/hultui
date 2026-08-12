using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Core;
using Meridian.Platform;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>P4: the compare table, the switcher, and keyring-backed secrets —
/// hosted headless over a real temp collection with three environments.</summary>
public class EnvironmentsViewTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-env-").FullName;

    public EnvironmentsViewTests(TestHttpServer server)
    {
        _server = server;
        Write("echo.req", "POST {{base_url}}/echo\nbody text\ntoken={{api_token}}\nend\n");
        Write("envs/local.env", $"base_url={_server.Origin}\ntimeout_ms=30000\nsecret api_token\n");
        Write("envs/staging.env", "base_url=https://staging.example\ntimeout_ms=30000\n");
        Write("envs/ci.env", "base_url=https://ci.example\n");
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

    internal sealed class FakeKeyring : IKeyring
    {
        public readonly Dictionary<string, string> Store = new(StringComparer.Ordinal);
        public string? Get(string workspaceRoot, string key) => Store.GetValueOrDefault($"{workspaceRoot}|{key}");
        public bool Set(string workspaceRoot, string key, string value)
        {
            Store[$"{workspaceRoot}|{key}"] = value;
            return true;
        }
    }

    private void Hosted(Action<HeadlessSession, MeridianApp, FakeKeyring> body)
    {
        using var session = HeadlessSession.Create(1440, 900);
        var keyring = new FakeKeyring();
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window,
                new NoPicker(), keyring: keyring);
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrame();
            body(session, app, keyring);
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

    private static void GoToEnvironments(HeadlessSession session)
    {
        session.Window.PressKey(KeyCode.Digit5);
        session.PumpFrames(2);
    }

    // ── The compare table ────────────────────────────────────────────────

    [Fact]
    public void AllKeysCrossAllEnvironments()
    {
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);

            // Every env file listed; local is auto-active.
            foreach (var env in new[] { "local", "staging", "ci" })
                Assert.NotNull(session.Window.FindByAutomationId("env-file-" + env));
            Assert.True(Contains(session, "local, active"));

            // Cells exist where values exist…
            Assert.NotNull(session.Window.FindByAutomationId("cell-base_url-local"));
            Assert.NotNull(session.Window.FindByAutomationId("cell-timeout_ms-staging"));
            // …and the missing cell carries the one-click fix.
            Assert.NotNull(session.Window.FindByAutomationId("add-timeout_ms-to-ci"));
        });
    }

    [Fact]
    public void TheMissingCellsFixAddsTheKeyAndCtrlSWritesTheFile()
    {
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);
            Click(session, "add-timeout_ms-to-ci");

            // Now an editable cell, and the column is dirty.
            Assert.NotNull(session.Window.FindByAutomationId("cell-timeout_ms-ci"));
            Assert.True(Contains(session, "1 unsaved"));

            session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            session.PumpFrames(2);

            Assert.True(Contains(session, "saved"));
            Assert.Contains("timeout_ms=", File.ReadAllText(Path.Combine(_root, "envs", "ci.env")));
        });
    }

    [Fact]
    public void EditingACellRewritesOnlyItsLine()
    {
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);
            var input = session.Window.FindByAutomationId("cell-timeout_ms-local");
            Assert.NotNull(input);
            input!.Click();
            session.PumpFrames(1);
            session.Window.TypeText("0");
            session.PumpFrames(2);

            session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            session.PumpFrames(2);

            var text = File.ReadAllText(Path.Combine(_root, "envs", "local.env"));
            Assert.Contains("timeout_ms=300000", text);
            Assert.Contains($"base_url={_server.Origin}", text);   // untouched
            Assert.Contains("secret api_token", text);             // flag preserved
        });
    }

    // ── Switcher ─────────────────────────────────────────────────────────

    [Fact]
    public void CtrlEOpensTheSwitcherAndPicksAnEnvironment()
    {
        Hosted((session, app, keyring) =>
        {
            Assert.Equal("local", app.ActiveEnvName);

            session.Window.PressKey(KeyCode.E, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Click(session, "popover-staging");

            Assert.Equal("staging", app.ActiveEnvName);
            Assert.True(Contains(session, "env: staging"));
        });
    }

    [Fact]
    public void NoEnvironmentIsALegalChoice()
    {
        Hosted((session, app, keyring) =>
        {
            session.Window.PressKey(KeyCode.E, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Click(session, "popover-no environment");

            Assert.Null(app.ActiveEnvName);
            Assert.True(Contains(session, "no environment"));

            // With no env, {{base_url}} is unresolved and the send blocks.
            session.Window.PressKey(KeyCode.Digit1);
            session.PumpFrames(2);
            Click(session, "req-echo");
            Click(session, "send");
            session.PumpFrames(3);
            Assert.Null(app.ActiveTab!.Result);
            Assert.True(Contains(session, "Unresolved in env (none)"));
        });
    }

    [Fact]
    public void SwitchingReResolvesTheUrlChips()
    {
        Hosted((session, app, keyring) =>
        {
            Click(session, "req-echo");
            // local defines base_url → resolved chip (no "unresolved" marker).
            Assert.False(Contains(session, "variable base_url, unresolved"));

            app.SelectEnvironment(null);
            session.PumpFrames(2);

            Assert.True(Contains(session, "variable base_url, unresolved"));
        });
    }

    // ── Secrets ──────────────────────────────────────────────────────────

    [Fact]
    public void AMissingSecretShowsTheFreshCloneStateAndSettingItSticks()
    {
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);

            Assert.NotNull(session.Window.FindByAutomationId("set-secret-api_token"));

            Click(session, "set-secret-api_token");
            Assert.NotNull(session.Window.FindByAutomationId("dialog-input"));
            session.Window.TypeText("s3cr3t-value");
            session.Window.PressKey(KeyCode.Enter);
            session.PumpFrames(2);

            Assert.Equal("s3cr3t-value", keyring.Get(app.Workspace!.Root, "api_token"));
            // The cell is now the masked keyring cell.
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-api_token"));
            Assert.True(Contains(session, "••••••••"));
        });
    }

    [Fact]
    public void RevealShowsTheValueTemporarily()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "api_token", "peekaboo");
            GoToEnvironments(session);

            Assert.False(Contains(session, "peekaboo"), "the value must start masked");
            Click(session, "reveal-api_token");

            Assert.True(Contains(session, "peekaboo"), "reveal should show the value");
        });
    }

    private static string _rootFull(MeridianApp app) => app.Workspace!.Root;

    [Fact]
    public void ASecretResolvesFromTheKeyringOnSend()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "api_token", "tok-123");

            Click(session, "req-echo");
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result != null, TimeSpan.FromSeconds(10));

            // The body template token={{api_token}} reached the server resolved.
            Assert.Contains("token=tok-123", app.ActiveTab!.Result!.Response!.Body.Text);
        });
    }

    [Fact]
    public void AMissingSecretBlocksTheSendWithTheMachineLocalCopy()
    {
        Hosted((session, app, keyring) =>
        {
            Click(session, "req-echo");
            Click(session, "send");
            session.PumpFrames(3);

            Assert.Null(app.ActiveTab!.Result);
            Assert.True(Contains(session, "secret api_token not set on this machine"),
                "the toast must name the machine-local fix, not the environment");
        });
    }

    [Fact]
    public void TheSecretsFileValueNeverLeaksIntoResolution()
    {
        // A secret key with a placeholder file value: the file value must NOT
        // resolve — only the keyring (else the "never on disk" rule is a lie).
        Write("envs/local.env", $"base_url={_server.Origin}\napi_token=LEAKED-FROM-FILE\nsecret api_token\n");
        Hosted((session, app, keyring) =>
        {
            Click(session, "req-echo");
            Click(session, "send");
            session.PumpFrames(3);

            Assert.Null(app.ActiveTab!.Result);   // blocked: keyring empty
            keyring.Set(_rootFull(app), "api_token", "from-keyring");
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result != null, TimeSpan.FromSeconds(10));

            Assert.Contains("token=from-keyring", app.ActiveTab!.Result!.Response!.Body.Text);
            Assert.DoesNotContain("LEAKED", app.ActiveTab.Result.Response.Body.Text);
        });
    }
}
