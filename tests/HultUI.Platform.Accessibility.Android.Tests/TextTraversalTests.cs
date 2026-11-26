using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Platform.Accessibility.Android.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// TalkBack's reading by character, word, line and paragraph: the units a node offers, each move
/// told as a traversal TalkBack speaks, from where the last move left off; in a field the caret goes
/// with it, through the router, and a caret moved elsewhere starts the moves from there.
/// </summary>
public class TextTraversalTests
{
    private static Harness Page(params Element[] children) =>
        new(Box.New().Width(400).Height(800).Children(children));

    private static (int From, int To, bool Forward) Moved(Harness h, int id, AndroidGranularity unit, bool forward = true, bool extend = false)
    {
        h.Sink.Clear();
        Assert.True(h.Bridge.Perform(id, forward ? AndroidActions.NextAtGranularity : AndroidActions.PreviousAtGranularity,
            new AndroidActionArgs(Granularity: unit, ExtendSelection: extend)), "no move");
        var e = Assert.Single(h.Sink.Events);
        Assert.Equal(AndroidEventType.ViewTextTraversed, e.Type);
        Assert.Equal(unit, e.Granularity);
        return (e.FromIndex, e.ToIndex, e.Forward);
    }

    private static string Said(Harness h) => h.Sink.Events[^1] is { Text: { } t } e ? t[e.FromIndex..e.ToIndex] : "";

    [Fact]
    public void TheUnits_AreALabelsCharactersWordsAndParagraphs_AndAFieldsLinesToo()
    {
        var label = Text.New("Hello world");
        var area = TextArea.New("one\ntwo").Width(300).Height(100);
        var input = TextInput.New("abc").Width(200);
        var secret = TextInput.New("abc").Password().Width(200);
        using var h = Page(label, area, input, secret);

        var all = AndroidGranularity.Character | AndroidGranularity.Word | AndroidGranularity.Paragraph;
        Assert.Equal(all, h.Node(label).Granularities);
        Assert.True(h.Node(label).Can(AndroidActions.NextAtGranularity | AndroidActions.PreviousAtGranularity));
        Assert.False(h.Node(label).Can(AndroidActions.SetSelection));

        Assert.Equal(all | AndroidGranularity.Line, h.Node(area).Granularities);
        Assert.True(h.Node(area).Can(AndroidActions.SetSelection));

        // A text input's caret is not in the tree; a password is never read out.
        Assert.Equal(AndroidGranularity.None, h.Node(input).Granularities);
        Assert.False(h.Node(input).Can(AndroidActions.NextAtGranularity));
        Assert.Equal(AndroidGranularity.None, h.Node(secret).Granularities);
    }

    [Fact]
    public void ALabel_IsReadWordByWord_ForwardFromItsStart_AndBack()
    {
        var label = Text.New("Hello big world");
        using var h = Page(label);
        int id = h.Id(label);

        Assert.Equal((0, 5, true), Moved(h, id, AndroidGranularity.Word));
        Assert.Equal("Hello", Said(h));
        Assert.Equal((6, 9, true), Moved(h, id, AndroidGranularity.Word));
        Assert.Equal((10, 15, true), Moved(h, id, AndroidGranularity.Word));
        Assert.False(h.Bridge.Perform(id, AndroidActions.NextAtGranularity, new AndroidActionArgs(Granularity: AndroidGranularity.Word)));

        Assert.Equal((10, 15, false), Moved(h, id, AndroidGranularity.Word, forward: false));
        Assert.Equal((6, 9, false), Moved(h, id, AndroidGranularity.Word, forward: false));
        Assert.Equal("big", Said(h));
        // A label moves no caret: nothing goes to the router.
        Assert.Empty(h.Host.Calls);
    }

    [Fact]
    public void BackFirst_ALabelIsReadFromItsEnd()
    {
        var label = Text.New("ab");
        using var h = Page(label);
        Assert.Equal((1, 2, false), Moved(h, h.Id(label), AndroidGranularity.Character, forward: false));
    }

    [Fact]
    public void TalkBacksFocusMovingAway_StartsTheMovesAgain()
    {
        var first = Text.New("one two");
        var second = Text.New("three");
        using var h = Page(first, second);
        int id = h.Id(first);
        Moved(h, id, AndroidGranularity.Word);
        Assert.True(h.Bridge.Perform(h.Id(second), AndroidActions.AccessibilityFocus));
        Assert.True(h.Bridge.Perform(id, AndroidActions.AccessibilityFocus));
        Assert.Equal((0, 3, true), Moved(h, id, AndroidGranularity.Word));
    }

