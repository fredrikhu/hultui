using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Junction.Platform;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Reordering the sidebar's repository list, and the two ways out of it into the desktop.
///
/// <para>The move itself is asserted against the workspace's stored order rather than
/// against the rendered rows, because that list is what actually matters: it is what the
/// digit shortcuts index into and what is written to settings. The gap arithmetic gets
/// its own cases — dropping <i>below</i> a row you started above is the case where a
/// naive remove-then-insert lands one place short.</para>
/// </summary>
public class RepoOrderTests
{
    // MoveRepo is the whole model behind the drag: the handlers only turn a pointer
    // position into a gap index and hand it over.
    private static void Move(JunctionApp app, string path, int gap) =>
        typeof(JunctionApp)
            .GetMethod("MoveRepo", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, [path, gap]);

    [Fact]
    public void DraggingARepositoryUpwardsPutsItAtTheGap()
    {
        Hosted(3, (paths, _, app, ws) =>
        {
            Move(app, paths[2], 0);

            Assert.Equal([paths[2], paths[0], paths[1]], ws.RepoPaths);
        });
    }

    [Fact]
    public void DraggingDownwardsAccountsForTheRowLeavingItsOwnPlace()
    {
        Hosted(3, (paths, _, app, ws) =>
        {
            // Gap 3 is "after the last row", counted before the move. Removing first
            // shifts it, which is the off-by-one this exists to pin.
            Move(app, paths[0], 3);

            Assert.Equal([paths[1], paths[2], paths[0]], ws.RepoPaths);
        });
    }

    [Fact]
    public void DroppingARowBackOnItsOwnGapChangesNothing()
    {
        Hosted(3, (paths, _, app, ws) =>
        {
            var before = ws.RepoPaths.ToList();

            Move(app, paths[1], 1);
            Move(app, paths[1], 2);   // the gap just below itself — still its own place

            Assert.Equal(before, ws.RepoPaths);
        });
    }

    [Fact]
    public void AnOutOfRangeGapClampsInsteadOfThrowing()
    {
        Hosted(2, (paths, _, app, ws) =>
        {
            Move(app, paths[0], 99);

            Assert.Equal([paths[1], paths[0]], ws.RepoPaths);
        });
    }

    [Fact]
    public void ARepositoryThatIsNoLongerInTheWorkspaceIsLeftAlone()
    {
        Hosted(2, (paths, _, app, ws) =>
        {
            var before = ws.RepoPaths.ToList();

            Move(app, Path.Combine(Path.GetTempPath(), "junction-not-in-this-workspace"), 0);

            Assert.Equal(before, ws.RepoPaths);
        });
    }

    /// <summary>The new order is the stored order — a reorder that did not persist would
    /// come back undone next launch, which is worse than not offering the drag.</summary>
    [Fact]
    public void TheNewOrderIsWhatGetsSaved()
    {
        Hosted(3, (paths, _, app, ws) =>
        {
            Move(app, paths[2], 0);

            var stored = typeof(JunctionApp)
                .GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(app) as JunctionSettingsData;
            Assert.Equal([paths[2], paths[0], paths[1]],
                stored!.Workspaces.Single().RepoPaths);
        });
    }

    /// <summary>The context menu is where "open in Explorer" and "open in terminal" live,
    /// so the test is that they are actually offered — the launch itself is the desktop's
    /// job and not something to start during a test run.</summary>
    [Fact]
    public void TheRepositoryMenuOffersTheFileManagerAndATerminal()
    {
        Hosted(1, (paths, _, app, _) =>
        {
            var items = (IReadOnlyList<ContextMenuItem>)typeof(JunctionApp)
                .GetMethod("RepoMenuItems", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, [paths[0]])!;
            var labels = items.Select(i => i.Label).ToList();

            Assert.Contains(ShellLauncher.FileManagerLabel, labels);
            Assert.Contains("Open in terminal", labels);
        });
    }

    /// <summary>
    /// The drag itself, through the real pointer pipeline: press on the last row, move
    /// into the top half of the first, release. This is what proves the rows are actually
    /// draggable and that the handlers turn a pointer position into the right gap —
    /// everything above tests the model those handlers call.
    /// </summary>
    /// <remarks>
    /// Frames are pumped between the moves rather than using the <c>Drag</c> helper, which
    /// sends press, moves and release with nothing in between. Showing the insertion
    /// marker rebuilds the sidebar, and a rebuilt element has no laid-out box until a
    /// frame runs — so without the pump the drop hit-tests against a tree that has been
    /// replaced but not yet measured and lands on nothing. A real drag renders frames
    /// throughout; a test that skipped them would be failing on its own shortcut.
    /// </remarks>
    [Fact]
    public void DraggingTheRowOnScreenReordersTheList()
    {
        Hosted(3, (paths, session, app, ws) =>
        {
            var last = RowBox(app, paths[2]);
            var first = RowBox(app, paths[0]);
            float x = last.X + last.Width / 2f;

            session.Window.MouseDown(x, last.Y + last.Height / 2f);
            // Far enough to pass the 5px drag threshold.
            session.Window.MoveMouse(x, last.Y + last.Height / 2f - 20f);
            session.PumpFrame();
            // The top half of the first row is the gap above it.
            session.Window.MoveMouse(x, first.Y + first.Height * 0.25f);
            session.PumpFrame();
            // Re-read the first row's box: the marker that just appeared above it moved it.
            var settled = RowBox(app, paths[0]);
            session.Window.MouseUp(x, settled.Y + settled.Height * 0.25f);
            session.PumpFrame();

            Assert.Equal([paths[2], paths[0], paths[1]], ws.RepoPaths);
        });
    }

    /// <summary>The laid-out box of a repository's sidebar row, found by the key the
    /// sidebar gives it.</summary>
    private static Rect RowBox(JunctionApp app, string path)
    {
        var key = "repo:" + path;
        var row = Descendants(app.Mount()).FirstOrDefault(e => e.Key == key)
            ?? throw new Xunit.Sdk.XunitException($"no sidebar row keyed \"{key}\"");
        return row.Layout.AbsoluteBorderBox;
    }

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static void Hosted(int repoCount,
        Action<List<string>, HeadlessSession, JunctionApp, WorkspaceData> body)
    {
        var repos = new List<TempRepo>();
        try
        {
            for (int i = 0; i < repoCount; i++)
                repos.Add(TempRepo.CreateWithInitialCommit());
            var paths = repos.Select(r => r.Path).ToList();

            var workspace = new WorkspaceData
            {
                Id = "w1", Name = "Tests", RepoPaths = [.. paths],
            };
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
                body(paths, session, app, workspace);
            }
            finally { app?.Dispose(); }
        }
        finally
        {
            foreach (var r in repos) r.Dispose();
        }
    }
}
