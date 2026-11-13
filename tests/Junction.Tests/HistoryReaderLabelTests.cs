using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// A reader is given History's items by their labels (a commit's subject, author and short hash), which the list
/// keeps while each item's commit is the same. What else a row shows — the graph, the remotes, the stats pages, the
/// compact chrome, the query — makes the rows on screen again through <c>InvalidateRows</c>, and since plan 17 round
/// 13 step 5 (a) that keeps the labels: with a reader listening, Junction's start made all 1,000 of them again at
/// each of its four row-shape changes (217 KB a time on a 1,910-commit repository), and again at every later one.
/// </summary>
public class HistoryReaderLabelTests
{
    [Fact]
    public void HistorysStart_AndItsRowShapeChanges_MakeTheLabelsOnce()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < 40; i++)
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

            // The first tree that names all 41 commits: the labels as they were first made.
            session.PumpUntil(() => List(session) is { } l && ((IAccessibleItemSource)l).ItemCount == 41
                && ReaderNames(bridge).Count == 41, TimeSpan.FromSeconds(30));
            var source = (IAccessibleItemSource)List(session);
            var first = Enumerable.Range(0, 41).Select(source.GetItemLabel).ToArray();
            Assert.Equal(first, ReaderNames(bridge));
            int made = app.HistoryRowsMade;

            // The rest of the start (whatever loads after the first tree), then the window across the compact width
            // and back: two row-shape changes, each making the rows on screen again.
            for (int i = 0; i < 10; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
            foreach (int width in new[] { 1000, 1600 })
            {
                int before = app.HistoryRowsMade;
                session.Window.SetClientSize(width, 900);
                for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(200));
                Assert.True(app.HistoryRowsMade > before, $"no row made again at {width} px");
            }
            Assert.True(app.HistoryRowsMade > made);

            source = (IAccessibleItemSource)List(session);
            for (int i = 0; i < first.Length; i++)
                Assert.True(ReferenceEquals(first[i], source.GetItemLabel(i)), $"the label of item {i} was made again");
            Assert.Equal(first, ReaderNames(bridge));
        }
        finally { app?.Dispose(); }
    }

    private static VirtualListElement<Git.GitCommit>? List(HeadlessSession session) =>
        LayoutProbe.Descendants<VirtualListElement<Git.GitCommit>>(Root(session), _ => true).SingleOrDefault();

    /// <summary>The names a reader has for History's items: the list node's item children.</summary>
    private static List<string?> ReaderNames(HeadlessAccessibilityBridge bridge)
    {
        var names = new List<string?>();
        if (bridge.CurrentTree is { } tree) Find(tree);
        return names;

        bool Find(AccessibilityNode node)
        {
            if (node.Role == AccessibilityRole.List && node.Children.Any(c => c.Role == AccessibilityRole.ListItem
                && c.Name?.Contains("Commit ", StringComparison.Ordinal) == true))
            {
                names.AddRange(node.Children.Where(c => c.Role == AccessibilityRole.ListItem).Select(c => c.Name));
                return true;
            }
            foreach (var child in node.Children)
                if (Find(child)) return true;
            return false;
        }
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;
}
