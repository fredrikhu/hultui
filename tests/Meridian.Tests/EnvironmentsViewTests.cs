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


    /// <summary>A keyring in a dictionary: one entry per collection, environment (empty for the
    /// shared value) and key. <paramref name="ignoreCase"/> plays Windows, whose Credential Manager
    /// matches names in any letter case.</summary>
    internal sealed class FakeKeyring(bool ignoreCase = false) : IKeyring
    {
        public readonly Dictionary<string, string> Store =
            new(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        /// <summary>The values asked for, found or not.</summary>
        public int Gets;
        private static string Entry(string root, string? env, string key) => $"{root}|{env}|{key}";
        public string? Get(string workspaceRoot, string? environment, string key)
        {
            Gets++;
            return Store.GetValueOrDefault(Entry(workspaceRoot, environment, key));
        }
        public bool Set(string workspaceRoot, string? environment, string key, string value)
        {
            // Remove first: a dictionary that ignores case keeps the old key's letters on a replace,
            // where the store takes the new name.
            Store.Remove(Entry(workspaceRoot, environment, key));
            Store[Entry(workspaceRoot, environment, key)] = value;
            return true;
        }
        public bool Delete(string workspaceRoot, string? environment, string key) =>
            Store.Remove(Entry(workspaceRoot, environment, key));
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

    /// <summary>A character in a value cell builds the view only when it makes its environment
    /// unsaved: the dot in the list and the footer's count. The cell holds the text and nothing else
    /// in the view reads it, and each build read every secret from the keyring again: 312 KB a
    /// character at 3 × 12 keys, 1.2 MB at 5 × 40 (plan 17 round 9).</summary>
    [Fact]
    public void ACellCharacterBuildsTheViewOnlyWhenItMakesTheEnvironmentUnsaved()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "local", "api_token", "tok-local");
            GoToEnvironments(session);
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

            Click(session, "cell-timeout_ms-local");
            Assert.Equal("saved", SaveState(session));
            Assert.Equal(1, Typed("1"));                     // local turns unsaved
            Assert.Equal("1 unsaved", SaveState(session));
            int gets = keyring.Gets;
            Assert.Equal(0, Typed("23"));
            Assert.Equal(0, keyring.Gets - gets);            // no secret read again
            Assert.Equal("cell-timeout_ms-local", Focused());
            Assert.Equal("30000123", Node(session, "cell-timeout_ms-local").Value);

            Click(session, "cell-timeout_ms-staging");
            Assert.Equal(1, Typed("9"));                     // and staging
            Assert.Equal("2 unsaved", SaveState(session));
            Assert.Equal(0, Typed("9"));

            Save(session);
            Assert.Equal("saved", SaveState(session));
            Assert.Contains("timeout_ms=30000123", EnvText("local"));
            Assert.Contains("timeout_ms=3000099", EnvText("staging"));
            Assert.Equal("cell-timeout_ms-staging", Focused());
            Assert.Equal(1, Typed("5"));                     // unsaved again
            Assert.Equal("1 unsaved", SaveState(session));
            Assert.Equal("30000995", Node(session, "cell-timeout_ms-staging").Value);
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
            Assert.NotNull(session.Window.FindByAutomationId("set-secret-client_secret-staging"));
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

            Assert.Equal("s3cr3t", keyring.Get(app.Workspace!.Root, "local", "client_secret"));
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-client_secret-local"));
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

            Assert.NotNull(session.Window.FindByAutomationId("set-secret-api_token-local"));

            Click(session, "set-secret-api_token-local");
            Assert.NotNull(session.Window.FindByAutomationId("dialog-input"));
            session.Window.TypeText("s3cr3t-value");
            session.Window.PressKey(KeyCode.Enter);
            session.PumpFrames(2);

            Assert.Equal("s3cr3t-value", keyring.Get(app.Workspace!.Root, "local", "api_token"));
            // The cell is now the masked keyring cell.
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-api_token-local"));
            Assert.True(Contains(session, "••••••••"));
        });
    }

    [Fact]
    public void RevealShowsTheValueTemporarily()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "local", "api_token", "peekaboo");
            GoToEnvironments(session);

            Assert.False(Contains(session, "peekaboo"), "the value must start masked");
            Click(session, "reveal-api_token-local");

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
            keyring.Set(_rootFull(app), "local", "api_token", "old-value");
            GoToEnvironments(session);

            // A reveal under way ends when the change starts, and the field starts empty.
            Click(session, "reveal-api_token-local");
            Assert.True(Contains(session, "old-value"));
            Menu(session, "secret-actions-api_token-local", "Change…");
            Assert.Equal("Change api_token for local",
                Walk(session.Window.AccessibilityTree).Single(n => n.Role == AccessibilityRole.Dialog).Name);
            Assert.True(string.IsNullOrEmpty(Node(session, "dialog-input").Value));
            Assert.False(Contains(session, "old-value"), "the old value must not show while it is changed");

            // Enter on the empty field is refused, not stored as an empty value.
            session.Window.PressKey(KeyCode.Enter);
            session.PumpFrames(2);
            Assert.Contains("Type the new value", Node(session, "dialog-error").Name);
            Assert.Equal("old-value", keyring.Get(_rootFull(app), "local", "api_token"));

            Submit(session, "new-value");
            Assert.Null(session.Window.FindByAutomationId("dialog-input"));
            Assert.Equal("new-value", keyring.Get(_rootFull(app), "local", "api_token"));
            Assert.Null(keyring.Get(_rootFull(app), null, "api_token"));
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-api_token-local"));
            Assert.False(Contains(session, "new-value"), "the new value stays masked");
            Assert.True(Contains(session, "api_token changed on this machine for local"));

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
            keyring.Set(_rootFull(app), "local", "api_token", "tok-123");
            GoToEnvironments(session);

            Menu(session, "secret-actions-api_token-local", "Forget");
            var confirm = Node(session, "confirm");
            Assert.Equal("Forget api_token for local on this machine?", confirm.Name);
            Assert.Contains("can't bring it back", confirm.Description);
            Assert.Null(session.Window.FindByAutomationId("confirm-alt"));

            // Cancel keeps it.
            Click(session, "confirm-cancel");
            Assert.Equal("tok-123", keyring.Get(_rootFull(app), "local", "api_token"));
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-api_token-local"));

            // Enter forgets: the cell is the fresh-machine one again, and no file changed.
            Menu(session, "secret-actions-api_token-local", "Forget");
            session.Window.PressKey(KeyCode.Enter);
            session.PumpFrames(2);
            Assert.Null(session.Window.FindByAutomationId("confirm"));
            Assert.Null(keyring.Get(_rootFull(app), "local", "api_token"));
            Assert.NotNull(session.Window.FindByAutomationId("set-secret-api_token-local"));
            Assert.Null(session.Window.FindByAutomationId("secret-value-api_token-local"));
            Assert.Equal("saved", SaveState(session));
            Assert.Equal(local, EnvText("local"));
        });
    }

    /// <summary>Forgetting an environment's own value leaves it the shared one. The shared value is
    /// changed or forgotten from any cell that shows it, for every environment that uses it — and
    /// the question names them.</summary>
    [Fact]
    public void ForgettingAnOwnValueFallsBackToTheShared_AndTheSharedOneNamesWhoUsesIt()
    {
        Write("envs/staging.env", $"base_url={_server.Origin}\nsecret api_token\n");
        Hosted((session, app, keyring) =>
        {
            string root = app.Workspace!.Root;
            keyring.Set(root, "local", "api_token", "tok-local");
            keyring.Set(root, null, "api_token", "tok-shared");
            GoToEnvironments(session);

            Menu(session, "secret-actions-api_token-local", "Forget");
            Assert.Contains("local then uses the shared value.", Node(session, "confirm").Description);
            Click(session, "confirm-ok");
            Assert.Null(keyring.Get(root, "local", "api_token"));
            Assert.Equal("shared", Source(session, "api_token", "local"));

            Menu(session, "secret-actions-api_token-staging", "Change shared…");
            Assert.Equal("Change api_token for every environment", Dialog(session).Name);
            Type(session, "tok-shared-2");
            Assert.Equal("tok-shared-2", keyring.Get(root, null, "api_token"));
            Assert.Contains("token=tok-shared-2", Send(session, app));

            session.Window.PressKey(KeyCode.Digit5);
            session.PumpFrames(2);
            Menu(session, "secret-actions-api_token-local", "Forget shared");
            var confirm = Node(session, "confirm");
            Assert.Equal("Forget the shared api_token on this machine?", confirm.Name);
            Assert.Contains("local and staging use it", confirm.Description);
            Click(session, "confirm-ok");
            Assert.Empty(keyring.Store);
            Assert.NotNull(session.Window.FindByAutomationId("set-secret-api_token-local"));
            Assert.NotNull(session.Window.FindByAutomationId("set-secret-api_token-staging"));
        });
    }

    /// <summary>Un-marking a key whose value this machine stores asks first, saying the value would
    /// sit in the file as plain text; moved, every file that marked the key gets the value it used —
    /// local its own, staging the shared one — on Ctrl+S, and the keyring keeps them until then.</summary>
    [Fact]
    public void UnmarkingAStoredSecretAsksThenMovesTheValueIntoTheFilesOnSave()
    {
        Write("envs/staging.env", "base_url=https://staging.example\nsecret api_token\n");
        string ci = EnvText("ci");
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "local", "api_token", "tok-local");
            keyring.Set(_rootFull(app), null, "api_token", "tok-123");
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
            Assert.Equal("tok-local", Node(session, "cell-api_token-local").Value);
            Assert.Equal("tok-123", Node(session, "cell-api_token-staging").Value);
            Assert.Equal("tok-123", keyring.Get(_rootFull(app), null, "api_token"));   // nothing goes before the save

            Save(session);
            Assert.Equal($"base_url={_server.Origin}\ntimeout_ms=30000\napi_token=tok-local\n", EnvText("local"));
            Assert.Equal("base_url=https://staging.example\napi_token=tok-123\n", EnvText("staging"));
            Assert.Equal(ci, EnvText("ci"));
            Assert.Empty(keyring.Store);   // the files are the one copy now
        });
    }

    [Fact]
    public void UnmarkingAStoredSecretCanForgetTheValueInsteadOnSave()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "local", "api_token", "tok-123");
            GoToEnvironments(session);
            Click(session, "secret-toggle-api_token");
            Assert.Equal("Move into local.env", Node(session, "confirm-ok").Name);
            Click(session, "confirm-alt");

            Assert.Null(session.Window.FindByAutomationId("confirm"));
            Assert.Equal(false, Node(session, "secret-toggle-api_token").IsToggled);
            Assert.True(string.IsNullOrEmpty(Node(session, "cell-api_token-local").Value));
            Assert.Equal("tok-123", keyring.Get(_rootFull(app), "local", "api_token"));

            Save(session);
            Assert.Equal($"base_url={_server.Origin}\ntimeout_ms=30000\napi_token=\n", EnvText("local"));
            Assert.Null(keyring.Get(_rootFull(app), "local", "api_token"));
        });
    }

    [Fact]
    public void AnUnmarkCancelledOrUndoneBeforeTheSaveKeepsTheStoredValue()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "local", "api_token", "tok-123");
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
            Assert.Equal("tok-123", keyring.Get(_rootFull(app), "local", "api_token"));
            Assert.NotNull(session.Window.FindByAutomationId("secret-value-api_token-local"));
        });
    }

    /// <summary>A line of a .env file can't hold a line break, so such a value is never offered
    /// the move: forgetting it is the one way to un-mark.</summary>
    [Fact]
    public void AStoredValueWithALineBreakIsNeverMovedIntoTheFile()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "local", "api_token", "line one\nline two");
            GoToEnvironments(session);
            Click(session, "secret-toggle-api_token");

            Assert.Contains("line break", Node(session, "confirm").Description);
            Assert.Equal("Forget value", Node(session, "confirm-ok").Name);
            Assert.Null(session.Window.FindByAutomationId("confirm-alt"));

            Click(session, "confirm-ok");
            Save(session);
            Assert.Equal($"base_url={_server.Origin}\ntimeout_ms=30000\napi_token=\n", EnvText("local"));
            Assert.Null(keyring.Get(_rootFull(app), "local", "api_token"));
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

    // ── Per environment ──────────────────────────────────────────────────
    // A secret's value belongs to one environment: local and staging each hold their own
    // api_token, set in their own column, and a send resolves the active one's.

    private static AccessibilityNode Dialog(HeadlessSession session) =>
        Walk(session.Window.AccessibilityTree).Single(n => n.Role == AccessibilityRole.Dialog);

    private static void Type(HeadlessSession session, string text)
    {
        session.Window.TypeText(text);
        session.PumpFrames(1);
        session.Window.PressKey(KeyCode.Enter);
        session.PumpFrames(2);
    }

    /// <summary>Opens a cell's menu with its ⋯ and picks an entry.</summary>
    private static void Menu(HeadlessSession session, string actionsId, string entry)
    {
        Click(session, actionsId);
        Click(session, "popover-" + entry);
    }

    private static string Source(HeadlessSession session, string key, string env) =>
        Node(session, $"secret-source-{key}-{env}").Name!;

    /// <summary>Sends echo.req in the active environment and returns what the server got.</summary>
    private static string Send(HeadlessSession session, MeridianApp app)
    {
        session.Window.PressKey(KeyCode.Digit1);
        session.PumpFrames(2);
        Click(session, "req-echo");
        var before = app.ActiveTab!.Result;
        Click(session, "send");
        session.PumpUntil(() => app.ActiveTab!.Result is { } r && !ReferenceEquals(r, before), TimeSpan.FromSeconds(10));
        return app.ActiveTab!.Result!.Response!.Body.Text;
    }

    [Fact]
    public void EachEnvironmentSetsItsOwnValue_AndASendResolvesTheActiveOnes()
    {
        Write("envs/staging.env", $"base_url={_server.Origin}\nsecret api_token\n");
        Hosted((session, app, keyring) =>
        {
            string root = app.Workspace!.Root;
            GoToEnvironments(session);
            // A fresh machine: each column asks for its own value.
            Assert.NotNull(session.Window.FindByAutomationId("set-secret-api_token-local"));
            Assert.NotNull(session.Window.FindByAutomationId("set-secret-api_token-staging"));

            Click(session, "set-secret-api_token-local");
            Assert.Equal("Secret api_token for local", Dialog(session).Name);
            Type(session, "tok-local");
            Assert.Equal("keyring", Source(session, "api_token", "local"));
            Assert.NotNull(session.Window.FindByAutomationId("set-secret-api_token-staging"));   // local's alone

            Click(session, "set-secret-api_token-staging");
            Type(session, "tok-staging");
            Assert.Equal("tok-local", keyring.Get(root, "local", "api_token"));
            Assert.Equal("tok-staging", keyring.Get(root, "staging", "api_token"));
            Assert.Null(keyring.Get(root, null, "api_token"));

            // The reveal is the cell's: staging's value shows, local's stays masked.
            Click(session, "reveal-api_token-staging");
            Assert.True(Contains(session, "tok-staging"));
            Assert.False(Contains(session, "tok-local"));

            Assert.Contains("token=tok-local", Send(session, app));
            app.SelectEnvironment("staging");
            session.PumpFrames(2);
            Assert.Contains("token=tok-staging", Send(session, app));
        });
    }

    [Fact]
    public void ASharedValueServesEveryEnvironmentWithoutItsOwn_AndOverrideGivesOneItsOwn()
    {
        Write("envs/staging.env", $"base_url={_server.Origin}\nsecret api_token\n");
        Hosted((session, app, keyring) =>
        {
            string root = app.Workspace!.Root;
            keyring.Set(root, null, "api_token", "tok-shared");   // as every entry was before values were per environment
            GoToEnvironments(session);
            Assert.Equal("shared", Source(session, "api_token", "local"));
            Assert.Equal("shared", Source(session, "api_token", "staging"));
            
            // A shared value has no own value to use everywhere; it can be overridden.
            Click(session, "secret-actions-api_token-staging");
            Assert.Null(session.Window.FindByAutomationId("popover-Use everywhere"));
            Click(session, "popover-Set own value…");
            Assert.Equal("Secret api_token for staging", Dialog(session).Name);
            Type(session, "tok-staging");
            Assert.Equal("keyring", Source(session, "api_token", "staging"));
            Assert.Equal("shared", Source(session, "api_token", "local"));
            Assert.Equal("tok-shared", keyring.Get(root, null, "api_token"));

            Assert.Contains("token=tok-shared", Send(session, app));
            app.SelectEnvironment("staging");
            session.PumpFrames(2);
            Assert.Contains("token=tok-staging", Send(session, app));
        });
    }

    [Fact]
    public void ShareUsesOneValueInEveryEnvironment_AskingFirstWhenOthersHaveTheirOwn()
    {
        Write("envs/staging.env", $"base_url={_server.Origin}\nsecret api_token\n");
        Write("envs/ci.env", "base_url=https://ci.example\nsecret api_token\n");
        Hosted((session, app, keyring) =>
        {
            string root = app.Workspace!.Root;
            GoToEnvironments(session);
            Click(session, "set-secret-api_token-local");
            Type(session, "tok-1");

            // No other environment had a value: no question.
            Menu(session, "secret-actions-api_token-local", "Use everywhere");
            Assert.Null(session.Window.FindByAutomationId("confirm"));
            Assert.Equal("tok-1", keyring.Get(root, null, "api_token"));
            Assert.Null(keyring.Get(root, "local", "api_token"));
            foreach (var env in new[] { "local", "staging", "ci" })
                Assert.Equal("shared", Source(session, "api_token", env));

            Menu(session, "secret-actions-api_token-staging", "Set own value…");
            Type(session, "tok-staging");
            Menu(session, "secret-actions-api_token-local", "Set own value…");
            Type(session, "tok-2");

            Menu(session, "secret-actions-api_token-local", "Use everywhere");
            var dialog = Dialog(session);
            Assert.Equal("Use local's api_token in every environment?", dialog.Name);
            Assert.Contains("staging has a value of its own on this machine, which this forgets.", dialog.Description);
            Assert.Equal("tok-staging", keyring.Get(root, "staging", "api_token"));   // nothing goes before the yes

            Click(session, "confirm-ok");
            Assert.Null(keyring.Get(root, "staging", "api_token"));
            Assert.Null(keyring.Get(root, "local", "api_token"));
            Assert.Equal("tok-2", keyring.Get(root, null, "api_token"));
            Assert.Equal("shared", Source(session, "api_token", "staging"));
        });
    }

    [Fact]
    public void ASecretResolvesFromTheKeyringOnSend()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), null, "api_token", "tok-123");   // shared, as every entry was before values were per environment

            Click(session, "req-echo");
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result != null, TimeSpan.FromSeconds(10));

            // The body template token={{api_token}} reached the server resolved.
            Assert.Contains("token=tok-123", app.ActiveTab!.Result!.Response!.Body.Text);
        });
    }

    // ── The Request tab: what went out ───────────────────────────────────

    /// <summary>The editor shows {{api_token}}; the Request tab shows what the wire carried —
    /// with the secret's value masked wherever it lands until it is revealed.</summary>
    [Fact]
    public void TheRequestTabShowsWhatWentOut_WithASecretMaskedUntilRevealed()
    {
        Hosted((session, app, keyring) =>
        {
            keyring.Set(_rootFull(app), "local", "api_token", "tok-12345");
            Click(session, "req-echo");
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result != null, TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            Click(session, "resptab-request");

            Assert.True(Contains(session, $"{_server.Origin}/echo"), "the request line");
            Assert.True(Contains(session, new Uri(_server.Origin).Authority), "the Host header");
            Assert.True(Contains(session, "token=••••••••"), "the body, its secret masked");
            Assert.False(Contains(session, "tok-12345"));

            Click(session, "sent-reveal");
            Assert.True(Contains(session, "token=tok-12345"), "reveal shows the value");
        });
    }

    [Fact]
    public void TheRequestTabMasksAnAuthorizationHeaderButKeepsItsScheme()
    {
        Write("authz.req", "GET {{base_url}}/ok\nheader Authorization: Bearer abc.def.ghi\n");
        Hosted((session, app, keyring) =>
        {
            Click(session, "req-authz");
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result != null, TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            Click(session, "resptab-request");

            // The editor shows the file's own line; the Request tab masks what went out.
            bool InPanel(string text) => Walk(Node(session, "response-right")).Any(n =>
                n.Name?.Contains(text, StringComparison.Ordinal) == true);
            Assert.True(InPanel("Bearer ••••••••"));
            Assert.False(InPanel("abc.def.ghi"));
            Assert.True(InPanel("no body"));

            Click(session, "sent-reveal");
            Assert.True(InPanel("Bearer abc.def.ghi"));
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
            keyring.Set(_rootFull(app), "local", "api_token", "from-keyring");
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result != null, TimeSpan.FromSeconds(10));

            Assert.Contains("token=from-keyring", app.ActiveTab!.Result!.Response!.Body.Text);
            Assert.DoesNotContain("LEAKED", app.ActiveTab.Result.Response.Body.Text);
        });
    }
}
