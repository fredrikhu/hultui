using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Junction.Git;
using Junction.GitLab;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// Phase 6, hosted against recordings: the four Options panes writing their settings as
/// they are touched, the shortcuts pane drawn from the one table, clone-from-GitLab's
/// project list filling the URL, branch-from-issue naming and checking out the branch, and
/// the todo filters thinning the bell.
/// </summary>
public class OptionsPanesTests
{
    private const string Api = "/api/v4";
    private const string MrList = $"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=1";
    private const string Membership = $"GET {Api}/projects?membership=true&min_access_level=30&order_by=last_activity_at&sort=desc&simple=true&per_page=100&page=1";

    [Theory]
    [InlineData(1042, "Split refunds across original tenders", "1042-split-refunds-across-original-tenders")]
    [InlineData(7, "  Fix: the (odd) café bug!! ", "7-fix-the-odd-caf-bug")]
    [InlineData(9, "!!!", "9")]
    public void TheBranchIsNamedTheWayGitLabsTemplateNamesIt(long iid, string title, string branch) =>
        Assert.Equal(branch, JunctionApp.BranchNameForIssue(iid, title));

    [Fact]
    public void ALongTitleIsCutAtADash()
    {
        var name = JunctionApp.BranchNameForIssue(1, string.Join(" ", Enumerable.Repeat("word", 30)));
        Assert.True(name.Length <= 2 + 60);
        Assert.False(name.EndsWith('-'));
    }

    [Fact]
    public void ACustomEditorCommandSplitsIntoProgramAndArguments()
    {
        var opts = JunctionApp.EditorLaunchOptions(new ExternalToolOptions { Editor = EditorPreset.Custom, EditorCommand = "meld {left} {right}" });
        Assert.Equal("meld", opts.ExternalDiffToolPath);
        Assert.Equal("{left} {right}", opts.ExternalDiffToolArgs);
        // No files named: they are appended, so the command still gets both.
        var bare = JunctionApp.EditorLaunchOptions(new ExternalToolOptions { Editor = EditorPreset.Custom, EditorCommand = "mytool --diff" });
        Assert.Equal("--diff {left} {right}", bare.ExternalDiffToolArgs);
        // An empty custom command falls back to VS Code rather than to nothing.
        Assert.Equal("code", JunctionApp.EditorLaunchOptions(new ExternalToolOptions { Editor = EditorPreset.Custom }).ExternalDiffToolPath);
    }

    [Fact]
    public void MergeToolPresetsHaveTheirCommandsAndNoneHasNone()
    {
        Assert.Null(ExternalMergeLauncher.Command(new ExternalToolOptions()));
        var code = ExternalMergeLauncher.Command(new ExternalToolOptions { MergeTool = MergeToolPreset.VSCode });
        Assert.Equal("code", code!.Value.Program);
        Assert.Contains("{merged}", code.Value.Args);
        // Custom with no program is as good as none; with one, its arguments default to the four files.
        Assert.Null(ExternalMergeLauncher.Command(new ExternalToolOptions { MergeTool = MergeToolPreset.Custom }));
        var custom = ExternalMergeLauncher.Command(new ExternalToolOptions { MergeTool = MergeToolPreset.Custom, ExternalMergeToolPath = "mytool" });
        Assert.Equal("{base} {local} {remote} {merged}", custom!.Value.Args);
    }

    /// <summary>Tabs are drawn at the width Options says and the model keeps them: the
    /// label shows spaces, the search and a copy still see the line as git has it.</summary>
    [Fact]
    public void TabsAreDrawnAtTheChosenWidth()
    {
        Hosted((repo, session, app, settings) =>
        {
            session.Window.PressKey(KeyCode.Digit3);
            session.PumpUntil(() => Code(session, "    x   y") is not null, TimeSpan.FromSeconds(20));

            app.OpenOptions(OptionsSection.CodeAndDiff);
            session.PumpFrames(2);
            session.Window.Click(Named(session, "8"));
            session.PumpFrames(2);
            Assert.Equal(8, settings.Appearance.TabWidth);
            session.Window.PressKey(KeyCode.Escape);
            session.PumpUntil(() => Code(session, "        x       y") is not null, TimeSpan.FromSeconds(20));
        }, prepare: repo =>
        {
            repo.Write("tabs.txt", "\tx\ty\n");
            repo.Git("add", "tabs.txt");
            repo.Git("commit", "-q", "-m", "tabs");
        });

        static TextElement? Code(HeadlessSession session, string content) =>
            Root(session) is { } root ? LayoutProbe.Descendants<TextElement>(root, t => t.Content == content).FirstOrDefault() : null;
    }

