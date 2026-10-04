using System.IO;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Drives the real Shift+F3 file viewer headlessly.
///
/// <para>The view exists because the preview pane stops at 500 lines and 200 characters a
/// line, so the first thing asserted here is the thing that made it worth building: the whole
/// file is there. The rest covers what the diff had to learn the hard way — a row as tall as
/// the text it wraps to, and a line that can be selected by pressing anywhere over it.</para>
/// </summary>
public class ViewerTests
{
    [Fact]
    public void ShiftF3OpensTheViewerAndEscapeLeavesIt()
    {
        using var t = new Fixture();
        t.Write("a.txt", "one\ntwo\nthree\n");
        t.Open();

        t.ViewFile("a.txt");
        Assert.True(t.ViewerOpen);

        t.Press(KeyCode.Escape);
        Assert.False(t.ViewerOpen);
    }

    [Fact]
    public void F3ClosesTheViewerItOpened()
    {
        using var t = new Fixture();
        t.Write("a.txt", "one\ntwo\n");
        t.Open();

        t.ViewFile("a.txt");
        t.Press(KeyCode.F3);
        Assert.False(t.ViewerOpen);
    }

    /// <summary>
    /// The whole point of the view. The preview pane reads
    /// <see cref="FileSystem.PreviewText"/>, which stops at 500 lines and truncates each to
    /// 200 characters; a reader that inherited either cap would be the same thing twice.
    /// </summary>
    [Fact]
    public void TheWholeFileIsLoaded_NotThePreviewExcerpt()
    {
        using var t = new Fixture();
        t.Write("big.txt", Lines("line {0}", 900) + "the last line is " + new string('x', 400) + "\n");
        t.Open();

        t.ViewFile("big.txt");

        Assert.Equal(ViewerState.Ok, t.State);
        // 900 numbered lines, the long one, and the empty string after the trailing newline.
        Assert.Equal(902, t.Lines.Length);
        Assert.Equal(400 + "the last line is ".Length, t.Lines[900].Length);
    }

    [Fact]
    public void ADirectoryIsNotOpened()
    {
        using var t = new Fixture();
        Directory.CreateDirectory(Path.Combine(t.Root, "sub"));
        t.Open();

        t.PutCursorOn("sub");
        t.Press(KeyCode.F3, KeyModifiers.Shift);

        Assert.False(t.ViewerOpen);
    }

    /// <summary>Windows writes its own desktop.ini files as UTF-16 LE with a byte-order
    /// mark, which puts a NUL after every ASCII character — exactly what the binary sniff
    /// looks for. The mark says what the file is, and wins.</summary>
    [Theory]
    [InlineData("utf-16LE")]
    [InlineData("utf-16BE")]
    [InlineData("utf-8")]
    public void AFileWithAByteOrderMarkIsDecodedAsItSays(string encodingName)
    {
        using var t = new Fixture();
        var enc = System.Text.Encoding.GetEncoding(encodingName);
        var bytes = enc.GetPreamble().Concat(enc.GetBytes("[.ShellClassInfo]\r\nIconResource=x\r\n")).ToArray();
        File.WriteAllBytes(Path.Combine(t.Root, "desktop.ini"), bytes);
        t.Open();

        t.ViewFile("desktop.ini");

        Assert.Equal(ViewerState.Ok, t.State);
        Assert.Equal(new[] { "[.ShellClassInfo]", "IconResource=x", "" }, t.Lines);
    }

    /// <summary>The 4 MB ceiling is a size, not a verdict on the content — a file over it is
    /// usually ordinary text, and calling it binary sent people looking at the file.</summary>
    [Fact]
    public void AFileOverTheSizeLimitSaysTooLargeRatherThanBinary()
    {
        using var t = new Fixture();
        using (var f = File.Create(Path.Combine(t.Root, "big.log")))
        {
            f.SetLength(CommanderApp.ViewMaxBytes + 1);
        }
        t.Open();

        t.ViewFile("big.log");

        Assert.Equal(ViewerState.TooLarge, t.State);
    }

    [Fact]
    public void ABinaryFileSaysSoRatherThanShowingGibberish()
    {
        using var t = new Fixture();
        File.WriteAllBytes(Path.Combine(t.Root, "b.bin"), new byte[] { 1, 2, 0, 3, 4 });
        t.Open();

        t.ViewFile("b.bin");

        Assert.Equal(ViewerState.Binary, t.State);
    }

    [Fact]
    public void TheArrowAndPageKeysScrollTheBody()
    {
        using var t = new Fixture();
        t.Write("a.txt", Lines("line {0}", 400));
        t.Open();

        t.ViewFile("a.txt");
        Assert.Equal(0f, t.ScrollY);

        t.Press(KeyCode.Down);
        float line = t.ScrollY;
        Assert.True(line > 0, "Down did not scroll");

        t.Press(KeyCode.PageDown);
        Assert.True(t.ScrollY > line * 4, $"PageDown moved {t.ScrollY - line}px, barely more than a line");

        t.Press(KeyCode.End);
        float bottom = t.ScrollY;
        t.Press(KeyCode.PageDown);
        Assert.Equal(bottom, t.ScrollY);      // clamped, not run off the end

        t.Press(KeyCode.Home);
        Assert.Equal(0f, t.ScrollY);
    }

