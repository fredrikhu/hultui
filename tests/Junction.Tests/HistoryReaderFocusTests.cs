using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// History's arrows walk the commits from the window's key handler, and the view had nothing focused:
/// a reader speaks focus moves only, and Narrator and NVDA said nothing as the arrows walked it
/// (2026-11-15). The list holds the focus while nothing else does, so a reader is told the caret's
/// commit has it (the list's current item, <see cref="AccessibilityNode.CurrentItem"/>), at each key.
/// </summary>
public class HistoryReaderFocusTests
{
    [Fact]
    public void History_HoldsTheFocusOnItsList_AndEachArrowMovesTheListsCurrentItem()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < 12; i++)
            repo.Git("commit", "--allow-empty", "-q", "-m", $"Commit {i}");

        using var session = HeadlessSession.Create(1600, 900);
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
            session.Window.PressKey(KeyCode.Digit2);   // History
            session.PumpUntil(() => List(session) is { } l && ((IAccessibleItemSource)l).ItemCount == 13
                && ListNode(bridge) is not null, TimeSpan.FromSeconds(30));
            for (int i = 0; i < 5; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));

            var list = List(session)!;
            Assert.Same(list, session.Window.Window.FocusedElement);
            var node = ListNode(bridge)!;
            Assert.Equal("History", node.Name);
            Assert.Equal(list.Id, bridge.FocusedNode?.ElementId);

            int start = node.CurrentItem;
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);
            Assert.Same(list, session.Window.Window.FocusedElement);
            Assert.Equal(list.SelectedIndex, ListNode(bridge)!.CurrentItem);
            Assert.NotEqual(start, ListNode(bridge)!.CurrentItem);
            Assert.Equal(list.SelectedIndex, ListNode(bridge)!.Children.ToList().FindIndex(c => c.IsSelected == true));
        }
        finally { app?.Dispose(); }
    }

    private static VirtualListElement<Git.GitCommit>? List(HeadlessSession session) =>
        LayoutProbe.Descendants<VirtualListElement<Git.GitCommit>>(Root(session), _ => true).SingleOrDefault();

    private static AccessibilityNode? ListNode(HeadlessAccessibilityBridge bridge) =>
        bridge.CurrentTree is { } tree ? Find(tree) : null;

    private static AccessibilityNode? Find(AccessibilityNode node)
    {
        if (node.Role == AccessibilityRole.List && node.Children.Any(c => c.Name?.Contains("Commit ", StringComparison.Ordinal) == true))
            return node;
        foreach (var child in node.Children)
            if (Find(child) is { } found) return found;
        return null;
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;
}
