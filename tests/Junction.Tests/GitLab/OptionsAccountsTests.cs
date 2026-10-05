using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Junction.GitLab;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// Phase 1 of the GitLab integration, hosted headlessly: the Options dialog, the accounts
/// pane driven the way a person drives it (paste a token, Test, Add account, Sign out), the
/// first-run strip, and — the rule every hosted surface stands behind — a repository with no
/// GitLab remote showing none of it.
/// </summary>
public class OptionsAccountsTests
{
    private const string Api = "/api/v4";
    private const string Token = "glpat-SECRET-1234567890";

    [Fact]
    public void CtrlCommaOpensOptionsAndEscCloses()
    {
        Hosted(gitlabOrigin: false, (_, session, app, _, _) =>
        {
            Assert.False(app.OptionsOpen);
            session.Window.PressKey(KeyCode.Comma, KeyModifiers.Ctrl);
            session.PumpFrame();
            Assert.True(app.OptionsOpen);
            Assert.True(TreeHas(session, "Settings save as you change them."));

            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrame();
            Assert.False(app.OptionsOpen);
        });
    }

    [Fact]
    public void ARepositoryWithoutAGitLabRemoteShowsNothingOfThis()
    {
        Hosted(gitlabOrigin: false, (_, session, _, _, _) =>
        {
            session.PumpFrames(2);
            Assert.Empty(Marks(session));
            Assert.False(TreeHas(session, "Connect to"));
        });
    }

    [Fact]
    public void TheFirstRunStripOffersToConnectAndDismissesForGood()
    {
        Hosted(gitlabOrigin: true, (_, session, _, settings, _) =>
        {
            session.PumpUntil(() => Marks(session).Count > 0, TimeSpan.FromSeconds(10));
            Assert.True(TreeHas(session, "Connect to gitlab.com to see merge requests and pipelines"));
            // Greyed: a host with no account.
            Assert.Contains(Marks(session), m => m.TooltipText!.StartsWith("Connect an account for gitlab.com"));

            session.Window.Click(Named(session, "Dismiss"));
            session.PumpFrame();

            Assert.False(TreeHas(session, "Connect to gitlab.com"));
            Assert.Contains("gitlab.com", settings.GitLabDismissedHosts);
        });
    }

    [Fact]
    public void ATokenPastedIntoTheHostCardBecomesAnAccountWithTheSecretInTheStore()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/user", "user")
            .Map($"GET {Api}/personal_access_tokens/self", "token_self");