    [Fact]
    public void EveryLineIsNumbered()
    {
        using var t = new Fixture();
        t.Write("a.txt", "alpha\nbeta\ngamma\n");
        t.Open();

        t.ViewFile("a.txt");

        Assert.Equal("1", t.GutterText(0));
        Assert.Equal("2", t.GutterText(1));
        Assert.Equal("3", t.GutterText(2));
    }

    /// <summary>
    /// The gutter widens with the largest line number. The diff can use a fixed width because
    /// its files are capped well below six figures; this view opens whatever it is pointed at,
    /// and a five-digit number in a three-digit gutter is simply clipped.
    ///
    /// <para>Compared against a small file's gutter rather than a pixel figure, because the
    /// claim is that the width tracks the digits — a fixed gutter, whatever its size, fails
    /// this and no measurement of one file alone can say so.</para>
    /// </summary>
    [Fact]
    public void TheGutterWidensForALargeFile()
    {
        float small, large;

        using (var t = new Fixture())
        {
            t.Write("a.txt", "one\ntwo\nthree\n");
            t.Open();
            t.ViewFile("a.txt");
            small = LayoutProbe.Painted(t.Rows()[0].Children[0]).Width;
        }
        using (var t = new Fixture())
        {
            t.Write("big.txt", Lines("line {0}", 12_000));
            t.Open();
            t.ViewFile("big.txt");
            large = LayoutProbe.Painted(t.Rows()[0].Children[0]).Width;
        }

        Assert.True(large > small,
            $"a 12,000-line file gets the same {large:0.#}px gutter as a 3-line one — five digits will not fit");
    }

    [Fact]
    public void TheFileIsSyntaxColoured()
    {
        using var t = new Fixture();
        t.Write("a.cs", "class A { }\n// a comment\n");
        t.Open();

        t.ViewFile("a.cs");
        t.WaitForHighlight();

        // Not merely that the tokenize landed: that the row actually carries the spans, which
        // is the step between having colours and drawing them.
        Assert.True(t.Label(0).Spans is { Count: > 0 }, "the first line took no syntax spans");
    }

    /// <summary>
    /// The code label covers its whole row, so there is no strip along a row's edge that
    /// belongs to the row rather than to the line drawn in it.
    ///
    /// <para>Asserted directly, as well as through the press below, because at these metrics
    /// the strip a centred label leaves is about 1.5px deep at each edge — and 1.5px is also
    /// the closest a press may safely be made to an edge before hit testing can round it into
    /// the neighbouring row. A press can therefore resolve the bottom edge but not the top;
    /// this can resolve both.</para>
    /// </summary>
    [Fact]
    public void TheLineLabelFillsItsRow()
    {
        using var t = new Fixture();
        t.Write("a.txt", "alpha\nbeta\ngamma\n");
        t.Open();
        t.ViewFile("a.txt");

        var label = t.Label(1);
        // Painted boxes on both sides: the row's content box is inset by its own padding by
        // definition, so comparing against that could not detect a label falling short of it.
        var line = LayoutProbe.Painted(label);
        var row = LayoutProbe.Painted(t.RowOf(label));

        Assert.True(line.Y <= row.Y + 0.5f && line.Bottom >= row.Bottom - 0.5f,
            $"the line covers {line.Y:0.#}–{line.Bottom:0.#} of a row spanning {row.Y:0.#}–{row.Bottom:0.#}"
            + " — the remainder is a dead strip that swallows presses");
    }

    /// <summary>
    /// A press anywhere over a line starts selecting it.
    ///
    /// <para>Measured in pixels from the row's edges rather than fractions of it: the strip a
    /// label that does not fill its row leaves is only a pixel or two deep, so 90% of the way
    /// down a 20px row is still well inside the label and says nothing about it. 1.5px in
    /// rather than 0.5px because half a pixel from an edge can round into the neighbouring
    /// row, which makes the test a coin toss under load.</para>
    ///
    /// <para>No top-edge case: the strip there is the same ~1.5px as the minimum safe inset,
    /// so a press cannot tell a covered edge from a bare one. <see cref="TheLineLabelFillsItsRow"/>
    /// is what covers that end.</para>
    /// </summary>
    [Theory]
    [InlineData(-1.5f)]     // near the bottom edge
    [InlineData(9f)]        // the middle, which always worked
    public void PressingAnywhereOverALineBeginsASelection(float inset)
    {
        using var t = new Fixture();
        t.Write("a.txt", "alpha\nbeta\ngamma\ndelta\n");
        t.Open();
        t.ViewFile("a.txt");

        // The second row, so a press near its top edge has a row above it to slide into if
        // the label does not reach the edge — the first row has nothing above it and would
        // pass by default.
        var label = t.Label(1);
        // The row's painted box, not the label's: the dead strip is the part of the row the
        // label does not cover, so pressing inside the label proves nothing. The row is found
        // by its relationship to the list, because wrapping and panning change how many
        // wrappers sit between it and the label.
        var box = LayoutProbe.Painted(t.RowOf(label));
        Assert.True(box.Height > 0, "the row has no height");

        var at = inset >= 0 ? LayoutProbe.InsetFromTop(box, inset)
                            : LayoutProbe.InsetFromBottom(box, -inset);
        t.PressInBody(at.X, at.Y);

        // That *this* label began the drag, not merely that something did — "any label" is
        // satisfied by the neighbouring row taking the press, which is exactly what happens
        // when a row edge is dead.
        Assert.True(label.IsDragging,
            $"a press {at.Y - box.Y:0.0}px into a {box.Height:0}px row went somewhere other than its own line");
    }

