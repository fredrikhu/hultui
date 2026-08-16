using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Opening a Files-tab row in the desktop.
///
/// <para>What is worth testing here is the gate, not the launch. The tree lists a
/// <i>commit's</i> files, so a row is only launchable while the working tree still carries
/// it — browse a commit from before a file existed, or one from before it was deleted, and
/// the path in the tree resolves to nothing on disk. Handing that to the OS produces a
/// mystery error box; the menu is supposed to say so instead.</para>
///
/// <para>Nothing here starts a process. Asserting that a double-click really opens Notepad
/// would mean opening Notepad on whoever runs the suite, and the part that could break —
/// which paths are offered — is decided before any of that.</para>
/// </summary>
[Collection("JunctionDiff")]
public class FilesLaunchTests
{
    [Fact]
    public void AFileStillInTheWorkingTreeResolvesToItsPathOnDisk()
    {
        Hosted((repo, app) =>
        {
            var full = WorktreePath(app, "kept.cs");
            Assert.NotNull(full);
            Assert.True(File.Exists(full));
            Assert.Equal("kept.cs", System.IO.Path.GetFileName(full));
        });
    }

    /// <summary>A file the commit has and the working tree does not. Git will happily show
    /// its contents; the desktop has nothing to open.</summary>
    [Fact]
    public void AFileDeletedSinceTheCommitResolvesToNothing()
    {
        Hosted((repo, app) => Assert.Null(WorktreePath(app, "gone.cs")));
    }

    [Fact]
    public void TheMenuOffersOpenAndRevealForAFileThatIsThere()
    {
        Hosted((repo, app) =>
        {
            var labels = MenuLabels(app, "kept.cs", isDir: false);

            Assert.Contains("Open", labels);
            Assert.Contains(labels, l => l.Contains("Reveal") || l.Contains("containing folder"));
            Assert.All(Menu(app, "kept.cs", isDir: false).Where(i => !i.IsSeparator),
                i => Assert.False(i.IsDisabled));
        });
    }

    /// <summary>The absent case keeps the entry and greys it, rather than dropping it — a row
    /// whose menu is simply shorter reads as a broken menu, not as an explanation.</summary>
    [Fact]
    public void TheMenuSaysWhyAMissingFileCannotBeOpened()
    {
        Hosted((repo, app) =>
        {
            var items = Menu(app, "gone.cs", isDir: false);
            var open = Assert.Single(items, i => i.Label.StartsWith("Open", StringComparison.Ordinal));

            Assert.True(open.IsDisabled);
            Assert.Contains("working tree", open.Label);
            Assert.Null(open.Action);
            // Nothing that would launch or locate the absent path is offered at all.
            Assert.DoesNotContain(items, i => i.Label.Contains("Reveal") || i.Label.Contains("full path"));
        });
    }

    [Fact]
    public void AFolderRowOffersToOpenTheFolder()
    {
        Hosted((repo, app) => Assert.Contains("Open folder", MenuLabels(app, "sub", isDir: true)));
    }

    /// <summary>The path is always copyable, present or not — it is the one thing about a row
    /// that is true regardless of what the working tree currently holds.</summary>
    [Fact]
    public void CopyPathIsOfferedEitherWay()
    {
        Hosted((repo, app) =>
        {
            Assert.Contains("Copy path", MenuLabels(app, "kept.cs", isDir: false));
            Assert.Contains("Copy path", MenuLabels(app, "gone.cs", isDir: false));
        });
    }

    /// <summary>Enter and Space part company in the tree: Enter launches (file-manager
    /// behaviour), Space only opens the file in the pane. Asserted through the key handler's
    /// own signature, because the launching half cannot be exercised without a process.</summary>
    [Fact]
    public void TheTreeActivateCallSeparatesLaunchingFromSelecting()
    {
        var activate = typeof(JunctionApp).GetMethod("FilesTreeActivate",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(activate);
        Assert.Contains(activate!.GetParameters(), p => p.Name == "launch" && p.ParameterType == typeof(bool));
    }

    // ── Reaching the app's state ─────────────────────────────────────────

    private static string? WorktreePath(JunctionApp app, string relPath) =>
        (string?)typeof(JunctionApp).GetMethod("WorktreePath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, [relPath]);

    /// <summary>Builds the framework TreeNode the menu builder takes — through the tree
    /// model's own factories, since the node type's shape is not this test's business.
    /// Constructed rather than clicked because a right-click would only reach the same
    /// call through the pointer.</summary>
    private static IReadOnlyList<ContextMenuItem> Menu(JunctionApp app, string path, bool isDir)
    {
        TreeNode<string> node;
        if (isDir)
        {
            // The folder gathering a child path, walked down to the requested depth.
            node = Tree.Build([path + "/x"], p => p)[0];
            while (node.Path != path) node = node.Children[0];
        }
        else
        {
            node = Tree.Flat([path], p => p)[0];
        }

        return (IReadOnlyList<ContextMenuItem>)typeof(JunctionApp)
            .GetMethod("FilesTreeMenuItems", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, [node])!;
    }

    private static List<string> MenuLabels(JunctionApp app, string path, bool isDir) =>
        Menu(app, path, isDir).Where(i => !i.IsSeparator).Select(i => i.Label).ToList();

    /// <summary>A repo whose HEAD commit holds three paths, one of which is then removed from
    /// the working tree — so the tree the Files view shows and the disk disagree, which is the
    /// whole subject here.</summary>
    private static void Hosted(Action<TempRepo, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("kept.cs", "class Kept { }\n");
        repo.Write("gone.cs", "class Gone { }\n");
        repo.Write("sub/nested.cs", "class Nested { }\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "three paths");
        File.Delete(System.IO.Path.Combine(repo.Path, "gone.cs"));

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

            session.Window.PressKey(KeyCode.Digit7);   // Files
            session.PumpUntil(() => app!.View == JunctionView.Files, TimeSpan.FromSeconds(20));

            body(repo, app);
        }
        finally { app?.Dispose(); }
    }
}