    [Fact]
    public void GeneralWritesItsSettingsAsTheyAreTouched()
    {
        Hosted((_, session, app, settings) =>
        {
            app.OpenOptions(OptionsSection.General);
            session.PumpFrames(2);
            Assert.False(settings.General.AutoFetchEnabled);
            Assert.False(TreeHas(session, "15 min"));

            session.Window.Click(Named(session, "Auto-fetch"));
            session.PumpFrames(2);
            Assert.True(settings.General.AutoFetchEnabled);
            session.Window.Click(Named(session, "15 min"));
            session.PumpFrames(2);
            Assert.Equal(15, settings.General.AutoFetchIntervalMinutes);

            session.Window.Click(Named(session, "Fast-forward only"));
            session.PumpFrames(2);
            Assert.Equal(PullStrategy.FastForwardOnly, settings.Git.PullStrategy);

            session.Window.Click(Named(session, "Confirm destructive actions"));
            session.PumpFrames(2);
            Assert.False(settings.General.ConfirmDestructive);

            session.Window.Click(Named(session, "Refresh GitLab data on a timer"));
            session.PumpFrames(2);
            Assert.Equal(5, settings.General.GitLabRefreshMinutes);

            // Silencing a todo kind writes the list.
            session.Window.Click(Named(session, "pipeline failed"));
            session.PumpFrames(2);
            Assert.Contains("build_failed", settings.General.TodoHiddenKinds);
        });
    }

    [Fact]
    public void CodeAndDiffSwitchesTheDiffAndTheShortcutsPaneReadsTheTable()
    {
        Hosted((_, session, app, settings) =>
        {
            app.OpenOptions(OptionsSection.CodeAndDiff);
            session.PumpFrames(2);
            Assert.NotNull(ById(session, "code-font-name"));   // moved here from Appearance
            session.Window.Click(Named(session, "Split"));
            session.PumpFrames(2);
            Assert.True(settings.DiffSplit);

            session.Window.Click(Named(session, "Keyboard shortcuts"));
            session.PumpFrames(2);
            Assert.True(TreeHas(session, "Search & commands"));
            Assert.True(TreeHas(session, "Ctrl+P · Ctrl+K"));
            Assert.True(TreeHas(session, "Next unresolved thread"));
            Assert.True(TreeHas(session, "Rebase"));   // a group the old table never had

            session.Window.Click(Named(session, "External tools"));
            session.PumpFrames(2);
            session.Window.Click(Named(session, "Rider"));
            session.PumpFrames(2);
            Assert.Equal(EditorPreset.Rider, settings.ExternalTools.Editor);
            session.Window.Click(Named(session, "PowerShell"));
            session.PumpFrames(2);
            Assert.Equal(TerminalPreset.PowerShell, settings.ExternalTools.Terminal);
        });
    }

    [Fact]
    public void CloneFromGitLabListsTheProjectsAndPickingOneFillsTheUrl()
    {
        Hosted((_, session, app, _) =>
        {
            app.OpenCloneRepo();
            session.PumpFrames(2);
            Assert.NotNull(ById(session, "clone-source"));
            session.Window.Click(Named(session, "GitLab"));
            session.PumpUntil(() => TreeHas(session, "acme/core-api"), TimeSpan.FromSeconds(15));
            Assert.True(TreeHas(session, "acme/web-client"));
            session.PumpFrames(2);
            session.Window.Click(Named(session, "acme/web-client"));
            session.PumpFrames(2);
            Assert.NotNull(ById(session, "clone-chosen"));
            Assert.True(TreeHas(session, "https://gitlab.com/acme/web-client.git"));
            // The name the clone will use, from the URL the pick filled.
            Assert.True(TreeHas(session, "web-client"));
        });
    }

