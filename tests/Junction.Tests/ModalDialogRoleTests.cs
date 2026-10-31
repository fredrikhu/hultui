using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Junction's modal cards stand on a dim backdrop whose click closes them. Each card is a dialog to
/// assistive technology, named by its title, directly inside the backdrop, so the backdrop is no
/// button (the user approved what a reader hears on 2026-10-27). Before, a box with a click around
/// the card was a button named after all of the card's text, the card a second one inside it, and
/// a text change inside either could not be patched: each built the window's whole accessibility
/// tree, 260 KB over 1,000 rows with UIA. The click beside the card and Escape still close it.
/// </summary>
public class ModalDialogRoleTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [Theory]
    [InlineData("OpenWorktrees", "Worktrees")]
    [InlineData("OpenReflog", "Reflog")]
    [InlineData("OpenSubmodules", "Submodules")]
    [InlineData("OpenRemotes", "Remotes")]
    [InlineData("OpenTodos", "To do")]
    [InlineData("OpenOptions", "Options")]
    [InlineData("OpenAddRepo", "Add local repository")]
    [InlineData("OpenCloneRepo", "Clone repository")]
    public void ACardIsADialogNamedByItsTitle_AndEscapeAndAClickBesideCloseIt(string open, string title)
    {
        Hosted((session, app) =>
        {
            Open(app, open);
            session.PumpFrames(4);
            AssertADialogAndNoButton(session, title);

            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(4);
            Assert.Null(Dialog(session, title));

            Open(app, open);
            session.PumpFrames(4);
            Assert.NotNull(Dialog(session, title));
            session.Window.Click(4, 4);
            session.PumpFrames(4);
            Assert.Null(Dialog(session, title));
        });
    }

    [Fact]
    public void AConfirmIsADialogNamedByItsTitle()
    {
        Hosted((session, app) =>
        {
            Call(app, "OpenConfirm", "Delete this branch?", "It cannot be undone.", "Delete", (Action)(() => { }));
            session.PumpFrames(4);
            var dialog = AssertADialogAndNoButton(session, "Delete this branch?");
            Assert.Contains(Walk(dialog), n => n.Role == AccessibilityRole.Label && n.Name == "It cannot be undone.");
            Assert.Contains(Walk(dialog), n => n.Role == AccessibilityRole.Button && n.Name == "Delete");

            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(4);
            Assert.Null(Dialog(session, "Delete this branch?"));
        });
    }

    /// <summary>The rebase card, and the reword card over it, which stands on a backdrop of its own
    /// beside the first card, in the first card's backdrop.</summary>
    [Fact]
    public void TheRebaseCardAndTheRewordCardInIt_AreDialogs()
    {
        Hosted((session, app) =>
        {
            var commits = (IReadOnlyList<GitCommit>)Get(Get(Get(app, "_history"), "Value"), "Commits");
            Call(app, "OpenRebase", commits[3]);
            session.PumpUntil(() => Dialog(session, "Interactive rebase") is not null, TimeSpan.FromSeconds(15));
            AssertADialogAndNoButton(session, "Interactive rebase");

            var rows = (System.Collections.IList)Get(Get(app, "_rebase"), "Rows");
            Call(app, "OpenRewordRow", rows[0]!);
            session.PumpFrames(4);
            AssertADialogAndNoButton(session, "Reword commit");
            Assert.NotNull(Dialog(session, "Interactive rebase"));

            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(4);
            Assert.Null(Dialog(session, "Reword commit"));
            Assert.NotNull(Dialog(session, "Interactive rebase"));
        });
    }

    /// <summary>What the role is for: a character in a field in the card changes the field's value
    /// and the text around it, and with the card a dialog that is patched, not built. (The first
    /// character builds one tree: the footer's button comes alive, which is another element.)</summary>
    [Fact]
    public void TextChangingInACard_BuildsNoTree()
    {
        Hosted((session, app) =>
        {
            Open(app, "OpenAddRepo");
            session.PumpFrames(4);
            Assert.IsType<TextInputElement>(session.Window.Window.FocusedElement);
            session.Window.TypeText("x");
            session.PumpFrames(4);

            int trees = session.Window.AccessibilityTreesBuilt;
            foreach (var ch in "abc")
            {
                session.Window.TypeText(ch.ToString());
                session.PumpFrames(2);
            }
            Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        });
    }

    private static AccessibilityNode AssertADialogAndNoButton(HeadlessSession session, string title)
    {
        var dialog = Dialog(session, title);
        Assert.NotNull(dialog);
        // No button stands for the card: none above it, none named from its text.
        for (var above = dialog!.Parent; above is not null; above = above.Parent)
            Assert.NotEqual(AccessibilityRole.Button, above.Role);
        Assert.DoesNotContain(Walk(session.Window.AccessibilityTree),
            n => n.Role == AccessibilityRole.Button && n.Name is { Length: > 0 } name && name.StartsWith(title + " ", StringComparison.Ordinal));
        return dialog;
    }

    private static AccessibilityNode? Dialog(HeadlessSession session, string title) =>
        Walk(session.Window.AccessibilityTree).FirstOrDefault(n => n.Role == AccessibilityRole.Dialog && n.Name == title);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Walk(child)) yield return n;
    }

    private static void Open(JunctionApp app, string name)
    {
        var method = typeof(JunctionApp).GetMethods(Any).First(m => m.Name == name);
        method.Invoke(app, new object?[method.GetParameters().Length]);
    }

    private static void Call(object target, string name, params object[] args) =>
        target.GetType().GetMethods(Any).First(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);

    private static object Get(object target, string name) =>
        target.GetType().GetField(name, Any)?.GetValue(target) ?? target.GetType().GetProperty(name, Any)!.GetValue(target)!;

    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < 6; i++)
            repo.Git("commit", "--allow-empty", "-m", $"Commit {i}");

        using var session = HeadlessSession.Create(1400, 900);
        session.Window.InstallAccessibilityBridge();
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
