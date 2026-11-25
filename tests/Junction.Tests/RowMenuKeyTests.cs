using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The Menu key and Shift+F10 in a focused list open the menu a right-click on the row the keyboard
/// is on opens (the list's current item's row, else its selected item's), under that row. All of
/// Junction's row menus are on rows, and the keys looked for a menu from the list up, never in its
/// rows: in History, which holds the focus, they opened nothing (plan 17 round 19, question 81).
/// The Files tree, the Commit tree and the Changes list hold no focus (nothing does in those views),
/// so their tests focus the list as History's is focused: what the keys open there then.
/// </summary>
[Collection("JunctionDiff")]
public class RowMenuKeyTests
{
    [Theory]
    [InlineData(KeyCode.F10, KeyModifiers.Shift)]
    [InlineData(KeyCode.Menu, KeyModifiers.None)]
    public void History_TheKeysOpenTheSelectedCommitsMenu_AsARightClickOnItsRowDoes(KeyCode key, KeyModifiers modifiers)
    {
        History(12, (session, list) =>
        {
            session.Window.PressKey(KeyCode.Down);
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);
            Assert.Equal(2, list.SelectedIndex);
            Assert.Equal(-1, list.CurrentIndex);
            var rightClick = RightClickLabels(session, Row(list, 2));
            Assert.Contains("Copy commit SHA", rightClick);

            session.Window.PressKey(key, modifiers);
            session.PumpFrames(2);

            var menu = Menu(session)!;
            Assert.Equal(rightClick, Labels(menu));
            AssertUnder(Row(list, 2), menu);
            Assert.Equal(0, Hovered(menu));
            Assert.Same(list, session.Window.Window.FocusedElement);

            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);
            Assert.Null(Menu(session));
            Assert.Same(list, session.Window.Window.FocusedElement);
        });
    }

    /// <summary>The selected commit scrolled out of view (the wheel moves the list, not the
    /// selection): the key scrolls it back in, the least, and opens its menu under it.</summary>
    [Fact]
    public void History_ACommitScrolledAway_IsScrolledBackIn_AndItsMenuOpensUnderIt()
    {
        History(60, (session, list) =>
        {
            Assert.Equal(0, list.SelectedIndex);
            var rightClick = RightClickLabels(session, Row(list, 0));
            list.ScrollOffsetY = 1_000_000f;   // to the end
            session.PumpFrames(2);
            Assert.True(list.ItemRect(0).Bottom < list.ScrollOffsetY, "row 0 is still in view");

            session.Window.PressKey(KeyCode.F10, KeyModifiers.Shift);
            session.PumpFrames(2);

            Assert.Equal(0f, list.ScrollOffsetY);
            var menu = Menu(session)!;
            Assert.Equal(rightClick, Labels(menu));
            AssertUnder(Row(list, 0), menu);
        });
    }

    /// <summary>A reader hears the keys' menu as it hears the row's menu opened from the row
    /// (d9bf8e25's <c>OpenContextMenu(row)</c>): a focus event on its first item, and on Escape one
    /// back on the commit.</summary>
    [Fact]
    public void History_AReaderHearsTheKeysMenu_AsTheRowsMenuOpenedFromTheRow()
    {
        if (!OperatingSystem.IsWindows()) return;
        History(12, (session, list) =>
        {
            if (!OperatingSystem.IsWindows()) return;   // for the analyzer: the reader is Windows' only
            using var reader = HeadlessUiaReader.Install(session.Window);
            for (int i = 0; i < 4; i++) session.PumpFrame(TimeSpan.FromMilliseconds(100));

            var byRow = Hear(session, reader, () => session.Window.Window.OpenContextMenu(Row(list, 0)));
            var byKey = Hear(session, reader, () => session.Window.PressKey(KeyCode.F10, KeyModifiers.Shift));

            Assert.Equal(byRow, byKey);
            Assert.EndsWith("| focus 'Checkout this commit (detached)'", byKey.Open);
            Assert.EndsWith($"| focus 'Commit 11, Junction Tests, {list.Items[0].ShortHash}'", byKey.Escape);
        });
    }

    /// <summary>The Files tree's cursor is its current item, and its rows are composite.</summary>
    [Fact]
    public void FilesTree_Focused_TheKeysOpenTheCursorRowsMenu_AsARightClickOnItDoes()
    {
        using var repo = TempRepo.CreateEmpty();
        for (int i = 0; i < 4; i++) repo.Write($"f{i}/x.txt", $"{i}\n");
        repo.Write("a.txt", "a\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "a tree");
        Host(repo, KeyCode.Digit7, s => Ready(s, "FileTreeRowItem", "CurrentIndex"), session =>
        {
            var list = List(session, "FileTreeRowItem")!;
            Assert.True(Get<bool>(list, "RowsAreComposite"));
            session.Window.PressKey(KeyCode.Up);   // onto the folder above the open file
            session.PumpFrames(3);
            int index = Get<int>(list, "CurrentIndex");
            Assert.Equal(-1, Get<int>(list, "SelectedIndex"));
            var rightClick = RightClickLabels(session, Row(list, index));
            Assert.Contains("Open folder", rightClick);

            FocusList(session, list);
            session.Window.PressKey(KeyCode.Menu);
            session.PumpFrames(2);

            var menu = Menu(session)!;
            Assert.Equal(rightClick, Labels(menu));
            AssertUnder(Row(list, index), menu);
        });
    }

    /// <summary>The Commit tree's rows have no menu: the keys open nothing, as a right-click there
    /// opens nothing, and the tree does not move.</summary>
    [Fact]
    public void CommitTree_Focused_TheKeysOpenNothing_AsARightClickOnTheCursorRowOpensNothing()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < 4; i++) repo.Write($"d{i}/x.txt", $"{i}\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "four folders");
        Host(repo, KeyCode.Digit3, s => Ready(s, "CommitTreeRowItem", "CurrentIndex"), session =>
        {
            var list = List(session, "CommitTreeRowItem")!;
            var box = Row(list, Get<int>(list, "CurrentIndex"), menu: false).WindowBorderBox;
            float x = box.X + box.Width / 3, y = box.Y + box.Height / 2;
            session.Window.MouseDown(x, y, MouseButton.Right);
            session.Window.MouseUp(x, y, MouseButton.Right);
            session.PumpFrames(2);
            Assert.Null(Menu(session));

            FocusList(session, list);
            float scroll = list.ScrollOffsetY;
            session.Window.PressKey(KeyCode.F10, KeyModifiers.Shift);
            session.PumpFrames(2);

            Assert.Null(Menu(session));
            Assert.Equal(scroll, list.ScrollOffsetY);
        });
    }

    /// <summary>The Changes list's caret is its selected item, and its rows are composite, with
    /// buttons in them: the row's own menu opens.</summary>
    [Fact]
    public void Changes_Focused_TheKeysOpenTheCaretRowsMenu_AsARightClickOnItDoes()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("a.txt", "a\n");
        repo.Write("b.txt", "b\n");
        repo.Write("c.txt", "c\n");
        Host(repo, KeyCode.Digit4, s => Ready(s, "ChangeListItem", "SelectedIndex"), session =>
        {
            var list = List(session, "ChangeListItem")!;
            Assert.True(Get<bool>(list, "RowsAreComposite"));
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(3);
            int index = Get<int>(list, "SelectedIndex");
            Assert.Equal(-1, Get<int>(list, "CurrentIndex"));
            var rightClick = RightClickLabels(session, Row(list, index));
            Assert.Contains("Stage", rightClick);
            Assert.Equal(index, Get<int>(list, "SelectedIndex"));

            FocusList(session, list);
            session.Window.PressKey(KeyCode.F10, KeyModifiers.Shift);
            session.PumpFrames(2);

            var menu = Menu(session)!;
            Assert.Equal(rightClick, Labels(menu));
            AssertUnder(Row(list, index), menu);
        });
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    private static void History(int commits, Action<HeadlessSession, VirtualListElement<GitCommit>> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < commits; i++) repo.Git("commit", "--allow-empty", "-q", "-m", $"Commit {i}");
        Host(repo, KeyCode.Digit2, s => List(s, "GitCommit") is { } l && ((IAccessibleItemSource)l).ItemCount == commits + 1
            && ReferenceEquals(s.Window.Window.FocusedElement, l),
            session => body(session, (VirtualListElement<GitCommit>)List(session, "GitCommit")!));
    }

    private static void Host(TempRepo repo, KeyCode view, Func<HeadlessSession, bool> ready, Action<HeadlessSession> body)
    {
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
            session.Window.PressKey(view);
            session.PumpUntil(() => ready(session), TimeSpan.FromSeconds(30));
            DiffScrollTests.Quiet(session, app);
            session.PumpFrames(3);
            body(session);
        }
        finally { app?.Dispose(); }
    }

    /// <summary>Focuses the list, as History's is focused: in the app nothing focuses it.</summary>
    private static void FocusList(HeadlessSession session, Element list)
    {
        list.IsFocusable = true;
        list.Focus();
        session.PumpFrame();
        Assert.Same(list, session.Window.Window.FocusedElement);
    }

    // ── Reading ──────────────────────────────────────────────────────────

    /// <summary>What a right-click on the row opens, closed again with Escape.</summary>
    private static string[] RightClickLabels(HeadlessSession session, Element row)
    {
        var box = row.WindowBorderBox;
        float x = box.X + box.Width / 3, y = box.Y + box.Height / 2;
        session.Window.MouseDown(x, y, MouseButton.Right);
        session.Window.MouseUp(x, y, MouseButton.Right);
        session.PumpFrames(2);
        var labels = Labels(Menu(session) ?? throw new Xunit.Sdk.XunitException("a right-click on the row opened no menu"));
        session.Window.PressKey(KeyCode.Escape);
        session.PumpFrames(2);
        Assert.Null(Menu(session));
        return labels;
    }

    /// <summary>The menu opens below the row's bottom-left corner.</summary>
    private static void AssertUnder(Element row, Element menu)
    {
        var r = row.WindowBorderBox;
        var m = menu.WindowBorderBox;
        Assert.Equal(r.X, m.X, 0.5f);
        Assert.Equal(r.Bottom, m.Y, 0.5f);
    }

    private static Element? Menu(HeadlessSession session) =>
        session.Window.OverlayContents.SingleOrDefault(e => e.GetType().Name == "ContextMenuElement");

    private static string[] Labels(Element menu)
    {
        var source = (IAccessibleItemSource)menu;
        return Enumerable.Range(0, source.ItemCount).Select(i => source.GetItemLabel(i) ?? "").ToArray();
    }

    private static int Hovered(Element menu) => (int)menu.GetType().GetProperty("HoveredIndex")!.GetValue(menu)!;

    /// <summary>The view's list of <paramref name="item"/>s, by the item type's name (some are the
    /// app's own private types).</summary>
    private static ScrollViewElement? List(HeadlessSession session, string item) =>
        LayoutProbe.Descendants<ScrollViewElement>(Root(session), e => e.GetType() is { IsGenericType: true } t
            && t.GetGenericTypeDefinition() == typeof(VirtualListElement<>) && t.GetGenericArguments()[0].Name == item).SingleOrDefault();

    private static T Get<T>(ScrollViewElement list, string property) => (T)list.GetType().GetProperty(property)!.GetValue(list)!;

    private static Rect ItemRect(ScrollViewElement list, int index) =>
        (Rect)list.GetType().GetMethod("ItemRect")!.Invoke(list, [index])!;

    /// <summary>The list is there and the row of the index its <paramref name="index"/> property
    /// names is realized.</summary>
    private static bool Ready(HeadlessSession session, string item, string index) =>
        List(session, item) is { } l && Get<int>(l, index) is >= 0 and var i && RowOrNull(l, i, menu: false) is not null;

    /// <summary>The realized row of item <paramref name="index"/>: the list's child whose middle is in
    /// the item's band (with <paramref name="menu"/>, the one with a menu).</summary>
    private static Element Row(ScrollViewElement list, int index, bool menu = true) =>
        RowOrNull(list, index, menu) ?? throw new Xunit.Sdk.XunitException($"row {index} is not realized");

    private static Element? RowOrNull(ScrollViewElement list, int index, bool menu)
    {
        var r = ItemRect(list, index);
        float mid = list.WindowContentBox.Y + r.Y - list.ScrollOffsetY + r.Height / 2;
        return list.Children.Skip(1).FirstOrDefault(e => (!menu || e.ContextMenuItems is not null)
            && e.WindowBorderBox.Height > 0
            && Math.Abs(e.WindowBorderBox.Y + e.WindowBorderBox.Height / 2 - mid) < r.Height / 2);
    }

    /// <summary>The events raised (kind and id, in order) and the names the focus events went to, at
    /// an opening and at the Escape after it.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static (string Open, string Escape) Hear(HeadlessSession session, HeadlessUiaReader reader, Action open)
    {
        reader.Clear();
        open();
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(100));
        string opened = Heard(reader);
        reader.Clear();
        session.Window.PressKey(KeyCode.Escape);
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(100));
        return (opened, Heard(reader));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string Heard(HeadlessUiaReader reader) =>
        string.Join(' ', reader.Raised.Select(e => $"{e.Kind}:{e.Id}")) + " | "
        + string.Join(' ', reader.FocusEvents().Select(id => $"focus '{(id is { } e ? reader.Name(e) : null)}'"));

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;
}
