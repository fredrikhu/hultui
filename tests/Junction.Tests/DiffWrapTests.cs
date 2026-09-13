using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Wrapping long lines instead of scrolling sideways. The two are alternatives: wrapped,
/// every line is visible in full, so there is nothing left to scroll to and no scrollbar.
/// <para>
/// The rows are virtualized, so this leans on the list being able to give each row its own
/// height — a wrapped line is as tall as the number of lines it breaks into, and its row has
/// to grow with it or the text is clipped.
/// </para>
/// </summary>
[Collection("JunctionDiff")]
public class DiffWrapTests
{
    private static bool Wrap(JunctionApp app) => Field<bool>(app, "_diffWrap");

    private static T Field<T>(JunctionApp app, string name) =>
        (T)typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static HorizontalPan Pan(JunctionApp app) => Field<HorizontalPan>(app, "_diffPan");

    [Fact]
    public void WPutsTheDiffIntoWrapModeAndBackAgain()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Assert.False(Wrap(app));
            Assert.True(Pan(app).CanPan, "the fixture's long lines left nothing to scroll");

            Press(session, app, KeyCode.W);
            Assert.True(Wrap(app));

            Press(session, app, KeyCode.W);
            Assert.False(Wrap(app));
        });
    }

    // Wrapping and panning are alternatives — if both were live the view would scroll
    // sideways past text that is already fully visible.
    [Fact]
    public void WrappingLeavesNothingToScrollSidewaysTo()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Press(session, app, KeyCode.W);

            Assert.False(Pan(app).CanPan);
            Assert.Equal(0f, Pan(app).Offset);
        });
    }

    // A reading preference, not a per-file decision — someone who wants wrapping wants it for
    // every diff, and pressing w on each one is the friction that makes a feature go unused.
    [Fact]
    public void TheWrapChoiceIsWrittenToSettingsAndReadBackOnStart()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            var settings = Field<JunctionSettingsData>(app, "_settings");
            Assert.False(settings.DiffWrap);

            Press(session, app, KeyCode.W);

            Assert.True(settings.DiffWrap, "toggling wrap did not reach the settings");

            // A fresh app over those settings starts wrapped.
            var revived = new JunctionApp(session.Application, session.Window.Window, settings);
            Assert.True(Wrap(revived));
        });
    }

    [Fact]
    public void TheSplitChoiceIsRememberedTheSameWay()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            var settings = Field<JunctionSettingsData>(app, "_settings");
            Assert.False(settings.DiffSplit);

            typeof(JunctionApp).GetField("_diffSplit", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(app, true);
            typeof(JunctionApp).GetMethod("Persist", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, null);

            Assert.True(settings.DiffSplit, "the split choice did not reach the settings");

            var revived = new JunctionApp(session.Application, session.Window.Window, settings);
            Assert.True(Field<bool>(revived, "_diffSplit"));
        });
    }

    [Fact]
    public void AWrappedLineGetsARowTallerThanOneLine()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            float before = TallestCodeRow(session, app);
            Press(session, app, KeyCode.W);
            session.PumpFrames(2);
            float after = TallestCodeRow(session, app);

            // The line count is the direct evidence that wrapping happened; the row growth is
            // that the list made room for it. The bar used to be `after > before * 1.5`, which
            // reads as a noise margin but is really an assumption about glyph width: the
            // fixture breaks into three lines at this machine's metrics and into two on the
            // build runner's, where correctly wrapped rows failed the test.
            int lines = MostLines(session, app);
            Assert.True(lines > 1, $"the longest line laid out as {lines} line(s) — it did not wrap");
            Assert.True(after > before,
                $"rows were {before:0}px and are {after:0}px — they did not grow to fit the wrap");
        });
    }

    // The row must be at least as tall as the text laid out in it. A row shorter than its own
    // text is not a slow row, it is a clipped one: the list forces the height it is given.
    [Fact]
    public void NoWrappedRowIsShorterThanItsOwnText()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Press(session, app, KeyCode.W);
            session.PumpFrames(2);

            var list = Body(app);
            int checkedRows = 0;
            foreach (var label in CodeLabels(session))
            {
                var row = RowOf(label, list);
                if (row is null) continue;
                float drawn = DrawnHeight(label);
                if (drawn <= 0) continue;
                checkedRows++;
                Assert.True(drawn <= row.Layout.AbsoluteBorderBox.Height + 0.5f,
                    $"{drawn:0}px of text sits in a {row.Layout.AbsoluteBorderBox.Height:0}px row");
            }
            Assert.True(checkedRows > 3, $"only {checkedRows} rows were checked — this proves nothing");
        });
    }

    /// <summary>
    /// No frame after the toggle may leave a row shorter than the text drawn in it.
    /// <para>
    /// The sibling test above checks the state the suite happens to settle into; this one
    /// checks every frame from the keypress onwards, because the wrap rebuild is a two-pass
    /// settle — the row heights, the pan's CanPan and the scrollbar around the list all move
    /// on different passes. A frame that realizes rows before the heights apply draws each
    /// line over its neighbour, and nothing marks layout dirty afterwards to correct it.
    /// </para>
    /// </summary>
    [Fact]
    public void NoFrameAfterWrappingDrawsALineOutsideItsRow()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            // Render once before the toggle, the way an app someone is looking at has. This
            // is not incidental: it settles the unwrapped layout, so the wrap rebuild starts
            // from a clean tree rather than a dirty one, and that changes which pass applies
            // the row heights.
            session.Window.CapturePixels().Dispose();

            Body(app).Focus();
            session.Window.PressKey(KeyCode.W);

            int seen = 0;
            for (int frame = 1; frame <= 12; frame++)
            {
                session.PumpFrames(1);
                // Re-read every frame: the wrap rebuild replaces the list rather than
                // patching it, so a reference from an earlier frame describes a detached tree.
                foreach (var row in Body(app).Children)
                {
                    var label = LayoutProbe.Descendants<TextElement>(row,
                        t => t is { Selectable: true, SelectionIndex: >= 0 }).FirstOrDefault();
                    if (label is null) continue;
                    float drawn = LayoutProbe.DrawnText(label).Height;
                    if (drawn <= 0) continue;
                    seen++;
                    float rowHeight = row.Layout.AbsoluteBorderBox.Height;
                    Assert.True(drawn <= rowHeight + 0.5f,
                        $"{frame} frame(s) after wrapping, {drawn:0}px of text sits in a {rowHeight:0}px row");
                }
            }
            // Counted across the frames rather than within each one: right after the toggle
            // the list has just been replaced and has realized nothing yet, so an early frame
            // legitimately has no rows to check — as the build runner showed.
            Assert.True(seen > 2, $"only {seen} row observations — this proves nothing");
        });
    }

    /// <summary>
    /// Narrowing the window re-measures the wrapped rows.
    /// <para>
    /// A wrapped row's height <i>is</i> a function of the width: it is measured in the build
    /// and handed to the virtual list, which stacks the rows from those numbers. Every other
    /// row in this app is a fixed height, so an ordinary resize deliberately did not rebuild
    /// — and a wrapped diff was then stacked at the old widths while each row laid its own
    /// text out at the new one. Every line that gained a visual line drew outside its row and
    /// over its neighbour, several rows at a time, and nothing afterwards corrected it.
    /// </para>
    /// <para>
    /// The height has to come from the label's own line boxes rather than from its laid-out
    /// box: the row forces its height on the label, so the box reads back as the row's and a
    /// check against it passes straight through the bug. The lines are what actually get
    /// drawn.
    /// </para>
    /// <para>
    /// The width moves without crossing a responsive breakpoint, since crossing one rebuilds
    /// for its own reasons and would hide what this covers.
    /// </para>
    /// </summary>
    [Fact]
    public void NarrowingTheWindowReMeasuresTheWrappedRows()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Press(session, app, KeyCode.W);
            session.PumpFrames(3);
            int before = MostLines(session, app);
            Assert.True(before > 1, "the fixture did not wrap at the starting width");

            // 1400 → 1150. Both are well clear of the diff-hiding breakpoint, so nothing but
            // the width itself changes.
            session.Window.SetClientSize(1150, 900);
            session.PumpFrames(6);

            Assert.True(MostLines(session, app) > before,
                "narrowing did not add a visual line — the fixture cannot show this bug");

            var list = Body(app);
            int checkedRows = 0;
            foreach (var label in CodeLabels(session))
            {
                var row = RowOf(label, list);
                if (row is null) continue;
                float drawn = DrawnHeight(label);
                if (drawn <= 0) continue;
                checkedRows++;
                float rowHeight = row.Layout.AbsoluteBorderBox.Height;
                Assert.True(drawn <= rowHeight + 0.5f,
                    $"after narrowing, {drawn:0}px of text sits in a {rowHeight:0}px row");
            }
            Assert.True(checkedRows > 3, $"only {checkedRows} rows were checked — this proves nothing");
        });
    }

    /// <summary>The same for widening, where the failure is a row left taller than its text
    /// rather than one overflowing: harmless to look at, and the same stale measurement.</summary>
    [Fact]
    public void WideningTheWindowReMeasuresThemToo()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Press(session, app, KeyCode.W);
            session.PumpFrames(3);
            float tallBefore = TallestCodeRow(session, app);

            session.Window.SetClientSize(1900, 900);
            session.PumpFrames(6);

            // More room per line means fewer lines per row, so the tallest row has to shrink.
            float tallAfter = TallestCodeRow(session, app);
            Assert.True(tallAfter < tallBefore,
                $"the tallest row was {tallBefore:0}px and is {tallAfter:0}px — the widening was not measured");
        });
    }

    /// <summary>The measurement the rows were laid out with has to be the width they were
    /// laid out into. This is the same fault one level down from the two tests above, and it
    /// is the one that says <i>why</i> — a row can happen to be tall enough at a stale width
    /// while the two numbers still disagree.</summary>
    [Fact]
    public void TheMeasuredWidthAgreesWithTheLaidOutOneAfterAResize()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Press(session, app, KeyCode.W);
            session.PumpFrames(3);

            session.Window.SetClientSize(1150, 900);
            session.PumpFrames(6);

            var body = Body(app);
            float laidOut = (float)typeof(JunctionApp)
                .GetMethod("CodeWidthFor", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, [body.Layout.AbsoluteContentBox.Width])!;
            var shape = Field<(bool Wrap, bool Split, float Width, int Rows, int Review)>(app, "_diffHeightShape");

            Assert.Equal(laidOut, shape.Width, 1);
        });
    }



    /// <summary>
    /// The rows are never measured at a width wider than the text actually gets.
    /// <para>
    /// This is the invariant behind the intermittent overlap, and it is one-sided. The code
    /// label carries its own horizontal padding and the measurement did not subtract it, so
    /// every line was wrapped at a width 14px wider than the label ever has. Any line whose
    /// length landed in that band came out one visual line short and drew over the row below
    /// — which is why it looked like it only happened sometimes, and only at some widths.
    /// </para>
    /// <para>
    /// Measuring a little <i>narrower</i> is not the same fault: it predicts a line break
    /// that does not happen, so the row is a line too tall and the extra shows as a small
    /// gap. Nothing is clipped and nothing overlaps. So this asserts the direction plus a
    /// sanity bound, rather than exact equality — a few pixels of layout slack between the
    /// body and the row is not worth pinning, and pinning it would make the test fail on any
    /// harmless change to the column model.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1400)]
    [InlineData(1120)]
    [InlineData(1000)]
    public void RowsAreNeverMeasuredWiderThanTheirTextActuallyGets(int windowWidth)
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Press(session, app, KeyCode.W);
            session.PumpFrames(3);
            session.Window.SetClientSize(windowWidth, 900);
            session.PumpFrames(6);

            var label = CodeLabels(session).FirstOrDefault(l => RowOf(l, Body(app)) is not null)
                ?? throw new Xunit.Sdk.XunitException("no code label on screen");
            float actual = label.Layout.AbsoluteContentBox.Width;
            var shape = Field<(bool Wrap, bool Split, float Width, int Rows, int Review)>(app, "_diffHeightShape");

            Assert.True(shape.Width <= actual + 0.5f,
                $"rows were measured at {shape.Width:0.0}px but the text only gets {actual:0.0}px "
                + "— every line that band is the difference for will draw outside its row");
            // Not so narrow that the rows are wastefully tall: a whole missing column would
            // pass the check above and is just as wrong.
            Assert.True(shape.Width >= actual - 8f,
                $"rows were measured at {shape.Width:0.0}px against {actual:0.0}px of text — too conservative");
        });
    }

    /// <summary>
    /// A line whose content is one unbreakable token stays inside its column.
    /// <para>
    /// The text engine breaks such a token by character, so this was never about wrapping.
    /// The code cell grew instead: <c>FlexGrow(1)</c> with the default <c>auto</c> basis
    /// sizes an item to its own content first, and the content width of a line holding one
    /// token is that token — a minified bundle, a base64 blob, a long hash. The label took a
    /// thousand pixels inside a three-hundred-pixel column, wrapped its text at the width it
    /// had taken rather than the one on screen, and the pane clipped the rest. Wrapping turns
    /// panning off, so nothing could bring it back into view.
    /// </para>
    /// <para>The row was the right height throughout — the heights are measured at the column
    /// width, which was never wrong. Only the label's box was, which is why this asks about
    /// the box and about how wide the lines came out.</para>
    /// </summary>
    [Theory]
    [InlineData(1400)]
    [InlineData(1100)]
    public void ALineOfOneUnbreakableTokenStaysInsideItsColumn(int windowWidth)
    {
        // One token far wider than any column this window can offer, plus an ordinary line
        // so the assertions below have something correct to sit beside.
        var content = "    // a token that cannot break: " + new string('x', 160) + " end\n"
            + "    " + new string('x', 160) + "\n"
            + "    // ordinary code that wraps at spaces the way real source does, at length";

        DiffScrollTests.Hosted((session, app) =>
        {
            Press(session, app, KeyCode.W);
            session.PumpFrames(3);
            session.Window.SetClientSize(windowWidth, 900);
            session.PumpFrames(6);

            var list = Body(app);
            int checkedRows = 0;
            foreach (var label in CodeLabels(session))
            {
                var row = RowOf(label, list);
                if (row is null) continue;
                var labelBox = label.Layout.AbsoluteContentBox;
                var rowBox = row.Layout.AbsoluteBorderBox;
                if (labelBox.Width <= 0) continue;
                checkedRows++;

                Assert.True(labelBox.X + labelBox.Width <= rowBox.X + rowBox.Width + 0.5f,
                    $"the code cell is {labelBox.Width:0}px wide and runs "
                    + $"{labelBox.X + labelBox.Width - rowBox.X - rowBox.Width:0}px past its row");

                foreach (float lineWidth in LineWidths(label))
                    Assert.True(lineWidth <= labelBox.Width + 0.5f,
                        $"a {lineWidth:0}px line sits in a {labelBox.Width:0}px cell");
            }
            Assert.True(checkedRows > 1, $"only {checkedRows} rows were checked — this proves nothing");
        }, content);
    }

    /// <summary>Each line's laid-out width, from the label's own line boxes.</summary>
    private static IEnumerable<float> LineWidths(TextElement label)
    {
        var lines = (HultUI.Core.Rendering.TextLineBox[])
            typeof(TextElement).GetField("_lines", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(label)!;
        foreach (var line in lines) yield return line.Width;
    }

    private static void Press(HeadlessSession session, JunctionApp app, KeyCode key)
    {
        var body = Field<HultUI.Core.Reactivity.Ref<Element>>(app, "_diffBodyRef").Current
            ?? throw new Xunit.Sdk.XunitException("the diff body is not mounted");
        body.Focus();
        session.Window.PressKey(key);
        session.PumpFrames(2);
    }

    /// <summary>The row a label belongs to: the ancestor that is a direct child of the list.
    /// Walking a fixed number of parents does not work — wrapped, the label sits straight in
    /// the row; unwrapped, a panning cell sits between them.</summary>
    private static Element? RowOf(Element label, Element list)
    {
        for (var e = label; e is not null; e = e.Parent)
            if (ReferenceEquals(e.Parent, list)) return e;
        return null;
    }

    private static Element Body(JunctionApp app) =>
        Field<HultUI.Core.Reactivity.Ref<Element>>(app, "_diffBodyRef").Current
        ?? throw new Xunit.Sdk.XunitException("the diff body is not mounted");

    /// <summary>The most lines any code label on screen broke into.</summary>
    private static int MostLines(HeadlessSession session, JunctionApp app)
    {
        int most = 0;
        foreach (var label in CodeLabels(session))
            most = System.Math.Max(most, LayoutProbe.DrawnText(label).LineCount);
        return most;
    }

    private static float TallestCodeRow(HeadlessSession session, JunctionApp app)
    {
        var list = Body(app);
        float tallest = 0;
        foreach (var label in CodeLabels(session))
            if (RowOf(label, list) is { } row)
                tallest = System.Math.Max(tallest, row.Layout.AbsoluteBorderBox.Height);
        return tallest;
    }

    private static float DrawnHeight(TextElement label)
    {
        var lines = (HultUI.Core.Rendering.TextLineBox[])
            typeof(TextElement).GetField("_lines", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(label)!;
        float bottom = 0;
        foreach (var line in lines) bottom = System.Math.Max(bottom, line.OffsetY + line.Height);
        return bottom;
    }

    private static List<TextElement> CodeLabels(HeadlessSession session)
    {
        var found = new List<TextElement>();
        Walk((Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window));
        return found;

        void Walk(Element? e)
        {
            if (e is null) return;
            if (e is TextElement { Selectable: true, SelectionIndex: >= 0 } te) found.Add(te);
            foreach (var c in e.Children) Walk(c);
        }
    }
}
