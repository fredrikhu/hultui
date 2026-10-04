using HultUI.Core.Elements;
using Xunit;
using Kind = HultUI.Core.Elements.TextUndoHistory.EditKind;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// The grouping rules of <see cref="TextUndoHistory"/> on their own. The fields' wiring is in
/// <see cref="TextUndoTests"/>; these are the cases that are awkward to reach through a field —
/// the step limit, a surrogate pair, a history that no longer matches its text.
/// </summary>
public class TextUndoHistoryTests
{
    /// <summary>Applies edits the way both fields do: record, then replace, caret collapsed after.</summary>
    private sealed class Editor
    {
        public readonly TextUndoHistory History = new();
        public string Text = "";
        public int Caret;
        public int Anchor;

        public void Edit(Kind kind, int start, int end, string inserted)
        {
            History.Record(kind, start, Text[start..end], inserted, Caret, Anchor);
            Text = string.Concat(Text.AsSpan(0, start), inserted, Text.AsSpan(end));
            Caret = Anchor = start + inserted.Length;
        }

        public void Type(params string[] keystrokes)
        {
            foreach (var s in keystrokes)
                Edit(Kind.Typing, Math.Min(Caret, Anchor), Math.Max(Caret, Anchor), s);
        }

        public void TypeChars(string chars)
        {
            foreach (char c in chars) Type(c.ToString());
        }

        public bool Undo() => History.Undo(ref Text, out Caret, out Anchor);
        public bool Redo() => History.Redo(ref Text, out Caret, out Anchor);
    }

    [Fact]
    public void TypingGroupsUpToTheStartOfTheNextWord()
    {
        var e = new Editor();
        e.TypeChars("hello  world");

        Assert.True(e.Undo());
        Assert.Equal("hello  ", e.Text);
        Assert.True(e.Undo());
        Assert.Equal("", e.Text);
        Assert.False(e.Undo());
    }

    [Fact]
    public void AnEmojiTypedAsASurrogatePairIsOneKeystroke()
    {
        var e = new Editor();
        e.Type("a", "😀", "b");

        Assert.True(e.Undo());
        Assert.Equal("", e.Text);
    }

    /// <summary>An input method commits a phrase as one insert; merging phrases would make a
    /// paragraph of Chinese one step.</summary>
    [Fact]
    public void AMultiCharacterCommitIsAStepOfItsOwn()
    {
        var e = new Editor();
        e.Type("你好", "世界");

        Assert.True(e.Undo());
        Assert.Equal("你好", e.Text);
        Assert.True(e.Undo());
        Assert.Equal("", e.Text);
    }

    [Fact]
    public void ARunOfBackspacesIsOneStep()
    {
        var e = new Editor();
        e.TypeChars("abcdef");
        e.History.Seal();
        for (int i = 0; i < 3; i++)
            e.Edit(Kind.DeleteBackward, e.Caret - 1, e.Caret, "");
        Assert.Equal("abc", e.Text);

        Assert.True(e.Undo());
        Assert.Equal("abcdef", e.Text);
        Assert.Equal(6, e.Caret);
    }

    [Fact]
    public void ARunOfForwardDeletesIsOneStep()
    {
        var e = new Editor { Text = "abcdef" };
        for (int i = 0; i < 3; i++)
            e.Edit(Kind.DeleteForward, 1, 2, "");
        Assert.Equal("aef", e.Text);

        Assert.True(e.Undo());
        Assert.Equal("abcdef", e.Text);
        Assert.False(e.Undo());
    }

    [Fact]
    public void ChangingFromTypingToDeletingStartsANewStep()
    {
        var e = new Editor();
        e.TypeChars("abc");
        e.Edit(Kind.DeleteBackward, 2, 3, "");

        Assert.True(e.Undo());
        Assert.Equal("abc", e.Text);
        Assert.True(e.Undo());
        Assert.Equal("", e.Text);
    }

    [Fact]
    public void SealEndsARunEvenWhenTheNextEditIsAdjacent()
    {
        var e = new Editor();
        e.TypeChars("ab");
        e.History.Seal();
        e.TypeChars("cd");

        Assert.True(e.Undo());
        Assert.Equal("ab", e.Text);
    }

    [Fact]
    public void RedoReappliesAndANewEditDiscardsIt()
    {
        var e = new Editor();
        e.TypeChars("one two");
        e.Undo();
        Assert.True(e.Redo());
        Assert.Equal("one two", e.Text);
        Assert.Equal(7, e.Caret);

        e.Undo();
        e.TypeChars("x");
        Assert.False(e.History.CanRedo);
    }

    [Fact]
    public void TheOldestStepIsDroppedPastTheLimit()
    {
        var e = new Editor();
        for (int i = 0; i < TextUndoHistory.MaxSteps + 5; i++)
            e.Edit(Kind.Other, e.Text.Length, e.Text.Length, "x");

        int undone = 0;
        while (e.Undo()) undone++;

        Assert.Equal(TextUndoHistory.MaxSteps, undone);
        Assert.Equal(new string('x', 5), e.Text);
    }

    /// <summary>A value changed without being recorded leaves steps whose offsets point at text
    /// that is not there. Applying one anyway would splice fragments into the wrong place; the
    /// history is dropped instead.</summary>
    [Fact]
    public void AHistoryThatNoLongerMatchesItsTextIsDropped()
    {
        var e = new Editor();
        e.TypeChars("abc");
        e.Text = "zz";

        Assert.False(e.Undo());
        Assert.Equal("zz", e.Text);
        Assert.False(e.History.CanUndo);
    }
}
