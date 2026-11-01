using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The Changes view's file list is one virtual list of tagged rows: section headers held at the top
/// (ChangesStickyHeaderTests), folders, files, conflicts and hidden files. Only the rows on screen
/// are elements and a key makes the rows whose item changed; at 300 changed files a key built
/// all of them, 3 MB a key.
/// </summary>
public class ChangesListTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    [Fact]
    public void AKey_MakesTheRowsItChanged_OutOfThreeHundred()
    {
        WithFiles(300, (repo, session, app) =>
        {
            var list = ChangeList(app);
            Assert.InRange(CountElements(list), 40, 260);
            int total = RowCount(app);
            Assert.True(total >= 306, $"{total} rows");

            // The first: the caret from the row it was on to the next, and no other.
            int made = app.ChangeRowsMade;
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);
            Assert.Equal(2, app.ChangeRowsMade - made);

            // On past the fold, a row scrolled in with most keys.
            made = app.ChangeRowsMade;
            const int Keys = 40;
            for (int i = 0; i < Keys; i++)
            {
                session.Window.PressKey(KeyCode.Down);
                session.PumpFrames(3);
            }
            Assert.InRange(app.ChangeRowsMade - made, Keys, 3 * Keys);
            Assert.InRange(CountElements(list), 40, 260);
        });
    }

    /// <summary>The caret moves in the view: the title bar, the sidebar, the toolbar and the composer
    /// show nothing of it, and were built for it, 305 KB a key with 21 files.</summary>
    [Fact]
    public void ACaretMove_BuildsTheView_NotTheChromeOrTheComposer()
    {
        WithFiles(12, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(3);
            int chrome = app.ChromeBuilds, composer = app.ComposerBuilds, builds = app.Builds;

            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Up);
            session.PumpFrames(3);
            // A file has nowhere to go on the right, its folder on the left; a folder closes and opens.
            session.Window.PressKey(KeyCode.Right);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Left);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Home);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Left);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Right);
            session.PumpFrames(3);
            Invoke(app, "SelectChange", "src/area1/file001.cs", false);
            session.PumpFrames(3);
            var row = ChangeList(app).Children.First(c => c.Key is { } k && k.StartsWith("u:", StringComparison.Ordinal) && !k.Contains("#s", StringComparison.Ordinal));
            session.Window.Click(row);
            session.PumpFrames(3);
            row = ChangeList(app).Children.First(c => c.Key is { } k && k.StartsWith("u:", StringComparison.Ordinal) && !k.Contains("#s", StringComparison.Ordinal));
            session.Window.Click(row, MouseButton.Right);
            session.PumpFrames(3);

            Assert.True(app.Builds > builds);
            Assert.Equal(chrome, app.ChromeBuilds);
            Assert.Equal(composer, app.ComposerBuilds);
        });
    }

    [Fact]
    public void AKey_KeepsTheRowsItsRowsWereFlattenedTo()
    {
        WithFiles(60, (repo, session, app) =>
        {
            var rows = Field<object>(app, "_changeListRows");
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);
            Assert.Same(rows, Field<object>(app, "_changeListRows"));
        });
    }

    [Fact]
    public void TheCaretWalksPastTheFold_AndBack_ClearOfTheHeaderHeldAtTheTop()
    {
        WithFiles(60, (repo, session, app) =>
        {
            var list = ChangeList(app);
            int rows = RowCount(app);
            for (int i = 0; i < rows; i++)
            {
                session.Window.PressKey(KeyCode.Down);
                session.PumpFrames(3);
                AssertCaretIsOnScreen(app, list);
            }
            Assert.True(list.ScrollOffsetY > 100f, $"the list stayed at {list.ScrollOffsetY}");
            float end = list.ScrollOffsetY;

            for (int i = 0; i < rows; i++)
            {
                session.Window.PressKey(KeyCode.Up);
                session.PumpFrames(3);
                AssertCaretIsOnScreen(app, list);
            }
            Assert.True(list.ScrollOffsetY < end);
        });
    }

    /// <summary>The caret's row is an element, drawn whole in the list and below the row held there.</summary>
    private static void AssertCaretIsOnScreen(JunctionApp app, ScrollViewElement list)
    {
        var caret = Field<object>(app, "_changeCaret");
        var key = (string)caret.GetType().GetProperty("Path")!.GetValue(caret)!;
        Element? row = null, held = null;
        foreach (var child in list.Children)
        {
            if (child.Key is { } k && k.Contains(key, StringComparison.Ordinal) && k.Contains("#f", StringComparison.Ordinal)) row = child;
            if (child.Position.Type == PositionType.Sticky && child.ScreenBorderBox.Y <= list.ScreenContentBox.Y + 0.5f
                && child.ScreenBorderBox.Bottom > list.ScreenContentBox.Y + 0.5f) held = child;
        }
        Assert.NotNull(row);
        var view = list.ScreenContentBox;
        var box = row!.ScreenBorderBox;
        Assert.True(box.Y >= view.Y - 0.5f && box.Bottom <= view.Bottom + 0.5f, $"the caret's row stands at {box.Y}–{box.Bottom} in {view.Y}–{view.Bottom}");
        if (held is not null)
            Assert.True(box.Y >= held.ScreenBorderBox.Bottom - 0.5f, $"the caret's row at {box.Y} is under the held row, down to {held.ScreenBorderBox.Bottom}");
    }

    [Fact]
    public void ARowOffScreen_IsNotInTheTree_TheHeaderHeldThereIs()
    {
        WithFiles(60, (repo, session, app) =>
        {
            session.Window.InstallAccessibilityBridge();
            var list = ChangeList(app);
            list.ScrollOffsetY = 900f;
            session.PumpFrame(TimeSpan.FromMilliseconds(200));
            session.PumpFrames(2);

            var names = Nodes(session.Window.AccessibilityTree).Select(n => n.Name).ToList();
            Assert.Contains("Stage all", names);
            Assert.DoesNotContain(names, n => n is not null && n.Contains("file006", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ARowKept_TakesTheSelectionAsItStandsNow()
    {
        WithFiles(12, (repo, session, app) =>
        {
            // The caret on the first file, then a Shift range of two more.
            var list = ChangeList(app);
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrames(3);

            // Every row in the selection offers all three; a row outside it, its own.
            var inside = list.Children.First(c => c.Key is { } k && k.StartsWith("u:", StringComparison.Ordinal) && k.Contains("#s", StringComparison.Ordinal));
            var check = inside.Children[0];
            Assert.Equal("Stage 3 files", check.TooltipText);
            var outside = list.Children.First(c => c.Key is { } k && k.StartsWith("u:", StringComparison.Ordinal) && !k.Contains("#s", StringComparison.Ordinal));
            Assert.Equal("Stage", outside.Children[0].TooltipText);

            session.Window.Click(check);
            session.PumpUntil(() => repo.Repo.GetStagedChanges().Count == 3, Wait);
        });
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void EveryRow_IsTheHeightItLaysOutTo(float scale)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        foreach (var name in new[] { "c0.txt", "sub/c1.txt", "s.txt", "u.txt", "d/u2.txt", "h.txt", "d/h2.txt" })
            repo.Write(name, "base\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "base");
        repo.Git("checkout", "-b", "side");
        repo.Write("c0.txt", "side\n");
        repo.Write("sub/c1.txt", "side\n");
        repo.Git("commit", "-am", "side");
        repo.Git("checkout", "-");
        repo.Write("c0.txt", "main\n");
        repo.Write("sub/c1.txt", "main\n");
        repo.Git("commit", "-am", "main");
        Assert.Throws<InvalidOperationException>(() => repo.Git("merge", "side"));   // it stops on the conflict
        repo.Write("s.txt", "staged\n");
        repo.Git("add", "s.txt");
        repo.Write("u.txt", "changed\n");
        repo.Write("d/u2.txt", "changed\n");
        repo.Git("update-index", "--assume-unchanged", "h.txt", "d/h2.txt");

        WithRepo(repo, 1400, 900, scale, (session, app) =>
        {
            // Three headers with a gap, one without, a folder and a file of each section, the hidden.
            Assert.True(AssertRowsAreTheirHeights(ChangeList(app)) >= 13);
        });
    }

    [Fact]
    public void AnEmptySectionsHeader_HasNoButton_AndTheHeightOfThat()
    {
        WithFiles(6, (repo, session, app) =>
        {
            Assert.NotNull(session.Window.FindByText("STAGED · 0"));
            AssertRowsAreTheirHeights(ChangeList(app));
        });
    }

    /// <summary>Each realized row is as tall as its tallest child and its padding. How many there are.</summary>
    private static int AssertRowsAreTheirHeights(ScrollViewElement list)
    {
        int rows = 0;
        foreach (var row in list.Children)
        {
            if (row.Children.Count == 0) continue;
            rows++;
            float tallest = 0f;
            foreach (var child in row.Children) tallest = MathF.Max(tallest, child.Layout.AbsoluteBorderBox.Height);
            var padding = row.Box.Padding;
            Assert.Equal(tallest + padding.Top + padding.Bottom, row.Layout.AbsoluteBorderBox.Height, 0.01f);
        }
        return rows;
    }

    [Fact]
    public void AFolderRow_SaysWhatItHoldsAndWhetherItIsOpen()
    {
        WithFiles(12, (repo, session, app) =>
        {
            var list = ChangeList(app);
            Element Folder() => list.Children.First(c => c.Key is { } k && k.Contains(":dir:src/area0", StringComparison.Ordinal));
            // Two files of one changed line each.
            var texts = HultUI.Headless.LayoutProbe.Descendants<TextElement>(Folder(), _ => true).Select(t => t.Content).ToList();
            Assert.Contains("+2", texts);
            Assert.Contains("−2", texts);
            Assert.True(Folder().AccessibleExpanded);

            session.Window.Click(Folder());
            session.PumpFrames(3);
            Assert.False(Folder().AccessibleExpanded);
        });
    }

    [Fact]
    public void NoChanges_IsARowOfItsHeight_AndComesAndGoes()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        WithRepo(repo, 1100, 700, 1f, (session, app) =>
        {
            var list = ChangeList(app);
            Assert.NotNull(session.Window.FindByText("No changes"));
            var empty = list.Children.First(c => c.Children.Count > 0);
            Assert.Equal(52f + empty.Children[0].Layout.AbsoluteBorderBox.Height, empty.Layout.AbsoluteBorderBox.Height, 0.01f);

            repo.Write("late.txt", "late\n");
            Invoke(app, "RefreshAll");
            session.PumpUntil(() => session.Window.FindByText("No changes") is null, Wait);
            session.PumpFrames(2);
            Assert.NotNull(session.Window.FindByText("late.txt"));

            repo.Git("clean", "-fdq");
            Invoke(app, "RefreshAll");
            session.PumpUntil(() => session.Window.FindByText("No changes") is not null, Wait);
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary><paramref name="files"/> modified files in six folders, the Changes view on screen.</summary>
    private static void WithFiles(int files, Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < files; i++) repo.Write($"src/area{i % 6}/file{i:000}.cs", $"line {i}\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "files");
        for (int i = 0; i < files; i++) repo.Write($"src/area{i % 6}/file{i:000}.cs", $"changed {i}\n");
        WithRepo(repo, 1100, 700, 1f, (session, app) => body(repo, session, app));
    }

    private static void WithRepo(TempRepo repo, int width, int height, float scale, Action<HeadlessSession, JunctionApp> body)
    {
        using var session = HeadlessSession.Create(width, height, scale);
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
            session.Window.PressKey(KeyCode.Digit4);
            var ready = app;
            session.PumpUntil(() => ChangeList(ready) is { Children.Count: > 1 }, Wait);
            session.PumpFrames(10);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static ScrollViewElement ChangeList(JunctionApp app)
    {
        var field = Field<object>(app, "_changeScrollRef");
        return (ScrollViewElement)field.GetType().GetProperty("Current")!.GetValue(field)!;
    }

    private static int RowCount(JunctionApp app) => ((System.Collections.ICollection)Field<object>(app, "_changeListRows")).Count;

    private static int CountElements(Element element)
    {
        int count = 1;
        foreach (var child in element.Children) count += CountElements(child);
        return count;
    }

    private static IEnumerable<AccessibilityNode> Nodes(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Nodes(child))
                yield return n;
    }

    private static T Field<T>(JunctionApp app, string field) =>
        (T)typeof(JunctionApp).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static void Invoke(JunctionApp app, string name, params object?[] args) =>
        typeof(JunctionApp).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(app, args);
}