    [Fact]
    public void InAField_TheCaretMovesWithEachCharacter_AndAnExtendingMoveSelects()
    {
        var area = TextArea.New("abc").Width(300).Height(100);
        area.Select(0, 0);
        using var h = Page(area);
        int id = h.Id(area);
        ulong element = area.Id.Value;

        Assert.Equal((0, 1, true), Moved(h, id, AndroidGranularity.Character));
        Assert.Equal((1, 2, true), Moved(h, id, AndroidGranularity.Character));
        Assert.Equal((1, 2, false), Moved(h, id, AndroidGranularity.Character, forward: false, extend: true));
        Assert.Equal([$"selection {element} 1 1", $"selection {element} 2 2", $"selection {element} 2 1"], h.Host.Calls);
    }

    [Fact]
    public void InATextArea_LinesAreReadWhole_ThenTheNext()
    {
        var area = TextArea.New("one\ntwo\nthree").Width(300).Height(100);
        area.Select(0, 0);
        using var h = Page(area);
        int id = h.Id(area);

        Assert.Equal((0, 3, true), Moved(h, id, AndroidGranularity.Line));
        Assert.Equal((4, 7, true), Moved(h, id, AndroidGranularity.Line));
        Assert.Equal("two", Said(h));
        Assert.Equal((8, 13, true), Moved(h, id, AndroidGranularity.Line));
        Assert.Equal((8, 13, false), Moved(h, id, AndroidGranularity.Line, forward: false));
        Assert.Equal((4, 7, false), Moved(h, id, AndroidGranularity.Line, forward: false));
    }

    [Fact]
    public void ParagraphsAreReadWithoutTheirLineBreak()
    {
        var area = TextArea.New("first one\nsecond").Width(300).Height(100);
        area.Select(0, 0);
        using var h = Page(area);
        int id = h.Id(area);
        Assert.Equal((0, 9, true), Moved(h, id, AndroidGranularity.Paragraph));
        Assert.Equal((10, 16, true), Moved(h, id, AndroidGranularity.Paragraph));
        Assert.Equal("second", Said(h));
    }

    [Fact]
    public void AFieldsCaretMovedElsewhere_TheMovesGoOnFromThere()
    {
        var area = TextArea.New("hello world").Width(300).Height(100);
        area.Select(0, 0);
        using var h = Page(area);
        int id = h.Id(area);
        Assert.Equal((0, 5, true), Moved(h, id, AndroidGranularity.Word));

        // The router is a fake: the caret stays where it was until the app moves it, as a tap would.
        area.Select(8, 8);
        h.Apply();
        Assert.Equal((8, 11, true), Moved(h, id, AndroidGranularity.Word));
        Assert.Equal("rld", Said(h));
    }

    [Fact]
    public void AFieldsCaret_WhereTheLastMovePutIt_IsWhereTheMovesGoOn()
    {
        var area = TextArea.New("hello big world").Width(300).Height(100);
        area.Select(0, 0);
        using var h = Page(area);
        int id = h.Id(area);
        Assert.Equal((0, 5, true), Moved(h, id, AndroidGranularity.Word));

        // The move landed: the caret is where it was asked to be.
        area.Select(5, 5);
        h.Apply();
        Assert.Equal((6, 9, true), Moved(h, id, AndroidGranularity.Word));
    }

    [Fact]
    public void TalkBacksSetSelection_GoesToTheField_AndTheMovesGoOnFromIt()
    {
        var area = TextArea.New("abcdef").Width(300).Height(100);
        using var h = Page(area);
        int id = h.Id(area);

        Assert.True(h.Bridge.Perform(id, AndroidActions.SetSelection, new AndroidActionArgs(SelectionStart: 2, SelectionEnd: 2)));
        Assert.Equal([$"selection {area.Id.Value} 2 2"], h.Host.Calls);
        Assert.Equal((2, 3, true), Moved(h, id, AndroidGranularity.Character));
    }

    [Theory]
    [InlineData("a  b", 1, true, 3, 4)]        // forward over blanks to the next word
    [InlineData("a  b", 3, false, 0, 1)]       // back over blanks to the word before
    [InlineData("hello", 2, true, 2, 5)]       // from inside a word: the rest of it
    [InlineData("hello", 2, false, 0, 2)]      // back from inside: its start to here
    [InlineData("a 😀 b", 2, true, 2, 4)]       // an emoji is one character
    public void TheUnitAround_AnOffset(string text, int from, bool forward, int start, int end)
    {
        var unit = text.Contains("😀") ? AndroidGranularity.Character : AndroidGranularity.Word;
        Assert.True(AndroidAccessibilityBridge.Unit(new AccessibleText(text), unit, forward, from, out int s, out int e));
        Assert.Equal((start, end), (s, e));
    }
}
