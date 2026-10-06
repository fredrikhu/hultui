using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Issue links in the History list. The log is where you go looking for the commit that
/// mentions a ticket, so the subject linkifies too — and clicking a link has to reach the
/// tracker without also opening the commit underneath it.
/// </summary>
[Collection("JunctionDiff")]
public class HistoryIssueLinkTests
{
    private const string Linked = "Older commit fixing ABCD-4291";
    private const string Plain = "Newer commit with no reference";
    private const string Url = "https://example.invalid/browse/ABCD-4291";

    [Fact]
    public void ASubjectWithAReferenceCarriesALink()
    {
        Hosted((session, app) =>
        {
            var subject = SubjectLabel(session, Linked);
            var link = Assert.Single(subject.Links!);

            Assert.Equal(Url, link.Target);
            Assert.Equal("ABCD-4291", Linked.Substring(link.Start, link.Length));
            Assert.Contains(subject.Spans!, s => s.Start == link.Start && s.Length == link.Length);
        });
    }

    [Fact]
    public void ASubjectWithoutOneIsLeftAlone()
    {
        Hosted((session, app) =>
        {
            var subject = SubjectLabel(session, Plain);
            Assert.True(subject.Links is null or { Count: 0 });
            Assert.True(subject.Spans is null or { Count: 0 });
        });
    }

    /// <summary>The row's own click handler must not also fire: following a link and opening
    /// a commit are different intentions, and the row fills the width of the list.</summary>
    [Fact]
    public void ClickingTheLinkOpensTheIssueWithoutSelectingTheCommit()
    {
        Hosted((session, app) =>
        {
            var subject = SubjectLabel(session, Linked);
            var opened = new List<string>();
            subject.LinkActivated = opened.Add;
            var before = SelectedHash(app);

            ClickAtOffset(session, subject, Linked, MidOf(subject.Links![0]));

            Assert.Equal([Url], opened);
            Assert.Equal(before, SelectedHash(app));
        });
    }

    /// <summary>And the rest of the subject still behaves like the row it sits in.</summary>
    [Fact]
    public void ClickingElsewhereInTheSubjectStillSelectsTheCommit()
    {
        Hosted((session, app) =>
        {
            var subject = SubjectLabel(session, Linked);
            var opened = new List<string>();
            subject.LinkActivated = opened.Add;
            var before = SelectedHash(app);

            ClickAtOffset(session, subject, Linked, 3);   // inside the word "Older"

            Assert.Empty(opened);
            Assert.NotEqual(before, SelectedHash(app));
        });
    }

    /// <summary>
    /// The rules are asked for once per rendered line, and every move of the selection builds
    /// the visible rows again. Whether the file changed is a question for the file system, two
    /// calls of it, and it was asked for each of those rows — most of what building a row cost.
    /// It is asked once a build now.
    /// </summary>
    [Fact]
    public void MovingTheSelectionLooksAtTheRulesFileOnceNotOnceARow()
    {
        Hosted((session, app) =>
        {
            int asks = app.IssueRulesAsks, looks = app.IssueRulesLooks;

            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(3);

            // Every one of the thirteen rows asked: the rows were built again. Each build looks
            // once — a move rebuilds once or twice as the commit's details arrive.
            Assert.True(app.IssueRulesAsks - asks >= 13, $"{app.IssueRulesAsks - asks} asks");
            Assert.InRange(app.IssueRulesLooks - looks, 1, 3);
        }, extraCommits: 10);
    }

    /// <summary>Asking once a build must not mean never: a rules file that appears while the
    /// app is open is in force from the build the repository watcher brings.</summary>
    [Fact]
    public void ARulesFileWrittenWhileOpenLinksTheSubjectsOnTheNextBuild()
    {
        Hosted((session, app, repo) =>
        {
            Assert.True(SubjectLabel(session, Linked).Links is null or { Count: 0 });

            repo.Write(IssueTrackerFile.FileName, Rules);
            session.PumpUntil(() => SubjectLabel(session, Linked).Links is { Count: 1 },
                TimeSpan.FromSeconds(30));
        }, withRules: false);
    }

    private static int MidOf(TextLink link) => link.Start + link.Length / 2;

    private static void ClickAtOffset(HeadlessSession session, TextElement label, string text, int offset)
    {
        var box = label.Layout.AbsoluteContentBox;
        float x = box.X + HultUI.Core.Rendering.TextMeasure.MeasureWidth(text[..offset], label.ResolvedInherited);
        session.Window.Click(x, box.Y + box.Height / 2f);
        session.PumpFrames(2);
    }

    private static string? SelectedHash(JunctionApp app) =>
        (string?)typeof(JunctionApp)
            .GetField("_selectedCommitHash", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app);

    private static TextElement SubjectLabel(HeadlessSession session, string subject) =>
        Labels(session).FirstOrDefault(l => l.Content == subject)
        ?? throw new Xunit.Sdk.XunitException($"no History row shows \"{subject}\"");

    private static List<TextElement> Labels(HeadlessSession session)
    {
        var found = new List<TextElement>();
        Walk(Root(session));
        return found;

        void Walk(Element? e)
        {
            if (e is null) return;
            if (e is TextElement te && te.Layout.AbsoluteBorderBox.Height > 0) found.Add(te);
            foreach (var c in e.Children) Walk(c);
        }
    }

    private const string Rules = """
        [issuetracker "Tracker"]
            regex = "ABCD-(\\d+)"
            url = "https://example.invalid/browse/ABCD-$1"
        """;

    private static void Hosted(Action<HeadlessSession, JunctionApp> body, int extraCommits = 0) =>
        Hosted((session, app, _) => body(session, app), extraCommits: extraCommits);

    private static void Hosted(Action<HeadlessSession, JunctionApp, TempRepo> body,
        bool withRules = true, int extraCommits = 0)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        if (withRules) repo.Write(IssueTrackerFile.FileName, Rules);
        repo.Write("a.txt", "a\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", Linked);
        repo.Write("b.txt", "b\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", Plain);
        for (int i = 0; i < extraCommits; i++)
            repo.Git("commit", "--allow-empty", "-m", $"Filler {i}");

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
            session.PumpUntil(() => Labels(session).Any(l => l.Content == Linked),
                TimeSpan.FromSeconds(30));
            session.PumpFrames(2);

            body(session, app, repo);
        }
        finally { app?.Dispose(); }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}
