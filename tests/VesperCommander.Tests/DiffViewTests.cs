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

    /// <summary>The rail is where a click cycles a file's direction — anywhere else on the row
    /// selects it — so it spans the row's height with its glyph centred, rather than being only
    /// as tall as the glyph.</summary>
    [Fact]
    public void TheSyncRailSpansItsRowWithItsGlyphCentred()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "one\n");
        t.Open();
        t.Press(KeyCode.F9);

        HultUI.Core.Elements.TextElement? Glyph(string text) =>
            LayoutProbe.Descendants<HultUI.Core.Elements.TextElement>(t.Root, l => l.Content == text).FirstOrDefault();
        t.Session.PumpUntil(() => Glyph("+→") != null);

        var glyph = Glyph("+→")!;
        var rail = LayoutProbe.Painted(glyph.Parent!);
        var row = LayoutProbe.Inner(glyph.Parent!.Parent!);
        var g = LayoutProbe.Painted(glyph);
        Assert.InRange(row.Height, 18f, 30f);
        Assert.Equal(row.Height, rail.Height, 0.01f);
        Assert.Equal(g.Y - rail.Y, rail.Bottom - g.Bottom, 0.01f);
        // And the file's name beside it is centred in the row too, not along its top.
        var name = LayoutProbe.Painted(LayoutProbe.Descendants<HultUI.Core.Elements.TextElement>(
            glyph.Parent!.Parent!, l => l.Content == "a.txt").Single());
        Assert.Equal(name.Y - row.Y, row.Bottom - name.Bottom, 0.01f);

        // Near the top of the row, well clear of the glyph, a click still cycles the direction.
        t.Session.Window.Click(rail.X + rail.Width / 2, row.Y + 1.5f);
        t.Session.PumpFrame();
        Assert.NotNull(Glyph("∅"));
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

    /// <summary>
    /// Holding the middle button right of the anchor pans the diff sideways.
    /// <para>The list cannot do this itself — it pins its content width to the viewport — so
    /// the position lives in the pan, and the framework's autoscroll used to drive the list's
    /// own offset, which had nowhere to go. The scrollbar right under the diff worked; the
    /// gesture that means the same thing did not.</para>
    /// </summary>
    [Fact]
    public void HoldingTheMiddleButtonRightOfTheAnchorPansTheDiffSideways()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", new string('L', 400) + "\n");
        t.WriteRight("a.txt", new string('R', 400) + "\n");
        t.Open();
        t.OpenDiffOn("a.txt");
        Assert.True(t.Pan.CanPan, "the fixture's diff has nothing to pan");
        float atRest = t.FirstLineCell().text.Layout.AbsoluteBorderBox.X;

        var box = t.FirstLineCell().cell.Layout.AbsoluteBorderBox;
        float x = box.X + 20f, y = box.Y + box.Height / 2f;
        t.Session.Window.MouseDown(x, y, MouseButton.Middle);
        t.Session.Window.MoveMouse(x + 14f + 60f, y);   // well past the dead zone, straight right
        for (int i = 0; i < 30; i++) t.Session.PumpFrame();

        Assert.True(t.Pan.Offset > 0, "holding the middle button right of the anchor did not pan the diff");
        Assert.True(t.FirstLineCell().text.Layout.AbsoluteBorderBox.X < atRest - 1f,
            "the pan offset changed but the code on screen did not move");
        t.Session.Window.MouseUp(x + 14f + 60f, y, MouseButton.Middle);
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

    /// <summary>
    /// Narrowing the window re-measures the wrapped rows.
    /// <para>
    /// A wrapped row's height is a function of the width, and the rows are virtualized — the
    /// list stacks them from heights measured in the build. An ordinary resize does not
    /// rebuild here (only a scale change does), so the risk is that layout runs at the new
    /// width against heights measured at the old one and every line that gained a visual line
    /// lands on the row beneath it. That is exactly what Junction's diff did.
    /// </para>
    /// <para>Asked after the resize in both directions, because narrowing and widening fail
    /// differently: narrowing overlaps, widening leaves gaps.</para>
    /// </summary>
    [Theory]
    [InlineData(900)]
    [InlineData(1600)]
    public void ResizingTheWindowReMeasuresTheWrappedRows(int width)
    {
        using var t = new Fixture();
        // Lines long enough that a few hundred pixels changes how many rows they take.
        var wide = string.Join(" ", System.Linq.Enumerable.Repeat("wrap me around the column", 20));
        t.WriteLeft("a.txt", "one\n" + wide + "\n" + wide + "\nthree\n");
        t.WriteRight("a.txt", "ONE\n" + wide + "\n" + wide + "\nTHREE\n");
        t.Open(wrap: true);
        t.OpenDiffOn("a.txt");

        t.Session.Window.SetClientSize(width, 820);
        t.Session.PumpFrames(6);

        var rows = t.BodyRows();
        Assert.True(rows.Count > 2, $"only {rows.Count} rows — this proves nothing");
        for (int i = 1; i < rows.Count; i++)
        {
            var prev = rows[i - 1].Layout.AbsoluteBorderBox;
            Assert.Equal(prev.Y + prev.Height, rows[i].Layout.AbsoluteBorderBox.Y, 1);
        }

        // And each row is still tall enough for the text it draws at the new width.
        foreach (var row in rows)
            foreach (var label in LayoutProbe.Descendants<HultUI.Core.Elements.TextElement>(
                         row, e => e is { Selectable: true, SelectionIndex: >= 0 }))
            {
                var (height, _) = t.DrawnText(label);
                if (height <= 0) continue;
                Assert.True(height <= row.Layout.AbsoluteBorderBox.Height + 0.5f,
                    $"at {width}px, {height:0}px of text sits in a {row.Layout.AbsoluteBorderBox.Height:0}px row");
            }
    }

    /// <summary>An unbreakable run stays inside its cell after a resize too — the cell is a
    /// grid fraction here rather than a flex item sized to its content, which is what let the
    /// same token push Junction's column open, so this pins the difference.</summary>
    [Fact]
    public void AnUnbreakableRunStaysInsideItsCellAcrossAResize()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", new string('L', 400) + "\n");
        t.WriteRight("a.txt", new string('R', 400) + "\n");
        t.Open(wrap: true);
        t.OpenDiffOn("a.txt");

        foreach (int width in new[] { 900, 1400, 1050 })
        {
            t.Session.Window.SetClientSize(width, 820);
            t.Session.PumpFrames(6);

            var (cell, text) = t.FirstLineCell();
            Assert.True(t.DrawnWidth(text) <= cell.Layout.AbsoluteContentBox.Width + 0.5f,
                $"at {width}px the run is {t.DrawnWidth(text):0}px in a "
                + $"{cell.Layout.AbsoluteContentBox.Width:0}px cell");
        }
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

    // ── virtualization ───────────────────────────────────────────────────

    [Fact]
    public void ALongComparisonBuildsOnlyTheRowsNearTheViewport()
    {
        using var t = new Fixture();
        t.WriteLeft("big.txt", Lines("left line {0}", 2000));
        t.WriteRight("big.txt", Lines("right line {0}", 2000));
        t.Open();

        t.OpenDiffOn("big.txt");

        // An 820-tall window over 20px rows shows about 40. Building all 2000 was the cost
        // virtualizing removed, so the row count is the assertion, not the wall clock.
        Assert.InRange(t.BodyRows().Count, 1, 80);
    }

    [Fact]
    public void ScrollingToTheEndDoesNotBuildEverythingInBetween()
    {
        using var t = new Fixture();
        t.WriteLeft("big.txt", Lines("left line {0}", 2000));
        t.WriteRight("big.txt", Lines("right line {0}", 2000));
        t.Open();
        t.OpenDiffOn("big.txt");

        t.Press(KeyCode.End);
        t.Session.PumpFrames(2);

        Assert.InRange(t.BodyRows().Count, 1, 80);
        // And it really is at the bottom, not merely cheap.
        Assert.True(t.ScrollY > 30000f, $"expected to be near the end, was at {t.ScrollY}");
    }

    // Virtualization destroys the label a drag started in as soon as it scrolls away. The
    // selection is anchored to line numbers instead, and the copy is read from the file's
    // lines rather than from the labels, so it survives both.
    [Fact]
    public void CopyingASelectionThatRunsPastTheViewportGetsEveryLine()
    {
        using var t = new Fixture();
        t.WriteLeft("big.txt", Lines("left line {0}", 2000));
        t.WriteRight("big.txt", Lines("right line {0}", 2000));
        t.Open();
        t.OpenDiffOn("big.txt");

        t.SelectLeftFromTo("left line 1", "left line 1501");
        Assert.True(t.LeftGroup.HasSelection, "the drag left no selection in the group");
        t.Press(KeyCode.C, KeyModifiers.Ctrl);

        var copied = t.Clipboard.Split('\n');
        Assert.Equal(1501, copied.Length);
        Assert.Equal("left line 1", copied[0]);
        Assert.Equal("left line 1000", copied[999]);   // recycled away long before the copy
        Assert.Equal("left line 1501", copied[1500]);
    }

    // The cell used to carry 8px of padding, and that padding belonged to the box painting
    // the row's colour rather than to the label. Pressing in it hit the box, so a drag begun
    // at a line's leading edge — where the eye goes, and where indented code leaves blank
    // space — selected nothing.
    [Fact]
    public void PressingAtALinesLeadingEdgeStartsASelection()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "    indented left line\n");
        t.WriteRight("a.txt", "    indented right line\n");
        t.Open();

        t.OpenDiffOn("a.txt");

        // The border box, not the content box: the content box is defined to sit inside any
        // padding, so pressing relative to it would land where the label already is and prove
        // nothing about the margin being complained of.
        var (cell, _) = t.FirstLineCell();
        var box = cell.Layout.AbsoluteBorderBox;
        t.PressInBody(box.X + 1f, box.Y + box.Height / 2);

        Assert.True(t.LeftGroup.HasSelection || t.PressedLabelIsDragging(),
            "a press at the very start of the line did not begin a selection");
    }

    // The same dead zone, vertically: the cell used to centre its label, so the slack above
    // and below belonged to the box rather than the line.
    [Theory]
    // In pixels from the row's edges, not fractions of it: the strip left by a centred label
    // is only a pixel or two deep, so a press 92% of the way down a 20px row lands inside the
    // label and proves nothing. The first and last pixel are the ones in question.
    [InlineData(1.5f)]     // near the top edge, inside the strip a centred label leaves
    [InlineData(-1.5f)]    // near the bottom edge, likewise. Not half a pixel: that rounds
                           // into the neighbouring row and makes the test a coin toss.
    [InlineData(9f)]       // the middle, which always worked
    public void PressingAtALinesTopOrBottomEdgeStartsASelection(float inset)
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "left line\n");
        t.WriteRight("a.txt", "right line\n");
        t.Open();

        t.OpenDiffOn("a.txt");

        var (cell, _) = t.FirstLineCell();
        var box = LayoutProbe.Painted(cell);
        var at = inset >= 0 ? LayoutProbe.InsetFromTop(box, inset)
                            : LayoutProbe.InsetFromBottom(box, -inset);
        t.PressInBody(at.X, at.Y);

        Assert.True(t.LeftGroup.HasSelection || t.PressedLabelIsDragging(),
            $"a press {at.Y - box.Y:0.0}px into a {box.Height:0}px row did not begin a selection");
    }

    // The row's tint should run the full width of its cell. With padding on the painted box
    // the colour stopped 8px short at each end, which made added and removed rows look
    // unevenly sized next to each other.
    [Fact]
    public void ALinesTintFillsItsWholeCell()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "left\n");
        t.WriteRight("a.txt", "right\n");
        t.Open();

        t.OpenDiffOn("a.txt");

        // Against the border box — the area that actually gets painted. The content box is
        // inset by any padding, so comparing to it holds however much padding there is.
        var (cell, label) = t.FirstLineCell();
        Assert.Equal(cell.Layout.AbsoluteBorderBox.Width, label.Layout.AbsoluteBorderBox.Width, 1);
        Assert.Equal(cell.Layout.AbsoluteBorderBox.X, label.Layout.AbsoluteBorderBox.X, 1);
    }

    [Fact]
    public void F8WalksTheChangesAndShiftF8WalksBack()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "1\n2\n3\n4\n5\n6\n7\n8\n9\n10\n11\n12\n");
        t.WriteRight("a.txt", "1\nX\n3\n4\n5\n6\n7\n8\n9\n10\nY\n12\n");
        t.Open();

        t.OpenDiffOn("a.txt");
        Assert.True(t.ChangeRows.Count >= 2);
        Assert.Equal(0, t.Field<int>("_diffChangeIdx"));

        t.Press(KeyCode.F8);
        Assert.Equal(1, t.Field<int>("_diffChangeIdx"));

        t.Press(KeyCode.F8, KeyModifiers.Shift);
        Assert.Equal(0, t.Field<int>("_diffChangeIdx"));
    }

    // Opening a comparison put a ring around the first change, which read as a selected row
    // in a list — an interaction this view does not have. It appears once you jump.
    [Fact]
    public void NoChangeIsRingedUntilYouJumpToOne()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "1\n2\n3\n");
        t.WriteRight("a.txt", "1\nX\n3\n");
        t.Open();

        t.OpenDiffOn("a.txt");
        // The ring is an outline, not a border: as a border it grew the row 2px, which in wrap
        // mode (MinHeight, no fixed height) pushed every row below it down.
        Assert.All(t.BodyRows(), row => Assert.False(((HultUI.Core.Elements.BoxElement)row).Outline.IsVisible));

        t.Press(KeyCode.F8);

        Assert.Contains(t.BodyRows(), row => ((HultUI.Core.Elements.BoxElement)row).Outline.IsVisible);
    }

    // A narrow enough pane used to squeeze the LEFT/RIGHT tag until it folded to two lines.
    [Fact]
    public void TheSideTagNeverFoldsOntoASecondLine()
    {
        using var t = new Fixture();
        t.WriteLeft("a-file-with-a-fairly-long-name.txt", "one\n");
        t.WriteRight("a-file-with-a-fairly-long-name.txt", "two\n");
        t.Open();

        t.OpenDiffOn("a-file-with-a-fairly-long-name.txt");

        // The box, not the line boxes: these labels are not selectable, and a TextElement
        // only records its laid-out lines when it is. One line of 9.5px type is ~13px tall,
        // so a folded tag would be nearer 26.
        var tags = t.SideTags();
        Assert.Equal(2, tags.Count);
        foreach (var tag in tags)
            Assert.True(tag.Layout.AbsoluteBorderBox.Height < 20f,
                $"the side tag is {tag.Layout.AbsoluteBorderBox.Height:0}px tall — it folded onto a second line");
    }

    // ── comparing off the UI thread, and saying why when it can't ────────

    // The comparison used to run inside the F3 handler, so a slow pair froze the window with
    // no way out. It opens on a "Comparing…" state now and the result arrives later.
    //
    // These two hold the comparison rather than counting on it being slow: a 400-line pair
    // used to stand in for "still running one frame later", and on a 2-core CI runner it
    // sometimes was not.
    [Fact]
    public void TheViewOpensBeforeTheComparisonHasFinished()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "left\n");
        t.WriteRight("a.txt", "right\n");
        t.Open();
        var release = t.HoldComparison();

        t.PutCursorOn('L', "a.txt");
        t.Press(KeyCode.F3);

        Assert.True(t.DiffOpen);
        Assert.Equal("Comparing", t.DiffState);

        release();
        t.Session.PumpUntil(() => t.DiffState != "Comparing", System.TimeSpan.FromSeconds(30));
        Assert.Equal("Ok", t.DiffState);
    }

    [Fact]
    public void EscapeStillClosesTheViewWhileItIsComparing()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", "left\n");
        t.WriteRight("a.txt", "right\n");
        t.Open();
        t.HoldComparison();   // never released: the view must close on a comparison still running

        t.PutCursorOn('L', "a.txt");
        t.Press(KeyCode.F3);
        Assert.Equal("Comparing", t.DiffState);

        t.Press(KeyCode.Escape);

        Assert.False(t.DiffOpen);
    }

    // A file too long to compare is ordinary readable text. Reporting it as binary sent
    // people looking for a problem with the file itself.
    [Fact]
    public void AFileTooLongToCompareSaysSoRatherThanClaimingItIsBinary()
    {
        using var t = new Fixture();
        var huge = Lines("line {0}", DiffEngine.MaxLines + 10);
        t.WriteLeft("a.txt", huge);
        t.WriteRight("a.txt", huge + "extra\n");
        t.Open();

        t.OpenDiffOn("a.txt");

        Assert.Equal("TooLarge", t.DiffState);
    }

    /// <summary>The same decoder as the viewer: a UTF-16 file with a byte-order mark is text
    /// on both sides of the comparison, not a binary that happens to have NULs in it.</summary>
    [Fact]
    public void AUtf16FileWithAByteOrderMarkComparesAsText()
    {
        using var t = new Fixture();
        var enc = System.Text.Encoding.Unicode;
        File.WriteAllBytes(Path.Combine(t.Left, "a.ini"),
            enc.GetPreamble().Concat(enc.GetBytes("[a]\r\nk=1\r\n")).ToArray());
        File.WriteAllBytes(Path.Combine(t.Right, "a.ini"),
            enc.GetPreamble().Concat(enc.GetBytes("[a]\r\nk=2\r\n")).ToArray());
        t.Open();

        t.OpenDiffOn("a.ini");

        Assert.Equal("Ok", t.DiffState);
    }

    [Fact]
    public void AFileOverTheSizeLimitSaysTooLargeRatherThanBinary()
    {
        using var t = new Fixture();
        using (var f = File.Create(Path.Combine(t.Left, "big.log")))
        {
            f.SetLength(CommanderApp.ViewMaxBytes + 1);
        }
        t.WriteRight("big.log", "small\n");
        t.Open();

        t.OpenDiffOn("big.log");

        Assert.Equal("TooLarge", t.DiffState);
    }

    [Fact]
    public void ABinaryFileIsStillReportedAsBinary()
    {
        using var t = new Fixture();
        t.WriteLeft("a.bin", "text\0with a nul\n");
        t.WriteRight("a.bin", "other\0with a nul\n");
        t.Open();

        t.OpenDiffOn("a.bin");

        Assert.Equal("Binary", t.DiffState);
    }

    // ── monospace fast path for row heights ──────────────────────────────
    //
    // Wrapped rows get their height from a shortcut: in a monospace face a line that is
    // short enough certainly fits on one line, which is arithmetic rather than a shaper
    // call. If that ever disagrees with what the row actually lays out to, the row is not
    // slow — it is clipped, because the list forces the height it was given. So the test is
    // equality against real layout, across the shapes that sit near the boundary.

    [Fact]
    public void EveryRowIsExactlyAsTallAsTheTextInIt()
    {
        using var t = new Fixture();
        // Lengths straddling the column width, and packed into the first rows on purpose:
        // the body is virtualized, so a sweep that put the interesting lengths further down
        // would never build them and would pass without testing anything.
        int[] lengths = [40, 60, 72, 78, 80, 82, 84, 86, 90, 96, 104, 120, 140, 160, 200, 300];
        var left = new System.Text.StringBuilder();
        var right = new System.Text.StringBuilder();
        for (int rep = 0; rep < 2; rep++)
            foreach (var n in lengths)
            {
                left.Append(new string('x', n)).Append('\n');
                right.Append(new string('y', n)).Append('\n');
            }
        t.WriteLeft("a.txt", left.ToString());
        t.WriteRight("a.txt", right.ToString());
        t.Open(wrap: true);

        t.OpenDiffOn("a.txt");
        t.Session.PumpFrames(2);

        // Every realized row must be at least as tall as the text laid out inside it. The
        // element box is clamped by the cell, so it can never show the overflow — the line
        // boxes can, which is what a row shorter than its own text actually looks like.
        int checkedRows = 0;
        foreach (var row in t.BodyRows())
        {
            if (row.Children.Count < 5) continue;   // a collapse row
            float rowH = row.Layout.AbsoluteBorderBox.Height;
            foreach (var cell in new[] { row.Children[1], row.Children[4] })
            {
                var label = FirstLabelIn(cell);
                if (label is null) continue;
                var (drawn, count) = t.DrawnText(label);
                checkedRows++;
                Assert.True(drawn <= rowH + 0.5f,
                    $"{count} lines of text ({drawn:0}px) sit in a {rowH:0}px row — the row is clipping them");
            }
        }
        Assert.True(checkedRows > 10, $"only {checkedRows} cells were checked — the sweep proves nothing");
    }

    [Fact]
    public void AWordThatOnlyJustFitsStillGetsAOneLineRow()
    {
        using var t = new Fixture();
        // Long unbroken runs: no spaces to break at, so the wrap decision is purely the
        // width comparison the fast path shortcuts.
        t.WriteLeft("a.txt", new string('a', 40) + "\n" + new string('b', 200) + "\n");
        t.WriteRight("a.txt", new string('c', 40) + "\n" + new string('d', 200) + "\n");
        t.Open(wrap: true);

        t.OpenDiffOn("a.txt");
        t.Session.PumpFrames(2);

        var rows = t.BodyRows();
        // The 40-character line fits its column; the 200-character one cannot.
        Assert.Equal(20f, rows[0].Layout.AbsoluteBorderBox.Height, 1);
        Assert.True(rows[1].Layout.AbsoluteBorderBox.Height > 20f,
            "a 200-character run should have wrapped to more than one line");
    }

    // Non-ASCII has to leave the fast path: those glyphs come from a fallback face whose
    // advance the monospace arithmetic knows nothing about.
    [Fact]
    public void ALineOfNonAsciiIsMeasuredNotGuessed()
    {
        using var t = new Fixture();
        t.WriteLeft("a.txt", new string('東', 60) + "\n");
        t.WriteRight("a.txt", new string('京', 60) + "\n");
        t.Open(wrap: true);

        t.OpenDiffOn("a.txt");
        t.Session.PumpFrames(2);

        var row = t.BodyRows()[0];
        var label = FirstLabelIn(row.Children[1]);
        Assert.NotNull(label);
        var (drawn, count) = t.DrawnText(label!);
        Assert.True(drawn <= row.Layout.AbsoluteBorderBox.Height + 0.5f,
            $"{count} lines of wide glyphs ({drawn:0}px) sit in a {row.Layout.AbsoluteBorderBox.Height:0}px row");
    }

    private static HultUI.Core.Elements.TextElement? FirstLabelIn(HultUI.Core.Elements.Element cell) =>
        LayoutProbe.Descendants<HultUI.Core.Elements.TextElement>(cell).FirstOrDefault();

    private static string Lines(string format, int count) =>
        string.Concat(System.Linq.Enumerable.Range(1, count)
            .Select(i => string.Format(format, i) + "\n"));

    // ── harness ──────────────────────────────────────────────────────────
    internal sealed class Fixture : System.IDisposable
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
            _root = _app.Mount();
            _session.Application.SetRoot(_root);
            _session.PumpUntil(() => Rows('L').Count > 0 && Rows('R').Count > 0);
        }

        private HultUI.Core.Elements.Element _root = null!;

        /// <summary>The mounted app, for finding an element by descent.</summary>
        public HultUI.Core.Elements.Element Root => _root;

        /// <summary>Put the left pane's cursor on a file and press F3 the way a user would.</summary>
        public void OpenDiffOn(string name)
        {
            PutCursorOn('L', name);
            Press(KeyCode.F3);
            // The comparison runs off the UI thread now, so the view opens on "Comparing…"
            // and the result lands a frame or more later. Wait for it to settle, or every
            // assertion below would be racing an empty view.
            _session.PumpUntil(() => DiffState != "Comparing", System.TimeSpan.FromSeconds(30));
            _session.PumpFrame();
        }

        public void Press(KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
        {
            _session.Window.PressKey(key, modifiers);
            _session.PumpFrame();
        }

        /// <summary>Holds every comparison from now on at "Comparing…" until the returned
        /// action is called. The gate completes off the test thread, so releasing it never
        /// runs the comparison inline here.</summary>
        public System.Action HoldComparison()
        {
            var gate = new System.Threading.Tasks.TaskCompletionSource(
                System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            _app.DiffCompareGate = () => gate.Task;
            return () => gate.TrySetResult();
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
        public float DrawnWidth(HultUI.Core.Elements.Element label) =>
            LayoutProbe.DrawnText((HultUI.Core.Elements.TextElement)label).Width;

        /// <summary>Height of the text actually laid out in a label, and how many lines it
        /// broke into. Read from the line boxes, not the element box: the box is clamped by
        /// its cell and so can never report overflowing it — only the laid-out text can.</summary>
        public (float Height, int LineCount) DrawnText(HultUI.Core.Elements.Element label)
        {
            var drawn = LayoutProbe.DrawnText((HultUI.Core.Elements.TextElement)label);
            return (drawn.Height, drawn.LineCount);
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

        /// <summary>The app under test, for the odd assertion that has to read a private
        /// property off it directly.</summary>
        public CommanderApp App => _app;

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
        private static HultUI.Core.Elements.Element FirstLabel(HultUI.Core.Elements.Element cell) =>
            LayoutProbe.Descendants<HultUI.Core.Elements.TextElement>(cell).FirstOrDefault()
            ?? throw new Xunit.Sdk.XunitException("the cell holds no label");

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

        public HultUI.Core.Elements.Element FirstRow() => BodyRows()[0];

        /// <summary>The rows the body has actually built. Virtualized, that is the ones near
        /// the viewport rather than every row in the file — the list's first child is the
        /// spacer standing in for everything scrolled past above.</summary>
        public IReadOnlyList<HultUI.Core.Elements.Element> BodyRows() =>
            Body.Children.Skip(1).ToList();

        public HultUI.Core.Primitives.Rect BodyBox() => Body.Layout.AbsoluteContentBox;

        public string Clipboard => _session.Window.Clipboard.GetText() ?? "";

        /// <summary>The "LEFT" / "RIGHT" labels in the sub-header.</summary>
        public IReadOnlyList<HultUI.Core.Elements.Element> SideTags()
        {
            var found = new List<HultUI.Core.Elements.Element>();
            Walk(Body.Parent?.Parent ?? Body);   // up from the body to the view that holds the sub-header
            return found;

            void Walk(HultUI.Core.Elements.Element e)
            {
                if (e is HultUI.Core.Elements.TextElement { Content: "LEFT" or "RIGHT" }) found.Add(e);
                foreach (var c in e.Children) Walk(c);
            }
        }

        /// <summary>Presses at a point inside the body, in screen coordinates — rows are laid
        /// out in the body's content space, so the scroll offset has to come off the Y.</summary>
        public void PressInBody(float x, float y)
        {
            var at = LayoutProbe.OnScreen(new HultUI.Core.Primitives.Point(x, y), Body);
            _session.Window.MouseDown(at.X, at.Y);
            _session.PumpFrame();
        }

        /// <summary>Whether any realized code label took the press and began a drag.</summary>
        public bool PressedLabelIsDragging()
        {
            foreach (var row in BodyRows())
            {
                if (row.Children.Count < 5) continue;
                foreach (var cell in new[] { row.Children[1], row.Children[4] })
                    if (FirstLabel(cell) is HultUI.Core.Elements.TextElement { IsDragging: true })
                        return true;
            }
            return false;
        }

        public HultUI.Core.Elements.TextSelectionGroup LeftGroup =>
            Field<HultUI.Core.Elements.TextSelectionGroup>("_diffGroupLeft");

        /// <summary>The realized left-hand label showing <paramref name="text"/>, or null if
        /// that line is not currently built.</summary>
        private HultUI.Core.Elements.TextElement? LeftLabel(string text)
        {
            foreach (var row in BodyRows())
            {
                if (row.Children.Count < 5) continue;             // a collapse row
                if (FirstLabel(row.Children[1]) is HultUI.Core.Elements.TextElement label
                    && label.Content == text)
                    return label;
            }
            return null;
        }

        /// <summary>Drags a selection down the left-hand side from one line to another,
        /// scrolling on the way — which recycles away the row the drag started in, exactly as
        /// it does for a user dragging past the bottom of a long diff.</summary>
        public void SelectLeftFromTo(string fromText, string toText)
        {
            var start = LeftLabel(fromText)
                ?? throw new Xunit.Sdk.XunitException($"'{fromText}' is not on screen to start from");
            var s = start.Layout.AbsoluteBorderBox;
            _session.Window.MouseDown(s.X, ScreenY(s));

            // Scroll until the far end is built. The anchor is long gone by then.
            for (int guard = 0; LeftLabel(toText) is null; guard++)
            {
                if (guard > 500) throw new Xunit.Sdk.XunitException($"never reached '{toText}'");
                Body.ScrollOffsetY += 400f;
                _session.PumpFrames(1);
            }

            var end = LeftLabel(toText)!;
            var e = end.Layout.AbsoluteBorderBox;
            // Past the end of the text but well inside the body, so the drag takes the whole
            // last line without the point falling outside the viewport and missing the hit test.
            float x = System.Math.Min(e.X + e.Width - 1, BodyBox().X + BodyBox().Width - 2);
            float y = ScreenY(e);
            _session.Window.MoveMouse(x, y);
            _session.Window.MouseUp(x, y);
            _session.PumpFrame();
        }

        /// <summary>Screen Y of a row's middle. Rows are laid out in the body's *content*
        /// space — the renderer applies the scroll offset — so a pointer coordinate has to
        /// subtract it or the click lands wherever that row would be if nothing had scrolled.</summary>
        private float ScreenY(HultUI.Core.Primitives.Rect box) =>
            box.Y - Body.ScrollOffsetY + box.Height / 2;

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
                if (rows[i].Name == name)
                {
                    // SetCursor is the plain state setter — in the app the cursor only ever
                    // moves from a key or click handler, which marks dirty on the way out.
                    // Reaching it directly skips that, so nothing rebuilds and anything
                    // driven by Build() (the preview's file load, above all) never happens.
                    Call("SetCursor", side, i);
                    MarkDirty();
                    _session.PumpFrame();
                    return;
                }
            Assert.Fail($"{name} is not listed in the {side} pane");
        }

        /// <summary>Put a pane's cursor on the row holding <paramref name="path"/>. The
        /// by-name form can't say which row is meant in a fed-in search result set, where
        /// several rows may share a name.</summary>
        public void PutCursorOnPath(char side, string path)
        {
            var rows = Rows(side);
            for (int i = 0; i < rows.Count; i++)
                if (string.Equals(rows[i].FullPath, path, System.StringComparison.OrdinalIgnoreCase))
                {
                    Call("SetCursor", side, i);
                    MarkDirty();
                    _session.PumpFrame();
                    return;
                }
            Assert.Fail($"{path} is not listed in the {side} pane");
        }

        public void MarkDirty() =>
            typeof(CommanderApp).GetMethod("MarkDirty",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.Invoke(_app, null);

        public T Field<T>(string name) =>
            (T)typeof(CommanderApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_app)!;

        public void SetField(string name, object? value) =>
            typeof(CommanderApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_app, value);

        /// <summary>The folder a pane is showing.</summary>
        public string Dir(char side) => (string)Call("Dir", side)!;

        /// <summary>A pane's mark set. Marks are held by <b>full path</b> (CommanderApp.MarkKey)
        /// so that a fed-in search result set, whose rows come from many folders, can tell two
        /// same-named files apart.</summary>
        public System.Collections.Generic.HashSet<string> MarkSet(char side) =>
            (System.Collections.Generic.HashSet<string>)Call("Marks", side)!;

        /// <summary>The mark set as leaf names — the readable form for the tests that work in
        /// a single folder.</summary>
        public System.Collections.Generic.HashSet<string> MarkNames(char side) =>
            MarkSet(side).Select(p => Path.GetFileName(p)).ToHashSet();

        /// <summary>Mark entries of a pane by name, as clicking each row would.</summary>
        public void Mark(char side, params string[] names)
        {
            var marks = MarkSet(side);
            foreach (var n in names) marks.Add(Path.Combine(side == 'L' ? Left : Right, n));
            MarkDirty();
        }

        /// <summary>Reach a private method on the app under test. Public counterpart of
        /// <see cref="Call"/>, for tests whose subject has no UI surface to drive.</summary>
        public object? Invoke(string method, params object?[] args) => Call(method, args);

        private object? Call(string method, params object?[] args)
        {
            var mi = typeof(CommanderApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!;
            // Fill in optional parameters the caller left off. Reflection, unlike the compiler,
            // doesn't apply defaults — so without this a method growing an optional argument
            // fails every test that already reaches it, with a parameter-count mismatch that
            // says nothing about what changed.
            var ps = mi.GetParameters();
            if (args.Length < ps.Length)
            {
                var full = new object?[ps.Length];
                Array.Copy(args, full, args.Length);
                for (int i = args.Length; i < ps.Length; i++) full[i] = ps[i].DefaultValue;
                args = full;
            }
            return mi.Invoke(_app, args);
        }

        public void Dispose()
        {
            _session?.Dispose();
            (_app as System.IDisposable)?.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(Left)!, true); } catch { }
        }
    }
}
