using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A key typed into a text field still bubbles to the window, and a window shortcut on a plain
/// letter fired as the user typed it: nothing on the key said the field took it. Now a key the
/// field takes as text says so (<see cref="KeyDownEvent.TargetTakesText"/>) and matches no typing
/// chord (<see cref="KeyChord.Matches"/>), and a key the field acted on (the caret's, Backspace,
/// its Ctrl+A) comes marked <see cref="ElementEvent.Handled"/>. Nothing is stopped: apps' handlers
/// that watch keys from a field (a search box's Up and Down) still get them.
/// </summary>
public class TypingShortcutTests
{
    private sealed record Seen(KeyCode Key, bool Handled, bool TakesText, bool Matched);

    private static (HeadlessSession Session, List<Seen> Seen) Mount(Element field, KeyChord chord)
    {
        var session = HeadlessSession.Create(500, 300);
        var seen = new List<Seen>();
        var plain = Box.New().Width(60f).Height(20f);
        plain.IsFocusable = true;
        var root = Flex.Column(gap: 8).Padding(8).Children(field, plain);
        root.On<KeyDownEvent>(e => seen.Add(new(e.Key, e.Handled, e.TargetTakesText, chord.Matches(e))));
        session.Window.SetRoot(root);
        session.PumpFrame();
        return (session, seen);
    }

    [Fact]
    public void APlainLetterTypedIntoAField_MatchesNoChord_ButOneElsewhereDoes()
    {
        var field = TextInput.New("").Width(Dimension.Px(200));
        var (session, seen) = Mount(field, new KeyChord(KeyCode.K));
        using var _ = session;

        session.Window.Click(field);
        session.Window.KeyDown(KeyCode.K, text: "k");
        Assert.Equal(new Seen(KeyCode.K, false, true, false), seen[^1]);
        Assert.Equal("k", field.Text);

        // The same key with the keyboard on an element that types nothing is the shortcut.
        session.Window.PressKey(KeyCode.Tab);
        session.Window.KeyDown(KeyCode.K, text: "k");
        Assert.Equal(new Seen(KeyCode.K, false, false, true), seen[^1]);
    }

    [Fact]
    public void ChordsThatTypeNothing_StillMatchInAField()
    {
        var field = TextInput.New("").Width(Dimension.Px(200));
        var (session, seen) = Mount(field, new KeyChord(KeyCode.K, KeyModifiers.Ctrl));
        using var _ = session;
        session.Window.Click(field);

        session.Window.KeyDown(KeyCode.K, KeyModifiers.Ctrl, text: "");
        Assert.True(seen[^1].Matched);

        var escape = new KeyChord(KeyCode.Escape);
        var f2 = new KeyChord(KeyCode.F2);
        var probe = new KeyDownEvent { Key = KeyCode.Escape, TargetTakesText = true };
        Assert.True(escape.Matches(probe));
        Assert.True(f2.Matches(new KeyDownEvent { Key = KeyCode.F2, TargetTakesText = true }));
        // A key the platform says types nothing is no typing, even a letter's.
        Assert.True(new KeyChord(KeyCode.K).Matches(new KeyDownEvent { Key = KeyCode.K, Text = "", TargetTakesText = true }));
    }

    [Fact]
    public void AReadOnlyViewer_TypesNothing_SoItsLettersAreShortcuts()
    {
        var viewer = TextArea.New("one\ntwo").Width(Dimension.Px(200)).Height(Dimension.Px(80));
        viewer.ReadOnly = true;
        var (session, seen) = Mount(viewer, new KeyChord(KeyCode.N));
        using var _ = session;

        session.Window.Click(viewer);
        session.Window.KeyDown(KeyCode.N, text: "n");

        Assert.Equal(new Seen(KeyCode.N, false, false, true), seen[^1]);
    }

    /// <summary>The field's own keys come marked; they still bubble.</summary>
    [Fact]
    public void TheKeysAFieldActsOn_ComeMarkedHandled()
    {
        var field = TextInput.New("abc").Width(Dimension.Px(200));
        var (session, seen) = Mount(field, new KeyChord(KeyCode.Left));
        using var _ = session;
        session.Window.Click(field);

        session.Window.PressKey(KeyCode.Left);
        session.Window.PressKey(KeyCode.Backspace);
        session.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);
        session.Window.PressKey(KeyCode.Up);   // a field of one line does nothing with it

        Assert.Equal(
            [(KeyCode.Left, true), (KeyCode.Backspace, true), (KeyCode.A, true), (KeyCode.Up, false)],
            seen.Select(s => (s.Key, s.Handled)));
    }

    /// <summary>Ctrl+Enter in a text area is the app's (a send): it inserted a newline as well.
    /// Enter and Shift+Enter still break the line.</summary>
    [Fact]
    public void CtrlEnterInATextArea_IsTheAppsAndTypesNoNewline()
    {
        var area = TextArea.New("").Width(Dimension.Px(200)).Height(Dimension.Px(80));
        var (session, seen) = Mount(area, new KeyChord(KeyCode.Enter, KeyModifiers.Ctrl));
        using var _ = session;
        session.Window.Click(area);

        session.Window.TypeText("hi");
        session.Window.PressKey(KeyCode.Enter, KeyModifiers.Ctrl);
        Assert.Equal("hi", area.Text);
        Assert.Equal(new Seen(KeyCode.Enter, false, true, true), seen[^1]);

        session.Window.PressKey(KeyCode.Enter);
        session.Window.PressKey(KeyCode.Enter, KeyModifiers.Shift);
        Assert.Equal("hi\n\n", area.Text);
    }
}
