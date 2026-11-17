using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Keys and input that belong to the whole window, whatever holds the keyboard. A modal dialog's
/// content is not under the window's root, so a key handler on the root never saw a key typed in
/// a dialog (docs/FOCUS.md, by design): a "lock now" shortcut did nothing while a dialog was open,
/// and typing in a dialog was no activity to an idle timeout. <see cref="Window.PreviewKeyDown"/>
/// sees every key first and can end it; <see cref="Window.InputActivity"/> hears every input.
/// </summary>
public class WindowPreviewKeyTests
{
    private static BoxElement FocusableBox()
    {
        var box = Box.New().Width(50f).Height(20f);
        box.IsFocusable = true;
        return box;
    }

    private static (HeadlessSession Session, TextInputElement Field, List<KeyCode> RootKeys) Mount()
    {
        var session = HeadlessSession.Create(500, 400);
        var field = TextInput.New("").Width(Dimension.Px(200));
        var keys = new List<KeyCode>();
        var root = Flex.Column(gap: 8).Padding(8).Children(field, FocusableBox());
        root.On<KeyDownEvent>(EventPhase.Capture, e => keys.Add(e.Key));
        session.Window.SetRoot(root);
        session.PumpFrame();
        return (session, field, keys);
    }

    [Fact]
    public void AKeyTypedInADialog_ReachesThePreview_ThoughTheRootNeverSeesIt()
    {
        var (session, _, rootKeys) = Mount();
        using var _s = session;
        var window = session.Window.Window;
        var previewed = new List<KeyCode>();
        window.PreviewKeyDown += e => previewed.Add(e.Key);

        _ = Dialog.Confirm(window, "Delete?", "Gone for good.");
        session.PumpFrame();
        session.Window.PressKey(KeyCode.L, KeyModifiers.Ctrl);

        Assert.Equal([KeyCode.L], previewed);
        Assert.Empty(rootKeys);   // the modal rule stands
    }

    /// <summary>A key the preview ends goes nowhere else, and does not type its text either: the
    /// text input that follows the key is held back.</summary>
    [Fact]
    public void AKeyThePreviewEnds_ReachesNothing_AndTypesNothing()
    {
        var (session, field, rootKeys) = Mount();
        using var _s = session;
        var window = session.Window.Window;
        window.PreviewKeyDown += e => { if (e.Key == KeyCode.K) e.StopPropagation = true; };
        session.Window.Click(field);
        session.PumpFrame();

        session.Window.PressKey(KeyCode.K, text: "k");
        session.Window.PressKey(KeyCode.J, text: "j");

        Assert.Equal("j", field.Text);
        Assert.Equal([KeyCode.J], rootKeys);
    }

    /// <summary>Only the text right behind the stopped key, and only its own: what comes later is
    /// typed.</summary>
    [Fact]
    public void TheTextHeldBack_IsOnlyTheStoppedKeysOwn()
    {
        var (session, field, _) = Mount();
        using var _s = session;
        var window = session.Window.Window;
        window.PreviewKeyDown += e => { if (e.Key == KeyCode.K) e.StopPropagation = true; };
        session.Window.Click(field);
        session.PumpFrame();

        session.Window.KeyDown(KeyCode.K, text: "k");   // its "k" is held back
        session.Window.TypeText("kk");                    // the next "k"s are typed

        Assert.Equal("kk", field.Text);
    }

    [Fact]
    public void ATabThePreviewEnds_DoesNotMoveTheFocus()
    {
        var (session, field, _) = Mount();
        using var _s = session;
        var window = session.Window.Window;
        session.Window.Click(field);
        session.PumpFrame();
        window.PreviewKeyDown += e => { if (e.Key == KeyCode.Tab) e.StopPropagation = true; };

        session.Window.PressKey(KeyCode.Tab);

        Assert.True(field.IsFocused);
    }

    /// <summary>Escape the preview ends leaves the dialog open; one it lets through closes it.</summary>
    [Fact]
    public async Task AnEscapeThePreviewEnds_LeavesTheDialogOpen()
    {
        var (session, _, _) = Mount();
        using var _s = session;
        var window = session.Window.Window;
        bool stop = true;
        window.PreviewKeyDown += e => { if (stop && e.Key == KeyCode.Escape) e.StopPropagation = true; };
        var answer = Dialog.Confirm(window, "Delete?", "Gone for good.");
        session.PumpFrame();

        session.Window.PressKey(KeyCode.Escape);
        session.PumpFrame();
        Assert.False(answer.IsCompleted);

        stop = false;
        session.Window.PressKey(KeyCode.Escape);
        session.PumpFrame();
        Assert.False(await answer);
    }

    /// <summary>Every input is activity: a key in a dialog, typed text, a pointer over a popover, a
    /// wheel turn, a press, and a key the preview ended.</summary>
    [Fact]
    public void EveryInput_IsActivity_WhateverItReaches()
    {
        var (session, field, _) = Mount();
        using var _s = session;
        var window = session.Window.Window;
        int activity = 0;
        window.InputActivity += () => activity++;
        window.PreviewKeyDown += e => { if (e.Key == KeyCode.F5) e.StopPropagation = true; };

        _ = Dialog.Confirm(window, "Delete?", "Gone for good.");
        session.PumpFrame();
        session.Window.KeyDown(KeyCode.A);
        Assert.Equal(1, activity);
        session.Window.TypeText("x");
        Assert.Equal(2, activity);
        session.Window.MoveMouse(250, 200);
        Assert.Equal(3, activity);
        session.Window.Wheel(250, 200, 0, -1);
        Assert.Equal(4, activity);
        session.Window.KeyDown(KeyCode.F5);
        Assert.Equal(5, activity);
    }
}
