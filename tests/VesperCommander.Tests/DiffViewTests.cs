using System.IO;
using System.Reflection;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Drives the real F3 diff view headlessly. Two regressions live here: Esc leaving the
/// view (dead while focus stayed on the unmounted pane), and the right-hand operand
/// resolving when the two panes are in different folders.
/// </summary>
public class DiffViewTests
{
    [Fact]
    public void EscapeLeavesTheDiffView()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\ntwo\n");
        t.WriteRight("a.txt", "one\nTWO\n");
        t.Open();

        t.OpenDiffOn("a.txt");
        Assert.True(t.DiffOpen);

        t.Press(KeyCode.Escape);

        Assert.False(t.DiffOpen);
    }

    [Fact]
    public void F3ClosesTheDiffViewItOpened()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\ntwo\n");
        t.WriteRight("a.txt", "one\nTWO\n");
        t.Open();

        t.OpenDiffOn("a.txt");
        Assert.True(t.DiffOpen);

        t.Press(KeyCode.F3);
        Assert.False(t.DiffOpen);
    }

    [Fact]
    public void NAndPWalkTheChangesWhileTheViewIsOpen()
    {
        using var t = new Fixture();
        // Two change blocks, far enough apart to survive the unchanged-run collapse.
        t.WriteLeft("a.txt", "1\n2\n3\n4\n5\n6\n7\n8\n9\n10\n11\n12\n");
        t.WriteRight("a.txt", "1\nX\n3\n4\n5\n6\n7\n8\n9\n10\nY\n12\n");
        t.Open();

        t.OpenDiffOn("a.txt");
        Assert.True(t.ChangeRows.Count >= 2);
        Assert.Equal(0, t.Field<int>("_diffChangeIdx"));

        t.Press(KeyCode.N);
        Assert.Equal(1, t.Field<int>("_diffChangeIdx"));

        t.Press(KeyCode.P);
        Assert.Equal(0, t.Field<int>("_diffChangeIdx"));
    }

    [Fact]
    public void TheArrowAndPageKeysScrollTheBody()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", Lines("left {0}", 200));
        t.WriteRight("a.txt", Lines("right {0}", 200));
        t.Open();

        t.OpenDiffOn("a.txt");
        Assert.Equal(0f, t.ScrollY);

        t.Press(KeyCode.Down);
        float line = t.ScrollY;
        Assert.True(line > 0, "Down did not scroll");

        t.Press(KeyCode.PageDown);
        Assert.True(t.ScrollY > line * 4, $"PageDown moved {t.ScrollY - line}px, barely more than a line");

        t.Press(KeyCode.End);
        float bottom = t.ScrollY;
        t.Press(KeyCode.PageDown);
        Assert.Equal(bottom, t.ScrollY);   // clamped, not run off the end

        t.Press(KeyCode.Home);
        Assert.Equal(0f, t.ScrollY);
    }

    [Fact]
    public void ALineStartsAtTheLeftEdgeOfItsCell()
    {
        using var t = new Fixture();
        // Short lines in a wide window: centred text would sit hundreds of px in.
        t.WriteLeft("a.txt", "one\n");
        t.WriteRight("a.txt", "two\n");
        t.Open();

        t.OpenDiffOn("a.txt");

        var (cell, text) = t.FirstLineCell();
        Assert.Equal(cell.Layout.AbsoluteContentBox.X, text.Layout.AbsoluteBorderBox.X, 1);
        // The cell is the one being asserted about — a narrow one would pass trivially.
        Assert.True(cell.Layout.AbsoluteContentBox.Width > 200, "the diff cell is too narrow to prove anything");
    }

    [Fact]
    public void TheLineNumberIsRightAlignedInItsGutter()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\n");
        t.WriteRight("a.txt", "two\n");
        t.Open();

        t.OpenDiffOn("a.txt");

        var (gutter, number) = t.FirstLineGutter();
        Assert.Equal(gutter.Layout.AbsoluteContentBox.Right, number.Layout.AbsoluteBorderBox.Right, 1);
    }

    [Fact]
    public void ThePaneGetsTheKeyboardBackAfterLeavingTheDiffView()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\ntwo\n");
        t.WriteLeft("b.txt", "other\n");
        t.WriteRight("a.txt", "one\nTWO\n");
        t.Open();

        t.OpenDiffOn("a.txt");
        t.Press(KeyCode.Escape);

        // Down is a plain pane key: it only moves the cursor if the rebuilt pane — not the
        // one the diff view replaced — is the focused element.
        int before = t.Cursor('L');
        t.Press(KeyCode.Down);
        Assert.Equal(before + 1, t.Cursor('L'));
    }

    [Fact]
    public void EscapeLeavesTheSyncView()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\n");
        t.WriteRight("b.txt", "two\n");
        t.Open();

        t.Press(KeyCode.F9);
        Assert.True(t.Field<bool>("_syncOpen"));

        t.Press(KeyCode.Escape);
        Assert.False(t.Field<bool>("_syncOpen"));
    }

    [Fact]
    public void TheSameNamedFileInTheOtherFolderIsPreferred()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\n");
        t.WriteRight("a.txt", "ONE\n");
        t.WriteRight("b.txt", "unrelated\n");
        t.Open();
        t.PutCursorOn('R', "b.txt");   // the other pane happens to be looking elsewhere

        t.OpenDiffOn("a.txt");

        Assert.Equal(Path.Combine(t.Right, "a.txt"), t.Field<string>("_diffRightPath"));
    }

    [Fact]
    public void WithNoSameNamedFileTheOtherPanesCursorIsCompared()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\ntwo\n");
        t.WriteRight("b.txt", "one\nTWO\n");
        t.Open();
        t.PutCursorOn('R', "b.txt");

        t.OpenDiffOn("a.txt");

        Assert.Equal(Path.Combine(t.Right, "b.txt"), t.Field<string>("_diffRightPath"));
        Assert.Equal("Ok", t.DiffState);   // a real diff, not the missing-file report
    }

    [Fact]
    public void WithNothingToPairAgainstTheMissingReportNamesTheExpectedPath()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\n");
        // The right pane holds only a folder, so its cursor is not a diffable file.
        Directory.CreateDirectory(Path.Combine(t.Right, "sub"));
        t.Open();
        t.PutCursorOn('R', "sub");

        t.OpenDiffOn("a.txt");

        Assert.Equal(Path.Combine(t.Right, "a.txt"), t.Field<string>("_diffRightPath"));
        Assert.Equal("Missing", t.DiffState);
    }

    private static string Lines(string format, int count) =>
        string.Concat(System.Linq.Enumerable.Range(1, count)
            .Select(i => string.Format(format, i) + "\n"));

    // ── harness ──────────────────────────────────────────────────────────
    private sealed class Fixture : System.IDisposable
    {
        public readonly string Left, Right;
        private HeadlessSession _session = null!;
        private CommanderApp _app = null!;

        public Fixture()
        {
            var root = Path.Combine(Path.GetTempPath(), "vdiff-" + System.Guid.NewGuid().ToString("N"));
            Left = Path.Combine(root, "left");
            Right = Path.Combine(root, "right");
            Directory.CreateDirectory(Left);
            Directory.CreateDirectory(Right);
        }

        public void WriteLeft(string name, string text) => File.WriteAllText(Path.Combine(Left, name), text);
        public void WriteRight(string name, string text) => File.WriteAllText(Path.Combine(Right, name), text);

        public void Open()
        {
            _session = HeadlessSession.Create(1240, 820);
            _app = new CommanderApp(_session.Application, _session.Window.Window, Left, Right);
            // The app loads the developer's own settings and session, so pin the two things
            // that would otherwise steer these tests: which folder each pane shows, and the
            // diff tool (VS Code shells out instead of opening the builtin view).
            Field<Settings>("_settings").DiffTool = DiffToolKind.Builtin;
            Call("NavigateTo", 'L', Left, null, false);
            Call("NavigateTo", 'R', Right, null, false);
            _session.Application.SetRoot(_app.Mount());
            _session.PumpUntil(() => Rows('L').Count > 0 && Rows('R').Count > 0);
        }

        /// <summary>Put the left pane's cursor on a file and press F3 the way a user would.</summary>
        public void OpenDiffOn(string name)
        {
            PutCursorOn('L', name);
            Press(KeyCode.F3);
        }

        public void Press(KeyCode key)
        {
            _session.Window.PressKey(key);
            _session.PumpFrame();
        }

        public bool DiffOpen => Field<bool>("_diffOpen");
        public int Cursor(char side) => (int)Call("Cursor", side)!;

        // The diff body reached through the app's own Ref, not window.Find — these are
        // plain Text/Box elements, and Find drops what it cannot resolve.
        private HultUI.Core.Elements.ScrollViewElement Body =>
            Field<HultUI.Core.Reactivity.Ref<HultUI.Core.Elements.ScrollViewElement>>("_diffScrollRef").Current
            ?? throw new Xunit.Sdk.XunitException("the diff body has no scroll view — is the state Ok?");

        public float ScrollY => Body.ScrollOffsetY;

        /// <summary>The left-hand text cell of the first rendered line, and the text in it.
        /// Row layout is gutter, text, marker, gutter, text.</summary>
        public (HultUI.Core.Elements.Element cell, HultUI.Core.Elements.Element text) FirstLineCell()
        {
            var cell = FirstRow().Children[1];
            return (cell, cell.Children[0]);
        }

        public (HultUI.Core.Elements.Element gutter, HultUI.Core.Elements.Element number) FirstLineGutter()
        {
            var gutter = FirstRow().Children[0];
            return (gutter, gutter.Children[0]);
        }

        private HultUI.Core.Elements.Element FirstRow() => Body.Children[0].Children[0];
        public string DiffState => Field<object>("_diffState").ToString()!;
        public System.Collections.Generic.List<int> ChangeRows => Field<System.Collections.Generic.List<int>>("_diffChangeRows");

        public System.Collections.Generic.IReadOnlyList<FileEntry> Rows(char side) =>
            (System.Collections.Generic.IReadOnlyList<FileEntry>)Call("Rows", side)!;

        public void PutCursorOn(char side, string name)
        {
            var rows = Rows(side);
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Name == name) { Call("SetCursor", side, i); _session.PumpFrame(); return; }
            Assert.Fail($"{name} is not listed in the {side} pane");
        }

        public T Field<T>(string name) =>
            (T)typeof(CommanderApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_app)!;

        private object? Call(string method, params object?[] args) =>
            typeof(CommanderApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(_app, args);

        public void Dispose()
        {
            _session?.Dispose();
            (_app as System.IDisposable)?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(Left)!, true); } catch { }
        }
    }
}
