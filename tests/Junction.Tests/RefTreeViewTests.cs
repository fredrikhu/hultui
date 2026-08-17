using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The tree presentation of the Branches and Tags tables, hosted headlessly against a
/// real repository. Assertions go through the accessibility tree — which is also the
/// only thing a screen reader gets from these tables, since the virtual list never
/// realizes an off-screen row as an element.
/// </summary>
public class RefTreeViewTests
{
    [Fact]
    public void TheBranchesTabGroupsRefsUnderAFolderRow()
    {
        Hosted(tree: true, repo =>
        {
            repo.Git("branch", "feature/login");
            repo.Git("branch", "feature/logout");
        }, (session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            PumpUntilVisible(session, "feature, folder");

            Assert.True(Contains(session, "feature, folder, 2 refs, expanded"),
                "the folder row does not say what is under it");
            Assert.True(Contains(session, "feature/login"), "the grouped branch is missing");
            Assert.True(Contains(session, "feature/logout"), "the grouped branch is missing");
        });
    }

    [Fact]
    public void TheFlatListHasNoFolderRowsAtAll()
    {
        Hosted(tree: false, repo =>
        {
            repo.Git("branch", "feature/login");
        }, (session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            PumpUntilVisible(session, "feature/login");

            Assert.False(Contains(session, ", folder,"), "the flat list grew a folder row");
        });
    }

    [Fact]
    public void CollapsingAFolderTakesItsBranchesOffTheList()
    {
        Hosted(tree: true, repo =>
        {
            repo.Git("branch", "feature/login");
            repo.Git("branch", "feature/logout");
        }, (session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            PumpUntilVisible(session, "feature, folder");

            // Row 0 is the checked-out branch (pinned to the top, and it carries no
            // separator so it stays at the root); row 1 is the folder.
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Enter);
            session.PumpFrames(3);

            Assert.True(Contains(session, "feature, folder, 2 refs, collapsed"),
                "the folder did not close.\n" + session.Window.DumpAccessibilityTree());
            Assert.False(Contains(session, "feature/login"),
                "a collapsed folder is still listing its branches");
        });
    }

    [Fact]
    public void TheRightArrowReopensACollapsedFolder()
    {
        Hosted(tree: true, repo =>
        {
            repo.Git("branch", "feature/login");
        }, (session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            PumpUntilVisible(session, "feature, folder");

            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Left);       // collapse
            session.PumpFrames(3);
            Assert.False(Contains(session, "feature/login"));

            session.Window.PressKey(KeyCode.Right);      // and open it again
            session.PumpFrames(3);
            Assert.True(Contains(session, "feature/login"),
                "the right arrow did not reopen the folder.\n" + session.Window.DumpAccessibilityTree());
        });
    }

    [Fact]
    public void ASearchOpensTheTreeSoEveryMatchIsReachable()
    {
        Hosted(tree: true, repo =>
        {
            repo.Git("branch", "feature/login");
        }, (session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            PumpUntilVisible(session, "feature, folder");

            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Enter);      // collapse the folder
            session.PumpFrames(3);
            Assert.False(Contains(session, "feature/login"));

            SetField(app, "_branchQuery", "login");
            MarkDirty(app);
            session.PumpFrames(3);

            Assert.True(Contains(session, "feature/login"),
                "a collapsed folder hid a search match.\n" + session.Window.DumpAccessibilityTree());
        });
    }

    [Fact]
    public void AChainOfSingleChildFoldersBecomesOneRow()
    {
        Hosted(tree: true, repo =>
        {
            // The naming scheme this UI is built around: every folder on the way down holds
            // exactly one thing, so three rows of chrome used to lead to one branch.
            repo.Git("branch", "feature/24.1/payments/PC-1043-refund");
        }, (session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            PumpUntilVisible(session, "PC-1043-refund");

            Assert.True(Contains(session, "feature/24.1/payments, folder"),
                "the single-child chain was not merged into one row.\n"
                + session.Window.DumpAccessibilityTree());
            // "feature, folder" would be the first row of the unmerged chain. Anchored on
            // the comma so the merged label (which starts "feature/…") doesn't match it.
            Assert.False(Contains(session, "feature, folder"), "the chain still has its own rows");
        });
    }

    [Fact]
    public void AFolderThatReallyBranchesIsLeftAlone()
    {
        Hosted(tree: true, repo =>
        {
            repo.Git("branch", "feature/login");
            repo.Git("branch", "feature/logout");
        }, (session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            PumpUntilVisible(session, "feature, folder");

            // Two children: a grouping, not a step on the way. Merging it would have to
            // pick one of them, so it stays.
            Assert.True(Contains(session, "feature, folder, 2 refs"));
        });
    }

    [Fact]
    public void TagsGroupByTheirOwnSeparatorsToo()
    {
        Hosted(tree: true, repo =>
        {
            repo.Git("tag", "v1/rc1");
            repo.Git("tag", "v1/rc2");
            repo.Git("tag", "v2.0.0");
        }, (session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit6);
            PumpUntilVisible(session, "v1, folder, 2 refs");

            Assert.True(Contains(session, "v1/rc1"), "the grouped tag is missing");
            // A plain semver tag carries no separator, so it stays where it was.
            Assert.True(Contains(session, "v2.0.0"), "the ungrouped tag is missing");
        });
    }

    // ── harness (same shape as TagsViewTests) ────────────────────────────
    private static void Hosted(bool tree, Action<TempRepo> arrange, Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        arrange(repo);

        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
                RefTreeView = tree,
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static void PumpUntilVisible(HeadlessSession session, string text)
    {
        try
        {
            session.PumpUntil(() => Contains(session, text), TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException(
                $"\"{text}\" never appeared in the accessibility tree.\n{e.Message}\n" +
                session.Window.DumpAccessibilityTree());
        }
    }

    private static bool Contains(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
            || n.Value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    private static void SetField(JunctionApp app, string name, object? value) =>
        typeof(JunctionApp).GetField(name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(app, value);

    private static void MarkDirty(JunctionApp app) =>
        typeof(JunctionApp).GetMethod("MarkDirty",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance)!.Invoke(app, null);
}
