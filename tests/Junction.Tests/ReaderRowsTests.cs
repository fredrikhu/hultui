using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The Files and Commit lists are virtual lists whose rows are tree items of their own
/// (<c>RowsAreComposite</c>), so a row is in the accessibility tree only as an element, and drawing
/// makes the elements after the frame's accessibility phase. A reader got none of the Commit view's
/// rows on opening it, and in Files a move of the cursor always a key behind, the last one never:
/// the tree of the frame had the rows as they were. The tree asks the list for its rows now.
/// </summary>
public class ReaderRowsTests
{
    private static void Host(KeyCode view, Func<JunctionApp, int> rows, Action<HeadlessSession, HeadlessAccessibilityBridge> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < 80; i++) repo.Write($"file{i:00}.txt", $"line {i}\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "Eighty files");

        using var session = HeadlessSession.Create(1400, 900);
        var bridge = session.Window.InstallAccessibilityBridge();
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
            session.Window.PressKey(view);
            var opened = app;
            session.PumpUntil(() => rows(opened) > 1, TimeSpan.FromSeconds(20));
            body(session, bridge);
        }
        finally { app?.Dispose(); }
    }

    private static int Children(JunctionApp app, string reference) =>
        ((HultUI.Core.Reactivity.Ref<HultUI.Core.Elements.ScrollViewElement>)typeof(JunctionApp)
            .GetField(reference, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(app)!).Current?.Children.Count ?? 0;

    /// <summary>The clock past whatever refresh is coming, as a reader at rest sees it.</summary>
    private static void Rest(HeadlessSession session)
    {
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
    }

    private static void AssertFresh(HeadlessSession session, HeadlessAccessibilityBridge bridge)
    {
        var diff = AccessibilityTreeDiffer.Diff(bridge.CurrentTree, session.Window.AccessibilityTree);
        Assert.True(diff.IsEmpty, $"stale: +{diff.Added.Count} -{diff.Removed.Count} "
            + string.Join(", ", diff.PropertyChanged.Select(c => $"{c.PropertyName} of {c.Node.Role} '{c.Node.Name}'")));
    }

    private static bool Has(AccessibilityNode? node, string name)
    {
        if (node is null) return false;
        if (node.Name?.Contains(name, StringComparison.Ordinal) == true) return true;
        foreach (var child in node.Children)
            if (Has(child, name)) return true;
        return false;
    }

    [Fact]
    public void TheCommitViewsRows_ReachAReader_OnOpening() =>
        Host(KeyCode.Digit3, app => Children(app, "_fileScrollRef"), (session, bridge) =>
        {
            Rest(session);

            Assert.True(Has(bridge.CurrentTree, "file05"), "no row in the tree");
            AssertFresh(session, bridge);
        });

    [Fact]
    public void AMoveInTheCommitView_ReachesAReader() =>
        Host(KeyCode.Digit3, app => Children(app, "_fileScrollRef"), (session, bridge) =>
        {
            Rest(session);
            for (int i = 0; i < 4; i++)
            {
                session.Window.PressKey(KeyCode.Down);
                session.PumpFrame();
            }
            Rest(session);

            AssertFresh(session, bridge);
        });

    [Fact]
    public void AMoveInFiles_IsInTheTreeOfItsFrame_NotAKeyBehind() =>
        Host(KeyCode.Digit7, app => Children(app, "_filesTreeScrollRef"), (session, bridge) =>
        {
            Rest(session);
            for (int i = 0; i < 50; i++)
            {
                session.Window.PressKey(KeyCode.Down);
                session.PumpFrame(TimeSpan.FromMilliseconds(120));
            }
            Rest(session);

            AssertFresh(session, bridge);
        });

    [Fact]
    public void AWheelInFiles_LeavesTheTreeWithTheRowsOnScreen() =>
        Host(KeyCode.Digit7, app => Children(app, "_filesTreeScrollRef"), (session, bridge) =>
        {
            Rest(session);
            for (int i = 0; i < 6; i++)
            {
                session.Window.Wheel(150, 300, 0, -1);
                session.PumpFrame(TimeSpan.FromMilliseconds(16));
            }
            Rest(session);

            AssertFresh(session, bridge);
        });
}
