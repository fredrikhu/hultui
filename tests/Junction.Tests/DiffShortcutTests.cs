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
    public void IIgnoresWhitespaceChangesAndSaysWhatThatCosts()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Assert.False(IgnoreWhitespace(app));

            session.Window.PressKey(KeyCode.I);
            // The diff reloads under `git -w`, so this is a wait rather than a pump: the
            // strip appears with the reloaded diff, not with the keypress.
            PumpUntil(session, () => Shows(session, "Ignoring whitespace"),
                "i did not turn on ignore-whitespace");
            Assert.True(IgnoreWhitespace(app));

            session.Window.PressKey(KeyCode.I);
            PumpUntil(session, () => !Shows(session, "Ignoring whitespace"),
                "i did not turn ignore-whitespace back off");
            Assert.False(IgnoreWhitespace(app));
        });
    }

    [Fact]
    public void FOpensTheFindBarOverTheDiff()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Assert.False(FindBarOpen(session), "the find bar starts open");

            session.Window.PressKey(KeyCode.F);
            PumpUntil(session, () => FindBarOpen(session), "f did not open the find bar");

            // Esc closes it, and the letter goes back to opening it.
            session.Window.PressKey(KeyCode.Escape);
            PumpUntil(session, () => !FindBarOpen(session), "Esc did not close the find bar");
            session.Window.PressKey(KeyCode.F);
            PumpUntil(session, () => FindBarOpen(session), "f did not reopen the find bar");
        });
    }

    [Fact]
    public void TypingIntoTheCommitMessageNeitherOpensNorDrivesTheFindBar()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            var field = LayoutProbe.Descendants<TextAreaElement>(Root(session)).FirstOrDefault()
                ?? throw new Xunit.Sdk.XunitException("the commit composer is not on screen");
            field.Focus();
            session.PumpFrames(2);

            session.Window.PressKey(KeyCode.F);
            session.PumpFrames(3);
            Assert.False(FindBarOpen(session),
                "typing 'f' into the commit message opened the find bar");

            // And with the bar open and a query in it, n / p are letters in that field rather
            // than match navigation — the composer is a TextArea, which the guard used to
            // miss, so typing "not quite" into it walked the matches instead.
            session.Window.PressKey(KeyCode.F, KeyModifiers.Ctrl);
            PumpUntil(session, () => FindBarOpen(session), "Ctrl+F did not open the find bar");
            session.Window.TypeText("line");
            session.PumpFrames(3);
            Assert.True(Search(app).Hits.Count > 1, "the fixture has too few matches to navigate");

            var at = Search(app).Index;
            field.Focus();
            session.PumpFrames(2);
            session.Window.PressKey(KeyCode.N);
            session.Window.PressKey(KeyCode.P);
            session.PumpFrames(3);

            Assert.Equal(at, Search(app).Index);
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
            var ws = IgnoreWhitespace(app);
            session.Window.PressKey(KeyCode.S);
            session.Window.PressKey(KeyCode.W);
            session.Window.PressKey(KeyCode.F11);
            session.PumpFrames(4);
            // I is the Branches tab's own key (include this branch in History), so it is
            // pressed apart from the others: what must not happen is the diff taking it.
            session.Window.PressKey(KeyCode.I);
            session.PumpFrames(4);

            Assert.Equal(split, Split(app));
            Assert.Equal(wrap, Wrap(app));
            Assert.Equal(ws, IgnoreWhitespace(app));
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
            session.Window.PressKey(KeyCode.I);
            session.PumpFrames(3);

            Assert.False(Split(app), "typing 's' into the commit message split the diff");
            Assert.False(Wrap(app), "typing 'w' into the commit message wrapped the diff");
            Assert.False(IgnoreWhitespace(app), "typing 'i' into the commit message changed the diff");
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

    private static bool Shows(HeadlessSession session, string text) =>
        LayoutProbe.Descendants<TextElement>(Root(session),
            t => t.Content?.Contains(text, StringComparison.Ordinal) == true
                 && t.Layout.AbsoluteBorderBox.Height > 0).Count > 0;

    private static void PumpUntil(HeadlessSession session, Func<bool> condition, string message)
    {
        try
        {
            session.PumpUntil(condition, TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException($"{message}.\n{e.Message}");
        }
    }

    /// <summary>The diff's find state, which is what says where in the matches the bar is.</summary>
    private static HultUI.Core.Builders.TextSearch Search(JunctionApp app)
    {
        var state = typeof(JunctionApp)
            .GetField("_diffFind", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        return (HultUI.Core.Builders.TextSearch)state.GetType()
            .GetField("Search", BindingFlags.Public | BindingFlags.Instance)!.GetValue(state)!;
    }

    /// <summary>The find bar by its key rather than by its placeholder: the placeholder is a
    /// property of the field, not text in the tree, so looking for it finds nothing whether
    /// the bar is there or not.</summary>
    private static bool FindBarOpen(HeadlessSession session) =>
        LayoutProbe.Descendants<Element>(Root(session), e => e.Key == "diff-find").Count > 0;

    private static bool Fullscreen(HeadlessSession session) =>
        LayoutProbe.Descendants<Element>(Root(session), e => e.Key == "fullscreen-diff").Count > 0;

    private static Element Body(JunctionApp app) =>
        ((HultUI.Core.Reactivity.Ref<Element>)typeof(JunctionApp)
            .GetField("_diffBodyRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!).Current
        ?? throw new Xunit.Sdk.XunitException("the diff body is not mounted");

    private static bool Split(JunctionApp app) => Flag(app, "_diffSplit");
    private static bool Wrap(JunctionApp app) => Flag(app, "_diffWrap");

    /// <summary>The setting behind the <c>-w</c> button. It lives on the settings object
    /// rather than on the app, being persisted and read by every diff loader.</summary>
    private static bool IgnoreWhitespace(JunctionApp app)
    {
        var settings = typeof(JunctionApp)
            .GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        var git = settings.GetType().GetProperty("Git")!.GetValue(settings)!;
        return (bool)git.GetType().GetProperty("IgnoreWhitespace")!.GetValue(git)!;
    }

    private static bool Flag(JunctionApp app, string name) =>
        (bool)typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}
