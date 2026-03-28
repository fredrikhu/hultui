using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Core.Tests.Events;

public class FocusTraversalTests
{
    private static BoxElement CreateTree()
    {
        // Tree structure:
        //   root
        //   ├── a (focusable)
        //   ├── b (not focusable)
        //   │   └── c (focusable)
        //   └── d (focusable)
        var root = new BoxElement();
        var a = new BoxElement { IsFocusable = true, Name = "a" };
        var b = new BoxElement { Name = "b" };
        var c = new BoxElement { IsFocusable = true, Name = "c" };
        var d = new BoxElement { IsFocusable = true, Name = "d" };
        b.AddChild(c);
        root.AddChild(a);
        root.AddChild(b);
        root.AddChild(d);
        return root;
    }

    [Fact]
    public void FindNext_FromNull_ReturnsFirstFocusable()
    {
        var root = CreateTree();
        var fm = new FocusManager();

        fm.MoveFocusNext(root);

        Assert.Equal("a", fm.FocusedElement?.Name);
    }

    [Fact]
    public void FindNext_CyclesThroughFocusableElements()
    {
        var root = CreateTree();
        var fm = new FocusManager();

        fm.MoveFocusNext(root); // → a
        Assert.Equal("a", fm.FocusedElement?.Name);

        fm.MoveFocusNext(root); // → c (b is skipped, not focusable)
        Assert.Equal("c", fm.FocusedElement?.Name);

        fm.MoveFocusNext(root); // → d
        Assert.Equal("d", fm.FocusedElement?.Name);

        fm.MoveFocusNext(root); // → a (wraps around)
        Assert.Equal("a", fm.FocusedElement?.Name);
    }

    [Fact]
    public void FindPrevious_CyclesBackward()
    {
        var root = CreateTree();
        var fm = new FocusManager();

        fm.MoveFocusPrevious(root); // → d (last focusable)
        Assert.Equal("d", fm.FocusedElement?.Name);

        fm.MoveFocusPrevious(root); // → c
        Assert.Equal("c", fm.FocusedElement?.Name);

        fm.MoveFocusPrevious(root); // → a
        Assert.Equal("a", fm.FocusedElement?.Name);

        fm.MoveFocusPrevious(root); // → d (wraps around)
        Assert.Equal("d", fm.FocusedElement?.Name);
    }

    [Fact]
    public void NoFocusableElements_DoesNothing()
    {
        var root = new BoxElement();
        root.AddChild(new BoxElement());
        var fm = new FocusManager();

        fm.MoveFocusNext(root);
        Assert.Null(fm.FocusedElement);

        fm.MoveFocusPrevious(root);
        Assert.Null(fm.FocusedElement);
    }

    [Fact]
    public void SingleFocusableElement_StaysOnSame()
    {
        var root = new BoxElement();
        var only = new BoxElement { IsFocusable = true, Name = "only" };
        root.AddChild(only);
        var fm = new FocusManager();

        fm.MoveFocusNext(root);
        Assert.Equal("only", fm.FocusedElement?.Name);

        fm.MoveFocusNext(root);
        Assert.Equal("only", fm.FocusedElement?.Name);
    }

    [Fact]
    public void FocusDispatchesBlurAndFocusEvents()
    {
        var root = new BoxElement();
        var a = new BoxElement { IsFocusable = true, Name = "a" };
        var b = new BoxElement { IsFocusable = true, Name = "b" };
        root.AddChild(a);
        root.AddChild(b);

        bool aBlurred = false;
        bool bFocused = false;
        a.On<BlurEvent>(_ => aBlurred = true);
        b.On<FocusEvent>(_ => bFocused = true);

        var fm = new FocusManager();
        fm.SetFocus(a);
        fm.MoveFocusNext(root);

        Assert.True(aBlurred);
        Assert.True(bFocused);
        Assert.Equal("b", fm.FocusedElement?.Name);
    }

    [Fact]
    public void InteractiveElements_AreFocusableByDefault()
    {
        Assert.True(new ButtonElement().IsFocusable);
        Assert.True(new CheckboxElement().IsFocusable);
        Assert.True(new RadioButtonElement().IsFocusable);
        Assert.True(new TextInputElement().IsFocusable);
    }

    [Fact]
    public void NonInteractiveElements_AreNotFocusableByDefault()
    {
        Assert.False(new BoxElement().IsFocusable);
        Assert.False(new TextElement().IsFocusable);
    }
}
