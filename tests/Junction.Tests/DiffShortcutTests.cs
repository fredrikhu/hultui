using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The diff's three view keys — <c>s</c> unified/split, <c>w</c> wrap, <c>F11</c> fullscreen.
///
/// <para>They are bare letters, so what these tests are really about is the boundaries: the
/// keys act wherever a diff is on screen (with the body focused or not), they are inert in
/// the views that have no diff, and a focused text field keeps them.</para>
/// </summary>
[Collection("JunctionDiff")]
public class DiffShortcutTests
{
    [Fact]
    public void STurnsTheDiffIntoTwoColumnsAndBackAgain()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Assert.Equal(1, CodeLabelsPerRow(app));   // unified: one column of code

            session.Window.PressKey(KeyCode.S);
            session.PumpFrames(3);
            Assert.True(Split(app), "s did not switch to split");
            Assert.Equal(2, CodeLabelsPerRow(app));   // split: the two files side by side

            session.Window.PressKey(KeyCode.S);
            session.PumpFrames(3);
            Assert.False(Split(app), "s did not switch back to unified");
            Assert.Equal(1, CodeLabelsPerRow(app));
        });
    }

    [Fact]
    public void WWrapsWithoutTheDiffBodyHavingToBeFocused()
    {
        // The point of moving the key out of the pane's own handler: it used to reach wrap
        // only while the body held the keyboard, so pressing w after clicking a file in the
        // list beside it did nothing.
        DiffScrollTests.Hosted((session, app) =>
        {
            Assert.False(Wrap(app));
            float before = TallestRow(app);

            session.Window.PressKey(KeyCode.W);   // nothing focused in the diff
            session.PumpFrames(4);

            Assert.True(Wrap(app), "w did not wrap the diff");
            Assert.True(TallestRow(app) > before,
                $"rows were {before:0}px and are {TallestRow(app):0}px — nothing wrapped");

            session.Window.PressKey(KeyCode.W);
            session.PumpFrames(4);
            Assert.False(Wrap(app), "w did not unwrap the diff");
        });
    }

    [Fact]
    public void F11FillsTheWindowWithTheDiffAndLeavesAgain()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.F11);
            session.PumpFrames(3);
            Assert.True(Fullscreen(session), "F11 did not open the fullscreen diff");

            session.Window.PressKey(KeyCode.F11);
            session.PumpFrames(3);
            Assert.False(Fullscreen(session), "F11 did not close the fullscreen diff");

            // Esc still leaves it, as the button in the bar says.
            session.Window.PressKey(KeyCode.F11);
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(3);
            Assert.False(Fullscreen(session), "Esc no longer leaves fullscreen");
        });
    }

    [Fact]
    public void TheKeysStillWorkWhileTheDiffIsFullscreen()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.F11);
            session.PumpFrames(3);

            session.Window.PressKey(KeyCode.S);
            session.PumpFrames(3);
            Assert.True(Split(app), "s is inert in the fullscreen diff");
            Assert.True(Fullscreen(session), "s dropped out of fullscreen");
        });
    }

    [Fact]
    public void TheyAreInertInAViewWithNoDiff()
    {
        // Bare letters have to stay free where there is nothing to toggle — the Branches
        // tab spends five of them on its own actions.
        DiffScrollTests.Hosted((session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);   // Branches
            session.PumpFrames(4);

            var split = Split(app);
            var wrap = Wrap(app);
            session.Window.PressKey(KeyCode.S);
            session.Window.PressKey(KeyCode.W);
            session.Window.PressKey(KeyCode.F11);
            session.PumpFrames(4);

            Assert.Equal(split, Split(app));
            Assert.Equal(wrap, Wrap(app));
            Assert.False(Fullscreen(session), "F11 opened a fullscreen diff over the Branches tab");
        });
    }

    [Fact]
    public void TypingSIntoTheCommitMessageDoesNotSplitTheDiff()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            // The Changes view is already up; its composer is the field to guard against.
            var field = LayoutProbe.Descendants<TextAreaElement>(Root(session)).FirstOrDefault()
                ?? throw new Xunit.Sdk.XunitException("the commit composer is not on screen");
            field.Focus();
            session.PumpFrames(2);

            session.Window.PressKey(KeyCode.S);
            session.Window.PressKey(KeyCode.W);
            session.PumpFrames(3);

            Assert.False(Split(app), "typing 's' into the commit message split the diff");
            Assert.False(Wrap(app), "typing 'w' into the commit message wrapped the diff");
        });
    }

    // ── probes ───────────────────────────────────────────────────────────

    /// <summary>Code labels in one row: one in unified, two in split — the difference the
    /// key is being asked to make, rather than the flag behind it.</summary>
    private static int CodeLabelsPerRow(JunctionApp app)
    {
        var list = Body(app);
        // A code row, not a hunk header: hunk headers carry no selectable label.
        foreach (var row in list.Children)
        {
            var labels = LayoutProbe.Descendants<TextElement>(row, t => t.Selectable).Count;
            if (labels > 0) return labels;
        }
        throw new Xunit.Sdk.XunitException("no code rows are realized");
    }

    private static float TallestRow(JunctionApp app)
    {
        float tallest = 0;
        foreach (var row in Body(app).Children)
            if (LayoutProbe.Descendants<TextElement>(row, t => t.Selectable).Count > 0)
                tallest = Math.Max(tallest, row.Layout.AbsoluteBorderBox.Height);
        return tallest;
    }

    private static bool Fullscreen(HeadlessSession session) =>
        LayoutProbe.Descendants<Element>(Root(session), e => e.Key == "fullscreen-diff").Count > 0;

    private static Element Body(JunctionApp app) =>
        ((HultUI.Core.Reactivity.Ref<Element>)typeof(JunctionApp)
            .GetField("_diffBodyRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!).Current
        ?? throw new Xunit.Sdk.XunitException("the diff body is not mounted");

    private static bool Split(JunctionApp app) => Flag(app, "_diffSplit");
    private static bool Wrap(JunctionApp app) => Flag(app, "_diffWrap");

    private static bool Flag(JunctionApp app, string name) =>
        (bool)typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}