    /// <summary>Every keystroke is a new search. The list keeps a fixed height and shows the
    /// last answer, dimmed, until the next one lands — skeleton rows are for the first load
    /// only, so typing does not empty the list and bounce the dialog.</summary>
    [Fact]
    public void TypingASearchKeepsTheListSteady()
    {
        Hosted((_, session, app, _) =>
        {
            app.OpenCloneRepo();
            session.PumpFrames(2);
            session.Window.Click(Named(session, "GitLab"));
            session.PumpUntil(() => TreeHas(session, "acme/core-api"), TimeSpan.FromSeconds(15));
            var list = ById(session, "clone-projects")!;
            float height = LayoutProbe.Painted(list).Height;
            Assert.Equal(260f, height, 1f);

            var field = LayoutProbe.Descendants<TextInputElement>(Root(session)!, t => t.Placeholder.StartsWith("Search projects")).First();
            field.Focus();
            session.PumpFrame();
            session.Window.TypeText("web");
            session.PumpFrames(2);
            // In flight or answered, the rows are there and the box has not moved.
            Assert.True(TreeHas(session, "acme/web-client"));
            Assert.Equal(height, LayoutProbe.Painted(ById(session, "clone-projects")!).Height, 1f);
            session.PumpUntil(() => !TreeHas(session, "acme/core-api"), TimeSpan.FromSeconds(15));
            Assert.True(TreeHas(session, "acme/web-client"));
        });
    }

    /// <summary>A request opened from another repository — the workspace-scoped list, the
    /// Overview strip — makes that repository active first: the Commit view compares in the
    /// active one, and a review left in the wrong one loaded forever.</summary>
    [Fact]
    public void ReviewingARequestFromAnotherRepositorySwitchesToIt()
    {
        using var other = TempRepo.CreateWithInitialCommit();
        other.Git("remote", "add", "origin", "https://gitlab.com/acme/web-client.git");
        Hosted((repo, session, app, _) =>
        {
            Assert.Equal(other.Path, app.ActiveRepositoryPath);
            app.StartReview(repo.Path, 412);
            session.PumpFrames(3);
            Assert.Equal(repo.Path, app.ActiveRepositoryPath);
            Assert.True(app.ReviewOpen);
            session.PumpUntil(() => TreeHas(session, "!412"), TimeSpan.FromSeconds(15));
        }, settings =>
        {
            settings.RepositoryPaths.Add(other.Path);
            settings.Workspaces[0].RepoPaths.Add(other.Path);
            settings.ActiveRepoPath = other.Path;
        });
    }

    [Fact]
    public void BranchFromIssueChecksOutTheNamedBranchFromTheDefaultBranch()
    {
        Hosted((repo, session, app, _) =>
        {
            // The issues load behind the first ask; the branch is created on the second.
            app.OpenBranchFromIssue();
            session.PumpUntil(() => TreeHas(session, "Loading the project's issues") || TreeHas(session, "Split refunds"), TimeSpan.FromSeconds(10));
            session.PumpUntil(() => { app.CreateBranchFromIssue(1042); return repo.Git("rev-parse", "--abbrev-ref", "HEAD").Stdout.Trim() == "1042-split-refunds-across-original-tenders"; },
                TimeSpan.FromSeconds(15));
            Assert.Equal(repo.Git("rev-parse", "main").Stdout.Trim(), repo.Git("rev-parse", "HEAD").Stdout.Trim());
        });
    }

    [Fact]
    public void ASilencedKindLeavesTheBell()
    {
        Hosted((_, session, _, _) =>
        {
            session.PumpUntil(() => ById(session, "todo-bell") is not null, TimeSpan.FromSeconds(15));
            Assert.Equal("To do: 1", ById(session, "todo-bell")!.AccessibleName);   // pipeline failed silenced
        }, settings => settings.General.TodoHiddenKinds.Add("build_failed"));
    }

    // ── Hosting ──────────────────────────────────────────────────────────────────────

