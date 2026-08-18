using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The commit message in the Commit tab: its whole text is on screen, it can be selected
/// across the subject/body break and copied, and any issue reference the repository's
/// <c>.issuetracker</c> recognises is a link.
/// </summary>
[Collection("JunctionDiff")]
public class CommitMessageTests
{
    private const string Subject = "Subject about ABCD-4291";
    private const string Body = "First body line.\nSecond body line.";

    [Fact]
    public void TheWholeMessageIsOnScreenAndSelectable()
    {
        Hosted((session, app) =>
        {
            var labels = MessageLabels(session);
            Assert.Contains(labels, l => l.Content == Subject);
            Assert.Contains(labels, l => l.Content == "First body line.");
            Assert.Contains(labels, l => l.Content == "Second body line.");
            Assert.All(labels, l => Assert.True(l.Selectable));
        });
    }

    [Fact]
    public void SelectAllThenCopyYieldsTheWholeMessage()
    {
        Hosted((session, app) =>
        {
            var subject = MessageLabels(session).First(l => l.Content == Subject);
            var box = subject.Layout.AbsoluteContentBox;
            session.Window.Click(box.X + 4f, box.Y + box.Height / 2f);
            session.PumpFrame();

            session.Window.KeyDown(KeyCode.A, KeyModifiers.Ctrl);
            session.Window.KeyDown(KeyCode.C, KeyModifiers.Ctrl);

            var copied = session.Window.Clipboard.GetText() ?? "";
            Assert.Contains(Subject, copied);
            Assert.Contains("First body line.", copied);
            Assert.Contains("Second body line.", copied);
        });
    }

    [Fact]
    public void AnIssueReferenceBecomesALink()
    {
        Hosted((session, app) =>
        {
            var subject = MessageLabels(session).First(l => l.Content == Subject);
            var link = Assert.Single(subject.Links!);

            Assert.Equal("https://example.invalid/browse/ABCD-4291", link.Target);
            Assert.Equal("ABCD-4291", Subject.Substring(link.Start, link.Length));
            // And it is painted as one, rather than only being clickable.
            Assert.Contains(subject.Spans!, s => s.Start == link.Start && s.Length == link.Length);
        });
    }

    [Fact]
    public void ClickingTheLinkCallsTheHandlerOnceWithItsUrl()
    {
        Hosted((session, app) =>
        {
            var subject = MessageLabels(session).First(l => l.Content == Subject);
            var link = subject.Links![0];

            // Swap the handler for one that records rather than launching a browser.
            var opened = new List<string>();
            subject.LinkActivated = opened.Add;

            var box = subject.Layout.AbsoluteContentBox;
            float mid = HultUI.Core.Rendering.TextMeasure.MeasureWidth(
                Subject[..(link.Start + link.Length / 2)], subject.ResolvedInherited);
            session.Window.Click(box.X + mid, box.Y + box.Height / 2f);
            session.PumpFrame();

            Assert.Equal(["https://example.invalid/browse/ABCD-4291"], opened);
        });
    }

    [Fact]
    public void TextOutsideALinkIsNotClickable()
    {
        Hosted((session, app) =>
        {
            var subject = MessageLabels(session).First(l => l.Content == Subject);
            var opened = new List<string>();
            subject.LinkActivated = opened.Add;

            var box = subject.Layout.AbsoluteContentBox;
            session.Window.Click(box.X + 2f, box.Y + box.Height / 2f);   // the word "Subject"
            session.PumpFrame();

            Assert.Empty(opened);
        });
    }

    /// <summary>Every label taking part in the commit-message selection group.</summary>
    private static List<TextElement> MessageLabels(HeadlessSession session)
    {
        var found = new List<TextElement>();
        Walk(Root(session));
        return found;

        void Walk(Element? e)
        {
            if (e is null) return;
            if (e is TextElement { Selectable: true, SelectionIndex: >= 0 } te
                && te.Layout.AbsoluteBorderBox.Height > 0)
                found.Add(te);
            foreach (var c in e.Children) Walk(c);
        }
    }

    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write(IssueTrackerFile.FileName, """
            [issuetracker "Tracker"]
                regex = "ABCD-(\\d+)"
                url = "https://example.invalid/browse/ABCD-$1"
            """);
        repo.Write("a.txt", "a\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", Subject, "-m", Body);

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

            session.Window.PressKey(KeyCode.Digit3);   // Commit
            session.PumpUntil(() => MessageLabels(session).Any(l => l.Content == "Second body line."),
                TimeSpan.FromSeconds(30));
            session.PumpFrames(2);

            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}
