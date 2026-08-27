using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Worktrees in the sidebar. Opening one used to register it as a repository of its own, so
/// the list carried two entries sharing one name, one history and one set of remotes — a
/// repository that does not exist. These pin the replacement: the sidebar derives worktrees
/// from git and nests them under the repository, opening one registers nothing, and the
/// entries the digit shortcuts index are the rows actually on screen.
///
/// <para>The folding is asserted through the rendered rows rather than through the grouping
/// helper, because the row keys are what the reconciler and the pointer both work from — a
/// grouping that was right and a sidebar that still drew a second top-level row would pass a
/// test written against the helper alone.</para>
/// </summary>
public class SidebarWorktreeTests
{
    // ── Reflection into the app's own state ──────────────────────────────

    private static T Field<T>(JunctionApp app, string name) =>
        (T)typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static void Invoke(JunctionApp app, string name, params object?[] args) =>
        typeof(JunctionApp).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, args);

    private static string ActivePath(JunctionApp app) => Field<string>(app, "_activeRepoPath");

    // ── Row lookup ───────────────────────────────────────────────────────

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    /// <summary>Rows key themselves as <c>&lt;kind&gt;:&lt;path&gt;</c> plus a suffix of
    /// <c>#</c>-flags for whatever changes how they render (active, main, locked). Matching
    /// on the path and the flag boundary keeps a lookup for one path from answering for
    /// another — the worktree fixture's folder is literally the repository's plus a suffix,
    /// so a plain StartsWith would find the wrong row every time.</summary>
    private static bool KeyIs(Element e, string key) =>
        e.Key == key || (e.Key?.StartsWith(key + "#", StringComparison.Ordinal) ?? false);

    private static bool HasRow(JunctionApp app, string key) =>
        Descendants(app.Mount()).Any(e => KeyIs(e, key));

    private static Element Row(JunctionApp app, string key) =>
        Descendants(app.Mount()).FirstOrDefault(e => KeyIs(e, key))
        ?? throw new Xunit.Sdk.XunitException($"no sidebar row keyed \"{key}\"");

    /// <summary>Worktrees load off the UI thread, so the nested rows are legitimately absent
    /// on the first frame; waiting for the row is what makes this deterministic.</summary>
    private static void PumpUntilRow(HeadlessSession session, JunctionApp app, string key)
    {
        try
        {
            session.PumpUntil(() => HasRow(app, key), TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
            var keys = Descendants(app.Mount())
                .Select(e => e.Key).Where(k => k != null).Take(80);
            throw new Xunit.Sdk.XunitException(
                $"no row keyed \"{key}\" appeared. Keys present:\n  " + string.Join("\n  ", keys));
        }
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public void ALinkedWorktreeIsNestedUnderItsRepositoryRatherThanListedBesideIt()
    {
        WithWorktree((repo, linked, session, app, _) =>
        {
            PumpUntilRow(session, app, "wtchild:" + linked);

            Assert.True(HasRow(app, "repo:" + repo), "the repository lost its own row");
            // The whole point: no second top-level entry for the same repository.
            Assert.False(HasRow(app, "repo:" + linked),
                "the worktree is still drawn as a repository of its own");
        },
        registerWorktree: true);
    }

    [Fact]
    public void AWorktreeIsNestedEvenWhenNothingEverRegisteredIt()
    {
        // Nothing adds a worktree to the workspace any more, so this is the ordinary case:
        // the row exists because git reports the worktree, not because settings hold it.
        WithWorktree((repo, linked, session, app, ws) =>
        {
            PumpUntilRow(session, app, "wtchild:" + linked);

            Assert.Equal([repo], ws.RepoPaths);
        },
        registerWorktree: false);
    }

    [Fact]
    public void RegisteringOnlyTheWorktreeMakesItTheEntryAndNestsTheMainCheckout()
    {
        WithWorktree((repo, linked, session, app, _) =>
        {
            PumpUntilRow(session, app, "wtchild:" + repo);

            Assert.True(HasRow(app, "repo:" + linked), "the registered worktree lost its row");
            Assert.False(HasRow(app, "repo:" + repo),
                "the main checkout is drawn as a second repository");
        },
        registerWorktree: true, registerMain: false);
    }

    [Fact]
    public void ClickingTheNestedRowSwitchesToThatCheckout()
    {
        WithWorktree((repo, linked, session, app, _) =>
        {
            PumpUntilRow(session, app, "wtchild:" + linked);
            var box = Row(app, "wtchild:" + linked).Layout.AbsoluteBorderBox;

            session.Window.Click(box.X + box.Width / 2f, box.Y + box.Height / 2f);
            session.PumpFrames(3);

            Assert.Equal(linked, ActivePath(app));
        },
        registerWorktree: false);
    }

    /// <summary>The digit shortcuts index the rows on screen. With a folded worktree in the
    /// workspace's stored list, indexing that list instead would land on a row that is not
    /// there — silently selecting the wrong repository.</summary>
    [Fact]
    public void TheDigitShortcutsIndexTheRowsOnScreenNotTheStoredList()
    {
        using var other = TempRepo.CreateWithInitialCommit();
        WithWorktree((repo, linked, session, app, ws) =>
        {
            // Stored order: repo, its worktree, other. On screen: repo, other.
            ws.RepoPaths.Add(other.Path);
            session.PumpFrames(2);
            PumpUntilRow(session, app, "wtchild:" + linked);

            Invoke(app, "SelectRepoByIndex", 1);
            session.PumpFrames(2);

            Assert.Equal(other.Path, ActivePath(app));
        },
        registerWorktree: true);
    }

    /// <summary>Stepping with Alt+↑/↓ from inside a worktree starts at the repository it
    /// hangs under, rather than falling back to the first row in the list.</summary>
    [Fact]
    public void SteppingFromAWorktreeStartsAtTheRepositoryItHangsUnder()
    {
        using var other = TempRepo.CreateWithInitialCommit();
        WithWorktree((repo, linked, session, app, ws) =>
        {
            ws.RepoPaths.Add(other.Path);
            session.PumpFrames(2);
            PumpUntilRow(session, app, "wtchild:" + linked);

            Invoke(app, "OpenWorktree", linked);
            session.PumpFrames(3);
            Invoke(app, "NavigateRepo", 1);
            session.PumpFrames(2);

            Assert.Equal(other.Path, ActivePath(app));
        },
        registerWorktree: false);
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>
    /// A repository with one linked worktree beside it, mounted headless.
    /// </summary>
    /// <param name="registerWorktree">Put the worktree in the workspace too — what an older
    /// build's settings file looks like, and what the folding has to clean up.</param>
    /// <param name="registerMain">Put the repository itself in the workspace. Off, the
    /// worktree is the only entry and the main checkout nests under it.</param>
    private static void WithWorktree(
        Action<string, string, HeadlessSession, JunctionApp, WorkspaceData> body,
        bool registerWorktree = false, bool registerMain = true)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        // A sibling of the fixture, so the junction-git-* sweeper reaches it too.
        var linked = Path.Combine(Path.GetDirectoryName(repo.Path)!,
            Path.GetFileName(repo.Path) + "-wt");
        var added = repo.Repo.AddWorktree(linked, newBranch: "side");
        Assert.True(added.Success, added.Stderr);

        try
        {
            var paths = new List<string>();
            if (registerMain) paths.Add(repo.Path);
            if (registerWorktree) paths.Add(linked);

            var workspace = new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [.. paths] };
            var settings = new JunctionSettingsData
            {
                RepositoryPaths = [.. paths],
                ActiveRepoPath = paths[0],
                Workspaces = [workspace],
                ActiveWorkspaceId = "w1",
            };

            using var session = HeadlessSession.Create(1440, 900);
            JunctionApp? app = null;
            try
            {
                app = new JunctionApp(session.Application, session.Window.Window, settings);
                session.Application.SetRoot(app.Mount());
                session.PumpFrame();
                body(repo.Path, linked, session, app, workspace);
            }
            finally { app?.Dispose(); }
        }
        finally
        {
            repo.Repo.RemoveWorktree(linked, force: true);
            if (Directory.Exists(linked)) Directory.Delete(linked, recursive: true);
        }
    }
}
