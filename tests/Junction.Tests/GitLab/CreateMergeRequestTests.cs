using System.Reflection;
using System.Web;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Junction.Git;
using Junction.GitLab;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// Phase 3, hosted against recordings: the create-merge-request card pre-filled from the
/// branch and sent as the form GitLab expects; the push confirm quoting a protected branch's
/// rule before git is asked, with the lease greyed where force is not allowed; the composer
/// offering open issues on <c>#</c> and saying what a closing keyword will do; and a bare
/// <c>#7</c> in a History subject linking to the project's issue without any
/// <c>.issuetracker</c> file.
/// </summary>
public class CreateMergeRequestTests
{
    private const string Api = "/api/v4";
    private const string Feature = "feature/2.4.0/payments/PC-1042-refund-splitting";

    [Theory]
    [InlineData("PC-1042-refund-splitting", "PC-1042: refund splitting")]
    [InlineData("refund-splitting", "refund splitting")]
    [InlineData("1042-split-refunds", "1042 split refunds")]
    [InlineData("main", "main")]
    public void TheTitleComesFromTheLeaf(string leaf, string title)
    {
        Assert.Equal(title, JunctionApp.TitleFromLeaf(leaf));
    }

    [Theory]
    [InlineData("1042-split-refunds", "1042")]
    [InlineData("PC-1042-refund-splitting", "1042")]
    [InlineData("fix-typo", null)]
    [InlineData("v2-cleanup", null)]
    public void TheIssueComesFromTheLeaf(string leaf, string? issue)
    {
        Assert.Equal(issue, JunctionApp.IssueFromLeaf(leaf));
    }

    [Fact]
    public void TheCardIsPrefilledAndSendsTheFormGitLabExpects()
    {
        var handler = Routes();
        Hosted((repo, session, app) =>
        {
            app.OpenCreateMergeRequest(Feature);
            session.PumpUntil(() => ById(session, "create-mr") is not null, TimeSpan.FromSeconds(10));

            // One commit over main: its subject is the title; the leaf's ticket closes no
            // GitLab issue (PC-1042 is not one of the open iids), so no Closes line.
            var title = (TextInputElement)ById(session, "create-mr-title")!;
            Assert.Equal("PC-1042: split a refund across tenders", title.Text);
            Assert.Null(ById(session, "create-mr-push-note"));      // upstream set, nothing ahead
            // The protection rules load behind the card; the line appears when they land.
            session.PumpUntil(() => TreeHas(session, "main is protected — maintainers can merge; the request waits for one of them."), TimeSpan.FromSeconds(15));
            Assert.True(TreeHas(session, "delete source"));          // the project's default, in the summary

            session.Window.Click(Named(session, "Create merge request"));
            session.PumpUntil(() => !app.CreateMergeRequestOpen, TimeSpan.FromSeconds(10));

            var post = handler.Seen.Single(s => s.Request.Method == System.Net.Http.HttpMethod.Post);
            Assert.Equal($"{Api}/projects/7781/merge_requests", post.Request.RequestUri!.AbsolutePath);
            var form = HttpUtility.ParseQueryString(post.Body!);
            Assert.Equal(Feature, form["source_branch"]);
            Assert.Equal("main", form["target_branch"]);
            Assert.Equal("PC-1042: split a refund across tenders", form["title"]);
            Assert.Equal("true", form["remove_source_branch"]);
            Assert.Equal("true", form["squash"]);
            Assert.Null(form["draft"]);

            session.PumpUntil(() => TreeHas(session, "Created !412"), TimeSpan.FromSeconds(5));
            Assert.NotNull(Named(session, "Open"));   // the toast's action
        }, handler);
    }

