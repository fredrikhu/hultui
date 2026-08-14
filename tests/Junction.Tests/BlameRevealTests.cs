using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// "Reveal in history" on a blame row.
///
/// <para>The blame gutter answers "which commit wrote this line". Opening that commit and
/// finding where it sits among its neighbours are different questions, and until now only
/// the first had an answer — the row and its menu both went to the Commit view.</para>
///
/// <para>The awkward part is timing: switching into History is usually what starts that
/// repository's log loading, so at the moment the menu item runs there is nothing to look
/// the hash up in. The request is therefore held and settled on a later build, which is
/// what most of these tests are really about.</para>
/// </summary>
public class BlameRevealTests
{
    private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void TheBlameMenuOffersBothDestinations()
    {
        Hosted((repo, session, app) =>
        {
            var labels = MenuLabels(app, Line(app, repo, 0));

            // Opening the commit and finding it in the log are separate entries: one goes to
            // the Commit view, the other to History.
            Assert.Contains(labels, l => l.StartsWith("Open commit", StringComparison.Ordinal));
            Assert.Contains("Reveal in history", labels);
        });
    }

    [Fact]
    public void RevealingACommitSwitchesToHistoryAndSelectsIt()
    {
        Hosted((repo, session, app) =>
        {
            var line = Line(app, repo, 2);            // written by the second commit
            Invoke(app, "RevealCommitInHistory", line.CommitHash);
            session.PumpUntil(() => app.View == JunctionView.History, TimeSpan.FromSeconds(20));
            session.PumpFrames(3);

            Assert.Equal(JunctionView.History, app.View);
            Assert.Equal(line.CommitHash, Field<string?>(app, "_selectedCommitHash"));
        });
    }

    [Fact]
    public void RevealingACommitFarDownTheListScrollsToIt()
    {
        // The whole point of "reveal" rather than "open": the commit has to end up on
        // screen. Selecting it and leaving the list at the top is the failure this guards.
        Hosted((repo, session, app) =>
        {
            var old = repo.Repo.Blame("story.txt")[0].CommitHash;   // the very first commit

            Invoke(app, "RevealCommitInHistory", old);
            session.PumpUntil(() => app.View == JunctionView.History, TimeSpan.FromSeconds(20));

            // Wait for what is asserted, not a frame count: the log for 60-odd commits
            // loads off-thread, and only then can the reveal settle and the post-frame
            // scroll run. A fixed PumpFrames passed alone and failed under a full suite.
            // The timeout falls through so the assertions report, not the pump.
            try
            {
                session.PumpUntil(
                    () => session.Window.DumpAccessibilityTree().Contains("first pass", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException) { }

            var list = Field<HultUI.Core.Reactivity.Ref<VirtualListElement<GitCommit>>>(app, "_commitListRef")
                .Current;
            Assert.NotNull(list);
            Assert.True(list!.ScrollOffsetY > 0,
                $"the list is still at the top ({list.ScrollOffsetY}) — the revealed commit is off screen");

            // Stronger than a non-zero offset, and the thing the reader actually cares
            // about: a VirtualList only realizes the rows in view, so the oldest commit's
            // subject reaching the tree means the row is genuinely on screen.
            Assert.Contains("first pass", session.Window.DumpAccessibilityTree(), StringComparison.Ordinal);
        }, extraCommits: 60);
    }

    [Fact]
    public void RevealingClearsASearchThatWouldHideTheRow()
    {
        Hosted((repo, session, app) =>
        {
            // A query left in the History search box filters the loaded log client-side. It
            // matches nothing here, so without clearing it the revealed row is filtered out
            // and the reveal lands on an empty list.
            typeof(JunctionApp).GetField("_historyQuery", Priv)!.SetValue(app, "zzzz-no-such-commit");

            var line = Line(app, repo, 0);
            Invoke(app, "RevealCommitInHistory", line.CommitHash);
            session.PumpUntil(() => app.View == JunctionView.History, TimeSpan.FromSeconds(20));
            session.PumpFrames(3);

            Assert.Equal("", Field<string>(app, "_historyQuery"));
            Assert.Equal(line.CommitHash, Field<string?>(app, "_selectedCommitHash"));
        });
    }

    [Fact]
    public void ACommitTheHistoryViewIsNotShowingSaysSoRatherThanDoingNothing()
    {
        Hosted((repo, session, app) =>
        {
            // A hash that parses like a commit but is in no log: the same shape as a commit
            // hidden by the Included-branches filter, which is the real case. Silence here
            // would be indistinguishable from a dead menu item.
            Invoke(app, "RevealCommitInHistory", new string('a', 40));
            session.PumpUntil(() => Field<string?>(app, "_toast") is not null, TimeSpan.FromSeconds(20));

            var toast = Field<string?>(app, "_toast");
            Assert.NotNull(toast);
            Assert.Contains("isn't in the history shown here", toast!, StringComparison.Ordinal);
            Assert.Contains("aaaaaaa", toast!, StringComparison.Ordinal);   // the short hash
        });
    }

    [Fact]
    public void AnUncommittedLineIsRefusedByBothDestinations()
    {
        Hosted((repo, session, app) =>
        {
            // git writes an all-zero hash for a line that is not committed yet.
            Invoke(app, "RevealCommitInHistory", new string('0', 40));
            session.PumpUntil(() => Field<string?>(app, "_toast") is not null, TimeSpan.FromSeconds(20));

            Assert.Contains("isn't committed yet", Field<string?>(app, "_toast")!, StringComparison.Ordinal);
            Assert.Equal(JunctionView.Files, app.View);   // no navigation happened
        });
    }

    // ── harness ──────────────────────────────────────────────────────────

    private static GitBlameLine Line(JunctionApp app, TempRepo repo, int index)
    {
        var lines = repo.Repo.Blame("story.txt");
        Assert.True(lines.Count > index, $"the fixture file has no line {index}");
        return lines[index];
    }

    private static string[] MenuLabels(JunctionApp app, GitBlameLine line)
    {
        var items = (System.Collections.IEnumerable)typeof(JunctionApp)
            .GetMethod("BlameMenuItems", Priv)!.Invoke(app, [line])!;
        return items.Cast<object>()
            .Select(i => (string?)i.GetType().GetProperty("Label")!.GetValue(i) ?? "")
            .ToArray();
    }

    private static void Invoke(JunctionApp app, string method, params object?[] args) =>
        typeof(JunctionApp).GetMethod(method, Priv)!.Invoke(app, args);

    private static T Field<T>(JunctionApp app, string name) =>
        (T)typeof(JunctionApp).GetField(name, Priv)!.GetValue(app)!;

    /// <summary>Three lines over two commits, opened on the Files view showing the file.
    /// <paramref name="extraCommits"/> piles unrelated commits on top, so the blamed ones
    /// end up far enough down the history that reaching them needs a scroll.</summary>
    private static void Hosted(Action<TempRepo, HeadlessSession, JunctionApp> body, int extraCommits = 0)
    {
        using var repo = TempRepo.CreateEmpty();
        repo.Write("story.txt", "first line\nsecond line\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "first pass");
        repo.Git("config", "user.name", "Second Author");
        repo.Git("config", "user.email", "second@example.invalid");
        repo.Write("story.txt", "first line\nsecond line\nthird line\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "second pass");

        for (var i = 0; i < extraCommits; i++)
        {
            repo.Write("filler.txt", $"{i}\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", $"filler {i}");
        }

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

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Digit7);   // Files
            session.PumpUntil(() => app!.View == JunctionView.Files, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);

            body(repo, session, app!);
        }
        finally { app?.Dispose(); }
    }
}
