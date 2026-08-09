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
    public void ALongLineScrollsSidewaysInsteadOfBeingCutOff()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", new string('L', 400) + "\n");
        t.WriteRight("a.txt", new string('R', 400) + "\n");
        t.Open();

        t.OpenDiffOn("a.txt");
        float max = t.MaxScrollX();
        Assert.True(max > 0, "a 400-character line left nothing to scroll into");

        var (cell, text) = t.FirstLineCell();
        float atRest = text.Layout.AbsoluteBorderBox.X;

        t.Press(KeyCode.Right);
        Assert.Equal(atRest - 28f, t.FirstLineCell().text.Layout.AbsoluteBorderBox.X, 1);

        t.Press(KeyCode.Right, KeyModifiers.Ctrl);
        Assert.Equal(atRest - max, t.FirstLineCell().text.Layout.AbsoluteBorderBox.X, 1);

        t.Press(KeyCode.Left, KeyModifiers.Ctrl);
        Assert.Equal(atRest, t.FirstLineCell().text.Layout.AbsoluteBorderBox.X, 1);
        Assert.Equal(cell.Layout.AbsoluteContentBox.X, t.FirstLineCell().text.Layout.AbsoluteBorderBox.X, 1);
    }

    [Fact]
    public void TheScrolledLineIsWidenedSoTextArrivesFromTheRight()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", new string('L', 400) + "\n");
        t.WriteRight("a.txt", new string('R', 400) + "\n");
        t.Open();

        t.OpenDiffOn("a.txt");
        float atRest = t.FirstLineCell().text.Layout.AbsoluteBorderBox.Width;

        t.Press(KeyCode.Right);

        // Shifting alone would leave a 28px hole at the right edge; the stretch has to give
        // the line back the width it was moved by.
        Assert.Equal(atRest + 28f, t.FirstLineCell().text.Layout.AbsoluteBorderBox.Width, 1);
    }

    [Fact]
    public void ALongLineFillsItsCellInsteadOfStoppingAtTheLastWordThatFits()
    {
        using var t = new Fixture();
        // Minified JSON: one enormous line whose only break opportunities are commas and
        // colons, far apart. This is the shape that made the text stop mid-cell.
        var json = "[" + string.Join(",", System.Linq.Enumerable.Range(0, 400)
            .Select(i => $"{{\"name\":\"thread_{i}\",\"ph\":\"M\",\"pid\":1,\"tid\":2}}")) + "]\n";
        t.WriteLeft("a.json", json);
        t.WriteRight("a.json", json.Replace("\"pid\":1", "\"pid\":9"));
        t.Open();

        t.OpenDiffOn("a.json");

        var (cell, text) = t.FirstLineCell();
        float drawn = t.DrawnWidth(text);
        float cellW = cell.Layout.AbsoluteContentBox.Width;
        // The line runs to thousands of characters, so the drawn part should reach the
        // cell's edge — within one character of it, since only whole characters are kept.
        // (The element box always fills the cell; it is the laid-out text that fell short.)
        Assert.True(drawn > cellW - 12f, $"the line stopped {cellW - drawn:0}px short of the cell edge");
        Assert.True(drawn <= cellW + 0.5f, "the line overflowed the cell");
    }

    [Fact]
    public void TheScrollbarThumbTracksTheOffset()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", new string('L', 4000) + "\n");
        t.WriteRight("a.txt", new string('R', 4000) + "\n");
        t.Open();

        t.OpenDiffOn("a.txt");
        var (track, thumb) = t.ScrollbarParts();
        Assert.True(thumb.Layout.AbsoluteBorderBox.Width < track.Layout.AbsoluteContentBox.Width,
            "a thumb that fills the track says there is nothing to scroll");
        Assert.Equal(track.Layout.AbsoluteContentBox.X, thumb.Layout.AbsoluteBorderBox.X, 1);

        t.Press(KeyCode.Right, KeyModifiers.Ctrl);   // all the way right

        var (track2, thumb2) = t.ScrollbarParts();
        Assert.Equal(track2.Layout.AbsoluteContentBox.Right,
            thumb2.Layout.AbsoluteBorderBox.Right, 1);
    }

    [Fact]
    public void DraggingTheScrollbarMovesTheText()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", new string('L', 4000) + "\n");
        t.WriteRight("a.txt", new string('R', 4000) + "\n");
        t.Open();

        t.OpenDiffOn("a.txt");
        float atRest = t.FirstLineCell().text.Layout.AbsoluteBorderBox.X;

        var (track, _) = t.ScrollbarParts();
        var box = track.Layout.AbsoluteContentBox;
        t.DragScrollbar(from: box.X + 20, to: box.X + box.Width / 2);

        Assert.True(t.FirstLineCell().text.Layout.AbsoluteBorderBox.X < atRest - 100,
            "dragging the thumb to the middle barely moved the text");
    }

    [Fact]
    public void AShortComparisonHasNoScrollbarAtAll()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\n");
        t.WriteRight("a.txt", "two\n");
        t.Open();

        t.OpenDiffOn("a.txt");

        Assert.Null(t.TryScrollbarTrack());
    }

    [Fact]
    public void AShortComparisonHasNowhereToScrollSideways()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\n");
        t.WriteRight("a.txt", "two\n");
        t.Open();

        t.OpenDiffOn("a.txt");

        Assert.Equal(0f, t.MaxScrollX());
        float atRest = t.FirstLineCell().text.Layout.AbsoluteBorderBox.X;
        t.Press(KeyCode.Right);
        Assert.Equal(atRest, t.FirstLineCell().text.Layout.AbsoluteBorderBox.X, 1);
    }

    [Fact]
    public void ClickingALineToSelectItDoesNotSwallowTheViewsOwnKeys()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", Lines("left {0}", 200));
        t.WriteRight("a.txt", Lines("right {0}", 200));
        t.Open();

        t.OpenDiffOn("a.txt");
        t.ClickFirstLine();
        // Without this the rest of the test proves nothing — a click that missed the label
        // leaves focus where it was and the keys would work for the old reason.
        Assert.Same(t.FirstLineCell().text, t.Focused);

        t.Press(KeyCode.Down);
        Assert.True(t.ScrollY > 0, "Down stopped scrolling once a line had focus");

        t.Press(KeyCode.Escape);
        Assert.False(t.DiffOpen, "Escape stopped closing the view once a line had focus");
    }

    [Fact]
    public void TheTwoSidesSelectSeparately()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\n");
        t.WriteRight("a.txt", "two\n");
        t.Open();

        t.OpenDiffOn("a.txt");

        var left = (HultUI.Core.Elements.TextElement)t.FirstLineCell().text;
        var right = (HultUI.Core.Elements.TextElement)t.FirstLineRightCell().text;
        Assert.True(left.Selectable);
        Assert.True(right.Selectable);
        Assert.NotNull(left.SelectionGroup);
        Assert.NotSame(left.SelectionGroup, right.SelectionGroup);
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

    // ── Syntax colouring ─────────────────────────────────────────────────
    [Fact]
    public void ARecognisedFileTypeGetsSyntaxColours()
    {
        using var t = new Fixture();
        t.WriteLeft("a.cs", "class Old { }\n");
        t.WriteRight("a.cs", "class New { }\n");
        t.Open();

        t.OpenDiffOn("a.cs");
        t.WaitForHighlight();

        var spans = ((HultUI.Core.Elements.TextElement)t.FirstLineCell().text).Spans;
        Assert.NotNull(spans);
        // "class" is a keyword, so it must come out in some colour of its own.
        Assert.Contains(spans!, s => s.Start == 0 && s.Length == 5 && s.Color != default);
    }

    [Fact]
    public void AnUnknownFileTypeStillMarksTheChangedRun()
    {
        using var t = new Fixture();
        t.WriteLeft("a.zzz", "value one\n");
        t.WriteRight("a.zzz", "value two\n");
        t.Open();

        t.OpenDiffOn("a.zzz");

        var spans = ((HultUI.Core.Elements.TextElement)t.FirstLineCell().text).Spans;
        var mark = Assert.Single(spans!);
        Assert.Equal("one", "value one".Substring(mark.Start, mark.Length));
        // The marker is a background wash; repainting the glyphs in a 28%-alpha tint would
        // make the changed word the *least* readable thing on the line.
        Assert.NotEqual(default, mark.Background);
        Assert.Equal(default, mark.Color);
    }

    [Fact]
    public void TheChangedRunKeepsItsSyntaxColourUnderTheMarker()
    {
        using var t = new Fixture();
        t.WriteLeft("a.cs", "class Old { }\n");
        t.WriteRight("a.cs", "class New { }\n");
        t.Open();

        t.OpenDiffOn("a.cs");
        t.WaitForHighlight();

        var spans = ((HultUI.Core.Elements.TextElement)t.FirstLineCell().text).Spans!;
        // Sorted and non-overlapping is the contract TextElement.Spans is documented under,
        // and the merge is where it would be broken.
        for (int i = 1; i < spans.Count; i++)
            Assert.True(spans[i].Start >= spans[i - 1].Start + spans[i - 1].Length,
                $"span {i} at {spans[i].Start} overlaps the one ending at {spans[i - 1].Start + spans[i - 1].Length}");
        // The keyword is outside the changed run, so it is coloured but not marked; the
        // changed word carries both.
        Assert.Contains(spans, s => s.Color != default && s.Background == default);
        Assert.Contains(spans, s => s.Color != default && s.Background != default);
    }

    [Fact]
    public void ColoursFromThePreviousComparisonDoNotLeakIntoTheNext()
    {
        using var t = new Fixture();
        t.WriteLeft("a.cs", "class Old { }\n");
        t.WriteRight("a.cs", "class New { }\n");
        t.WriteLeft("b.zzz", "plain one\n");
        t.WriteRight("b.zzz", "plain two\n");
        t.Open();

        t.OpenDiffOn("a.cs");
        t.Press(KeyCode.Escape);
        t.OpenDiffOn("b.zzz");
        t.Session.PumpFrames(5);

        // b.zzz has no grammar, so nothing here should be coloured. The generation key also
        // guards the harder case — a slow tokenize landing after the switch — but that one
        // can't be made to happen on demand, so this covers the reachable half.
        var spans = ((HultUI.Core.Elements.TextElement)t.FirstLineCell().text).Spans!;
        Assert.All(spans, s => Assert.Equal(default, s.Color));
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

        public void Press(KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
        {
            _session.Window.PressKey(key, modifiers);
            _session.PumpFrame();
        }

        public void ClickFirstLine()
        {
            var box = FirstLineCell().text.Layout.AbsoluteBorderBox;
            _session.Window.Click(box.X + 4, box.Y + box.Height / 2);
            _session.PumpFrame();
        }

        public float MaxScrollX() => (float)Call("DiffMaxScrollX")!;

        /// <summary>How wide the text a label actually painted is — not its element box,
        /// which always stretches to the cell. A selectable TextElement keeps the lines it
        /// laid out so it can hit-test a selection against them; this reads those.</summary>
        public float DrawnWidth(HultUI.Core.Elements.Element label)
        {
            var lines = (HultUI.Core.Rendering.TextLineBox[])
                typeof(HultUI.Core.Elements.TextElement)
                    .GetField("_lines", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(label)!;
            float w = 0;
            foreach (var line in lines) w = System.Math.Max(w, line.Width);
            return w;
        }

        public HultUI.Core.Elements.BoxElement? TryScrollbarTrack() =>
            Field<HultUI.Core.Reactivity.Ref<HultUI.Core.Elements.BoxElement>>("_diffHTrackRef").Current is { Parent: not null } track
                ? track : null;

        public (HultUI.Core.Elements.Element track, HultUI.Core.Elements.Element thumb) ScrollbarParts()
        {
            var track = TryScrollbarTrack() ?? throw new Xunit.Sdk.XunitException("there is no horizontal scrollbar");
            return (track, track.Children[0]);
        }

        public void DragScrollbar(float from, float to)
        {
            var y = ScrollbarParts().track.Layout.AbsoluteContentBox.Y + 4;
            _session.Window.MouseDown(from, y);
            _session.PumpFrame();
            _session.Window.MoveMouse(to, y);
            _session.PumpFrame();
            _session.Window.MouseUp(to, y);
            _session.PumpFrame();
        }

        public HultUI.Core.Elements.Element? Focused => _session.Window.Window.FocusedElement;

        public HeadlessSession Session => _session;

        /// <summary>Wait for the background tokenize to land. Genuinely asynchronous work,
        /// which is what PumpUntil is for — the assertions that follow are about content,
        /// not about whether an idle app repaints.</summary>
        public void WaitForHighlight() =>
            _session.PumpUntil(() => Field<object?>("_diffHlLeft") != null, System.TimeSpan.FromSeconds(20));

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

        /// <summary>The right-hand code cell of the first line — grid column 4.</summary>
        public (HultUI.Core.Elements.Element cell, HultUI.Core.Elements.Element text) FirstLineRightCell()
        {
            var cell = FirstRow().Children[4];
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
