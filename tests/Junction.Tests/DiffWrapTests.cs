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
