using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

/// <summary>Rename and delete for requests, folders and environments, made in the app so no file
/// needs moving or removing by hand — hosted headless over a real temp collection, with a bin
/// that keeps what it is given in a folder of its own (the tests never fill the real one).</summary>
public class FileManagementTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-files-").FullName;
    private readonly string _bin = Directory.CreateTempSubdirectory("meridian-bin-").FullName;

    public FileManagementTests()
    {
        Write("orders/_folder.req", "auth bearer {{token}}\n");
        Write("orders/get-charge.req", "GET {{base_url}}/charges/1\n");
        Write("orders/list-charges.req", "GET {{base_url}}/charges\n");
        Write("orders/notes.txt", "not a request\n");
        Write("health.req", "GET {{base_url}}/health\n");
        Write("envs/local.env", "base_url=http://localhost:1\ntimeout_ms=30000\nsecret api_token\n");
        Write("envs/staging.env", "base_url=https://staging.example\ntimeout_ms=30000\n");
        Write("envs/ci.env", "base_url=https://ci.example\n");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_bin, recursive: true);
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string P(string relative) => Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A bin in a temp folder. <see cref="Name"/> null plays a platform without one;
    /// <see cref="Refuse"/> a path the bin won't take (another file system, a share).</summary>
    private sealed class FakeTrash(string dir) : IFileTrash
    {
        public string? Name { get; set; } = "Recycle Bin";
        public string? Refuse { get; set; }
        public readonly List<string> Binned = [];

        public string? Delete(string path)
        {
            if (Refuse != null) return Refuse;
            if (Name == null) return FileTrash.DeletePermanently(path);
            string target = Path.Combine(dir, Binned.Count + "-" + Path.GetFileName(path));
            if (Directory.Exists(path)) Directory.Move(path, target);
            else File.Move(path, target);
            Binned.Add(path);
            return null;
        }
    }

    private sealed class FakeKeyring : IKeyring
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

    private void Hosted(Action<HeadlessSession, MeridianApp, FakeTrash> body, FakeKeyring? keyring = null)
    {
        using var session = HeadlessSession.Create(1440, 900);
        var trash = new FakeTrash(_bin);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window, keyring: keyring ?? new FakeKeyring(), trash: trash);
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrame();
            body(session, app, trash);
        }
        finally
        {
            app?.Dispose();
        }
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    private static bool Contains(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true
            || n.Value?.Contains(text, StringComparison.Ordinal) == true);

    private static AccessibilityNode Node(HeadlessSession session, string automationId) =>
        Walk(session.Window.AccessibilityTree).Single(n => n.AutomationId == automationId);

    private static void Click(HeadlessSession session, string automationId)
    {
        var el = session.Window.FindByAutomationId(automationId);
        Assert.True(el != null, $"#{automationId} not found");
        el!.Click();
        session.PumpFrames(2);
    }

    private static void Key(HeadlessSession session, KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
    {
        session.Window.PressKey(key, modifiers);
        session.PumpFrames(2);
    }

    /// <summary>Opens a row's menu with its ⋯ and picks an entry.</summary>
    private static void Menu(HeadlessSession session, string actionsId, string entry)
    {
        Click(session, actionsId);
        Click(session, "popover-" + entry);
    }

    private static void Submit(HeadlessSession session, string text)
    {
        session.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);
        session.Window.PressKey(KeyCode.Backspace);
        session.Window.TypeText(text);
        session.PumpFrames(1);
        Key(session, KeyCode.Enter);
    }

    private static AccessibilityNode Dialog(HeadlessSession session) =>
        Walk(session.Window.AccessibilityTree).Single(n => n.Role == AccessibilityRole.Dialog);

    private string EnvText(string name) => File.ReadAllText(P($"envs/{name}.env"));

    private string[] EnvNames() =>
        Directory.GetFiles(P("envs")).Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal).ToArray()!;

    // ── Environments ─────────────────────────────────────────────────────

    [Fact]
    public void RenamingAnEnvironmentMovesItsFileAndKeepsItActive_WithItsUnsavedEdits()
    {
        Hosted((session, app, trash) =>
        {
            Key(session, KeyCode.Digit5);
            Assert.Equal("local", app.ActiveEnvName);
            session.Window.FindByAutomationId("cell-timeout_ms-local")!.Click();
            session.PumpFrames(1);
            session.Window.TypeText("0");
            session.PumpFrames(2);
            Assert.True(Contains(session, "1 unsaved"));

            Menu(session, "env-actions-local", "Rename…");
            Assert.Equal("Rename local.env", Dialog(session).Name);
            Assert.Equal("local", Node(session, "dialog-input").Value);
            // The name starts selected: typing replaces it.
            session.Window.TypeText("dev");
            session.PumpFrames(1);
            Key(session, KeyCode.Enter);

            Assert.Null(session.Window.FindByAutomationId("dialog-input"));
            Assert.Equal(["ci", "dev", "staging"], EnvNames());
            Assert.Equal("dev", app.ActiveEnvName);
            Assert.True(Contains(session, "dev, active"));
            Assert.NotNull(session.Window.FindByAutomationId("env-file-dev"));
            Assert.Null(session.Window.FindByAutomationId("env-file-local"));
            // The edit came along, unsaved, and saves into the new file.
            Assert.True(Contains(session, "1 unsaved"));
            Key(session, KeyCode.S, KeyModifiers.Ctrl);
            Assert.Equal("base_url=http://localhost:1\ntimeout_ms=300000\nsecret api_token\n", EnvText("dev"));
            Assert.Empty(trash.Binned);
        });
    }

    [Fact]
    public void ARenameRefusesATakenOrBadName_AndMayChangeOnlyTheCase()
    {
        Hosted((session, app, trash) =>
        {
            Key(session, KeyCode.Digit5);
            Menu(session, "env-actions-staging", "Rename…");
            foreach (var (bad, reason) in new[]
            {
                ("CI", "ci.env already exists"),
                ("", "Give the environment a name"),
                ("con", "Windows keeps CON"),
                ("prod.", "can't end with a dot"),
                ("../up", "can't contain"),
            })
            {
                Submit(session, bad);
                Assert.True(session.Window.FindByAutomationId("dialog-input") != null, $"'{bad}' closed the prompt");
                Assert.Contains(reason, Node(session, "dialog-error").Name);
            }
            Assert.Equal(["ci", "local", "staging"], EnvNames());

            Submit(session, "Staging");
            Assert.Null(session.Window.FindByAutomationId("dialog-input"));
            Assert.Equal(["Staging", "ci", "local"], EnvNames());
            Assert.NotNull(session.Window.FindByAutomationId("env-file-Staging"));
        });
    }

    [Fact]
    public void DeletingTheActiveEnvironmentAsksNamingTheFile_AndLocalBecomesActive()
    {
        Hosted((session, app, trash) =>
        {
            Key(session, KeyCode.Digit5);
            app.SelectEnvironment("staging");
            session.PumpFrames(2);

            // Right-click on the row opens the same menu as its ⋯.
            session.Window.FindByAutomationId("env-file-staging")!.RightClick();
            session.PumpFrames(2);
            Click(session, "popover-Delete…");

            var dialog = Dialog(session);
            Assert.Equal("Delete staging.env?", dialog.Name);
            Assert.Contains("envs/staging.env moves to the Recycle Bin, where it can be restored.", dialog.Description);
            Assert.Contains("local becomes the active environment.", dialog.Description);
            Assert.Equal("Delete", Node(session, "confirm-ok").Name);
            Assert.True(session.Window.FindByAutomationId("confirm-ok")!.Element.IsFocused, "the confirming button takes the keyboard");
            Assert.True(File.Exists(P("envs/staging.env")), "nothing goes before the confirmation");

            Click(session, "confirm-ok");
            Assert.Null(session.Window.FindByAutomationId("confirm"));
            Assert.Equal([P("envs/staging.env")], trash.Binned);
            Assert.Equal(["ci", "local"], EnvNames());
            Assert.Equal("local", app.ActiveEnvName);
            Assert.Null(session.Window.FindByAutomationId("env-file-staging"));
            Assert.True(Contains(session, "staging.env moved to the Recycle Bin"));

            // With local gone too, the first that is left: ci.
            Menu(session, "env-actions-local", "Delete…");
            Assert.Contains("ci becomes the active environment.", Dialog(session).Description);
            Key(session, KeyCode.Enter);
            Assert.Equal(["ci"], EnvNames());
            Assert.Equal("ci", app.ActiveEnvName);
        });
    }

    [Fact]
    public void DeleteOnTheEnvironmentsViewAsksAboutTheActiveOne_AndEscapeKeepsIt()
    {
        Hosted((session, app, trash) =>
        {
            Key(session, KeyCode.Digit5);
            Key(session, KeyCode.Delete);
            Assert.Equal("Delete local.env?", Dialog(session).Name);
            Key(session, KeyCode.Escape);
            Assert.Null(session.Window.FindByAutomationId("confirm"));
            Assert.Empty(trash.Binned);
            Assert.Equal(["ci", "local", "staging"], EnvNames());

            // F2 is rename, for the same environment.
            Key(session, KeyCode.F2);
            Assert.Equal("Rename local.env", Dialog(session).Name);
        });
    }

    [Fact]
    public void WithNoBinTheConfirmationSaysTheDeleteIsPermanent()
    {
        Hosted((session, app, trash) =>
        {
            trash.Name = null;
            Key(session, KeyCode.Digit5);
            Menu(session, "env-actions-ci", "Delete…");

            Assert.Contains("envs/ci.env is deleted permanently. This can't be undone.", Dialog(session).Description);
            Assert.Equal("Delete permanently", Node(session, "confirm-ok").Name);
            Click(session, "confirm-ok");
            Assert.Equal(["local", "staging"], EnvNames());
        });
    }

    [Fact]
    public void WhenTheBinRefusesAPathASecondConfirmationOffersToDeleteItForGood()
    {
        Hosted((session, app, trash) =>
        {
            trash.Refuse = "Invalid cross-device link.";
            Key(session, KeyCode.Digit5);
            Menu(session, "env-actions-ci", "Delete…");
            Click(session, "confirm-ok");

            // Still there, and the app asks again, saying why and what the second yes does.
            Assert.Equal(["ci", "local", "staging"], EnvNames());
            var dialog = Dialog(session);
            Assert.Equal("Delete ci.env permanently?", dialog.Name);
            Assert.Contains("could not go to the Recycle Bin: Invalid cross-device link.", dialog.Description);
            Assert.Contains("This can't be undone.", dialog.Description);

            Click(session, "confirm-ok");
            Assert.Equal(["local", "staging"], EnvNames());
        });
    }

    [Fact]
    public void RemovingASecretKeyForgetsItsKeyringValueOnSave()
    {
        var keyring = new FakeKeyring();
        Hosted((session, app, trash) =>
        {
            string root = app.Workspace!.Root;
            keyring.Set(root, "api_token", "tok-123");
            keyring.Set(root, "timeout_ms", "kept");   // no secret flag: not the app's to forget
            Key(session, KeyCode.Digit5);

            Click(session, "remove-key-api_token");
            Click(session, "remove-key-timeout_ms");
            Assert.Equal("tok-123", keyring.Get(root, "api_token"));   // nothing goes before the save

            Key(session, KeyCode.S, KeyModifiers.Ctrl);
            Assert.Equal("base_url=http://localhost:1\n", EnvText("local"));
            Assert.Null(keyring.Get(root, "api_token"));
            Assert.Equal("kept", keyring.Get(root, "timeout_ms"));
        }, keyring);
    }

    // ── Requests ─────────────────────────────────────────────────────────

    [Fact]
    public void RenamingARequestMovesItsFile_AndItsOpenTabFollowsWithItsEdits()
    {
        Hosted((session, app, trash) =>
        {
            Click(session, "req-get-charge");
            Click(session, "subtab-headers");
            Click(session, "add-header");
            Assert.True(app.ActiveTab!.Dirty);

            session.Window.FindByAutomationId("req-get-charge")!.RightClick();
            session.PumpFrames(2);
            Click(session, "popover-Rename…");
            Assert.Equal("Rename get-charge.req", Dialog(session).Name);
            Submit(session, "fetch-charge.req");

            Assert.False(File.Exists(P("orders/get-charge.req")));
            Assert.True(File.Exists(P("orders/fetch-charge.req")));
            Assert.Equal(P("orders/fetch-charge.req"), app.ActiveTab!.Path);
            Assert.True(app.ActiveTab.Dirty, "the unsaved edit came along");
            Assert.NotNull(session.Window.FindByAutomationId("req-fetch-charge"));
            Assert.Null(session.Window.FindByAutomationId("req-get-charge"));
            Assert.True(Contains(session, "orders/fetch-charge.req"));   // the editor's footer

            Key(session, KeyCode.S, KeyModifiers.Ctrl);
            Assert.Contains("Name: value", File.ReadAllText(P("orders/fetch-charge.req")));
            Assert.False(File.Exists(P("orders/get-charge.req")), "the save went to the new name");
        });
    }

    [Fact]
    public void ARequestRenameRefusesTheFolderDefaultsATakenNameAndAPath()
    {
        Hosted((session, app, trash) =>
        {
            Menu(session, "req-actions-get-charge", "Rename…");
            foreach (var (bad, reason) in new[]
            {
                ("_folder", "holds a folder's defaults"),
                ("List-Charges", "list-charges.req already exists here"),
                ("sub/x", "can't contain"),
                (".hidden", "can't start with a dot"),
            })
            {
                Submit(session, bad);
                Assert.True(session.Window.FindByAutomationId("dialog-input") != null, $"'{bad}' closed the prompt");
                Assert.Contains(reason, Node(session, "dialog-error").Name);
            }
            Key(session, KeyCode.Escape);
            Assert.True(File.Exists(P("orders/get-charge.req")));
        });
    }

    [Fact]
    public void DeletingARequestOpenWithUnsavedEditsWarns_AndClosesItsTab()
    {
        Hosted((session, app, trash) =>
        {
            Click(session, "req-health");
            Click(session, "req-get-charge");
            Click(session, "subtab-headers");
            Click(session, "add-header");

            Menu(session, "req-actions-get-charge", "Delete…");
            var dialog = Dialog(session);
            Assert.Equal("Delete get-charge.req?", dialog.Name);
            Assert.Contains("orders/get-charge.req moves to the Recycle Bin", dialog.Description);
            Assert.Contains("It is open with unsaved changes, which go with it.", dialog.Description);
            Assert.True(Contains(session, "unsaved changes, which go with it"));   // on screen too

            Click(session, "confirm-ok");
            Assert.Equal([P("orders/get-charge.req")], trash.Binned);
            Assert.Equal(1, app.OpenTabCount);
            Assert.Equal(P("health.req"), app.ActiveTab!.Path);
            Assert.Null(session.Window.FindByAutomationId("req-get-charge"));
        });
    }

    [Fact]
    public void DeleteInTheRequestsViewAsksAboutTheOpenRequest_AndEnterConfirms()
    {
        Hosted((session, app, trash) =>
        {
            Click(session, "req-health");
            Key(session, KeyCode.Delete);
            Assert.Contains("health.req moves to the Recycle Bin", Dialog(session).Description);
            Assert.Contains("Its tab closes.", Dialog(session).Description);

            Key(session, KeyCode.Enter);
            Assert.Equal([P("health.req")], trash.Binned);
            Assert.Equal(0, app.OpenTabCount);
            Assert.True(Contains(session, "no request open"));
        });
    }

    [Fact]
    public void ThePaletteRenamesAndDeletesTheOpenRequest()
    {
        Hosted((session, app, trash) =>
        {
            Click(session, "req-health");
            Key(session, KeyCode.P, KeyModifiers.Ctrl);
            session.Window.TypeText("rename request");
            session.PumpFrames(3);
            Key(session, KeyCode.Enter);
            Assert.False(app.PaletteOpen);
            Assert.Equal("Rename health.req", Dialog(session).Name);
            session.Window.TypeText("status");   // the name is selected: typing replaces it
            session.PumpFrames(1);
            Key(session, KeyCode.Enter);
            Assert.True(File.Exists(P("status.req")));
            Assert.Equal(P("status.req"), app.ActiveTab!.Path);

            Key(session, KeyCode.P, KeyModifiers.Ctrl);
            session.Window.TypeText("delete request");
            session.PumpFrames(3);
            Key(session, KeyCode.Enter);
            Assert.Equal("Delete status.req?", Dialog(session).Name);
            Click(session, "confirm-ok");
            Assert.Equal([P("status.req")], trash.Binned);
        });
    }

    // ── Folders ──────────────────────────────────────────────────────────

    [Fact]
    public void RenamingAFolderTakesItsDefaultsAndOpenTabsAlong()
    {
        Hosted((session, app, trash) =>
        {
            Click(session, "req-get-charge");
            Click(session, "subtab-auth");
            Assert.True(Contains(session, "Auth inherited"));

            Menu(session, "folder-actions-orders", "Rename…");
            Assert.Equal("Rename orders/", Dialog(session).Name);
            Submit(session, "billing");

            Assert.False(Directory.Exists(P("orders")));
            foreach (var file in new[] { "_folder.req", "get-charge.req", "list-charges.req", "notes.txt" })
                Assert.True(File.Exists(P("billing/" + file)), file);
            Assert.Equal(P("billing/get-charge.req"), app.ActiveTab!.Path);
            Assert.NotNull(session.Window.FindByAutomationId("folder-billing"));
            Assert.True(Contains(session, "Auth inherited"), "the folder's defaults still apply");
        });
    }

    [Fact]
    public void DeletingAFolderNamesEverythingThatGoes_AndClosesTheTabsUnderIt()
    {
        Hosted((session, app, trash) =>
        {
            Click(session, "req-get-charge");
            Click(session, "req-list-charges");
            Click(session, "req-health");

            session.Window.FindByAutomationId("folder-orders")!.RightClick();
            session.PumpFrames(2);
            Click(session, "popover-Delete…");
            var dialog = Dialog(session);
            Assert.Equal("Delete orders/?", dialog.Name);
            // _folder.req and notes.txt are the other files.
            Assert.Contains("orders/ and the 2 requests in it (and 2 other files) move to the Recycle Bin, where they can be restored.",
                dialog.Description);
            Assert.Contains("Their open tabs close.", dialog.Description);

            Click(session, "confirm-ok");
            Assert.Equal([P("orders")], trash.Binned);
            Assert.False(Directory.Exists(P("orders")));
            Assert.Equal(1, app.OpenTabCount);
            Assert.Equal(P("health.req"), app.ActiveTab!.Path);
            Assert.Null(session.Window.FindByAutomationId("folder-orders"));
        });
    }

    [Fact]
    public void TheRowActionsAreButtonsNamedForTheirRow()
    {
        Hosted((session, app, trash) =>
        {
            var request = Node(session, "req-actions-get-charge");
            Assert.Equal(AccessibilityRole.Button, request.Role);
            Assert.Equal("actions for Get charge", request.Name);
            Assert.Equal("actions for folder orders", Node(session, "folder-actions-orders").Name);

            // The ⋯ opens the menu without opening the request behind it.
            Click(session, "req-actions-get-charge");
            Assert.Equal(0, app.OpenTabCount);
            Assert.Equal(AccessibilityRole.MenuItem, Node(session, "popover-Rename…").Role);
            Assert.Equal(AccessibilityRole.MenuItem, Node(session, "popover-Delete…").Role);
            session.Window.Click(20, 880);   // beside the menu: it closes
            session.PumpFrames(2);
            Assert.Null(session.Window.FindByAutomationId("popover-Rename…"));

            Key(session, KeyCode.Digit5);
            Assert.Equal("actions for local.env", Node(session, "env-actions-local").Name);
        });
    }
}