    /// <summary>
    /// Wrapped, a row is as tall as the text laid out in it.
    ///
    /// <para>Row heights are measured before layout and the list then <i>forces</i> them, so a
    /// height computed against a width the text does not actually get is not a slow row, it is
    /// a clipped one. Asserted against the laid-out line boxes rather than the label's element
    /// box, which is clamped by its row and so can never report overflowing it.</para>
    /// </summary>
    [Fact]
    public void AWrappedLineGetsARowAsTallAsItsText()
    {
        using var t = new Fixture();
        // Long enough to wrap several times at any plausible window width, and made of short
        // words so it wraps at spaces rather than falling into the character-level break.
        t.Write("a.txt", "short\n" + string.Join(' ', Enumerable.Repeat("wrap me", 300)) + "\nshort\n");
        t.Open(wrap: true);
        t.ViewFile("a.txt");

        var label = t.Label(1);
        var drawn = LayoutProbe.DrawnText(label);
        Assert.True(drawn.LineCount > 3, $"the line only wrapped to {drawn.LineCount} lines — it is not a wrap test");

        float row = LayoutProbe.Painted(t.RowOf(label)).Height;
        Assert.True(row >= drawn.Height - 0.5f,
            $"a {drawn.LineCount}-line block needs {drawn.Height:0.#}px but its row is {row:0.#}px — the text is clipped");
    }

    /// <summary>The viewer re-measures its wrapped rows when the window resizes, for the same
    /// reason the diff does: the row heights are measured in the build at the column width,
    /// and layout alone would stack them from heights measured at the old one.</summary>
    [Theory]
    [InlineData(900)]
    [InlineData(1600)]
    public void ResizingTheWindowReMeasuresTheWrappedRows(int width)
    {
        using var t = new Fixture();
        t.Write("a.txt", "short\n" + string.Join(' ', Enumerable.Repeat("wrap me", 300)) + "\nshort\n");
        t.Open(wrap: true);
        t.ViewFile("a.txt");

        t.Session.Window.SetClientSize(width, 820);
        t.Session.PumpFrames(6);

        var label = t.Label(1);
        var drawn = LayoutProbe.DrawnText(label);
        Assert.True(drawn.LineCount > 3, $"the line only wrapped to {drawn.LineCount} lines — not a wrap test");

        float row = LayoutProbe.Painted(t.RowOf(label)).Height;
        Assert.True(row >= drawn.Height - 0.5f,
            $"at {width}px a {drawn.LineCount}-line block needs {drawn.Height:0.#}px "
            + $"but its row is {row:0.#}px — the text is clipped");
    }

    [Fact]
    public void WrapIsTheSameSettingTheDiffUses()
    {
        using var t = new Fixture();
        t.Write("a.txt", "one\ntwo\n");
        t.Open();
        t.ViewFile("a.txt");

        bool before = t.Settings.DiffWrap;
        t.Press(KeyCode.W);

        Assert.NotEqual(before, t.Settings.DiffWrap);
        // And it is the persisted one, not a copy the viewer keeps: reloading from disk has
        // to see the change, or the diff would open under the old answer.
        Assert.Equal(t.Settings.DiffWrap, SettingsStore.Load().DiffWrap);
    }

    /// <summary>
    /// Toggling wrap in one view is what the other opens with — asserted on what each view
    /// actually draws, not on the field behind them.
    ///
    /// <para>The field test above passes just as well if the diff never reads it, or reads a
    /// copy taken when it was last open. This drives the real keys through both views in turn
    /// and asks each one whether a long line broke into several lines, which is the thing the
    /// user is choosing when they press <c>w</c>.</para>
    /// </summary>
    [Fact]
    public void WrapSetInOneViewIsWhatTheOtherOpensWith()
    {
        using var t = new Fixture();
        // Short words, so the line wraps at spaces rather than falling into the
        // character-level break, and long enough to wrap at any plausible column width.
        string longLine = string.Join(' ', Enumerable.Repeat("wrap me", 300));
        t.Write("a.txt", longLine + "\n");
        t.WriteOther("a.txt", longLine + " and then some\n");
        t.Open(wrap: false);

        // Turn wrapping on from the viewer, leave, and open the diff — which nobody has
        // touched. It has to come up wrapped.
        t.ViewFile("a.txt");
        t.Press(KeyCode.W);
        t.Press(KeyCode.Escape);

        t.DiffFile("a.txt");
        Assert.True(LayoutProbe.DrawnText(t.DiffLabel(0)).LineCount > 1,
            "the viewer's wrap did not reach the diff — the long line is still on one line there");

        // And back the other way: turn it off from the diff, and the viewer has to come up
        // unwrapped.
        t.Press(KeyCode.W);
        t.Press(KeyCode.Escape);

        t.ViewFile("a.txt");
        Assert.Equal(1, LayoutProbe.DrawnText(t.Label(0)).LineCount);
    }

