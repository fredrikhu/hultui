using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Undo and redo in fields the way apps use them: controlled, so every keystroke reports the
/// value, the app stores it and the rebuild hands it back. That rebuild patches the field with
/// the value it already holds, once per key — a history that took it for a value set from code
/// would be gone by the time anyone pressed Ctrl+Z. The element-level cases are in
/// <c>HultUI.Core.Tests</c>' <c>TextUndoTests</c>.
/// </summary>
public class TextUndoTests
{
    private sealed class Form : Component
    {
        public string Name = "";
        public string Notes = "";

        public override Element Build() => Box.New().Padding(10f).Gap(10f).Children(
            TextInput.New(Name).Width(300f).OnTextChanged(v => { Name = v; MarkDirty(); }),
            TextArea.New(Notes).Width(300f).Height(80f).OnTextChanged(v => { Notes = v; MarkDirty(); }));

        public void Rebuild() => MarkDirty();
    }

    private static (HeadlessSession Session, Form Form, Element Root) Open()
    {
        var session = HeadlessSession.Create(400, 200);
        var form = new Form();
        var root = form.Mount();
        session.Application.SetRoot(root);
        session.PumpFrame();
        return (session, form, root);
    }

    /// <summary>A frame per key, as a person types: each one rebuilds before the next arrives.</summary>
    private static void TypeSlowly(HeadlessSession session, string text)
    {
        foreach (char c in text)
        {
            session.Window.TypeText(c.ToString());
            session.PumpFrame();
        }
    }

    private static void Press(HeadlessSession session, KeyCode key, KeyModifiers mods = KeyModifiers.None, int layoutKey = 0)
    {
        session.Window.PressKey(key, mods, layoutKey: layoutKey);
        session.PumpFrame();
    }

    [Fact]
    public void AControlledFieldUndoesThroughItsRebuilds()
    {
        var (session, form, root) = Open();
        using var _ = session;
        var input = First<TextInputElement>(root);
        session.Window.Click(input);
        TypeSlowly(session, "hello world");
        Assert.Equal("hello world", form.Name);

        Press(session, KeyCode.Z, KeyModifiers.Ctrl);
        Assert.Equal("hello ", form.Name);
        Press(session, KeyCode.Z, KeyModifiers.Ctrl);
        Assert.Equal("", form.Name);

        Press(session, KeyCode.Y, KeyModifiers.Ctrl);
        Assert.Equal("hello ", form.Name);
        Press(session, KeyCode.Z, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Assert.Equal("hello world", form.Name);

        // Patched every time, never replaced — otherwise the history would be a fresh
        // element's and this would be testing nothing.
        Assert.Same(input, First<TextInputElement>(root));
        Assert.Equal("hello world", input.Text);
    }

    /// <summary>The layout's letter travels through the input funnel: on QWERTZ the key
    /// printed Z is <see cref="KeyCode.Y"/>, and Ctrl on it undoes.</summary>
    [Fact]
    public void CtrlZUndoesOnAGermanKeyboard()
    {
        var (session, form, root) = Open();
        using var _ = session;
        session.Window.Click(First<TextInputElement>(root));
        TypeSlowly(session, "Grüße");

        Press(session, KeyCode.Y, KeyModifiers.Ctrl, layoutKey: 'z');

        Assert.Equal("", form.Name);
    }

    /// <summary>The app clearing the field — a form submitted — is a value set from code, and
    /// Ctrl+Z must not resurrect what was there, in a field that no longer holds it, from
    /// offsets that no longer describe it.</summary>
    [Fact]
    public void AFieldTheAppClearedHasNothingToUndo()
    {
        var (session, form, root) = Open();
        using var _ = session;
        session.Window.Click(First<TextInputElement>(root));
        TypeSlowly(session, "sent");

        form.Name = "";
        form.Rebuild();
        session.PumpFrame();
        Press(session, KeyCode.Z, KeyModifiers.Ctrl);

        Assert.Equal("", form.Name);
        Assert.False(First<TextInputElement>(root).CanUndo);
    }

    [Fact]
    public void AControlledAreaUndoesThroughItsRebuilds()
    {
        var (session, form, root) = Open();
        using var _ = session;
        var area = First<TextAreaElement>(root);
        session.Window.Click(area);
        TypeSlowly(session, "first");
        Press(session, KeyCode.Enter);
        TypeSlowly(session, "second");
        Assert.Equal("first\nsecond", form.Notes);

        Press(session, KeyCode.Z, KeyModifiers.Ctrl);

        Assert.Equal("first\n", form.Notes);
        Assert.Same(area, First<TextAreaElement>(root));
    }

    private static T First<T>(Element root) where T : Element
    {
        if (root is T match) return match;
        foreach (var child in root.Children)
            if (First<T>(child) is { } found)
                return found;
        return null!;
    }
}
