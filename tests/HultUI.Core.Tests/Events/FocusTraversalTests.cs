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
    public void RemoveChild_ClearsFocusInsideRemovedSubtree()
    {
        var root = new BoxElement();
        var parent = new BoxElement();
        var input = new TextInputElement { Name = "input" };
        parent.AddChild(input);
        root.AddChild(parent);

        var fm = new FocusManager();
        root.FocusManager = fm;
        parent.FocusManager = fm;
        input.FocusManager = fm;

        fm.SetFocus(input);
        root.RemoveChild(parent);

        Assert.Null(fm.FocusedElement);
    }

    [Fact]
    public void ReplaceChildren_PreservesFocusForRetainedChild()
    {
        var root = new BoxElement();
        var focused = new TextInputElement { Name = "focused" };
        var removed = new BoxElement();
        root.ReplaceChildren(removed, focused);

        var fm = new FocusManager();
        root.FocusManager = fm;
        focused.FocusManager = fm;
        removed.FocusManager = fm;

        fm.SetFocus(focused);
        root.ReplaceChildren(focused);

        Assert.Same(focused, fm.FocusedElement);
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

    [Fact]
    public void TabIndex_PositiveValuesVisitedFirst_InAscendingOrder()
    {
        // Document order: a, b, c, d — but TabIndex overrides it.
        var root = new BoxElement();
        var a = new BoxElement { IsFocusable = true, Name = "a", TabIndex = 0 };
        var b = new BoxElement { IsFocusable = true, Name = "b", TabIndex = 2 };
        var c = new BoxElement { IsFocusable = true, Name = "c", TabIndex = 1 };
        var d = new BoxElement { IsFocusable = true, Name = "d", TabIndex = 0 };
        root.AddChild(a);
        root.AddChild(b);
        root.AddChild(c);
        root.AddChild(d);

        var fm = new FocusManager();
        fm.MoveFocusNext(root); Assert.Equal("c", fm.FocusedElement?.Name); // TabIndex=1 first
        fm.MoveFocusNext(root); Assert.Equal("b", fm.FocusedElement?.Name); // TabIndex=2 next
        fm.MoveFocusNext(root); Assert.Equal("a", fm.FocusedElement?.Name); // TabIndex=0, doc order
        fm.MoveFocusNext(root); Assert.Equal("d", fm.FocusedElement?.Name); // TabIndex=0, doc order
        fm.MoveFocusNext(root); Assert.Equal("c", fm.FocusedElement?.Name); // wraps to top of sequence
    }

    [Fact]
    public void TabIndex_EqualPositiveValues_PreserveDocumentOrder()
    {
        var root = new BoxElement();
        var a = new BoxElement { IsFocusable = true, Name = "a", TabIndex = 1 };
        var b = new BoxElement { IsFocusable = true, Name = "b", TabIndex = 1 };
        var c = new BoxElement { IsFocusable = true, Name = "c", TabIndex = 1 };
        root.AddChild(a);
        root.AddChild(b);
        root.AddChild(c);

        var fm = new FocusManager();
        fm.MoveFocusNext(root); Assert.Equal("a", fm.FocusedElement?.Name);
        fm.MoveFocusNext(root); Assert.Equal("b", fm.FocusedElement?.Name);
        fm.MoveFocusNext(root); Assert.Equal("c", fm.FocusedElement?.Name);
    }

    /// <summary>Positive values anywhere in the tree, nested at different depths, ties among
    /// them, zeros before and after, and a negative: Tab visits the positives in ascending
    /// order (ties in document order), then the zeros in document order, and Shift+Tab walks the
    /// same sequence backwards. Twice round, because the list the traversal collects into is
    /// kept between calls.</summary>
    [Fact]
    public void TabIndex_MixedValuesInNestedContainers_KeepTheHtmlOrder()
    {
        BoxElement F(string name, int tabIndex = 0) => new() { IsFocusable = true, Name = name, TabIndex = tabIndex };
        var root = new BoxElement();
        var outer = new BoxElement();
        var inner = new BoxElement();
        root.AddChild(F("z1"));
        root.AddChild(outer);
        outer.AddChild(F("p3a", 3));
        outer.AddChild(inner);
        inner.AddChild(F("z2"));
        inner.AddChild(F("p1", 1));
        inner.AddChild(F("neg", -1));
        outer.AddChild(F("p3b", 3));
        root.AddChild(F("p2", 2));
        root.AddChild(F("z3"));

        string[] expected = ["p1", "p2", "p3a", "p3b", "z1", "z2", "z3"];
        var fm = new FocusManager();
        var forward = new List<string?>();
        for (int i = 0; i < expected.Length * 2; i++)
        {
            fm.MoveFocusNext(root);
            forward.Add(fm.FocusedElement?.Name);
        }
        Assert.Equal([.. expected, .. expected], forward);

        var backward = new List<string?>();
        for (int i = 0; i < expected.Length * 2; i++)
        {
            fm.MoveFocusPrevious(root);
            backward.Add(fm.FocusedElement?.Name);
        }
        string[] reversed = [.. Enumerable.Reverse(expected)];
        // From z3, Shift+Tab steps back through the sequence and wraps from p1 to z3.
        Assert.Equal([.. reversed.Skip(1), reversed[0], .. reversed.Skip(1), reversed[0]], backward);
    }

    /// <summary>A form of 300 fields in rows, none with a positive TabIndex: finding the next
    /// and previous field allocates nothing. It walked the tree with a boxed enumerator per
    /// container, grew a fresh list, and sorted it with OrderBy + ToList although every key was
    /// equal: 27.7 KB per Tab on the perf suite's input/tab row (plan 17, runtime-frame-floor).</summary>
    [Fact]
    public void FindingTheNextField_AllocatesNothing_WhenNoTabIndexIsPositive()
    {
        var root = new BoxElement();
        var fields = new BoxElement[300];
        for (int i = 0; i < fields.Length; i++)
        {
            var row = new BoxElement();
            row.AddChild(new TextElement());
            row.AddChild(fields[i] = new BoxElement { IsFocusable = true });
            root.AddChild(row);
        }
        FocusTraversal.FindNext(root, fields[5]);
        FocusTraversal.FindPrevious(root, fields[5]);

        var next = new Element?[20];
        var previous = new Element?[20];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++)
        {
            next[i] = FocusTraversal.FindNext(root, fields[i]);
            previous[i] = FocusTraversal.FindPrevious(root, fields[i + 1]);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        for (int i = 0; i < 20; i++)
        {
            Assert.Same(fields[i + 1], next[i]);
            Assert.Same(fields[i], previous[i]);
        }
        Assert.Equal(0, allocated);
    }

    /// <summary>The traversal keeps its list between calls; it must not keep the elements. A
    /// tree that has been Tabbed through and then dropped is collectable.</summary>
    [Fact]
    public void FindingTheNextField_KeepsNoElementAlive()
    {
        var dropped = TabThroughATreeAndDropIt();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(dropped.TryGetTarget(out _), "a tree Tabbed through is still reachable");

        // Not inlined, so no local of this frame keeps the tree alive.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static WeakReference<BoxElement> TabThroughATreeAndDropIt()
        {
            var root = new BoxElement();
            var a = new BoxElement { IsFocusable = true };
            root.AddChild(a);
            root.AddChild(new BoxElement { IsFocusable = true });
            FocusTraversal.FindNext(root, a);
            FocusTraversal.FindPrevious(root, a);
            return new WeakReference<BoxElement>(a);
        }
    }

    [Fact]
    public void TabIndex_Negative_SkippedByTabButStillProgrammaticallyFocusable()
    {
        var root = new BoxElement();
        var a = new BoxElement { IsFocusable = true, Name = "a" };
        var skip = new BoxElement { IsFocusable = true, Name = "skip", TabIndex = -1 };
        var b = new BoxElement { IsFocusable = true, Name = "b" };
        root.AddChild(a);
        root.AddChild(skip);
        root.AddChild(b);

        var fm = new FocusManager();
        fm.MoveFocusNext(root); Assert.Equal("a", fm.FocusedElement?.Name);
        fm.MoveFocusNext(root); Assert.Equal("b", fm.FocusedElement?.Name); // skip is skipped
        fm.MoveFocusNext(root); Assert.Equal("a", fm.FocusedElement?.Name); // wraps

        // But SetFocus still works — programmatic focus bypasses TabIndex.
        fm.SetFocus(skip);
        Assert.Equal("skip", fm.FocusedElement?.Name);

        // And Tab from a TabIndex<0 element still advances to the next Tab-eligible one.
        fm.MoveFocusNext(root);
        Assert.Equal("a", fm.FocusedElement?.Name);
    }

    [Fact]
    public void TabIndex_DefaultIsZero()
    {
        Assert.Equal(0, new BoxElement().TabIndex);
        Assert.Equal(0, new ButtonElement().TabIndex);
    }

    [Fact]
    public void TheFocusEvent_SaysHowFocusArrived()
    {
        var root = CreateTree();
        var a = root.Children[0];
        var d = root.Children[2];
        var reasons = new List<(string?, FocusReason)>();
        foreach (var el in new[] { a, root.Children[1].Children[0], d })
            el.On<FocusEvent>(e => reasons.Add((e.CurrentTarget?.Name, e.Reason)));

        var fm = new FocusManager();
        fm.SetFocus(a);                     // code: Element.Focus, a host
        fm.MoveFocusNext(root);             // Tab
        fm.MoveFocusPrevious(root);         // Shift+Tab
        fm.SetFocus(d, FocusReason.Pointer);

        Assert.Equal(
            [("a", FocusReason.Programmatic), ("c", FocusReason.Next), ("a", FocusReason.Previous), ("d", FocusReason.Pointer)],
            reasons);
    }

    [Fact]
    public void IsFocused_IsTrue_ForAnElementWithNoFocusStyle()
    {
        // It lived in the interaction state, which only a focus style creates, so an
        // unstyled element read false while it held focus.
        var root = CreateTree();
        var a = root.Children[0];
        var d = root.Children[2];
        var fm = new FocusManager();

        fm.SetFocus(a);
        Assert.True(a.IsFocused);

        fm.SetFocus(d);
        Assert.False(a.IsFocused);
        Assert.True(d.IsFocused);

        fm.ClearFocus();
        Assert.False(d.IsFocused);
    }
}