    // ── Archive members ──────────────────────────────────────────────────

    /// <summary>
    /// A file inside an archive opens too. The preview already reads members through the VFS,
    /// so a reader that could not would leave a file you can glance at but not open.
    /// </summary>
    [Fact]
    public void AMemberInsideAnArchiveOpens()
    {
        using var t = new Fixture();
        // Past the preview's 500-line cap, so this also says the member is read whole rather
        // than through the excerpt reader the preview uses.
        t.WriteZip("bundle.zip", ("notes.txt", Lines("packed {0}", 700)));
        t.Open();

        t.EnterArchive("bundle.zip", "notes.txt");
        t.ViewFile("notes.txt");

        Assert.Equal(ViewerState.Ok, t.State);
        Assert.Equal(701, t.Lines.Length);        // 700 lines plus the trailing newline's empty one
        Assert.Equal("packed 1", t.Lines[0]);
        Assert.Equal("packed 700", t.Lines[699]);
    }

    [Fact]
    public void AMemberIsSyntaxColouredAndSearchable()
    {
        using var t = new Fixture();
        t.WriteZip("bundle.zip", ("code.cs", "class Alpha { }\nclass Beta { }\n"));
        t.Open();

        t.EnterArchive("bundle.zip", "code.cs");
        t.ViewFile("code.cs");
        t.WaitForHighlight();

        Assert.Contains(t.Label(0).Spans!, s => s.Color != default);

        t.Find("class");
        Assert.Equal(2, t.Hits.Count);
    }

    [Fact]
    public void AnEmptyMemberIsAnEmptyFile_NotAnUnreadableOne()
    {
        using var t = new Fixture();
        t.WriteZip("bundle.zip", ("empty.txt", ""));
        t.Open();

        t.EnterArchive("bundle.zip", "empty.txt");
        t.ViewFile("empty.txt");

        Assert.Equal(ViewerState.Ok, t.State);
    }

    [Fact]
    public void ABinaryMemberSaysSoRatherThanShowingGibberish()
    {
        using var t = new Fixture();
        t.WriteZip("bundle.zip", ("blob.bin", "ab\0cd"));
        t.Open();

        t.EnterArchive("bundle.zip", "blob.bin");
        t.ViewFile("blob.bin");

        Assert.Equal(ViewerState.Binary, t.State);
    }

    // ── Find in file (Ctrl+F) ────────────────────────────────────────────

    [Fact]
    public void CtrlFOpensTheFindBarAndFocusesIt()
    {
        using var t = new Fixture();
        t.Write("a.txt", "alpha\nbeta\n");
        t.Open();
        t.ViewFile("a.txt");

        t.Press(KeyCode.F, KeyModifiers.Ctrl);

        Assert.True(t.FindOpen);
        Assert.IsType<TextInputElement>(t.Focused);
    }

    /// <summary>Ctrl+F on a bar that is already open is "search for something else": the
    /// query is selected whole, so typing replaces it instead of tacking onto its end.</summary>
    [Fact]
    public void CtrlFOnAnOpenBarSelectsTheQuery()
    {
        using var t = new Fixture();
        t.Write("a.txt", "alpha\nbeta\n");
        t.Open();
        t.ViewFile("a.txt");
        t.Find("alpha");

        t.Press(KeyCode.F, KeyModifiers.Ctrl);

        var field = Assert.IsType<TextInputElement>(t.Focused);
        Assert.Equal("alpha", field.SelectedText);

        t.Find("beta");
        Assert.Equal("beta", t.Search.Query);
    }

    /// <summary>
    /// Ctrl+F with text selected in the file searches for it: the query is the selection's
    /// first line — a drag across lines contributes the one it started on — and the current
    /// match is the selection itself rather than the first occurrence in the file.
    /// </summary>
    [Fact]
    public void CtrlFSeedsTheQueryFromTheSelectedText()
    {
        using var t = new Fixture();
        t.Write("a.txt", "beta\nalpha\nbeta\ngamma\n");
        t.Open();
        t.ViewFile("a.txt");

        var group = t.Field<TextSelectionGroup>("_viewerGroup");
        group.Anchor(2, 0);
        group.ExtendTo(3, 3);   // "beta\ngam"
        Assert.Equal("beta\ngam", group.SelectedText);

        t.Press(KeyCode.F, KeyModifiers.Ctrl);

        Assert.True(t.FindOpen);
        Assert.Equal("beta", t.Search.Query);
        Assert.Equal(2, t.Hits.Count);
        Assert.Equal(2, t.Search.Current!.Value.Line);   // the selected one, not line 0's
        var field = Assert.IsType<TextInputElement>(t.Focused);
        Assert.Equal("beta", field.Text);
        Assert.Equal("beta", field.SelectedText);
    }

