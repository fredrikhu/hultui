using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Events;

/// <summary>
/// <see cref="PointerState.UpdateHover"/> follows mouseenter/mouseleave: when the hovered
/// element changes, leave fires on the old element and its ancestors up to the one both share,
/// and enter on the new element and its ancestors up to it, deepest first; the shared ancestors
/// hear nothing.
/// </summary>
public class PointerStateHoverTests
{
    //   root
    //   ├── list
    //   │   ├── row1
    //   │   │   └── label1
    //   │   └── row2
    //   │       └── cell
    //   │           └── label2
    //   └── footer
    private sealed class Tree
    {
        public readonly BoxElement Root = new() { Name = "root" };
        public readonly BoxElement List = new() { Name = "list" };
        public readonly BoxElement Row1 = new() { Name = "row1" };
        public readonly BoxElement Label1 = new() { Name = "label1" };
        public readonly BoxElement Row2 = new() { Name = "row2" };
        public readonly BoxElement Cell = new() { Name = "cell" };
        public readonly BoxElement Label2 = new() { Name = "label2" };
        public readonly BoxElement Footer = new() { Name = "footer" };
        public readonly List<string> Log = [];

        public Tree()
        {
            Root.AddChild(List);
            List.AddChild(Row1);
            Row1.AddChild(Label1);
            List.AddChild(Row2);
            Row2.AddChild(Cell);
            Cell.AddChild(Label2);
            Root.AddChild(Footer);
            foreach (var e in new[] { Root, List, Row1, Label1, Row2, Cell, Label2, Footer })
            {
                e.On<PointerEnterEvent>(evt => Log.Add("enter " + evt.CurrentTarget?.Name));
                e.On<PointerLeaveEvent>(evt => Log.Add("leave " + evt.CurrentTarget?.Name));
            }
        }
    }

    private static List<string> Move(PointerState state, Tree tree, Element? to)
    {
        tree.Log.Clear();
        state.UpdateHover(to, new Point(1, 1), KeyModifiers.None);
        return [.. tree.Log];
    }

    [Fact]
    public void FromNothing_EntersTheWholeChain_DeepestFirst()
    {
        var tree = new Tree();
        var state = new PointerState();

        Assert.Equal(["enter label1", "enter row1", "enter list", "enter root"], Move(state, tree, tree.Label1));
    }

    [Fact]
    public void BetweenCousinsAtDifferentDepths_StopsAtTheSharedAncestor()
    {
        var tree = new Tree();
        var state = new PointerState();
        Move(state, tree, tree.Label2);

        Assert.Equal(["leave label2", "leave cell", "leave row2", "enter label1", "enter row1"],
            Move(state, tree, tree.Label1));
        Assert.Equal(["leave label1", "leave row1", "enter label2", "enter cell", "enter row2"],
            Move(state, tree, tree.Label2));
        Assert.Equal(["leave label2", "leave cell", "leave row2", "leave list", "enter footer"],
            Move(state, tree, tree.Footer));
    }

    [Fact]
    public void ToAnAncestorOrADescendant_OnlyLeavesOrOnlyEnters()
    {
        var tree = new Tree();
        var state = new PointerState();
        Move(state, tree, tree.Label2);

        Assert.Equal(["leave label2", "leave cell"], Move(state, tree, tree.Row2));
        Assert.Equal(["enter label2", "enter cell"], Move(state, tree, tree.Label2));
        Assert.Equal(["leave label2", "leave cell", "leave row2", "leave list", "leave root"], Move(state, tree, null));
    }

    [Fact]
    public void IntoAnotherTree_LeavesAndEntersEverything()
    {
        // An overlay is a separate root, and a row removed under the pointer has a chain
        // of its own: nothing is shared, so both chains are walked to the top.
        var tree = new Tree();
        var overlay = new BoxElement { Name = "overlay" };
        var item = new BoxElement { Name = "item" };
        overlay.AddChild(item);
        overlay.On<PointerEnterEvent>(evt => tree.Log.Add("enter " + evt.CurrentTarget?.Name));
        item.On<PointerEnterEvent>(evt => tree.Log.Add("enter " + evt.CurrentTarget?.Name));
        var state = new PointerState();
        Move(state, tree, tree.Row1);

        Assert.Equal(["leave row1", "leave list", "leave root", "enter item", "enter overlay"],
            Move(state, tree, item));
    }

    private static object? _sink;

    /// <summary>A hover change allocates its two events and nothing else. Finding the shared
    /// ancestor built a set of the old element's ancestors, ~350 bytes on every change (plan 17,
    /// runtime-frame-floor).</summary>
    [Fact]
    public void AHoverChange_AllocatesOnlyItsEvents()
    {
        var root = new BoxElement();
        var leaves = new BoxElement[4];
        for (int i = 0; i < leaves.Length; i++)
        {
            Element parent = root;
            for (int depth = 0; depth < 6; depth++)
            {
                var box = new BoxElement();
                parent.AddChild(box);
                parent = box;
            }
            parent.AddChild(leaves[i] = new BoxElement());
        }
        var state = new PointerState();
        for (int i = 0; i < 8; i++) state.UpdateHover(leaves[i % 4], new Point(i, i), KeyModifiers.None);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            _sink = new PointerLeaveEvent { Position = new Point(i, i) };
            _sink = new PointerEnterEvent { Position = new Point(i, i) };
        }
        long events = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) state.UpdateHover(leaves[i % 4], new Point(i, i), KeyModifiers.None);
        long changes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(changes <= events,
            $"100 hover changes allocated {changes} B; their events alone are {events} B");
    }
}
