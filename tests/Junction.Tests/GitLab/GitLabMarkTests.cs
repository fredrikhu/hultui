using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Platform;
using HultUI.Headless;
using Junction.Git;
using Junction.GitLab;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// The GitLab mark after the repository's name in the title bar sits on the same vertical
/// centre as the name and the repository icon beside it. It did not: wrapped in a box that
/// measured taller than its glyph, it hung below the text.
/// </summary>
public class GitLabMarkTests
{
    [Fact]
    public void TheTitleBarMarkIsCentredOnTheRepositoryName()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("remote", "add", "origin", "https://gitlab.com/acme/core-api.git");
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
        var app = new JunctionApp(session.Application, session.Window.Window, settings,
            credentialStore: store, gitLabHandler: _ => new FakeGitLabHandler());
        try
        {
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);
            var root = (Element)typeof(HultUI.Window).GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session.Window.Window)!;
            var name = LayoutProbe.Descendants<TextElement>(root, t => t.Content == System.IO.Path.GetFileName(repo.Path)).MinBy(t => LayoutProbe.Painted(t).Y)!;   // the title bar sits highest
            var mark = LayoutProbe.Descendants<Element>(root, e => e.AutomationId == "gitlab-mark")
                .First(m => Math.Abs(LayoutProbe.Painted(m).Y - LayoutProbe.Painted(name).Y) < 40);   // the title bar's, not the sidebar's
            var nameBox = LayoutProbe.Painted(name);
            var markBox = LayoutProbe.Painted(mark);
            float nameMid = nameBox.Y + nameBox.Height / 2, markMid = markBox.Y + markBox.Height / 2;
            // The mark is the glyph itself, so its box is what is drawn — a wrapper that took the
            // row's height and parked the glyph at its bottom passed a test on the wrapper.
            Assert.True(markBox.Height <= 14f, $"the mark is a bare glyph, not a box: {markBox}");
            Assert.True(Math.Abs(nameMid - markMid) <= 1.5f,
                $"mark centre {markMid} vs name centre {nameMid} (mark {markBox}, name {nameBox})");
        }
        finally { app.Dispose(); }
    }
}
