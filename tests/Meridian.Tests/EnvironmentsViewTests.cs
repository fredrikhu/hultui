using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Core;
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


    internal sealed class FakeKeyring : IKeyring
    {
        public readonly Dictionary<string, string> Store = new(StringComparer.Ordinal);
        public string? Get(string workspaceRoot, string key) => Store.GetValueOrDefault($"{workspaceRoot}|{key}");
        public bool Set(string workspaceRoot, string key, string value)
        {
            Store[$"{workspaceRoot}|{key}"] = value;
            return true;
        }
        public bool Delete(string workspaceRoot, string key) => Store.Remove($"{workspaceRoot}|{key}");
    }

    private void Hosted(Action<HeadlessSession, MeridianApp, FakeKeyring> body)
    {
        using var session = HeadlessSession.Create(1440, 900);
        var keyring = new FakeKeyring();
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window,
                keyring: keyring);
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

    // ── Made in the app: environments, secret flags, removal ────────────
    // Nothing here needs a hand-written file: the walkthrough user's envs\local.env
    // and its `secret client_secret` line come from the view itself.

    private string EnvText(string name) => File.ReadAllText(Path.Combine(_root, "envs", name + ".env"));

    private static AccessibilityNode Node(HeadlessSession session, string automationId) =>
        Walk(session.Window.AccessibilityTree).Single(n => n.AutomationId == automationId);

    private static void Submit(HeadlessSession session, string text)
    {
        session.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);   // clear what an earlier try left
        session.Window.PressKey(KeyCode.Backspace);
        session.Window.TypeText(text);
        session.PumpFrames(1);
        session.Window.PressKey(KeyCode.Enter);
        session.PumpFrames(2);
    }

    [Fact]
    public void NewEnvironmentFromTheEmptyStateWritesTheFileAndMakesItActive()
    {
        Directory.Delete(Path.Combine(_root, "envs"), recursive: true);
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);
            Assert.True(Contains(session, "no environments"));
            Assert.Null(app.ActiveEnvName);

            Click(session, "new-env");
            Assert.Equal("New environment",
                Walk(session.Window.AccessibilityTree).Single(n => n.Role == AccessibilityRole.Dialog).Name);
            Submit(session, "local");

            Assert.Null(session.Window.FindByAutomationId("dialog-input"));
            Assert.Equal("", EnvText("local"));                 // envs/ made, the file empty
            Assert.Equal("local", app.ActiveEnvName);
            Assert.True(Contains(session, "local, active"));

            // The new environment is the one "＋ add key" fills, and Ctrl+S writes it.
            Click(session, "add-env-key");
            Submit(session, "client_id");
            session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Assert.Equal("client_id=\n", EnvText("local"));
        });
    }

    [Fact]
    public void ABadEnvironmentNameIsRefusedInThePrompt()
    {
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);
            Click(session, "new-env");

            foreach (var (bad, reason) in new[]
            {
                ("../escape", "can't contain"),
                ("sub\\dir", "can't contain"),
                ("", "Give the environment a name"),
                ("Staging", "staging.env already exists"),
                ("ci.env", "ci.env already exists"),
                ("a:b", "can't contain"),
            })
            {
                Submit(session, bad);
                Assert.True(session.Window.FindByAutomationId("dialog-input") != null, $"'{bad}' closed the prompt");
                Assert.Contains(reason, Node(session, "dialog-error").Name);
                Assert.Contains(reason, Node(session, "dialog-input").Description);
            }
            Assert.False(File.Exists(Path.Combine(_root, "escape.env")));
            Assert.Equal(3, Directory.GetFiles(Path.Combine(_root, "envs")).Length);
            Assert.Equal("local", app.ActiveEnvName);

            // A good name after the refusals: created, active, the prompt gone.
            Submit(session, " qa.env ");
            Assert.Null(session.Window.FindByAutomationId("dialog-input"));
            Assert.Equal("", EnvText("qa"));
            Assert.Equal("qa", app.ActiveEnvName);
            Assert.NotNull(session.Window.FindByAutomationId("env-file-qa"));
        });
    }

    [Fact]
    public void MarkingAKeySecretWritesASecretLineAndLeavesEveryOtherByte()
    {
        const string staging = "# staging\r\nbase_url=https://staging.example\r\nclient_secret=\r\ntimeout_ms=30000\r\n";
        string local = $"base_url={_server.Origin}\ntimeout_ms=30000\nsecret api_token\nclient_secret=\n";
        Write("envs/staging.env", staging);
        Write("envs/local.env", local);
        string ci = File.ReadAllText(Path.Combine(_root, "envs", "ci.env"));
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);
            Assert.Equal(false, Node(session, "secret-toggle-client_secret").IsToggled);
            Assert.NotNull(session.Window.FindByAutomationId("cell-client_secret-staging"));

            Click(session, "secret-toggle-client_secret");
            Assert.Equal(true, Node(session, "secret-toggle-client_secret").IsToggled);
            Assert.True(Contains(session, "2 unsaved"));
            // The keyring cell takes over the column: the fresh-machine fix.
            Assert.NotNull(session.Window.FindByAutomationId("set-secret-client_secret"));
            Assert.Null(session.Window.FindByAutomationId("cell-client_secret-staging"));

            session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            session.PumpFrames(2);

            Assert.Equal(staging + "secret client_secret\r\n", EnvText("staging"));   // its CRLF, too
            Assert.Equal(local + "secret client_secret\n", EnvText("local"));
            Assert.Equal(ci, EnvText("ci"));                                           // no client_secret: untouched
        });
    }

    [Fact]
    public void UnmarkingASecretPutsAFlagOnlyKeyBackInTheFile()
    {
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);
            Assert.Equal(true, Node(session, "secret-toggle-api_token").IsToggled);

            Click(session, "secret-toggle-api_token");
            Assert.Equal(false, Node(session, "secret-toggle-api_token").IsToggled);
            Assert.NotNull(session.Window.FindByAutomationId("cell-api_token-local"));

            session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Assert.Equal($"base_url={_server.Origin}\ntimeout_ms=30000\napi_token=\n", EnvText("local"));
        });
    }

    /// <summary>An environment without a secret's flag would leave it unresolved there, so its
    /// column offers the missing fix, not the keyring cell — and the fix adds the flag alone.</summary>
    [Fact]
    public void AnEnvironmentMissingASecretGetsItsFlagAndNoValue()
    {
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);
            Click(session, "add-api_token-to-staging");

            session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Assert.Equal("base_url=https://staging.example\ntimeout_ms=30000\nsecret api_token\n", EnvText("staging"));
            Assert.NotNull(session.Window.FindByAutomationId("add-api_token-to-ci"));
        });
    }

    [Fact]
    public void AValueTypedBeforeTheFlagMovesToTheKeyring()
    {
        Write("envs/local.env", $"base_url={_server.Origin}\nclient_secret=s3cr3t\n");
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);
            Click(session, "secret-toggle-client_secret");

            // Flagged, but the file still holds it in plain text: the cell says so.
            Assert.True(Contains(session, "still in local.env — move to keyring"));
            Click(session, "plain-secret-client_secret-local");

            Assert.Equal("s3cr3t", keyring.Get(app.Workspace!.Root, "client_secret"));
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-client_secret"));
            session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Assert.Equal($"base_url={_server.Origin}\nclient_secret=\nsecret client_secret\n", EnvText("local"));
        });
    }

    [Fact]
    public void RemovingAKeyTakesItOutOfEveryEnvironment()
    {
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);
            Click(session, "remove-key-timeout_ms");
            Assert.Null(session.Window.FindByAutomationId("cell-timeout_ms-local"));

            session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Assert.Equal($"base_url={_server.Origin}\nsecret api_token\n", EnvText("local"));
            Assert.Equal("base_url=https://staging.example\n", EnvText("staging"));
            Assert.Equal("base_url=https://ci.example\n", EnvText("ci"));
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

    // ── A stored value, managed in the app ───────────────────────────────
    // Change, forget, and what un-marking does with the value: none of it needs the OS's
    // credential manager.

    private static void Save(HeadlessSession session)
    {
        session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
        session.PumpFrames(2);
    }

    private static string SaveState(HeadlessSession session) => Node(session, "env-save-state").Name!;

    [Fact]
    public void ChangeStoresANewValueAndNeverShowsTheOldOne()
    {
        string local = EnvText("local");
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "api_token", "old-value");
            GoToEnvironments(session);
            Assert.Equal("change api_token's value on this machine", Node(session, "change-secret-api_token").Name);

            // A reveal under way ends when the change starts, and the field starts empty.
            Click(session, "reveal-api_token");
            Assert.True(Contains(session, "old-value"));
            Click(session, "change-secret-api_token");
            Assert.Equal("Change api_token",
                Walk(session.Window.AccessibilityTree).Single(n => n.Role == AccessibilityRole.Dialog).Name);
            Assert.True(string.IsNullOrEmpty(Node(session, "dialog-input").Value));
            Assert.False(Contains(session, "old-value"), "the old value must not show while it is changed");

            // Enter on the empty field is refused, not stored as an empty value.
            session.Window.PressKey(KeyCode.Enter);
            session.PumpFrames(2);
            Assert.Contains("Type the new value", Node(session, "dialog-error").Name);
            Assert.Equal("old-value", keyring.Get(_rootFull(app), "api_token"));

            Submit(session, "new-value");
            Assert.Null(session.Window.FindByAutomationId("dialog-input"));
            Assert.Equal("new-value", keyring.Get(_rootFull(app), "api_token"));
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-api_token"));
            Assert.False(Contains(session, "new-value"), "the new value stays masked");
            Assert.True(Contains(session, "api_token changed on this machine"));

            // The keyring alone changed: no file to save.
            Assert.Equal("saved", SaveState(session));
            Assert.Equal(local, EnvText("local"));
        });
    }

    [Fact]
    public void ForgetAsksNamingTheKeyThenTheCellAsksForAValueAgain()
    {
        string local = EnvText("local");
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "api_token", "tok-123");
            GoToEnvironments(session);
            Assert.Equal("forget api_token's value on this machine", Node(session, "forget-secret-api_token").Name);

            Click(session, "forget-secret-api_token");
            var confirm = Node(session, "confirm");
            Assert.Equal("Forget api_token on this machine?", confirm.Name);
            Assert.Contains("can't bring it back", confirm.Description);
            Assert.Null(session.Window.FindByAutomationId("confirm-alt"));

            // Cancel keeps it.
            Click(session, "confirm-cancel");
            Assert.Equal("tok-123", keyring.Get(_rootFull(app), "api_token"));
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-api_token"));

            // Enter forgets: the cell is the fresh-machine one again, and no file changed.
            Click(session, "forget-secret-api_token");
            session.Window.PressKey(KeyCode.Enter);
            session.PumpFrames(2);
            Assert.Null(session.Window.FindByAutomationId("confirm"));
            Assert.Null(keyring.Get(_rootFull(app), "api_token"));
            Assert.NotNull(session.Window.FindByAutomationId("set-secret-api_token"));
            Assert.Null(session.Window.FindByAutomationId("secret-value-api_token"));
            Assert.Equal("saved", SaveState(session));
            Assert.Equal(local, EnvText("local"));
        });
    }

    /// <summary>Un-marking a key whose value this machine stores asks first, saying the value would
    /// sit in the file as plain text; moved, every file that marked the key gets it on Ctrl+S, and
    /// the keyring keeps it until then.</summary>
    [Fact]
    public void UnmarkingAStoredSecretAsksThenMovesTheValueIntoTheFilesOnSave()
    {
        Write("envs/staging.env", "base_url=https://staging.example\nsecret api_token\n");
        string ci = EnvText("ci");
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "api_token", "tok-123");
            GoToEnvironments(session);
            Click(session, "secret-toggle-api_token");

            // The question, before anything changes.
            var confirm = Node(session, "confirm");
            Assert.Equal("Stop keeping api_token secret?", confirm.Name);
            Assert.Contains("local.env and staging.env", confirm.Description);
            Assert.Contains("plain text", confirm.Description);
            Assert.Equal("Move into the files", Node(session, "confirm-ok").Name);
            Assert.Equal("Forget value", Node(session, "confirm-alt").Name);
            Assert.Equal(true, Node(session, "secret-toggle-api_token").IsToggled);
            Assert.Equal("saved", SaveState(session));

            Click(session, "confirm-ok");
            Assert.Equal(false, Node(session, "secret-toggle-api_token").IsToggled);
            Assert.Equal("tok-123", Node(session, "cell-api_token-local").Value);
            Assert.Equal("tok-123", Node(session, "cell-api_token-staging").Value);
            Assert.Equal("tok-123", keyring.Get(_rootFull(app), "api_token"));   // nothing goes before the save

            Save(session);
            Assert.Equal($"base_url={_server.Origin}\ntimeout_ms=30000\napi_token=tok-123\n", EnvText("local"));
            Assert.Equal("base_url=https://staging.example\napi_token=tok-123\n", EnvText("staging"));
            Assert.Equal(ci, EnvText("ci"));
            Assert.Null(keyring.Get(_rootFull(app), "api_token"));   // the file is the one copy now
        });
    }

    [Fact]
    public void UnmarkingAStoredSecretCanForgetTheValueInsteadOnSave()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "api_token", "tok-123");
            GoToEnvironments(session);
            Click(session, "secret-toggle-api_token");
            Assert.Equal("Move into local.env", Node(session, "confirm-ok").Name);
            Click(session, "confirm-alt");

            Assert.Null(session.Window.FindByAutomationId("confirm"));
            Assert.Equal(false, Node(session, "secret-toggle-api_token").IsToggled);
            Assert.True(string.IsNullOrEmpty(Node(session, "cell-api_token-local").Value));
            Assert.Equal("tok-123", keyring.Get(_rootFull(app), "api_token"));

            Save(session);
            Assert.Equal($"base_url={_server.Origin}\ntimeout_ms=30000\napi_token=\n", EnvText("local"));
            Assert.Null(keyring.Get(_rootFull(app), "api_token"));
        });
    }

    [Fact]
    public void AnUnmarkCancelledOrUndoneBeforeTheSaveKeepsTheStoredValue()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "api_token", "tok-123");
            GoToEnvironments(session);

            // Escape: the question goes, nothing else.
            Click(session, "secret-toggle-api_token");
            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);
            Assert.Null(session.Window.FindByAutomationId("confirm"));
            Assert.Equal(true, Node(session, "secret-toggle-api_token").IsToggled);
            Assert.Equal("saved", SaveState(session));

            // Forgotten, then marked again before Ctrl+S: the value stays.
            Click(session, "secret-toggle-api_token");
            Click(session, "confirm-alt");
            Click(session, "secret-toggle-api_token");
            Assert.Equal(true, Node(session, "secret-toggle-api_token").IsToggled);
            Save(session);
            Assert.Contains("secret api_token", EnvText("local"));
            Assert.Equal("tok-123", keyring.Get(_rootFull(app), "api_token"));
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-api_token"));
        });
    }

    /// <summary>A line of a .env file can't hold a line break, so such a value is never offered
    /// the move: forgetting it is the one way to un-mark.</summary>
    [Fact]
    public void AStoredValueWithALineBreakIsNeverMovedIntoTheFile()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "api_token", "line one\nline two");
            GoToEnvironments(session);
            Click(session, "secret-toggle-api_token");

            Assert.Contains("line break", Node(session, "confirm").Description);
            Assert.Equal("Forget value", Node(session, "confirm-ok").Name);
            Assert.Null(session.Window.FindByAutomationId("confirm-alt"));

            Click(session, "confirm-ok");
            Save(session);
            Assert.Equal($"base_url={_server.Origin}\ntimeout_ms=30000\napi_token=\n", EnvText("local"));
            Assert.Null(keyring.Get(_rootFull(app), "api_token"));
        });
    }

    // ── The prompt ───────────────────────────────────────────────────────

    /// <summary>The prompt ("New key", "Secret …", "Import OpenAPI", "Discover an API") is a dialog
    /// named by its title, directly inside its backdrop, so the backdrop is no button: a reader
    /// heard a button named after all of its text holding a second one, and a text change in it
    /// (its title, here, as a tick would write it) could not be patched under a node named from its
    /// content, so each built the window's whole accessibility tree. A click beside the card and
    /// Escape still close it (user-approved 2026-10-27, plan 17 round 6).</summary>
    [Fact]
    public void ThePromptIsADialogNamedByItsTitle_AndATextChangeInItBuildsNoTree()
    {
        Hosted((session, app, keyring) =>
        {
            session.Window.InstallAccessibilityBridge();
            GoToEnvironments(session);
            Click(session, "add-env-key");

            var dialog = Walk(session.Window.AccessibilityTree).Single(n => n.Role == AccessibilityRole.Dialog);
            Assert.Equal("New key", dialog.Name);
            Assert.Contains(Walk(dialog), n => n.AutomationId == "dialog-input");
            Assert.Contains(Walk(dialog), n => n.AutomationId == "dialog-ok");
            Assert.DoesNotContain(Walk(session.Window.AccessibilityTree),
                n => n.Role == AccessibilityRole.Button && n.Name?.Contains("New key") == true);

            // The title, rewritten on screen: patched, no tree.
            TextElement? title = null;
            void Find(Element e)
            {
                if (title is null && e is TextElement t && t.Content == "New key") title = t;
                foreach (var c in e.Children) Find(c);
            }
            Find(session.Window.FindByAutomationId("dialog-input")!.Element.Parent!);
            Assert.NotNull(title);
            int trees = session.Window.AccessibilityTreesBuilt;
            for (int i = 0; i < 6; i++)
            {
                title!.Content = i % 2 == 0 ? "New key (1)" : "New key";
                session.PumpFrames(2);
            }
            session.Window.TypeText("abc");
            session.PumpFrames(2);
            Assert.Equal(0, session.Window.AccessibilityTreesBuilt - trees);

            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);
            Assert.Null(session.Window.FindByAutomationId("dialog-input"));

            Click(session, "add-env-key");
            Assert.NotNull(session.Window.FindByAutomationId("dialog-input"));
            session.Window.Click(20, 880);   // the backdrop, beside the card
            session.PumpFrames(2);
            Assert.Null(session.Window.FindByAutomationId("dialog-input"));
        });
    }

    /// <summary>A submitted prompt builds the window once: the action builds for what it changed and
    /// the prompt goes with that build; the window was built again after it (plan 17 round 7). An
    /// action that builds nothing (no name given) still takes the prompt down.</summary>
    [Fact]
    public void ASubmittedPromptBuildsTheWindowOnce()
    {
        Hosted((session, app, keyring) =>
        {
            GoToEnvironments(session);
            Click(session, "add-env-key");
            session.Window.TypeText("new_key");
            session.PumpFrames(2);
            int builds = app.Builds;
            session.Window.PressKey(KeyCode.Enter);
            Assert.Equal(1, app.Builds - builds);
            session.PumpFrames(2);
            Assert.Null(session.Window.FindByAutomationId("dialog-input"));
            Assert.True(Contains(session, "new_key"), "the new key's row");

            Click(session, "add-env-key");
            builds = app.Builds;
            session.Window.PressKey(KeyCode.Enter);
            Assert.Equal(1, app.Builds - builds);
            session.PumpFrames(2);
            Assert.Null(session.Window.FindByAutomationId("dialog-input"));
        });
    }

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
