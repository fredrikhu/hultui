using System.Reflection;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The New branch prompt opens on the root of the branch you last created.
///
/// <para>Branches in a repository almost all share a prefix — <c>feature/</c>,
/// <c>fredrik/fix/</c> — and retyping it is most of the friction in naming one. The root is
/// recorded only after git accepts the name, so a rejected branch cannot leave a seed
/// behind, and a branch created at the top level records an empty root rather than keeping
/// a stale one the next prompt would have to be cleared of.</para>
/// </summary>
public class BranchRootMemoryTests
{
    [Theory]
    [InlineData("feature/login", "feature/")]
    [InlineData("fredrik/fix/crash-on-open", "fredrik/fix/")]
    [InlineData("hotfix", "")]              // no slash ⇒ no root, not a stale one
    public void CreatingABranchRecordsItsRoot(string branch, string expectedRoot)
    {
        Hosted((session, app, settings) =>
        {
            CreateBranch(app, branch);
            session.PumpFrame();

            Assert.Equal(expectedRoot, settings.LastBranchRoot);
        });
    }

    [Fact]
    public void TheNextPromptOpensOnThatRoot()
    {
        Hosted((session, app, settings) =>
        {
            CreateBranch(app, "feature/login");
            session.PumpFrame();

            foreach (var (method, args) in new (string, object?[])[]
            {
                ("PromptNewBranch", []),
                ("PromptCreateBranchAt", ["HEAD", "HEAD"]),
            })
            {
                Invoke(app, method, args);
                Assert.Equal("feature/", Field<string>(app, "_dialogValue"));
                Invoke(app, "CloseDialog", []);
            }
        });
    }

    [Fact]
    public void ARootGitRefusedIsNotRemembered()
    {
        Hosted((session, app, settings) =>
        {
            CreateBranch(app, "feature/login");
            session.PumpFrame();

            // ".." is invalid in a ref name, so git refuses the branch outright. The
            // earlier root has to survive that — the failed attempt is not a preference.
            CreateBranch(app, "broken/x..y");
            session.PumpFrame();

            Assert.Equal("feature/", settings.LastBranchRoot);
        });
    }

    [Fact]
    public void TheRootSurvivesARestart()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var settings = SettingsFor(repo.Path);

        Run(repo, settings, (session, app) =>
        {
            CreateBranch(app, "feature/login");
            session.PumpFrame();
        });

        // A second launch reads the file back rather than the object we just mutated.
        var reloaded = JunctionSettings.Load();
        Assert.Equal("feature/", reloaded.LastBranchRoot);
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void CreateBranch(JunctionApp app, string name) =>
        Invoke(app, "DoCreateBranch", [name, null, false]);

    private static object? Invoke(JunctionApp app, string method, object?[] args)
    {
        var m = typeof(JunctionApp).GetMethod(method, Private);
        Assert.NotNull(m);
        return m!.Invoke(app, args);
    }

    private static T Field<T>(JunctionApp app, string name) =>
        (T)typeof(JunctionApp).GetField(name, Private)!.GetValue(app)!;

    private static JunctionSettingsData SettingsFor(string repoPath) => new()
    {
        RepositoryPaths = [repoPath],
        ActiveRepoPath = repoPath,
        Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repoPath] }],
        ActiveWorkspaceId = "w1",
    };

    private static void Hosted(Action<HeadlessSession, JunctionApp, JunctionSettingsData> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var settings = SettingsFor(repo.Path);
        Run(repo, settings, (session, app) => body(session, app, settings));
    }

    private static void Run(TempRepo repo, JunctionSettingsData settings, Action<HeadlessSession, JunctionApp> body)
    {
        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, settings);
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