    private static FakeGitLabHandler Routes() =>
        new FakeGitLabHandler()
            .Map($"GET {Api}/projects/acme%2Fcore-api", "project")
            .Map($"GET {Api}/projects/7781/protected_branches?per_page=100&page=1", "protected_branches")
            .Map(MrList, "merge_requests_page1")
            .MapJson($"GET {Api}/projects/7781/pipelines?order_by=updated_at&sort=desc&per_page=100&page=1", "[]")
            .Map($"GET {Api}/projects/7781/issues?state=opened&order_by=updated_at&sort=desc&per_page=100&page=1", "issues")
            .Map($"GET {Api}/todos?state=pending&per_page=100&page=1", "todos")
            .MapJson(Membership.Replace("&simple=true", "&simple=true&search=web"),
                "[{\"id\":7782,\"name\":\"web-client\",\"path\":\"web-client\",\"path_with_namespace\":\"acme/web-client\",\"http_url_to_repo\":\"https://gitlab.com/acme/web-client.git\",\"visibility\":\"internal\"}]")
            .MapJson(Membership,
                "[{\"id\":7781,\"name\":\"core-api\",\"path\":\"core-api\",\"path_with_namespace\":\"acme/core-api\",\"http_url_to_repo\":\"https://gitlab.com/acme/core-api.git\",\"visibility\":\"private\",\"last_activity_at\":\"2026-09-12T09:00:00.000Z\",\"namespace\":{\"id\":1,\"name\":\"acme\",\"path\":\"acme\",\"full_path\":\"acme\",\"kind\":\"group\"}}," +
                "{\"id\":7782,\"name\":\"web-client\",\"path\":\"web-client\",\"path_with_namespace\":\"acme/web-client\",\"http_url_to_repo\":\"https://gitlab.com/acme/web-client.git\",\"visibility\":\"internal\",\"namespace\":{\"id\":1,\"name\":\"acme\",\"path\":\"acme\",\"full_path\":\"acme\",\"kind\":\"group\"}}]");

    private static void Hosted(Action<TempRepo, HeadlessSession, JunctionApp, JunctionSettingsData> body, Action<JunctionSettingsData>? tune = null,
        Action<TempRepo>? prepare = null)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("remote", "add", "origin", "https://gitlab.com/acme/core-api.git");
        prepare?.Invoke(repo);
        var handler = Routes();

        var store = new InMemoryCredentialStore();
        store.Write(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.TokenAccount, "glpat-test");
        var settings = new JunctionSettingsData
        {
            RepositoryPaths = [repo.Path],
            ActiveRepoPath = repo.Path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
            ActiveWorkspaceId = "w1",
        };
        settings.GitLabAccounts.Add(new GitLabAccountData
        {
            Host = "gitlab.com", WebBaseUrl = "https://gitlab.com", UserId = 42, Username = "dosei", Name = "Dana Osei",
            Kind = GitLabTokenKind.Pat, Scopes = ["api"], ExpiresAt = DateTimeOffset.UtcNow.AddDays(200),
        });
        tune?.Invoke(settings);

        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, settings,
                credentialStore: store, gitLabHandler: _ => handler);
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            // The repository's summary says whether its remote is on GitLab, and it lands a frame
            // or more after mount (later when a file event restarts the load); until it does,
            // every GitLab command answers "not on GitLab". The mark by the repository's name is
            // how the app shows it knows, and a user cannot reach those commands before it.
            session.PumpUntil(() => ById(session, "gitlab-mark") is not null, TimeSpan.FromSeconds(15));
            body(repo, session, app, settings);
        }
        finally { app?.Dispose(); }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);

    private static bool TreeHas(HeadlessSession session, string text) =>
        Root(session) is { } root &&
        LayoutProbe.Descendants<TextElement>(root, t => t.Content.Contains(text, StringComparison.Ordinal)).Count > 0;

    private static Element? ById(HeadlessSession session, string id) =>
        Root(session) is { } root
            ? LayoutProbe.Descendants<Element>(root, e => e.AutomationId == id).FirstOrDefault()
            : null;

    private static Element Named(HeadlessSession session, string name)
    {
        var el = Root(session) is { } root
            ? LayoutProbe.Descendants<Element>(root, e => e.AccessibleName == name).FirstOrDefault()
            : null;
        return el ?? throw new Xunit.Sdk.XunitException($"nothing on screen is named \"{name}\"");
    }
}
