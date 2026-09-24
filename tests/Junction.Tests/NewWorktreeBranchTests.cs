using System.IO;
using System.Reflection;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The New worktree prompt asks for the branch name on its own field, instead of naming
/// the branch after the folder. A worktree folder is usually <c>repo-wt</c> or
/// <c>repo-review</c>; the branch in it is <c>feature/login</c>. The field sits under the
/// "new branch" checkbox, shows only while it is ticked, opens on the last branch root the
/// way the New branch prompt does, and has to hold a valid name before the prompt closes.
/// </summary>
public class NewWorktreeBranchTests
{
    [Fact]
    public void TheWorktreeGetsTheTypedBranchNotTheFolderName()
    {
        Run((session, app, repo) =>
        {
            var linked = repo.Path + "-wt";
            try
            {
                Invoke(app, "PromptNewWorktree");
                SetField(app, "_dialogValue", linked);
                SetField(app, "_dialogValue2", "feature/login");
                Invoke(app, "ConfirmDialog");
                session.PumpFrame();

                var wt = Assert.Single(repo.Repo.GetWorktrees(), w => !w.IsMain);
                Assert.Equal("feature/login", wt.Branch);
                Assert.Null(Field<object?>(app, "_dialog"));
            }
            finally
            {
                repo.Repo.RemoveWorktree(linked, force: true);
                if (Directory.Exists(linked)) Directory.Delete(linked, recursive: true);
            }
        });
    }

    [Fact]
    public void TheBranchFieldShowsOnlyWhileNewBranchIsTicked()
    {
        Run((session, app, _) =>
        {
            Invoke(app, "PromptNewWorktree");
            session.PumpFrame();
            Assert.NotNull(session.Window.FindByAutomationId("dialog-input-2"));

            SetField(app, "_dialogChecked", false);
            Invoke(app, "MarkDirty");
            session.PumpFrame();
            Assert.Null(session.Window.FindByAutomationId("dialog-input-2"));
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("feature/")]        // the seeded root alone is not a name yet
    [InlineData("has space")]
    public void AnInvalidBranchNameKeepsThePromptOpen(string branch)
    {
        Run((session, app, repo) =>
        {
            Invoke(app, "PromptNewWorktree");
            SetField(app, "_dialogValue", repo.Path + "-bad");
            SetField(app, "_dialogValue2", branch);
            Invoke(app, "ConfirmDialog");
            session.PumpFrame();

            Assert.NotNull(Field<object?>(app, "_dialog"));
            Assert.Single(repo.Repo.GetWorktrees());
        });
    }

    [Fact]
    public void TheBranchFieldOpensOnTheLastBranchRoot()
    {
        Run((session, app, _) =>
        {
            Invoke(app, "DoCreateBranch", "feature/earlier", null, false);
            session.PumpFrame();

            Invoke(app, "PromptNewWorktree");
            Assert.Equal("feature/", Field<string>(app, "_dialogValue2"));
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static object? Invoke(JunctionApp app, string method, params object?[] args)
    {
        var m = typeof(JunctionApp).GetMethod(method, Private)
            ?? typeof(JunctionApp).GetMethod(method, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(m);
        return m!.Invoke(app, args);
    }

    private static T Field<T>(JunctionApp app, string name) =>
        (T)typeof(JunctionApp).GetField(name, Private)!.GetValue(app)!;

    private static void SetField(JunctionApp app, string name, object? value) =>
        typeof(JunctionApp).GetField(name, Private)!.SetValue(app, value);

    private static void Run(System.Action<HeadlessSession, JunctionApp, TempRepo> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var settings = new JunctionSettingsData
        {
            RepositoryPaths = [repo.Path],
            ActiveRepoPath = repo.Path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
            ActiveWorkspaceId = "w1",
        };
        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, settings);
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(session, app, repo);
        }
        finally { app?.Dispose(); }
    }
}