    [Fact]
    public void ADraftWithADescriptionSendsBoth()
    {
        var handler = Routes();
        Hosted((repo, session, app) =>
        {
            app.OpenCreateMergeRequest(Feature);
            session.PumpUntil(() => ById(session, "create-mr") is not null, TimeSpan.FromSeconds(10));
            session.PumpFrames(2);   // found in the tree is a frame short of laid out
            session.Window.Click(ById(session, "create-mr-draft")!);
            session.PumpFrame();
            // PC-1042 names an open issue in the recording, so once the issues land the
            // description is pre-filled with the closing line; typing appends to it.
            session.PumpUntil(() => DescriptionHas(session, "Closes #1042"), TimeSpan.FromSeconds(15));
            session.Window.Click(ById(session, "create-mr-description")!);
            session.Window.TypeText(" (draft)");
            session.PumpFrame();
            Assert.True(TreeHas(session, "draft · squash · delete source"));

            session.Window.PressKey(KeyCode.Enter, KeyModifiers.Ctrl);
            session.PumpUntil(() => !app.CreateMergeRequestOpen, TimeSpan.FromSeconds(10));

            var form = HttpUtility.ParseQueryString(handler.Seen.Single(s => s.Request.Method == System.Net.Http.HttpMethod.Post).Body!);
            Assert.Equal("true", form["draft"]);
            Assert.StartsWith("Draft: ", form["title"]);
            Assert.Contains("Closes #1042", form["description"]);
            Assert.EndsWith(" (draft)", form["description"]);
        }, handler);
    }

