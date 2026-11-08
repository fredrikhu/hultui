using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Meridian.Tests;

/// <summary>A collection, its requests and its folders, made in the app — hosted headless over
/// real temp folders. Nothing here writes a .req or .env file by hand except the collection the
/// tree tests start from.</summary>
public class CreateInAppTests : IDisposable
{
    private readonly string _parent = Directory.CreateTempSubdirectory("meridian-create-").FullName;
    private string Collection => Path.Combine(_parent, "shop");

    public void Dispose() => Directory.Delete(_parent, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(Collection, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <param name="open">The collection to open first, or null for the front door.</param>
    private static void Hosted(Action<HeadlessSession, MeridianApp> body, string? open = null, string? pick = null)
    {
        using var session = HeadlessSession.Create(1440, 900);
        if (pick != null) session.Window.FileDialogs.EnqueueResult(pick);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(app.Mount());
            if (open != null) app.OpenWorkspace(open);
            session.PumpFrame();
            body(session, app);
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
        Walk(session.Window.AccessibilityTree).Any(n => n.Name?.Contains(text, StringComparison.Ordinal) == true);

    private static AccessibilityNode Node(HeadlessSession session, string automationId) =>
        Walk(session.Window.AccessibilityTree).Single(n => n.AutomationId == automationId);

    /// <summary>The open prompt's title, or null when none is up.</summary>
    private static string? Prompt(HeadlessSession session) =>
        Walk(session.Window.AccessibilityTree).SingleOrDefault(n => n.Role == AccessibilityRole.Dialog)?.Name;

    private static void Click(HeadlessSession session, string automationId)
    {
        var el = session.Window.FindByAutomationId(automationId);
        Assert.True(el != null, $"#{automationId} not found");
        el!.Click();
        session.PumpFrames(2);
    }

    private static void Submit(HeadlessSession session, string text)
    {
        session.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);   // clear what an earlier try left
        session.Window.PressKey(KeyCode.Backspace);
        session.Window.TypeText(text);
        session.PumpFrames(1);
        session.Window.PressKey(KeyCode.Enter);
        session.PumpFrames(2);
    }

    /// <summary>Each bad name keeps the prompt up, with the reason under the field and on it.</summary>
    private static void AssertRefused(HeadlessSession session, params (string Bad, string Reason)[] cases)
    {
        string? title = Prompt(session);
        foreach (var (bad, reason) in cases)
        {
            Submit(session, bad);
            Assert.True(Prompt(session) == title, $"'{bad}' closed the prompt");
            Assert.Contains(reason, Node(session, "dialog-error").Name);
            Assert.Contains(reason, Node(session, "dialog-input").Description);
        }
    }

    // ── New collection ───────────────────────────────────────────────────

    [Fact]
    public void TheNewCollectionCardMakesTheFolderItsFirstRequestAndItsEnvironment()
    {
        Hosted((session, app) =>
        {
            Assert.False(Contains(session, "not available yet"));
            Click(session, "door-new-collection");
            session.PumpUntil(() => Prompt(session) != null, TimeSpan.FromSeconds(10));
            Assert.Equal($"New collection in {_parent}", Prompt(session));

            Submit(session, " shop ");
            Assert.Equal("Base URL for shop", Prompt(session));
            Assert.False(Directory.Exists(Collection), "nothing is written before the last prompt");
            Submit(session, "https://api.example.com/");

            Assert.Null(Prompt(session));
            Assert.Equal("GET {{base_url}}/\n", File.ReadAllText(Path.Combine(Collection, "first-request.req")));
            Assert.Equal("base_url=https://api.example.com\n", File.ReadAllText(Path.Combine(Collection, "envs", "local.env")));

            // Opened, local active, the first request open in the editor with its base URL resolved.
            Assert.Equal(Collection, app.Workspace!.Root);
            Assert.Equal("local", app.ActiveEnvName);
            Assert.NotNull(session.Window.FindByAutomationId("req-first-request"));
            Assert.EndsWith("first-request.req", app.ActiveTab!.Path);
            Assert.True(Contains(session, "variable base_url"));
            Assert.False(Contains(session, "variable base_url, unresolved"));
        }, pick: _parent);
    }

    [Fact]
    public void ABlankBaseUrlIsSetLaterAndABadOneIsRefused()
    {
        Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.N, KeyModifiers.Ctrl);   // the card's key
            session.PumpUntil(() => Prompt(session) != null, TimeSpan.FromSeconds(10));
            Submit(session, "shop");

            AssertRefused(session,
                ("localhost:8080", "starts with http:// or https://"),
                ("ftp://files.example", "starts with http:// or https://"));
            Assert.False(Directory.Exists(Collection));

            Submit(session, "");
            Assert.Null(Prompt(session));
            Assert.Equal("base_url=\n", File.ReadAllText(Path.Combine(Collection, "envs", "local.env")));
            // The key waits in the Environments table for its value.
            session.Window.PressKey(KeyCode.Digit5);
            session.PumpFrames(2);
            Assert.NotNull(session.Window.FindByAutomationId("cell-base_url-local"));
        }, pick: _parent);
    }

    [Fact]
    public void ABadCollectionNameIsRefusedAndACancelWritesNothing()
    {
        Directory.CreateDirectory(Path.Combine(_parent, "Taken"));
        Hosted((session, app) =>
        {
            Click(session, "door-new-collection");
            session.PumpUntil(() => Prompt(session) != null, TimeSpan.FromSeconds(10));

            AssertRefused(session,
                ("", "Give the collection a name"),
                ("taken", "Taken already exists there"),
                ("a/b", "can't contain"),
                ("..", "can't contain"),
                (".hidden", "can't start with a dot"),
                ("shop.", "can't end with a dot"),
                ("con", "for a device; pick another name"),
                ("a|b", "can't contain"));

            // A good name, then Escape at the base URL: still nothing on disk.
            Submit(session, "shop");
            Assert.Equal("Base URL for shop", Prompt(session));
            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);

            Assert.Null(Prompt(session));
            Assert.Null(app.Workspace);
            Assert.Equal(["Taken"], Directory.GetFileSystemEntries(_parent).Select(Path.GetFileName));
        }, pick: _parent);
    }

    [Fact]
    public void ADeclinedPickerAsksNothing()
    {
        Hosted((session, app) =>
        {
            Click(session, "door-new-collection");
            session.PumpFrames(5);
            Assert.Null(Prompt(session));
            Assert.Null(app.Workspace);
        });
    }

    // ── New request ──────────────────────────────────────────────────────

    private void Shop()
    {
        Write("users/list-users.req", "GET {{base_url}}/users\n");
        Write("envs/local.env", "base_url=https://api.example.com\n");
    }

    [Fact]
    public void NewRequestAtTheRootWritesTheFileAndOpensItsUrlForTyping()
    {
        Shop();
        Hosted((session, app) =>
        {
            Assert.Equal("New request", Node(session, "tree-new-request").Name);
            Click(session, "tree-new-request");
            Assert.Equal("New request in shop/", Prompt(session));
            Submit(session, "login");

            Assert.Null(Prompt(session));
            var path = Path.Combine(Collection, "login.req");
            Assert.Equal("GET {{base_url}}/\n", File.ReadAllText(path));
            Assert.NotNull(session.Window.FindByAutomationId("req-login"));
            Assert.Equal(path, app.ActiveTab!.Path);

            // The URL box is open and has the keyboard: what is typed lands in the request.
            var url = session.Window.FindByAutomationId("url-input");
            Assert.NotNull(url);
            Assert.Same(url!.Element, session.Window.Window.FocusedElement);
            session.Window.PressKey(KeyCode.End);
            session.Window.TypeText("auth/login");
            session.PumpFrames(2);
            Assert.Equal("{{base_url}}/auth/login", app.ActiveTab.Doc.Url);

            session.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Assert.Equal("GET {{base_url}}/auth/login\n", File.ReadAllText(path));
        }, open: Collection);
    }

    [Fact]
    public void NewRequestInAFolderLandsInThatFolder()
    {
        Shop();
        Hosted((session, app) =>
        {
            Assert.Equal("New request in users", Node(session, "tree-new-request-users").Name);
            Click(session, "tree-new-request-users");
            Assert.Equal("New request in shop/users/", Prompt(session));
            Submit(session, "get-user.req");   // the suffix is optional

            Assert.True(File.Exists(Path.Combine(Collection, "users", "get-user.req")));
            Assert.False(File.Exists(Path.Combine(Collection, "users", "get-user.req.req")));
            Assert.NotNull(session.Window.FindByAutomationId("req-get-user"));
            Assert.EndsWith(Path.Combine("users", "get-user.req"), app.ActiveTab!.Path);
        }, open: Collection);
    }

    [Fact]
    public void ABadRequestNameIsRefusedInThePrompt()
    {
        Shop();
        Hosted((session, app) =>
        {
            Click(session, "tree-new-request-users");
            AssertRefused(session,
                ("", "Give the request a name"),
                ("  .req ", "Give the request a name"),
                ("List-Users", "list-users.req already exists"),
                ("../escape", "can't contain"),
                ("sub\\dir", "can't contain"),
                ("_folder", "holds a folder's defaults"),
                ("nul", "for a device; pick another name"),
                ("what?", "can't contain"));
            Assert.Equal(["list-users.req"], Directory.GetFileSystemEntries(Path.Combine(Collection, "users")).Select(Path.GetFileName));
            Assert.False(File.Exists(Path.Combine(Collection, "escape.req")));
        }, open: Collection);
    }

    [Fact]
    public void AnEmptyCollectionOffersItsFirstRequest()
    {
        Directory.CreateDirectory(Collection);
        Hosted((session, app) =>
        {
            Assert.True(Contains(session, "no requests yet"));
            Click(session, "tree-first-request");
            Assert.Equal("New request in shop/", Prompt(session));
            Submit(session, "ping");

            Assert.True(File.Exists(Path.Combine(Collection, "ping.req")));
            Assert.False(Contains(session, "no requests yet"));
            Assert.NotNull(session.Window.FindByAutomationId("req-ping"));
        }, open: Collection);
    }

    // ── New folder ───────────────────────────────────────────────────────

    [Fact]
    public void NewFolderShowsBeforeItsFirstRequest()
    {
        Shop();
        Hosted((session, app) =>
        {
            Click(session, "tree-new-folder");
            Assert.Equal("New folder in shop/", Prompt(session));
            Submit(session, "admin");

            Assert.Null(Prompt(session));
            Assert.True(File.Exists(Path.Combine(Collection, "admin", "_folder.req")));
            Assert.NotNull(session.Window.FindByAutomationId("tree-empty-admin"));
            Assert.Equal("New folder in admin", Node(session, "tree-new-folder-admin").Name);

            // Its own ＋ request fills it.
            Click(session, "tree-new-request-admin");
            Assert.Equal("New request in shop/admin/", Prompt(session));
            Submit(session, "audit");
            Assert.True(File.Exists(Path.Combine(Collection, "admin", "audit.req")));
            Assert.Null(session.Window.FindByAutomationId("tree-empty-admin"));
            Assert.NotNull(session.Window.FindByAutomationId("req-audit"));
        }, open: Collection);
    }

    [Fact]
    public void NewFolderInAFolderNests()
    {
        Shop();
        Hosted((session, app) =>
        {
            Click(session, "tree-new-folder-users");
            Assert.Equal("New folder in shop/users/", Prompt(session));
            Submit(session, "roles");

            Assert.True(File.Exists(Path.Combine(Collection, "users", "roles", "_folder.req")));
            Assert.Equal("New request in users/roles", Node(session, "tree-new-request-users/roles").Name);
            Assert.NotNull(session.Window.FindByAutomationId("req-list-users"));   // the folder's request stays
        }, open: Collection);
    }

    [Fact]
    public void ABadFolderNameIsRefusedInThePrompt()
    {
        Shop();
        Hosted((session, app) =>
        {
            Click(session, "tree-new-folder");
            AssertRefused(session,
                ("", "Give the folder a name"),
                ("USERS", "users already exists"),
                ("Envs", "envs/ holds the environments"),
                (".git", "can't start with a dot"),
                ("a/b", "can't contain"),
                ("lpt1", "for a device; pick another name"));
            Assert.Equal(["envs", "users"],
                Directory.GetFileSystemEntries(Collection).Select(Path.GetFileName).Order(StringComparer.Ordinal));

            // envs is refused only at the root: below it, it is an ordinary name.
            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);
            Click(session, "tree-new-folder-users");
            Submit(session, "envs");
            Assert.True(Directory.Exists(Path.Combine(Collection, "users", "envs")));
        }, open: Collection);
    }

    // ── The palette ──────────────────────────────────────────────────────

    [Fact]
    public void ThePaletteOffersEachNewThingWhereItCanWork()
    {
        Hosted((session, app) =>
        {
            var titles = app.Commands().Select(c => c.Title).ToList();
            Assert.Contains("New collection…", titles);
            Assert.DoesNotContain("New request…", titles);   // no collection to put it in
        });

        Shop();
        Hosted((session, app) =>
        {
            var titles = app.Commands().Select(c => c.Title).ToList();
            Assert.Contains("New request…", titles);
            Assert.Contains("New folder…", titles);

            app.Commands().Single(c => c.Title == "New folder…").Run();
            session.PumpFrames(2);
            Assert.Equal("New folder in shop/", Prompt(session));
        }, open: Collection);
    }
}
