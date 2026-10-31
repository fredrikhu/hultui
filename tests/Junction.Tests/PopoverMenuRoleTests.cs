using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Junction's left-click dropdowns (<c>_popover</c>, drawn by <c>BuildPopover</c>) are menus to
/// assistive technology: role Menu, items MenuItem, the entry under the pointer selected. Before,
/// the layer and the card were two buttons named after every row, with a button for each row in
/// them. Escape and a click outside it close it as they did, and a disabled row still runs nothing.
/// </summary>
public class PopoverMenuRoleTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [Fact]
    public void ThePopoverIsAMenuOfItems_AndNoButton()
    {
        Hosted((session, app) =>
        {
            Show(session, app);
            var menu = Menu(session)!;
            Assert.Equal(["Add existing repository…", "Clone repository…"],
                menu.Children.Where(c => c.Role == AccessibilityRole.MenuItem).Select(c => c.Name));
            for (var above = menu.Parent; above is not null; above = above.Parent)
                Assert.NotEqual(AccessibilityRole.Button, above.Role);
            Assert.DoesNotContain(Walk(session.Window.AccessibilityTree),
                n => n.Role == AccessibilityRole.Button && n.Name is { } name && name.Contains("Clone repository…", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Escape_AndAClickOutside_CloseIt()
    {
        Hosted((session, app) =>
        {
            Show(session, app);
            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(3);
            Assert.Null(Menu(session));

            Show(session, app);
            session.Window.Click(900, 600);
            session.PumpFrames(3);
            Assert.Null(Menu(session));
        });
    }

    [Fact]
    public void TheEntryUnderThePointer_IsSelected_AndBuildsNoTree()
    {
        Hosted((session, app) =>
        {
            var bridge = session.Window.InstallAccessibilityBridge();
            Show(session, app);
            session.PumpFrames(3);
            AccessibilityNode Entry(string name) =>
                Walk(bridge.CurrentTree!).Single(n => n.Role == AccessibilityRole.MenuItem && n.Name == name);
            const string Add = "Add existing repository…", Clone = "Clone repository…";
            Assert.False(Entry(Add).IsSelected);
            Assert.False(Entry(Clone).IsSelected);
            int trees = session.Window.AccessibilityTreesBuilt;
            var (first, second) = (Entry(Add).BoundingRect, Entry(Clone).BoundingRect);

            session.Window.MoveMouse(first.X + 20, first.Y + first.Height / 2);
            session.PumpFrames(2);
            Assert.True(Entry(Add).IsSelected);

            session.Window.MoveMouse(second.X + 20, second.Y + second.Height / 2);
            session.PumpFrames(2);
            Assert.False(Entry(Add).IsSelected);
            Assert.True(Entry(Clone).IsSelected);

            Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        });
    }

    /// <summary>A click on an entry runs it.</summary>
    [Fact]
    public void AClickOnAnEntry_RunsIt()
    {
        Hosted((session, app) =>
        {
            Show(session, app);
            var box = Menu(session)!.Children.First(c => c.Name == "Clone repository…").BoundingRect;
            session.Window.Click(box.X + 20, box.Y + box.Height / 2);
            session.PumpFrames(4);
            Assert.Null(Menu(session));
            Assert.NotNull(typeof(JunctionApp).GetField("_modal", Any)!.GetValue(app));
        });
    }

    private static void Show(HeadlessSession session, JunctionApp app)
    {
        var items = typeof(JunctionApp).GetMethods(Any).First(m => m.Name == "RepoAddMenuItems").Invoke(app, null);
        typeof(JunctionApp).GetField("_popoverX", Any)!.SetValue(app, 300f);
        typeof(JunctionApp).GetField("_popoverY", Any)!.SetValue(app, 120f);
        typeof(JunctionApp).GetField("_popover", Any)!.SetValue(app, items);
        Rebuild(app);
        session.PumpFrames(4);
    }

    private static void Rebuild(JunctionApp app)
    {
        for (var t = typeof(JunctionApp); t != null; t = t.BaseType)
            if (t.GetMethod("MarkDirty", Any) is { } m)
            { m.Invoke(app, null); return; }
        throw new Xunit.Sdk.XunitException("no MarkDirty to call");
    }

    private static AccessibilityNode? Menu(HeadlessSession session) =>
        Walk(session.Window.AccessibilityTree).FirstOrDefault(n => n.Role == AccessibilityRole.Menu);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Walk(child)) yield return n;
    }

    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        using var session = HeadlessSession.Create(1400, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Digit2);   // History
            session.PumpFrames(8);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
