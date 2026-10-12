using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Theme;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The Files tree is a virtual list: only the rows on screen are elements, and the list keeps a
/// row until its item changes — the node, and whether it is the open file, under the cursor, or
/// an open folder. A key makes the rows it changes; the visible rows are kept between builds
/// while the tree and its open folders are unchanged. One test per input, each failing with
/// that input left out; and the tree's rows still answer to a reader as tree items.
/// </summary>
[Collection("JunctionDiff")]
public class FileTreeRowKeepTests
{
    [Fact]
    public void APlainMove_MakesTheTwoRowsItMovedBetween_AndKeepsTheRest()
    {
        Hosted((session, app) =>
        {
            var before = Rows(session);
            int made = app.FileTreeRowsMade;

            Keys(session, KeyCode.Down);   // a.txt → b.txt, which opens it

            Assert.Equal(2, app.FileTreeRowsMade - made);
            var after = Rows(session);
            Assert.Equal(before.Count, after.Count);
            Assert.Equal(before.Count - 2, before.Zip(after).Count(p => ReferenceEquals(p.First, p.Second)));
            Assert.Equal(Nocturne.Accent900, Row(session, "b.txt").BackgroundColor);
            Assert.NotEqual(Nocturne.Accent900, Row(session, "a.txt").BackgroundColor);
        });
    }

    /// <summary>The cursor onto a folder leaves the file open: its row loses the cursor and keeps
    /// the accent, the folder takes the cursor's tint.</summary>
    [Fact]
    public void TheCursorOnAFolder_LeavesTheOpenFileItsAccent()
    {
        Hosted((session, app) =>
        {
            int made = app.FileTreeRowsMade;

            Keys(session, KeyCode.Up);   // a.txt → src

            Assert.Equal(2, app.FileTreeRowsMade - made);
            Assert.Equal(Nocturne.Accent900, Row(session, "a.txt").BackgroundColor);
            Assert.Equal(Nocturne.Neutral800, Row(session, "src").BackgroundColor);
        });
    }

    /// <summary>A search puts the cursor on its first match without opening it; Space opens it.
    /// Neither row moves the cursor: the item's open file is what makes both again.</summary>
    [Fact]
    public void OpeningTheFileUnderTheCursor_MovesTheAccentToIt()
    {
        Hosted((session, app) =>
        {
            Search(session, "txt");
            Assert.Equal("docs/guide.txt", Field<string?>(app, "_filesCursorPath"));
            Assert.Equal(Nocturne.Neutral800, Row(session, "docs/guide.txt").BackgroundColor);
            Assert.Equal(Nocturne.Accent900, Row(session, "a.txt").BackgroundColor);

            Keys(session, KeyCode.Space);

            Assert.Equal("docs/guide.txt", Field<string?>(app, "_filesSelPath"));
            Assert.Equal(Nocturne.Accent900, Row(session, "docs/guide.txt").BackgroundColor);
            Assert.NotEqual(Nocturne.Accent900, Row(session, "a.txt").BackgroundColor);
        });
    }

    /// <summary>Right on a closed folder opens it where it stands: the cursor stays, the folder's
    /// row is made open by its item, and its children come into the rows.</summary>
    [Fact]
    public void OpeningAFolder_MakesItsRowOpen()
    {
        Hosted((session, app) =>
        {
            Keys(session, KeyCode.Up);
            Assert.False(Row(session, "src").AccessibleExpanded);

            Keys(session, KeyCode.Right);

            Assert.True(Row(session, "src").AccessibleExpanded, "the folder's row still says it is closed");
        });
    }

    [Fact]
    public void OpeningAFolder_ListsItsChildren()
    {
        Hosted((session, app) =>
        {
            Keys(session, KeyCode.Up);
            Assert.DoesNotContain("src/readme.txt", Visible(app));

            Keys(session, KeyCode.Right);

            Assert.Contains("src/readme.txt", Visible(app));
            Assert.NotNull(Row(session, "src/readme.txt"));
        });
    }

    [Fact]
    public void AThemeSwitch_MakesTheRowsInTheNewColours()
    {
        Hosted((session, app) =>
        {
            try
            {
                var before = Row(session, "a.txt").BackgroundColor;
                Assert.Equal(Nocturne.Accent900, before);

                Nocturne.Use("solarized-light");
                Rebuild(app);
                session.PumpFrames(3);

                var after = Row(session, "a.txt").BackgroundColor;
                Assert.Equal(Nocturne.Accent900, after);
                Assert.NotEqual(before, after);
            }
            finally
            {
                Nocturne.Use("nocturne");
            }
        });
    }

    /// <summary>A move to a screen of another scale retires the glyph rasters the rows hold
    /// (and the next one frees them): the rows are made again with glyphs cut for it.</summary>
    [Fact]
    public void ADisplayScaleChange_MakesTheRowsAgain()
    {
        Hosted((session, app) =>
        {
            try
            {
                int onScreen = Rows(session).Count;
                int made = app.FileTreeRowsMade;

                session.Window.SetScale(1.5f);
                session.PumpFrames(3);

                Assert.True(app.FileTreeRowsMade - made >= onScreen, $"{app.FileTreeRowsMade - made} rows made of {onScreen}");
            }
            finally
            {
                session.Window.SetScale(1f);
                session.PumpFrames(2);
            }
        });
    }