        Hosted(gitlabOrigin: true, (_, session, app, settings, store) =>
        {
            OpenAccountsPane(session, app);
            Assert.True(TreeHas(session, "No accounts."));

            session.Window.Click(Named(session, "Add self-managed host"));
            session.PumpFrame();
            session.Window.Click(Input(session, "gitlab-host-url"));
            session.Window.TypeText("gitlab.com");          // appended to the prefilled https://
            session.Window.Click(Input(session, "gitlab-host-token"));
            session.Window.TypeText(Token);
            session.PumpFrame();

            session.Window.Click(Named(session, "Test"));
            session.PumpUntil(() => ById(session, "gitlab-host-verified") is not null, TimeSpan.FromSeconds(10));
            Assert.True(TreeHas(session, "Dana Osei"));
            Assert.True(TreeHas(session, "scopes api, write_repository"));

            session.Window.Click(Named(session, "Add account"));
            session.PumpUntil(() => settings.GitLabAccounts.Count == 1, TimeSpan.FromSeconds(10));

            var account = settings.GitLabAccounts[0];
            Assert.Equal("gitlab.com", account.Host);
            Assert.Equal("dosei", account.Username);
            Assert.Equal(Token, store.Read(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.TokenAccount));
            Assert.Equal(Token, handler.Seen[0].Request.Headers.GetValues("PRIVATE-TOKEN").Single());

            // The pane now shows the row, the strip is gone, and the mark is lit.
            session.PumpUntil(() => TreeHas(session, "@dosei"), TimeSpan.FromSeconds(10));
            Assert.True(TreeHas(session, "expires in"));
            Assert.True(TreeHas(session, "connected"));
            session.Window.Click(Named(session, "Done"));
            session.PumpUntil(() => !app.OptionsOpen, TimeSpan.FromSeconds(5));
            Assert.False(TreeHas(session, "Connect to gitlab.com"));
            Assert.Contains(Marks(session), m => m.TooltipText == "acme/core-api on gitlab.com");
        }, handler);
    }

    [Fact]
    public void AReadOnlyTokenShowsTheFailedRowAndAddsNothing()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/user", "user")
            .Map($"GET {Api}/personal_access_tokens/self", "token_self_readonly");

        Hosted(gitlabOrigin: true, (_, session, app, settings, store) =>
        {
            OpenAccountsPane(session, app);
            session.Window.Click(Named(session, "Add self-managed host"));
            session.PumpFrame();
            session.Window.Click(Input(session, "gitlab-host-url"));
            session.Window.TypeText("gitlab.com");
            session.Window.Click(Input(session, "gitlab-host-token"));
            session.Window.TypeText("glpat-readonly");
            session.PumpFrame();

            session.Window.Click(Named(session, "Test"));
            session.PumpUntil(() => ById(session, "gitlab-host-failed") is not null, TimeSpan.FromSeconds(10));

            Assert.True(TreeHas(session, "The token has read_api only."));
            Assert.Null(ById(session, "gitlab-host-verified"));
            Assert.Empty(settings.GitLabAccounts);
            Assert.Equal(0, store.Count);
        }, handler);
    }

    [Fact]
    public void ARefusedTokenNamesTheHostInTheFailedRow()
    {
        var handler = new FakeGitLabHandler().Map($"GET {Api}/user", "error_401", 401);

        Hosted(gitlabOrigin: true, (_, session, app, _, _) =>
        {
            OpenAccountsPane(session, app);
            session.Window.Click(Named(session, "Add self-managed host"));
            session.PumpFrame();
            session.Window.Click(Input(session, "gitlab-host-url"));
            session.Window.TypeText("gitlab.com");
            session.Window.Click(Input(session, "gitlab-host-token"));
            session.Window.TypeText("glpat-wrong");
            session.PumpFrame();

            session.Window.Click(Named(session, "Test"));
            session.PumpUntil(() => ById(session, "gitlab-host-failed") is not null, TimeSpan.FromSeconds(10));

            Assert.True(TreeHas(session, "401 — gitlab.com rejected the token."));
        }, handler);
    }

    [Fact]
    public void SigningOutAsksFirstThenForgetsTheAccountAndItsSecret()
    {
        var store = new InMemoryCredentialStore();
        store.Write(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.TokenAccount, Token);
        var settings = new JunctionSettingsData();
        settings.GitLabAccounts.Add(new GitLabAccountData
        {
            Host = "gitlab.com", WebBaseUrl = "https://gitlab.com", Username = "dosei", Name = "Dana Osei",
            Kind = GitLabTokenKind.Pat, Scopes = ["api"], ExpiresAt = DateTimeOffset.UtcNow.AddDays(200),
        });
        settings.GitLabDismissedHosts.Add("gitlab.com");

        Hosted(gitlabOrigin: true, (_, session, app, _, _) =>
        {
            session.PumpUntil(() => Marks(session).Count > 0, TimeSpan.FromSeconds(10));
            Assert.Contains(Marks(session), m => m.TooltipText == "acme/core-api on gitlab.com");
            Assert.False(TreeHas(session, "Connect to gitlab.com"));

            OpenAccountsPane(session, app);
            Assert.True(TreeHas(session, "@dosei"));
            session.Window.Click(Named(session, "Sign out of gitlab.com"));
            session.PumpFrame();
            Assert.True(TreeHas(session, "Sign out of gitlab.com?"));

            session.Window.PressKey(KeyCode.Enter);   // the confirm
            session.PumpUntil(() => settings.GitLabAccounts.Count == 0, TimeSpan.FromSeconds(10));

            Assert.Equal(0, store.Count);
            Assert.DoesNotContain("gitlab.com", settings.GitLabDismissedHosts);   // the strip may come back
            session.PumpUntil(() => TreeHas(session, "No accounts."), TimeSpan.FromSeconds(10));
        }, settings: settings, store: store);
    }

    // ── Hosting ──────────────────────────────────────────────────────────────────────

    private static void OpenAccountsPane(HeadlessSession session, JunctionApp app)
    {
        app.OpenOptions(OptionsSection.GitLabAccounts);
        session.PumpFrames(2);
        Assert.Equal(OptionsSection.GitLabAccounts, app.OptionsSectionShown);
    }

    private static void Hosted(bool gitlabOrigin,
        Action<TempRepo, HeadlessSession, JunctionApp, JunctionSettingsData, InMemoryCredentialStore> body,
        FakeGitLabHandler? handler = null, JunctionSettingsData? settings = null, InMemoryCredentialStore? store = null)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        if (gitlabOrigin) repo.Git("remote", "add", "origin", "https://gitlab.com/acme/core-api.git");
        handler ??= new FakeGitLabHandler();
        store ??= new InMemoryCredentialStore();
        settings ??= new JunctionSettingsData();
        settings.RepositoryPaths = [repo.Path];
        settings.ActiveRepoPath = repo.Path;
        settings.Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }];
        settings.ActiveWorkspaceId = "w1";

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
            if (gitlabOrigin) session.PumpUntil(() => ById(session, "gitlab-mark") is not null, TimeSpan.FromSeconds(15));
            body(repo, session, app, settings, store);
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

    private static List<Element> Marks(HeadlessSession session) =>
        Root(session) is { } root
            ? LayoutProbe.Descendants<Element>(root, e => e.AutomationId == "gitlab-mark")
            : [];

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

    private static TextInputElement Input(HeadlessSession session, string id) =>
        (TextInputElement?)ById(session, id) ?? throw new Xunit.Sdk.XunitException($"no input {id}");
}
