using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The whole app, mounted headless against a real repository. Everything else in this
/// suite tests a layer; this tests that the layers still fit together — git runs on
/// background threads and posts results back through the synchronization context, and a
/// view that throws while building takes the window with it.
///
/// <para>Assertions go through the accessibility tree, which is also what a screen reader
/// walks: content that cannot be found here cannot be found by assistive technology
/// either. They are deliberately shallow — pinning exact chrome text would break on
/// every design pass, while "this view built, loaded, and shows the repository's real
/// content" would not.</para>
/// </summary>
public class JunctionAppHostTests
{
    private static JunctionSettingsData SettingsFor(TempRepo repo) =>
        new()
        {
            RepositoryPaths = [repo.Path],
            ActiveRepoPath = repo.Path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
            ActiveWorkspaceId = "w1",
        };

    /// <summary>Mounts the app over a two-commit repository and hands both to
    /// <paramref name="body"/>, tearing them down in the right order afterwards.</summary>
    private static void Hosted(Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("tracked.txt", "one\ntwo\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "a distinctive subject");

        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, SettingsFor(repo));
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();

            body(repo, session, app);
        }
        finally
        {
            app?.Dispose();
        }
    }

    /// <summary>
    /// Pumps until <paramref name="text"/> appears somewhere in an accessible name, or
    /// gives up with the tree in the failure message.
    ///
    /// <para>Substring rather than equality on purpose: a row's accessible name is the
    /// concatenation of its parts ("a distinctive subject, Junction Tests, af7fe36"), so
    /// <c>FindByText</c> would only ever match a whole row verbatim — including the
    /// commit hash, which changes every run.</para>
    ///
    /// <para>Views load asynchronously, so the first frame after a switch is legitimately
    /// empty; the wait is what makes these tests deterministic rather than a race.</para>
    /// </summary>
    private static void PumpUntilVisible(HeadlessSession session, string text)
    {
        try
        {
            session.PumpUntil(() => Contains(session, text), TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException(
                $"\"{text}\" never appeared in the accessibility tree.\n{e.Message}\n" +
                session.Window.DumpAccessibilityTree());
        }
    }

    /// <summary>
    /// True when some node in the accessibility tree carries <paramref name="text"/>.
    ///
    /// <para>This walks the tree itself rather than calling <c>HeadlessWindow.Find</c>,
    /// which returns a <c>HeadlessElement</c> and therefore drops any match whose element
    /// it cannot resolve — including the recycled rows of a virtualized list, which is
    /// exactly where the history view puts its commits.</para>
    /// </summary>
    private static bool Contains(HeadlessSession session, string text)
    {
        return Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true
            || n.Value?.Contains(text, StringComparison.Ordinal) == true);
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    [Fact]
    public void TheHistoryViewLoadsTheRepositorysCommits()
    {
        Hosted((repo, session, app) =>
        {
            Assert.Equal(JunctionView.History, app.View);
            PumpUntilVisible(session, "a distinctive subject");

            // Both commits, exposed as list items rather than anonymous boxes.
            Assert.Contains(Walk(session.Window.AccessibilityTree), n => n.Role == AccessibilityRole.List);
            Assert.Equal(2, Walk(session.Window.AccessibilityTree).Count(n => n.Role == AccessibilityRole.ListItem));
            Assert.True(Contains(session, "initial"), "the root commit is missing from the list");
        });
    }

    [Theory]
    [InlineData(KeyCode.Digit1, JunctionView.Overview)]
    [InlineData(KeyCode.Digit2, JunctionView.History)]
    [InlineData(KeyCode.Digit3, JunctionView.Commit)]
    [InlineData(KeyCode.Digit4, JunctionView.Changes)]
    [InlineData(KeyCode.Digit5, JunctionView.Branches)]
    [InlineData(KeyCode.Digit6, JunctionView.Files)]
    public void EveryViewBuildsWithoutThrowing(KeyCode key, JunctionView expected)
    {
        // A view that throws mid-build kills the frame, and with nothing else on screen
        // that is an unrecoverable window. Each is driven through the real key handler,
        // then given frames for its async load to arrive and build a second time.
        Hosted((repo, session, app) =>
        {
            session.Window.PressKey(key);
            session.PumpFrames(10);

            Assert.Equal(expected, app.View);
        });
    }

    [Fact]
    public void EveryViewIsReachableByAName()
    {
        // The nav rail is the app's primary navigation. Buttons without accessible names
        // are unreachable by assistive technology and unfindable by any future test.
        Hosted((repo, session, app) =>
        {
            var buttons = Walk(session.Window.AccessibilityTree)
                .Where(n => n.Role == AccessibilityRole.Button)
                .Select(n => n.Name ?? "")
                .ToList();

            foreach (var name in new[] { "Overview", "History", "Commit", "Changes", "Branches", "Files" })
                Assert.Contains(buttons, b => b.StartsWith(name, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void TheChangesViewShowsAnEditedFile()
    {
        Hosted((repo, session, app) =>
        {
            repo.Write("tracked.txt", "one\nEDITED\n");

            session.Window.PressKey(KeyCode.Digit4);
            PumpUntilVisible(session, "tracked.txt");
        });
    }

    [Fact]
    public void TheBranchesViewShowsABranchCreatedOnDisk()
    {
        Hosted((repo, session, app) =>
        {
            repo.Git("branch", "feature/from-a-test");

            session.Window.PressKey(KeyCode.Digit5);
            PumpUntilVisible(session, "from-a-test");
        });
    }

    [Fact]
    public void TheSidebarTogglesWithoutLosingTheView()
    {
        // Collapsing the sidebar changes the content width, which re-runs the responsive
        // breakpoint check — the path that used to drop every icon on a scale change.
        Hosted((repo, session, app) =>
        {
            PumpUntilVisible(session, "a distinctive subject");

            session.Window.PressKey(KeyCode.Backtick);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Backtick);
            session.PumpFrames(3);

            Assert.Equal(JunctionView.History, app.View);
            Assert.True(Contains(session, "a distinctive subject"));
        });
    }

    [Fact]
    public void AResizeAcrossTheCompactBreakpointKeepsRendering()
    {
        // Below ~930dip the chrome switches to icon-only. Both sides have to build, and
        // crossing the line must not leave a half-rebuilt tree.
        Hosted((repo, session, app) =>
        {
            PumpUntilVisible(session, "a distinctive subject");

            session.Window.SetClientSize(820, 700);
            session.PumpFrames(3);
            session.Window.SetClientSize(1440, 900);
            session.PumpFrames(3);

            Assert.True(Contains(session, "a distinctive subject"));
        });
    }
}