    /// <summary>A reader sees the rows on screen as it saw every row before: tree items, a
    /// folder's open state on it, in a scroll view.</summary>
    [Fact]
    public void TheRowsAreTreeItemsInAScrollView()
    {
        Hosted((session, app) =>
        {
            var tree = session.Window.AccessibilityTree;
            var container = Walk(tree).Single(n => n.Children.Any(c => c.Role == AccessibilityRole.TreeItem));
            Assert.Equal(AccessibilityRole.ScrollView, container.Role);
            var items = container.Children.Where(c => c.Role == AccessibilityRole.TreeItem).ToList();
            Assert.Equal(7, items.Count);   // docs, src and five files
            Assert.Equal(2, items.Count(i => i.Expanded == false));

            Keys(session, KeyCode.Up);
            Keys(session, KeyCode.Right);

            tree = session.Window.AccessibilityTree;
            container = Walk(tree).Single(n => n.Children.Any(c => c.Role == AccessibilityRole.TreeItem));
            Assert.Contains(container.Children, c => c.Role == AccessibilityRole.TreeItem && c.Expanded == true);
        });
    }

    /// <summary>Six hundred files in a search: the rows on screen are the only elements, and the
    /// cursor walked forty rows down is still one of them, inside the list's window.</summary>
    [Fact]
    public void AWalkPastTheWindow_KeepsTheCursorOnScreen()
    {
        Hosted((session, app) =>
        {
            Search(session, "file");
            var list = List(session);
            Assert.True(list.Children.Count < 60, $"{list.Children.Count} rows realized");

            for (int i = 0; i < 40; i++) Keys(session, KeyCode.Down);

            var cursor = Field<string?>(app, "_filesCursorPath")!;
            var row = Row(session, cursor);
            Assert.NotNull(row);
            var view = list.Layout.AbsoluteContentBox;
            float top = row!.Layout.AbsoluteY - list.ScrollOffsetY;
            Assert.True(top >= view.Y - 0.5f && top + row.Layout.Height <= view.Y + view.Height + 0.5f,
                $"the cursor's row is at {top:0.#}, outside {view.Y:0.#}-{view.Y + view.Height:0.#}");
        }, manyFiles: true);
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    /// <summary>Five files at the top, docs/ and src/ beside them (folders first, closed),
    /// a.txt open, at 1400 × 900; with <paramref name="manyFiles"/>, 600 more under many/.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp> body, bool manyFiles = false)
    {
        using var repo = TempRepo.CreateEmpty();
        foreach (var f in new[] { "a.txt", "b.txt", "c.txt", "d.txt", "e.txt", "docs/guide.txt", "docs/notes.md",
                     "src/app/main.cs", "src/app/util.cs", "src/lib/core.cs", "src/readme.txt" })
            repo.Write(f, f + "\n");
        if (manyFiles)
            for (int i = 0; i < 600; i++)
                repo.Write($"many/f{i / 20:00}/file{i:000}.cs", $"// {i}\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "a tree");

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
            session.Window.PressKey(KeyCode.Digit7);
            session.PumpUntil(() => app!.View == JunctionView.Files && Visible(app!).Count > 0
                && Field<string?>(app!, "_filesSelPath") == "a.txt" && Row(session, "a.txt") is not null,
                TimeSpan.FromSeconds(30));
            session.PumpFrames(3);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static void Search(HeadlessSession session, string query)
    {
        session.Window.PressKey(KeyCode.Slash);
        session.PumpFrames(3);
        session.Window.TypeText(query);
        session.PumpFrames(3);
    }

    private static void Keys(HeadlessSession session, KeyCode key)
    {
        session.Window.PressKey(key);
        session.PumpFrames(3);
    }

    private static void Rebuild(JunctionApp app)
    {
        for (var t = typeof(JunctionApp); t != null; t = t.BaseType)
            if (t.GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance) is { } m)
            { m.Invoke(app, null); return; }
        throw new Xunit.Sdk.XunitException("no MarkDirty to call");
    }

    private static T Field<T>(JunctionApp app, string name) =>
        (T)typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    /// <summary>The tree's visible rows by path, from the app's own list.</summary>
    private static List<string> Visible(JunctionApp app) =>
        Field<List<TreeNode<string>>>(app, "_filesFlat").Select(n => n.Path).ToList();

    private static VirtualListElement<JunctionApp.FileTreeRowItem> List(HeadlessSession session) =>
        LayoutProbe.Descendants<VirtualListElement<JunctionApp.FileTreeRowItem>>(Root(session), _ => true).Single();

    /// <summary>The realized rows, top to bottom (the list's first child is its spacer).</summary>
    private static List<BoxElement> Rows(HeadlessSession session) =>
        List(session).Children.Skip(1).Cast<BoxElement>().ToList();

    /// <summary>The realized row of a path (its key is the path, then its states).</summary>
    private static BoxElement? Row(HeadlessSession session, string path) =>
        LayoutProbe.Descendants<VirtualListElement<JunctionApp.FileTreeRowItem>>(Root(session), _ => true)
            .SingleOrDefault() is { } list
            ? list.Children.Skip(1).Cast<BoxElement>()
                .FirstOrDefault(r => r.Key is { } k && k[2..].Split('#')[0] == path)
            : null;

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var d in Walk(child))
                yield return d;
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;
}
