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

            Assert.True(after > before * 1.5f,
                $"rows were {before:0}px and are {after:0}px — the long lines did not wrap");
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