    /// <summary>The description is edited as rich text — headings, bold, lists, typed with
    /// Markdown's own shortcuts — and GitLab is sent the Markdown it reads.</summary>
    [Fact]
    public void TheDescriptionIsEditedAsRichTextAndSentAsMarkdown()
    {
        var handler = Routes();
        Hosted((repo, session, app) =>
        {
            app.OpenCreateMergeRequest(Feature);
            session.PumpUntil(() => ById(session, "create-mr") is not null, TimeSpan.FromSeconds(10));
            session.PumpUntil(() => DescriptionHas(session, "Closes #1042"), TimeSpan.FromSeconds(15));
            session.PumpFrames(2);   // found in the tree is a frame short of laid out

            var editor = (RichTextEditorElement)ById(session, "create-mr-description")!;
            session.Window.Click(editor);
            session.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);
            session.Window.PressKey(KeyCode.Delete);
            session.Window.TypeText("## Why");
            session.Window.PressKey(KeyCode.Enter);
            session.Window.TypeText("Because **this** matters");
            session.Window.PressKey(KeyCode.Enter);
            session.Window.TypeText("- one");
            session.Window.PressKey(KeyCode.Enter);
            session.Window.TypeText("two");
            session.PumpFrame();
            Assert.Equal(HultUI.Core.RichText.BlockKind.Heading, BlockKindOf(editor, 0));
            Assert.Equal(HultUI.Core.RichText.BlockKind.BulletItem, BlockKindOf(editor, 2));

            session.Window.PressKey(KeyCode.Enter, KeyModifiers.Ctrl);
            session.PumpUntil(() => !app.CreateMergeRequestOpen, TimeSpan.FromSeconds(10));

            var form = HttpUtility.ParseQueryString(handler.Seen.Single(s => s.Request.Method == System.Net.Http.HttpMethod.Post).Body!);
            Assert.Equal("## Why\n\nBecause **this** matters\n\n- one\n- two", form["description"]);
        }, handler);
    }

    /// <summary>The bar's Table button puts a table at the caret, Tab walks its cells, and
    /// GitLab is sent the pipe table it reads.</summary>
    [Fact]
    public void TheTableButtonInsertsATableSentAsAPipeTable()
    {
        var handler = Routes();
        Hosted((repo, session, app) =>
        {
            app.OpenCreateMergeRequest(Feature);
            session.PumpUntil(() => ById(session, "create-mr") is not null, TimeSpan.FromSeconds(10));
            session.PumpUntil(() => DescriptionHas(session, "Closes #1042"), TimeSpan.FromSeconds(15));
            session.PumpFrames(2);   // found in the tree is a frame short of laid out

            var editor = (RichTextEditorElement)ById(session, "create-mr-description")!;
            session.Window.Click(editor);
            session.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);
            session.Window.PressKey(KeyCode.Delete);
            session.PumpFrame();
            session.Window.Click(ById(session, "richtext-table")!);
            session.PumpFrame();
            Assert.NotNull(editor.State.CurrentTable);

            string[] cells = ["Step", "Who", "When", "Build", "CI", "Now", "Ship", "Me", "Later"];
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) session.Window.PressKey(KeyCode.Tab);
                session.Window.TypeText(cells[i]);
            }
            session.PumpFrame();

            session.Window.PressKey(KeyCode.Enter, KeyModifiers.Ctrl);
            session.PumpUntil(() => !app.CreateMergeRequestOpen, TimeSpan.FromSeconds(10));

            var form = HttpUtility.ParseQueryString(handler.Seen.Single(s => s.Request.Method == System.Net.Http.HttpMethod.Post).Body!);
            Assert.Equal(
                "| Step | Who | When |\n| --- | --- | --- |\n| Build | CI | Now |\n| Ship | Me | Later |",
                form["description"]);
        }, handler);
    }

    /// <summary>The description editor offers only what Markdown can say: no underline,
    /// colour or alignment for GitLab to drop.</summary>
    [Fact]
    public void TheDescriptionOffersOnlyMarkdownsFormats()
    {
        Hosted((repo, session, app) =>
        {
            app.OpenCreateMergeRequest(Feature);
            session.PumpUntil(() => ById(session, "create-mr") is not null, TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            var editor = (RichTextEditorElement)ById(session, "create-mr-description")!;
            Assert.Equal(HultUI.RichText.Markdown.RichMarkdown.Features, editor.Features);
            Assert.NotNull(ById(session, "richtext-bold"));
            Assert.Null(ById(session, "richtext-underline"));
            Assert.Null(ById(session, "richtext-aligncenter"));
        });
    }

    private static bool DescriptionHas(HeadlessSession session, string text) =>
        ById(session, "create-mr-description") is RichTextEditorElement d && d.Document.ToPlainText().Contains(text);

    private static HultUI.Core.RichText.BlockKind BlockKindOf(RichTextEditorElement editor, int block) =>
        editor.Document[block].Kind;

    [Fact]
    public void AnExistingMergeRequestOpensInsteadOfASecondCard()
    {
        Hosted((_, session, app) =>
        {
            // The recorded !412 is from this very branch: its chip on the Branches row says
            // the merge requests have loaded.
            session.Window.PressKey(KeyCode.Digit5);
            session.PumpUntil(() => ById(session, "mr-chip") is not null, TimeSpan.FromSeconds(15));
            app.OpenCreateMergeRequest(Feature);
            session.PumpFrames(2);
            Assert.False(app.CreateMergeRequestOpen);
            Assert.True(app.MergeRequestsOpen);
            Assert.True(TreeHas(session, "!412 already exists for"));
        }, Routes(withMr: true));
    }

    [Fact]
    public void ThePushConfirmQuotesTheProtectedRuleAndGreysTheLease()
    {
        Hosted((_, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            session.PumpUntil(() => TreeHas(session, "MAIN"), TimeSpan.FromSeconds(15));
            // Wait for the protection rules before asking, or the card is built without them.
            session.PumpUntil(() => Root(session) is { } r && LayoutProbe.Descendants<Element>(r, e => e.TooltipText?.StartsWith("main is protected") == true).Count > 0,
                TimeSpan.FromSeconds(15));

            session.Window.PressKey(KeyCode.P);   // the selected row is main, the current branch
            session.PumpFrames(2);

            Assert.True(TreeHas(session, "main is protected on gitlab.com — maintainers can push, force push is not allowed."));
            Assert.NotNull(ById(session, "dialog-check-off"));
            Assert.True(TreeHas(session, "— not allowed on this branch"));
            Assert.True(TreeHas(session, "Publish"));   // main has no upstream in this repo
            session.Window.PressKey(KeyCode.Escape);
        });
    }

    [Fact]
    public void TheComposerOffersOpenIssuesAndSaysWhatAClosingKeywordDoes()
    {
        Hosted((repo, session, app) =>
        {
            repo.Write("new.txt", "x\n");
            session.Window.PressKey(KeyCode.Digit4);
            session.PumpUntil(() => SummaryField(session) is not null, TimeSpan.FromSeconds(15));
            session.PumpFrames(2);
            var summary = SummaryField(session)!;

            session.Window.Click(summary);
            session.Window.TypeText("Fix #10");
            session.PumpUntil(() => ById(session, "issue-suggestions") is not null, TimeSpan.FromSeconds(15));
            Assert.True(TreeHas(session, "Split refunds across original tenders"));
            Assert.True(TreeHas(session, "Refund report is off by one day"));

            session.Window.PressKey(KeyCode.Enter);   // picks #1042, the first
            session.PumpFrames(2);
            Assert.Equal("Fix #1042 ", summary.Text);
            Assert.Null(ById(session, "issue-suggestions"));
            Assert.True(TreeHas(session, "Fix #1042 (\"Split refunds across original tenders\") will close the issue when this reaches main."));
        });
    }

    [Fact]
    public void ABareIssueNumberInAHistorySubjectLinksToTheProject()
    {
        Hosted((repo, session, app) =>
        {
            session.PumpUntil(() => Subject(session, "Fixes #7 for good") is { Links.Count: 1 }, TimeSpan.FromSeconds(15));
            var link = Subject(session, "Fixes #7 for good")!.Links![0];
            Assert.Equal("https://gitlab.com/acme/core-api/-/issues/7", link.Target);
            Assert.Equal("#7", "Fixes #7 for good".Substring(link.Start, link.Length));
        });
    }

    // ── Hosting ──────────────────────────────────────────────────────────────────────

    private static FakeGitLabHandler Routes(bool withMr = false) =>
        new FakeGitLabHandler()
            .Map($"GET {Api}/projects/acme%2Fcore-api", "project")
            .Map($"GET {Api}/projects/7781/protected_branches?per_page=100&page=1", "protected_branches")
            .MapJson($"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=1",
                withMr ? FakeGitLabHandler.Recording("merge_requests_page1") : "[]")
            .MapJson($"GET {Api}/projects/7781/pipelines?order_by=updated_at&sort=desc&per_page=100&page=1", "[]")
            .Map($"GET {Api}/projects/7781/issues?state=opened&order_by=updated_at&sort=desc&per_page=100&page=1", "issues")
            .MapJson($"GET {Api}/projects/7781/members/all?per_page=100&page=1", "[]")
            .MapJson($"GET {Api}/projects/7781/labels?with_counts=false&per_page=100&page=1", "[]")
            .Map($"POST {Api}/projects/7781/merge_requests", _ =>
                FakeGitLabHandler.Json(FakeGitLabHandler.Recording("merge_requests_page1").TrimStart('[').Split(",\n{")[0]))
            .Map($"GET {Api}/projects/7781/merge_requests/412/approvals", "approvals")
            .Map($"GET {Api}/projects/7781/merge_requests/412/discussions?per_page=100&page=1", "discussions")
            .MapJson($"GET {Api}/projects/7781/merge_requests/412/diffs?per_page=100&page=1", "[]");

    private static void Hosted(Action<TempRepo, HeadlessSession, JunctionApp> body, FakeGitLabHandler? handler = null)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("remote", "add", "origin", "https://gitlab.com/acme/core-api.git");
        repo.Git("commit", "--allow-empty", "-m", "Fixes #7 for good");
        // A feature branch one commit ahead of main, "published": its remote-tracking ref
        // exists and is its upstream, so the card has nothing to push first.
        repo.Git("checkout", "-q", "-b", Feature);
        repo.Write("src/Orders.cs", "class Orders {}\n");
        repo.Git("add", "src/Orders.cs");
        repo.Git("commit", "-q", "-m", "PC-1042: split a refund across tenders");
        repo.Git("update-ref", "refs/remotes/origin/" + Feature, "HEAD");
        repo.Git("branch", "-q", "--set-upstream-to=origin/" + Feature);
        repo.Git("update-ref", "refs/remotes/origin/main", "main");
        repo.Git("checkout", "-q", "main");
        handler ??= Routes();

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
            body(repo, session, app);
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

    private static TextInputElement? SummaryField(HeadlessSession session) =>
        Root(session) is { } root
            ? LayoutProbe.Descendants<TextInputElement>(root, t => t.Placeholder == "Summary (required)").FirstOrDefault()
            : null;

    private static TextElement? Subject(HeadlessSession session, string text) =>
        Root(session) is { } root
            ? LayoutProbe.Descendants<TextElement>(root, t => t.Content == text).FirstOrDefault()
            : null;

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