    [Fact]
    public void TypingFindsEveryMatch_CaseInsensitiveByDefault()
    {
        using var t = new Fixture();
        t.Write("a.txt", "Alpha beta\nGAMMA alpha\ndelta\nalpha alpha\n");
        t.Open();
        t.ViewFile("a.txt");

        t.Find("alpha");

        // Two on the last line, one each on the first two — and "Alpha"/"alpha" both count.
        Assert.Equal(4, t.Hits.Count);
        Assert.Equal(new[] { 0, 1, 3, 3 }, t.Hits.Select(h => h.Line).ToArray());
    }

    [Fact]
    public void MatchCaseNarrowsTheResults()
    {
        using var t = new Fixture();
        t.Write("a.txt", "Alpha\nalpha\n");
        t.Open();
        t.ViewFile("a.txt");

        t.Find("alpha");
        Assert.Equal(2, t.Hits.Count);

        t.Press(KeyCode.C, KeyModifiers.Alt);
        Assert.Single(t.Hits);
        Assert.Equal(1, t.Hits[0].Line);
    }

    [Fact]
    public void EnterWalksTheMatchesAndWrapsAround()
    {
        using var t = new Fixture();
        t.Write("a.txt", "one hit\ntwo\nanother hit\nthree\nlast hit\n");
        t.Open();
        t.ViewFile("a.txt");

        t.Find("hit");
        Assert.Equal(3, t.Hits.Count);
        Assert.Equal(0, t.HitIndex);

        t.Press(KeyCode.Enter);
        Assert.Equal(1, t.HitIndex);
        t.Press(KeyCode.Enter);
        Assert.Equal(2, t.HitIndex);
        // Past the end, back to the first — a search that stops dead reads as broken when the
        // match you wanted is above where you started.
        t.Press(KeyCode.Enter);
        Assert.Equal(0, t.HitIndex);

        t.Press(KeyCode.Enter, KeyModifiers.Shift);
        Assert.Equal(2, t.HitIndex);
    }

    /// <summary>
    /// A match far below the viewport is scrolled to. This is the whole point of walking the
    /// hits — the index knows every line's position whether or not the list has realized it,
    /// so the answer does not depend on the match already being on screen.
    /// </summary>
    [Fact]
    public void WalkingToAMatchScrollsItIntoView()
    {
        using var t = new Fixture();
        t.Write("a.txt", Lines("filler {0}", 400) + "the needle is here\n");
        t.Open();
        t.ViewFile("a.txt");
        Assert.Equal(0f, t.ScrollY);

        t.Find("needle");

        Assert.Single(t.Hits);
        Assert.True(t.ScrollY > 0, "the match is 400 lines down and the body never moved");
        // And it is actually on screen, not merely somewhere below the top.
        float top = t.LineTop(t.Hits[0].Line);
        Assert.InRange(top - t.ScrollY, 0f, t.BodyHeight);
    }

    /// <summary>
    /// The matched text is tinted where it sits, and the hit the cursor is on is tinted
    /// differently from the rest — otherwise "3 of 27" is the only thing telling you which
    /// one you are looking at.
    /// </summary>
    [Fact]
    public void MatchesAreTintedAndTheCurrentOneStandsOut()
    {
        using var t = new Fixture();
        t.Write("a.txt", "needle and needle\n");
        t.Open();
        t.ViewFile("a.txt");

        t.Find("needle");
        Assert.Equal(2, t.Hits.Count);

        var spans = t.Label(0).Spans;
        Assert.NotNull(spans);
        var tinted = spans!.Where(s => s.Background != default).ToList();
        Assert.Equal(2, tinted.Count);
        Assert.Equal(0, tinted[0].Start);
        Assert.Equal("needle".Length, tinted[0].Length);
        // The first hit is the current one, so its tint differs from the second's.
        Assert.NotEqual(tinted[0].Background, tinted[1].Background);

        t.Press(KeyCode.Enter);   // move to the second hit — the bright tint moves with it
        var after = t.Label(0).Spans!.Where(s => s.Background != default).ToList();
        Assert.Equal(tinted[0].Background, after[1].Background);
        Assert.Equal(tinted[1].Background, after[0].Background);
    }

