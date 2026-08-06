using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class TextGroupSelectionTests
{
    private static TextElement Label(string content, object group)
    {
        var el = TextElement.New(content);
        el.Selectable = true;
        el.SelectionGroup = group;
        return el;
    }

    [Fact]
    public void Apply_SelectsEndpointsPartially_AndMiddleFully()
    {
        var g = new object();
        var a = Label("line one", g);    // len 8
        var b = Label("line two", g);
        var c = Label("line three", g);  // len 10
        var ordered = new[] { a, b, c };

        TextGroupSelection.Apply(ordered, a, 5, c, 4);

        Assert.Equal("one", a.SelectedText);       // from offset 5 to end
        Assert.Equal("line two", b.SelectedText);  // full middle line
        Assert.Equal("line", c.SelectedText);      // start to offset 4
    }

    [Fact]
    public void Apply_IsDirectionAgnostic()
    {
        var g = new object();
        var a = Label("alpha", g);
        var b = Label("beta", g);
        var c = Label("gamma", g);
        var forward = new[] { a, b, c };

        TextGroupSelection.Apply(forward, a, 2, c, 3);
        var f = (a.SelectedText, b.SelectedText, c.SelectedText);

        TextGroupSelection.Clear(forward);
        TextGroupSelection.Apply(forward, c, 3, a, 2);   // dragged the other way
        Assert.Equal(f, (a.SelectedText, b.SelectedText, c.SelectedText));
    }

    [Fact]
    public void Apply_SingleLabel_LeavesOthersUnselected()
    {
        var g = new object();
        var a = Label("alpha", g);
        var b = Label("beta", g);
        var c = Label("gamma", g);
        var ordered = new[] { a, b, c };

        TextGroupSelection.Apply(ordered, b, 1, b, 3);

        Assert.False(a.HasSelection);
        Assert.Equal("et", b.SelectedText);
        Assert.False(c.HasSelection);
    }

    [Fact]
    public void GatherText_JoinsSelectedLinesWithNewlines()
    {
        var g = new object();
        var a = Label("line one", g);
        var b = Label("line two", g);
        var c = Label("line three", g);
        var ordered = new[] { a, b, c };

        TextGroupSelection.Apply(ordered, a, 5, c, 4);

        Assert.Equal("one\nline two\nline", TextGroupSelection.GatherText(ordered));
    }

    [Fact]
    public void Clear_RemovesEverySelection()
    {
        var g = new object();
        var a = Label("a text", g);
        var b = Label("b text", g);
        var ordered = new[] { a, b };
        TextGroupSelection.Apply(ordered, a, 0, b, 6);
        Assert.True(a.HasSelection && b.HasSelection);

        TextGroupSelection.Clear(ordered);

        Assert.False(a.HasSelection);
        Assert.False(b.HasSelection);
        Assert.Equal("", TextGroupSelection.GatherText(ordered));
    }

    [Fact]
    public void Apply_MissingEndpoint_IsNoOp()
    {
        var g = new object();
        var a = Label("alpha", g);
        var b = Label("beta", g);
        var stray = Label("stray", g);   // not in the ordered list
        var ordered = new[] { a, b };

        TextGroupSelection.Apply(ordered, a, 0, stray, 2);

        Assert.False(a.HasSelection);
        Assert.False(b.HasSelection);
    }
}
