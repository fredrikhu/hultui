using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The files <c>git status</c> reports and <c>git diff</c> does not.
///
/// <para>status answers from the size cached in the index and calls a file modified the
/// moment that differs; diff opens the file and compares its cleaned contents. A file
/// checked out as CRLF and then rewritten with LF carries the committed content and the
/// wrong cached length, so the two disagree on every run until something stages it.</para>
///
/// <para>The Changes tab's badge counts status and its list came from diff, which is how a
/// repository came to advertise one change over a list reading "No changes". The fixture
/// below is that state, built the way a real working tree falls into it.</para>
/// </summary>
[Collection("JunctionDiff")]
public class StatusOnlyChangeTests
{
    /// <summary>The premise everything here rests on. Should git ever start answering both
    /// ways consistently, the reconciliation under test is dead weight — and this is the
    /// test that says so rather than leaving it to be puzzled over.</summary>
    [Fact]
    public void GitStatusAndGitDiffReallyDoDisagreeAboutSuchAFile()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        MakeStatusOnlyChange(repo);

        Assert.Contains(repo.Repo.GetStatus(), c => c.Path == Phantom);
        Assert.DoesNotContain(repo.Repo.GetWorkingDiff(staged: false), d => d.Path == Phantom);
    }

    [Fact]
    public void SuchAFileIsStillListedAsAnUnstagedChange()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        MakeStatusOnlyChange(repo);

        var change = Assert.Single(repo.Repo.GetUnstagedChanges(), c => c.Path == Phantom);
        Assert.Equal(FileChangeKind.Modified, change.Kind);
        // The working tree's, not the index's: nothing was ever staged here.
        Assert.Empty(repo.Repo.GetStagedChanges());
    }

    /// <summary>Staging it is the cure, which is the point of listing it at all: <c>git
    /// add</c> rewrites the cached size, and the file then leaves both lists because its
    /// content never differed in the first place.</summary>
    [Fact]
    public void StagingSuchAFileEndsTheDisagreement()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        MakeStatusOnlyChange(repo);

        Assert.True(repo.Repo.Stage(Phantom));

        Assert.Empty(repo.Repo.GetStatus());
        Assert.Empty(repo.Repo.GetUnstagedChanges());
        Assert.Empty(repo.Repo.GetStagedChanges());
    }

    /// <summary>The symptom as reported: the tab's badge said one change and the list said
    /// "No changes". The row is there now, and the pane beside it says what it found —
    /// a file with no diff entry used to render as "Select a file" next to the very file
    /// that had just been selected.</summary>
    [Fact]
    public void TheChangesViewShowsTheRowAndAPaneThatExplainsIt()
    {
        Hosted(MakeStatusOnlyChange, (repo, session, app) =>
        {
            PumpUntil(session, () => Contains(session, Phantom),
                $"the Changes view never listed {Phantom}");

            Assert.False(Contains(session, "No changes"));
            Assert.True(Contains(session, "No textual changes"),
                "the diff pane should say it found nothing, not ask for a selection");
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private const string Phantom = "crlf.txt";

    /// <summary>Leaves <see cref="Phantom"/> in the state under test: committed with LF
    /// endings, checked out as CRLF so the index caches the longer size, then written back
    /// with the LF bytes. Cleaned, it is the committed blob exactly.</summary>
    private static void MakeStatusOnlyChange(TempRepo repo)
    {
        const string lf = "a\nb\nc\nd\n";
        repo.Write(".gitattributes", "* text=auto\n");
        repo.Write(Phantom, lf);
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "a file committed with LF endings");

        // core.eol rather than the platform's default, so the fixture is the same
        // repository on Windows and on Linux.
        repo.Git("config", "core.eol", "crlf");
        File.Delete(Path.Combine(repo.Path, Phantom));
        repo.Git("checkout", "--", Phantom);
        repo.Write(Phantom, lf);
    }

    private static void PumpUntil(HeadlessSession session, Func<bool> done, string message)
    {
        try
        {
            session.PumpUntil(done, TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException(
                $"{message}.\n{e.Message}\n{session.Window.DumpAccessibilityTree()}");
        }
    }

    private static bool Contains(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true
            || n.Value?.Contains(text, StringComparison.Ordinal) == true);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    private static void Hosted(Action<TempRepo> arrange, Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        arrange(repo);

        using var session = HeadlessSession.Create(1440, 900);
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
            session.PumpFrame();
            session.Window.PressKey(KeyCode.Digit4);   // Changes
            session.PumpFrames(6);
            body(repo, session, app);
        }
        finally { app?.Dispose(); }
    }
}
