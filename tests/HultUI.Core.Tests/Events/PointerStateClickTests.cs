using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Events;

public class PointerStateClickTests
{
    private static (PointerState state, BoxElement parent) NewParent()
        => (new PointerState(), BoxElement.New());

    [Fact]
    public void Click_SynthesizedOnSameElement()
    {
        var (state, parent) = NewParent();
        var el = BoxElement.New();
        parent.AddChild(el);

        state.HandlePointerDown(el, new Point(5, 5), 0, KeyModifiers.None);
        var (_, click, _) = state.HandlePointerUp(el, new Point(6, 6), 0, KeyModifiers.None);

        Assert.NotNull(click);
    }

    [Fact]
    public void Click_Synthesized_WhenPressedRowRecycledButPointerStationary()
    {
        // Reproduces a VirtualList row rebuilt between down and up: the pressed element
        // is removed from the tree and replaced by a fresh instance at the same spot.
        var (state, parent) = NewParent();
        var pressed = BoxElement.New();
        parent.AddChild(pressed);

        var pos = new Point(10, 10);
        state.HandlePointerDown(pressed, pos, 0, KeyModifiers.None);

        parent.RemoveChild(pressed);            // recycled away (Parent → null)
        var replacement = BoxElement.New();
        parent.AddChild(replacement);

        var (_, click, _) = state.HandlePointerUp(replacement, pos, 0, KeyModifiers.None);

        Assert.NotNull(click);                  // click still fires on the new row
    }

    [Fact]
    public void Click_NotSynthesized_WhenPointerMovedToLiveSibling()
    {
        // A genuine drag to a different, still-mounted element must not become a click.
        var (state, parent) = NewParent();
        var a = BoxElement.New();
        var b = BoxElement.New();
        parent.AddChild(a);
        parent.AddChild(b);

        state.HandlePointerDown(a, new Point(10, 10), 0, KeyModifiers.None);
        var (_, click, _) = state.HandlePointerUp(b, new Point(200, 200), 0, KeyModifiers.None);

        Assert.Null(click);
    }

    [Fact]
    public void Click_NotSynthesized_WhenRecycledButPointerMovedFar()
    {
        // Detached pressed element but a large move → a drag, not a click.
        var (state, parent) = NewParent();
        var pressed = BoxElement.New();
        parent.AddChild(pressed);

        state.HandlePointerDown(pressed, new Point(10, 10), 0, KeyModifiers.None);
        parent.RemoveChild(pressed);
        var replacement = BoxElement.New();
        parent.AddChild(replacement);

        var (_, click, _) = state.HandlePointerUp(replacement, new Point(300, 300), 0, KeyModifiers.None);

        Assert.Null(click);
    }
}
