using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The diff names its rows for a screen reader: what happened to the line, its number, its
/// text. The list set no label, so the framework's fallback, the row's <c>ToString()</c>, was
/// read out: "DiffRow { Type = Unified, Index = 1, LineIndex = -1, Review = , … }" for every
/// one of them. The labels are made once per flattened row and kept with it, because the tree
/// asks for every item's label each time it is built.
/// </summary>
[Collection("JunctionDiff")]
public class DiffItemLabelTests
{
    [Fact]
    public void EachRowIsNamedByWhatHappenedToItsLine()
    {
        Hosted((session, app) =>
        {
            var names = ItemNames(session);
            Assert.Contains(names, n => n.StartsWith("hunk @@ -1,", StringComparison.Ordinal));
            Assert.Contains("line 1: class Notes", names);
            Assert.Contains("removed, line 3: \tint tabbed = 1;", names);
            Assert.Contains("added, line 3: \tint tabbed = 2;", names);
            Assert.Contains("added, line 4:     int value4 = 40; // alpha BETA gamma 4", names);
            Assert.DoesNotContain(names, n => n.StartsWith("DiffRow", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ASplitRowNamesBothSides()
    {
        Hosted((session, app) =>
        {
            Invoke(app, "SetDiffSplit", true);
            session.PumpFrames(2);

            var names = ItemNames(session);
            Assert.Contains("removed, line 4:     int value4 = 4; // alpha beta gamma 4; added, line 4:     int value4 = 40; // alpha BETA gamma 4", names);
            Assert.Contains("line 1: class Notes", names);
        });
    }

    /// <summary>Made once: a keystroke beside the diff rebuilds the window and the tree, and the
    /// tree reads the same label strings, not new ones.</summary>
    [Fact]
    public void TheLabelsAreMadeOnce()
    {
        Hosted((session, app) =>
        {
            var before = ItemNames(session).First(n => n.StartsWith("added, line 4:", StringComparison.Ordinal));
            int gen = DataGeneration(app);

            var summary = Get<Ref<TextInputElement>>(app, "_summaryRef").Current!;
            session.Window.Click(summary);
            session.Window.TypeText("x");
            session.PumpFrames(2);

            var after = ItemNames(session).First(n => n.StartsWith("added, line 4:", StringComparison.Ordinal));
            // A reload between the two reads makes new rows and new labels; say so if one came
            // (the fixture's git writes no longer start one: FixtureQuietTests).
            Assert.True(ReferenceEquals(before, after),
                $"the label was made again (the repository's data generation moved by {DataGeneration(app) - gen})");
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static List<string> ItemNames(HeadlessSession session)
    {
        var list = Walk(session.Window.AccessibilityTree)
            .Where(n => n.Role == AccessibilityRole.List && n.Children.Any(c => c.Name?.StartsWith("hunk ", StringComparison.Ordinal) == true
                || c.Name?.StartsWith("DiffRow", StringComparison.Ordinal) == true))
            .FirstOrDefault() ?? throw new Xunit.Sdk.XunitException("no diff list in the tree");
        return list.Children.Select(c => c.Name ?? "").ToList();
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node) =>
        node.Children.SelectMany(Walk).Prepend(node);

    /// <summary>The data generations of the app's repositories, summed: a reload bumps it.</summary>
    private static int DataGeneration(JunctionApp app) => Get<Dictionary<string, int>>(app, "_dataGen").Values.Sum();

    private static T Get<T>(JunctionApp app, string field) =>
        (T)typeof(JunctionApp).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static void Invoke(JunctionApp app, string name, params object?[] args) =>
        typeof(JunctionApp).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, args);

    private static ScrollViewElement? ListOrNull(JunctionApp app) =>
        Get<Ref<Element>>(app, "_diffBodyRef").Current as ScrollViewElement;

    private static string Notes(Func<int, string> line, string tabbed) =>
        "class Notes\n{\n" + tabbed + "\n" + string.Concat(Enumerable.Range(4, 30).Select(i => line(i) + "\n")) + "}\n";

    /// <summary>Changes, on a C# file with a line holding a tab and seven more changed word-wise.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("src/Notes.cs", Notes(i => $"    int value{i} = {i}; // alpha beta gamma {i}", "\tint tabbed = 1;"));
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add notes");
        repo.Write("src/Notes.cs", Notes(i => i <= 10
            ? $"    int value{i} = {i * 10}; // alpha BETA gamma {i}"
            : $"    int value{i} = {i}; // alpha beta gamma {i}", "\tint tabbed = 2;"));

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
            session.Window.PressKey(KeyCode.Digit4);   // Changes
            session.PumpUntil(() => ListOrNull(app!) is { } l && l.Children.Count > 1, TimeSpan.FromSeconds(30));
            session.PumpFrames(3);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
