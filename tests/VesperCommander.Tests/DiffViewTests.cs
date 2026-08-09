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
        // The first build runs before any layout, so it sizes the code column from the
        // window; give it the frame it needs to learn the real width before asking the
        // scrollbar to be exactly at either end.
        t.Session.PumpFrames(2);

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

    // ── Wrap mode ────────────────────────────────────────────────────────
    [Fact]
    public void WrappingGrowsTheRowAndKeepsBothSidesOnTheSameBand()
    {
        using var t = new Fixture();
        // One side far longer than the other: the row has to take the taller side's height
        // and every cell in it — both gutters included — has to share that band, or the two
        // files stop lining up from this row onward.
        t.WriteLeft("a.txt", "short\n");
        t.WriteRight("a.txt", string.Join(" ", System.Linq.Enumerable.Repeat("a long stretch of words", 30)) + "\n");
        t.Open(wrap: true);

        t.OpenDiffOn("a.txt");

        // The content box, not the border box: the current change row draws a 1px ring, and
        // the cells sit inside it.
        var row = t.FirstRow().Layout.AbsoluteContentBox;
        var (leftCell, _) = t.FirstLineCell();
        var (rightCell, rightText) = t.FirstLineRightCell();
        var (gutter, _) = t.FirstLineGutter();

        Assert.True(rightText.Layout.AbsoluteBorderBox.Height > 3 * 20f,
            $"the long side is {rightText.Layout.AbsoluteBorderBox.Height:0}px tall — it did not wrap");
        // The row takes the taller side, and every cell in it shares that band. Without
        // this the two files stop lining up from here on.
        foreach (var cell in new[] { leftCell, rightCell, gutter })
        {
            Assert.Equal(row.Y, cell.Layout.AbsoluteBorderBox.Y, 1);
            Assert.Equal(row.Height, cell.Layout.AbsoluteBorderBox.Height, 1);
        }
    }

    [Fact]
    public void WrappedRowsStackWithoutOverlapping()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\n" + string.Join(" ", System.Linq.Enumerable.Repeat("wrap me", 40)) + "\nthree\n");
        t.WriteRight("a.txt", "ONE\n" + string.Join(" ", System.Linq.Enumerable.Repeat("wrap me", 40)) + "\nTHREE\n");
        t.Open(wrap: true);

        t.OpenDiffOn("a.txt");

        var rows = t.BodyRows();
        for (int i = 1; i < rows.Count; i++)
        {
            var prev = rows[i - 1].Layout.AbsoluteBorderBox;
            Assert.Equal(prev.Y + prev.Height, rows[i].Layout.AbsoluteBorderBox.Y, 1);
        }
    }

    [Fact]
    public void WrappingLeavesNothingToScrollSidewaysTo()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", new string('L', 400) + "\n");
        t.WriteRight("a.txt", new string('R', 400) + "\n");
        t.Open(wrap: true);

        t.OpenDiffOn("a.txt");

        Assert.Equal(0f, t.MaxScrollX());
        Assert.Null(t.TryScrollbarTrack());
        float atRest = t.FirstLineCell().text.Layout.AbsoluteBorderBox.X;
        t.Press(KeyCode.Right);
        Assert.Equal(atRest, t.FirstLineCell().text.Layout.AbsoluteBorderBox.X, 1);
    }

    [Fact]
    public void WPutsTheViewIntoWrapModeAndBack()
    {
        using var t = new Fixture();
        var wide = string.Join(" ", System.Linq.Enumerable.Repeat("a long stretch of words", 30));
        t.WriteLeft("a.txt", wide + " one\n");
        t.WriteRight("a.txt", wide + " two\n");
        t.Open();

        t.OpenDiffOn("a.txt");
        Assert.True(t.MaxScrollX() > 0);   // scrolling by default

        t.Press(KeyCode.W);
        t.Session.PumpFrames(2);   // the rebuild the toggle asked for, then its layout
        Assert.True(t.Wrap);
        Assert.Equal(0f, t.MaxScrollX());
        Assert.True(t.FirstRow().Layout.AbsoluteBorderBox.Height > 20f, "pressing w did not wrap anything");

        t.Press(KeyCode.W);
        t.Session.PumpFrames(2);
        Assert.False(t.Wrap);
        Assert.True(t.MaxScrollX() > 0);
        Assert.Equal(20f, t.FirstRow().Layout.AbsoluteBorderBox.Height, 1);
    }

    [Fact]
    public void AMinifiedLineWrapsBecauseItsPunctuationOffersBreaks()
    {
        using var t = new Fixture();
        // The case this feature was asked for: one enormous line of minified JSON, no
        // spaces. ICU's line-break rules offer opportunities after its punctuation.
        var json = "[" + string.Join(",", System.Linq.Enumerable.Range(0, 60)
            .Select(i => $"{{\"name\":\"thread_{i}\",\"ph\":\"M\",\"pid\":1}}")) + "]\n";
        t.WriteLeft("a.json", json);
        t.WriteRight("a.json", json.Replace("\"pid\":1", "\"pid\":9"));
        t.Open(wrap: true);

        t.OpenDiffOn("a.json");

        Assert.True(t.FirstRow().Layout.AbsoluteBorderBox.Height > 4 * 20f,
            $"the minified line came out {t.FirstRow().Layout.AbsoluteBorderBox.Height:0}px tall — it did not wrap");
    }

    [Fact]
    public void AnUnbreakableRunWrapsByCharacter()
    {
        using var t = new Fixture();
        // 400 characters with no space and no punctuation — base64, a hash, a hex blob.
        // ICU offers no break inside a word, so this only wraps because the pipeline cuts
        // it by character; otherwise it would run out of a box that has no sideways scroll.
        t.WriteLeft("a.txt", new string('L', 400) + "\n");
        t.WriteRight("a.txt", new string('R', 400) + "\n");
        t.Open(wrap: true);

        t.OpenDiffOn("a.txt");

        var (cell, text) = t.FirstLineCell();
        Assert.True(t.FirstRow().Layout.AbsoluteBorderBox.Height > 3 * 20f,
            $"the run came out {t.FirstRow().Layout.AbsoluteBorderBox.Height:0}px tall — it did not break");
        Assert.True(t.DrawnWidth(text) <= cell.Layout.AbsoluteContentBox.Width + 0.5f,
            "a broken line is still wider than the cell");
    }

    [Fact]
    public void NAndPStillLandOnAChangeWhenRowsAreDifferentHeights()
    {
        using var t = new Fixture();
        // A tall wrapped change, a stretch of context, then a second change. If n scrolled
        // by index × row-height it would land far short of the second one.
        var filler = string.Join("\n", System.Linq.Enumerable.Range(1, 40).Select(i => $"line {i}"));
        var wide = string.Join(" ", System.Linq.Enumerable.Repeat("a very long changed line", 40));
        t.WriteLeft("a.txt", $"{wide} one\n{filler}\ntail one\n");
        t.WriteRight("a.txt", $"{wide} two\n{filler}\ntail two\n");
        t.Open(wrap: true);

        t.OpenDiffOn("a.txt");
        Assert.True(t.ChangeRows.Count >= 2);

        t.Press(KeyCode.N);
        t.Session.PumpFrames(3);   // the scroll is applied after the row has been laid out

        var target = t.CurrentChangeRow();
        var body = t.BodyBox();
        float top = target.Layout.AbsoluteBorderBox.Y - body.Y - t.ScrollY;
        Assert.InRange(top, 0, body.Height - 1);
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

        public void Open(bool wrap = false)
        {
            _session = HeadlessSession.Create(1240, 820);
            _app = new CommanderApp(_session.Application, _session.Window.Window, Left, Right);
            // The app loads the developer's own settings and session, so pin the two things
            // that would otherwise steer these tests: which folder each pane shows, and the
            // diff tool (VS Code shells out instead of opening the builtin view).
            var settings = Field<Settings>("_settings");
            settings.DiffTool = DiffToolKind.Builtin;
            settings.DiffWrap = wrap;
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

        public HultUI.Core.Builders.HorizontalPan Pan =>
            Field<HultUI.Core.Builders.HorizontalPan>("_diffPan");

        public float MaxScrollX() => Pan.MaxOffset;

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

        public HultUI.Core.Elements.Element? TryScrollbarTrack() =>
            Pan.ScrollbarElement is { Parent: not null } track ? track : null;

        public (HultUI.Core.Elements.Element track, HultUI.Core.Elements.Element thumb) ScrollbarParts()
        {
            var track = TryScrollbarTrack() ?? throw new Xunit.Sdk.XunitException("there is no horizontal scrollbar");
            // Children: [travel before, thumb, travel after].
            return (track, track.Children[1]);
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
            return (cell, FirstLabel(cell));
        }

        /// <summary>The label inside a code cell. Scrolling wraps it in the pan's clipping
        /// cell, wrapping does not, so it is found by descent rather than by index.</summary>
        private static HultUI.Core.Elements.Element FirstLabel(HultUI.Core.Elements.Element cell)
        {
            foreach (var d in Descend(cell))
                if (d is HultUI.Core.Elements.TextElement) return d;
            throw new Xunit.Sdk.XunitException("the cell holds no label");
        }

        private static IEnumerable<HultUI.Core.Elements.Element> Descend(HultUI.Core.Elements.Element e)
        {
            foreach (var c in e.Children)
            {
                yield return c;
                foreach (var d in Descend(c)) yield return d;
            }
        }

        /// <summary>The right-hand code cell of the first line — grid column 4.</summary>
        public (HultUI.Core.Elements.Element cell, HultUI.Core.Elements.Element text) FirstLineRightCell()
        {
            var cell = FirstRow().Children[4];
            return (cell, FirstLabel(cell));
        }

        public (HultUI.Core.Elements.Element gutter, HultUI.Core.Elements.Element number) FirstLineGutter()
        {
            var gutter = FirstRow().Children[0];
            return (gutter, gutter.Children[0]);
        }

        public HultUI.Core.Elements.Element FirstRow() => Body.Children[0].Children[0];

        public IReadOnlyList<HultUI.Core.Elements.Element> BodyRows() => Body.Children[0].Children;

        public HultUI.Core.Primitives.Rect BodyBox() => Body.Layout.AbsoluteContentBox;

        public bool Wrap => Field<Settings>("_settings").DiffWrap;

        public HultUI.Core.Elements.Element CurrentChangeRow() =>
            Field<HultUI.Core.Reactivity.Ref<HultUI.Core.Elements.Element>>("_diffChangeRowRef").Current
            ?? throw new Xunit.Sdk.XunitException("no row is marked as the current change");
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