    /// <summary>Syntax colouring survives a search. A hit takes a background, never a
    /// foreground, so a matched keyword keeps the colour its grammar gave it.</summary>
    [Fact]
    public void AMatchKeepsItsSyntaxColour()
    {
        using var t = new Fixture();
        t.Write("a.cs", "class Alpha { }\n");
        t.Open();
        t.ViewFile("a.cs");
        t.WaitForHighlight();

        var before = t.Label(0).Spans!.Where(s => s.Color != default).ToList();
        Assert.NotEmpty(before);

        t.Find("class");

        var after = t.Label(0).Spans!;
        Assert.Contains(after, s => s.Background != default);
        // Every foreground the grammar asked for is still asked for somewhere in the line.
        foreach (var s in before)
            Assert.Contains(after, a => a.Color == s.Color);
    }

    /// <summary>Alt+R reads the query as a pattern rather than as text to find literally.</summary>
    [Fact]
    public void TheRegexToggleChangesWhatTheQueryMeans()
    {
        using var t = new Fixture();
        t.Write("a.txt", "value = 42\nvalue = abc\nvalue = 7\n");
        t.Open();
        t.ViewFile("a.txt");

        t.Find(@"\d+");
        Assert.Empty(t.Hits);                 // no literal backslash-d anywhere in the file

        t.Press(KeyCode.R, KeyModifiers.Alt);
        Assert.Equal(2, t.Hits.Count);
        Assert.Equal(new[] { 0, 2 }, t.Hits.Select(h => h.Line).ToArray());
    }

    /// <summary>A pattern that will not compile says so rather than reporting no matches,
    /// which would send you looking for the text instead of at what you typed.</summary>
    [Fact]
    public void AnUnparseablePatternIsReportedInTheBar()
    {
        using var t = new Fixture();
        t.Write("a.txt", "alpha\n");
        t.Open();
        t.ViewFile("a.txt");

        // The query first, then the toggle — Alt+R belongs to the bar, so it does nothing
        // until the bar is open, and flipping it re-runs whatever is already typed.
        t.Find("([unclosed");
        t.Press(KeyCode.R, KeyModifiers.Alt);

        Assert.Equal("bad pattern", t.Search.Error);
        Assert.Empty(t.Hits);
    }

    [Fact]
    public void EscapeClosesTheBarFirstAndTheViewSecond()
    {
        using var t = new Fixture();
        t.Write("a.txt", "alpha\n");
        t.Open();
        t.ViewFile("a.txt");
        t.Find("alpha");

        t.Press(KeyCode.Escape);
        Assert.False(t.FindOpen);
        Assert.True(t.ViewerOpen);

        t.Press(KeyCode.Escape);
        Assert.False(t.ViewerOpen);
    }

    /// <summary>
    /// Closing the bar leaves the keyboard somewhere still in the tree.
    ///
    /// <para>An event dispatched to a detached element reaches only that element — it never
    /// bubbles to the handler that reads this view's keys. Leaving focus on the unmounted
    /// find field would therefore make the viewer deaf to everything, which is exactly the
    /// bug that once left the diff unable to see its own Esc.</para>
    /// </summary>
    [Fact]
    public void TheViewStillTakesKeysAfterTheBarCloses()
    {
        using var t = new Fixture();
        t.Write("a.txt", Lines("line {0}", 400));
        t.Open();
        t.ViewFile("a.txt");

        t.Press(KeyCode.F, KeyModifiers.Ctrl);
        t.Press(KeyCode.Escape);              // bar closed, field unmounted

        Assert.NotNull(t.Focused);
        Assert.False(t.Focused is TextInputElement, "focus stayed on the unmounted find field");

        t.Press(KeyCode.PageDown);
        Assert.True(t.ScrollY > 0, "the view took no keys after the find bar closed");
    }

    /// <summary>A letter typed into the find field is a letter, not a shortcut. Body keys are
    /// routed before the field sees anything, so <c>w</c> would otherwise toggle wrapping
    /// halfway through typing "window".</summary>
    [Fact]
    public void BodyShortcutsDoNotFireWhileTypingInTheField()
    {
        using var t = new Fixture();
        t.Write("a.txt", Lines("line {0}", 400));
        t.Open();
        t.ViewFile("a.txt");

        bool wrap = t.Settings.DiffWrap;
        t.Press(KeyCode.F, KeyModifiers.Ctrl);
        t.Press(KeyCode.W);
        t.Press(KeyCode.PageDown);

        Assert.Equal(wrap, t.Settings.DiffWrap);
        Assert.Equal(0f, t.ScrollY);
    }

