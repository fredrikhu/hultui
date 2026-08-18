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

    /// <summary>The blank line git puts between subject and body is not drawn — the header
    /// already separates the two — but it is still in the message, so a copy of the whole
    /// thing round-trips what git stores rather than what the screen happened to show.</summary>
    [Fact]
    public void TheLeadingBlankLineIsNotDrawnButIsStillCopied()
    {
        Hosted((session, app) =>
        {
            Assert.DoesNotContain(MessageLabels(session), l => l.Content.Trim().Length == 0);

            var subject = MessageLabels(session).First(l => l.Content == Subject);
            var box = subject.Layout.AbsoluteContentBox;
            session.Window.Click(box.X + 4f, box.Y + box.Height / 2f);
            session.Window.KeyDown(KeyCode.A, KeyModifiers.Ctrl);
            session.Window.KeyDown(KeyCode.C, KeyModifiers.Ctrl);

            Assert.Equal($"{Subject}\n\nFirst body line.\nSecond body line.",
                session.Window.Clipboard.GetText());
        });
    }

    /// <summary>Blank lines *between* paragraphs are the author's, and stay. Without them
    /// every message reads as one block however it was written.</summary>
    [Fact]
    public void AParagraphBreakInsideTheBodyIsDrawn()
    {
        Hosted((session, app) =>
        {
            Assert.Contains(MessageLabels(session), l => l.Content.Trim().Length == 0);
        }, body: "First paragraph.\n\nSecond paragraph.");
    }

    // ── Collapsing the body ──────────────────────────────────────────────

    [Fact]
    public void TheBodyStartsShownAndTheButtonHidesIt()
    {
        Hosted((session, app) =>
        {
            Assert.Contains(MessageLabels(session), l => l.Content == "First body line.");

            Toggle(session).Click();
            session.PumpFrames(2);

            // The body is gone; the subject, which is the message's title rather than a
            // detail of it, stays.
            Assert.DoesNotContain(MessageLabels(session), l => l.Content == "First body line.");
            Assert.Contains(MessageLabels(session), l => l.Content == Subject);
        });
    }

    [Fact]
    public void CollapsedTheButtonSaysHowMuchIsHidden()
    {
        Hosted((session, app) =>
        {
            Toggle(session).Click();
            session.PumpFrames(2);

            // Two lines of prose, so it reads "2 more lines" — not 3, which is what
            // counting git's blank separator line would give. Collapsed, the button is
            // the only sign there is anything to open.
            Assert.NotNull(session.Window.FindByText("2 more lines"));
        });
    }

    [Fact]
    public void TheKeyTogglesItToo()
    {
        Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.M);
            session.PumpFrames(2);
            Assert.DoesNotContain(MessageLabels(session), l => l.Content == "First body line.");

            session.Window.PressKey(KeyCode.M);
            session.PumpFrames(2);
            Assert.Contains(MessageLabels(session), l => l.Content == "First body line.");
        });
    }

    /// <summary>The choice is a reading preference, so it outlives the commit it was made
    /// on — and the settings object the app was handed carries it back out.</summary>
    [Fact]
    public void TheChoiceIsRemembered()
    {
        Hosted((session, app) =>
        {
            Toggle(session).Click();
            session.PumpFrames(2);

            var settings = (JunctionSettingsData)typeof(JunctionApp)
                .GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
            Assert.False(settings.CommitMessageExpanded);
        });
    }

    /// <summary>A one-line commit has nothing to collapse, so there is no button — and the
    /// letter stays free.</summary>
    [Fact]
    public void AMessageWithNoBodyOffersNoToggle()
    {
        Hosted((session, app) =>
        {
            Assert.Null(session.Window.FindByText("Hide message"));
        }, body: null);
    }

    private static HeadlessElement Toggle(HeadlessSession session) =>
        session.Window.FindByText("Hide message")
        ?? throw new Xunit.Sdk.XunitException("the message has no collapse button");

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

    private static void Hosted(Action<HeadlessSession, JunctionApp> run, string? body = Body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write(IssueTrackerFile.FileName, """
            [issuetracker "Tracker"]
                regex = "ABCD-(\\d+)"
                url = "https://example.invalid/browse/ABCD-$1"
            """);
        repo.Write("a.txt", "a\n");
        repo.Git("add", "-A");
        if (body is null) repo.Git("commit", "-m", Subject);
        else repo.Git("commit", "-m", Subject, "-m", body);

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
            session.PumpUntil(() => MessageLabels(session).Any(l => l.Content == Subject),
                TimeSpan.FromSeconds(30));
            session.PumpFrames(4);

            run(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}