    private static string Lines(string format, int count)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= count; i++) sb.Append(string.Format(format, i)).Append('\n');
        return sb.ToString();
    }

    /// <summary>Mirrors the app's private <c>ViewerState</c>, which the tests compare by name
    /// rather than reaching for the private enum type.</summary>
    internal enum ViewerState { Loading, Ok, Binary, TooLarge, Missing, Unreadable }

    /// <summary>
    /// The viewer reads in the face and size the settings name. Both used to be constants,
    /// and the row height was a third one — so this also checks that the rows grew with the
    /// text rather than clipping it into the same 20 units.
    /// </summary>
    [Fact]
    public void TheViewerUsesTheTextFontFromSettings()
    {
        using var t = new Fixture();
        t.Write("a.txt", "one\ntwo\nthree\n");
        t.Open();
        t.Settings.TextFont = "Courier New";
        t.Settings.TextFontSize = 18f;

        t.ViewFile("a.txt");

        var label = t.Label(0);
        Assert.Equal(18f, label.ResolvedInherited.FontSize, 2);
        Assert.Equal("Courier New", label.ResolvedInherited.FontFamily);
        // 18pt text does not fit in the 20-unit row the 11.5pt default was drawn in.
        Assert.True(t.RowOf(label).Layout.AbsoluteBorderBox.Height > 20f,
            $"the row is {t.RowOf(label).Layout.AbsoluteBorderBox.Height} tall — the text outgrew it");
    }

    [Fact]
    public void AnEmptyTextFontMeansThePlatformMonospace()
    {
        using var t = new Fixture();
        t.Write("a.txt", "one\n");
        t.Open();
        t.Settings.TextFont = "";

        t.ViewFile("a.txt");

        Assert.Equal(Fonts.Mono, t.Label(0).ResolvedInherited.FontFamily);
    }

    internal sealed class Fixture : System.IDisposable
    {
        public readonly string Root, Other;
        private HeadlessSession _session = null!;
        private CommanderApp _app = null!;

        public Fixture()
        {
            var root = Path.Combine(Path.GetTempPath(), "vview-" + System.Guid.NewGuid().ToString("N"));
            Root = Path.Combine(root, "left");
            Other = Path.Combine(root, "right");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Other);
        }

        public void Write(string name, string text) => File.WriteAllText(Path.Combine(Root, name), text);

        /// <summary>The same name in the other pane's folder — what F3 compares against.</summary>
        public void WriteOther(string name, string text) => File.WriteAllText(Path.Combine(Other, name), text);

        public void WriteZip(string name, params (string Entry, string Text)[] members)
        {
            using var zip = System.IO.Compression.ZipFile.Open(
                Path.Combine(Root, name), System.IO.Compression.ZipArchiveMode.Create);
            foreach (var (entry, text) in members)
            {
                using var s = zip.CreateEntry(entry).Open();
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                s.Write(bytes, 0, bytes.Length);
            }
        }

        /// <summary>Browses into an archive the way Enter on its row does, so the pane really
        /// is inside it — which is what makes the cursor entry an archive member rather than
        /// a path on disk. Waits for the member itself: the pane still lists the folder the
        /// archive is in while the listing runs, and "has rows" is satisfied by that.</summary>
        public void EnterArchive(string name, string member)
        {
            Call("NavigateTo", 'L', Path.Combine(Root, name), null, false);
            _session.PumpUntil(() => Rows('L').Any(r => r.Name == member), System.TimeSpan.FromSeconds(20));
            Assert.True(ArchivePath.InArchive((string)Call("Dir", 'L')!),
                "the pane did not end up inside the archive");
        }

        public void Open(bool wrap = false)
        {
            _session = HeadlessSession.Create(1240, 820);
            _app = new CommanderApp(_session.Application, _session.Window.Window, Root, Other);
            Settings.DiffWrap = wrap;
            // VS Code shells out instead of opening the builtin view, and the tests that
            // compare the two views need the builtin one.
            Settings.DiffTool = DiffToolKind.Builtin;
            Call("NavigateTo", 'L', Root, null, false);
            Call("NavigateTo", 'R', Other, null, false);
            _session.Application.SetRoot(_app.Mount());
            _session.PumpUntil(() => Rows('L').Count > 0 && Rows('R').Count > 0);
        }

        /// <summary>Put the cursor on a file and press F3, then wait for the off-thread
        /// comparison — the diff opens on "Comparing…" and lands a frame or more later.</summary>
        public void DiffFile(string name)
        {
            PutCursorOn(name);
            Press(KeyCode.F3);
            _session.PumpUntil(() => Field<object>("_diffState").ToString() != "Comparing",
                System.TimeSpan.FromSeconds(30));
            _session.PumpFrame();
        }

        /// <summary>The left-hand code label of the diff's <paramref name="n"/>th realized
        /// row. Row layout is gutter, text, marker, gutter, text.</summary>
        public TextElement DiffLabel(int n)
        {
            var body = Field<Ref<ScrollViewElement>>("_diffScrollRef").Current
                ?? throw new Xunit.Sdk.XunitException("the diff body has no scroll view");
            var row = body.Children.Skip(1).ToList()[n];
            return LayoutProbe.Descendants<TextElement>(row.Children[1]).FirstOrDefault()
                ?? throw new Xunit.Sdk.XunitException($"diff row {n} holds no code label");
        }

        /// <summary>Put the left pane's cursor on a file and press Shift+F3 the way a user
        /// would, then wait for the off-thread read to land — the view opens on "Loading…"
        /// and every assertion below would otherwise be racing an empty body.</summary>
        public void ViewFile(string name)
        {
            PutCursorOn(name);
            Press(KeyCode.F3, KeyModifiers.Shift);
            _session.PumpUntil(() => State != ViewerState.Loading, System.TimeSpan.FromSeconds(30));
            _session.PumpFrame();
        }

        /// <summary>The session, so a test can resize the window — a wrapped row's height
        /// depends on the width, which makes the resize part of the behaviour.</summary>
        public HeadlessSession Session => _session;

        public void Press(KeyCode key, KeyModifiers modifiers = KeyModifiers.None)
        {
            _session.Window.PressKey(key, modifiers);
            _session.PumpFrame();
        }

        /// <summary>Wait for the background tokenize to land — genuinely asynchronous work,
        /// which is what PumpUntil is for.</summary>
        public void WaitForHighlight() =>
            _session.PumpUntil(() => Field<object?>("_viewerHl") != null, System.TimeSpan.FromSeconds(20));

        public bool ViewerOpen => Field<bool>("_viewerOpen");
        public string[] Lines => Field<string[]>("_viewerLines");
        public Settings Settings => Field<Settings>("_settings");

        // ── Find in file ─────────────────────────────────────────────────
        private HultUI.Core.Builders.FindBar FindState => Field<HultUI.Core.Builders.FindBar>("_viewerFind");

        public bool FindOpen => FindState.IsOpen;
        public HultUI.Core.Builders.TextSearch Search => FindState.Search;
        public IReadOnlyList<HultUI.Core.Builders.TextSearch.Hit> Hits => Search.Hits;
        public int HitIndex => Search.Index;
        public Element? Focused => _session.Window.Window.FocusedElement;

        /// <summary>Opens the find bar and types a query the way a user does — real text
        /// input to the focused field, so the app runs whatever it runs on a keystroke rather
        /// than the test setting the query and calling the scan itself.</summary>
        public void Find(string query)
        {
            if (!FindOpen) Press(KeyCode.F, KeyModifiers.Ctrl);
            _session.Window.TypeText(query);
            _session.PumpFrame();
        }

        /// <summary>Y of a line in the body's own content space, from the row index.</summary>
        public float LineTop(int line) => Field<float[]>("_viewerTops")[line];

        public float BodyHeight => Body.Layout.AbsoluteContentBox.Height;

        public ViewerState State =>
            System.Enum.Parse<ViewerState>(Field<object>("_viewerState").ToString()!);

        private ScrollViewElement Body =>
            Field<Ref<ScrollViewElement>>("_viewerScrollRef").Current
            ?? throw new Xunit.Sdk.XunitException("the viewer body has no scroll view — is the state Ok?");

        public float ScrollY => Body.ScrollOffsetY;

        /// <summary>The rows the body has realized — the ones near the viewport rather than
        /// every line of the file. The list's first child is the spacer standing in for
        /// everything scrolled past above.</summary>
        public IReadOnlyList<Element> Rows() => Body.Children.Skip(1).ToList();

        /// <summary>The code label of the <paramref name="n"/>th realized row. Found by
        /// descent, not by index: unwrapped the label sits inside the pan's clipping cell and
        /// wrapped it does not.</summary>
        public TextElement Label(int n)
        {
            var row = Rows()[n];
            return LayoutProbe.Descendants<TextElement>(row.Children[1]).FirstOrDefault()
                ?? throw new Xunit.Sdk.XunitException($"row {n} holds no code label");
        }

        public string GutterText(int n) =>
            ((TextElement)Rows()[n].Children[0].Children[0]).Content;

        /// <summary>The row a label belongs to — the ancestor that is a direct child of the
        /// list. Counting parents instead is what LayoutProbe.RowIn exists to prevent.</summary>
        public Element RowOf(Element label) =>
            LayoutProbe.RowIn(label, Body)
            ?? throw new Xunit.Sdk.XunitException("the label is not inside a row of the list");

        /// <summary>Presses at a point in the body's own laid-out space. Rows are laid out
        /// unscrolled — the renderer applies the offset — so the coordinate has to be
        /// converted or the press lands wherever that row would be if nothing had scrolled.</summary>
        public void PressInBody(float x, float y)
        {
            var at = LayoutProbe.OnScreen(new Point(x, y), Body);
            _session.Window.MouseDown(at.X, at.Y);
            _session.PumpFrame();
        }

        public IReadOnlyList<FileEntry> Rows(char side) =>
            (IReadOnlyList<FileEntry>)Call("Rows", side)!;

        public void PutCursorOn(string name)
        {
            var rows = Rows('L');
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Name == name)
                {
                    // SetCursor is the plain state setter — in the app the cursor only ever
                    // moves from a key or click handler, which marks dirty on the way out.
                    Call("SetCursor", 'L', i);
                    typeof(CommanderApp).GetMethod("MarkDirty",
                        BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(_app, null);
                    _session.PumpFrame();
                    return;
                }
            Assert.Fail($"{name} is not listed in the left pane");
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
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}
