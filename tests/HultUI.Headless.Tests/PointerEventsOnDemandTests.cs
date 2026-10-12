using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Testing;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A pointer move, enter or leave event is made only for elements that listen for it (plan 17
/// round 3, runtime-pointer-events-on-demand). Hover styles, cursors and tooltips need no event,
/// and most moves reach no move handler at all, yet every move made a 64-byte move event and every
/// hover change a 56-byte enter and leave besides. What a listener sees is unchanged: every move,
/// in every phase, at the pointer; enter and leave on each listener of the chain, one event shared
/// up it; a handler added on the way up heard exactly when it was before.
/// </summary>
public class PointerEventsOnDemandTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
    private static readonly Color Rest = new(240, 240, 240);
    private static readonly Color Lit = new(200, 210, 255);
    private static object? _sink;

    /// <summary>Ten rows of 30 px with a hover style, each holding a label, in a column at the
    /// top of a 400 × 400 window: row i spans y 30i to 30i + 30, its label from x 0.</summary>
    private static (HeadlessSession Session, BoxElement Root, BoxElement List, BoxElement[] Rows) Screen()
    {
        var rows = new BoxElement[10];
        for (int i = 0; i < rows.Length; i++)
            rows[i] = Flex.Row().Height(Dimension.Px(30)).FlexShrink(0).Background(Rest)
                .OnHover(b => b.BackgroundColor = Lit)
                .Children(TextElement.New($"Row {i}"));
        var list = Flex.Column(gap: 0).FlexShrink(0).Children(rows);
        var root = Flex.Column(gap: 0).FlexGrow(1).Children(list);
        var session = HeadlessSession.Create(400, 400);
        session.Window.SetRoot(root);
        for (int i = 0; i < 3; i++) session.Application.RunFrame(Frame);
        return (session, root, list, rows);
    }

    /// <summary>A point on row <paramref name="row"/>'s label.</summary>
    private static Point In(int row) => new(10, 30 * row + 15);

    [Fact]
    public void HoverChangesOverHoverStyles_WithNothingListening_AllocateOnlyThePlatformsInput()
    {
        var (session, _, _, rows) = Screen();
        using var _ = session;
        var window = session.Window;
        var app = session.Application;
        for (int i = 0; i < 10; i++) { var p = In(2 + 3 * (i % 2)); window.MoveMouse(p.X, p.Y); app.RunFrame(Frame); }

        long input = Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++)
                _sink = new InputEvent.MouseMoveInput(200, 75, 1, 0, KeyModifiers.None);
        });
        int k = 0;
        long moves = Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                var p = In(2 + 3 * (k++ % 2));
                window.MoveMouse(p.X, p.Y);
                app.RunFrame(Frame);
            }
        }, goal: input);

        Assert.True(moves <= input,
            $"100 hover changes allocated {moves} B; the platform's input alone is {input} B (an enter, a leave and a move event for each are {100 * (56 + 56 + 64)} B)");
        // The hover style still follows the pointer.
        int last = 2 + 3 * ((k - 1) % 2);
        Assert.Equal(Lit, rows[last].BackgroundColor);
        Assert.Equal(Rest, rows[last == 5 ? 2 : 5].BackgroundColor);
    }

    [Fact]
    public void AMoveHandlerOnAnAncestor_HearsEveryMove_InEachPhase()
    {
        var (session, root, list, rows) = Screen();
        using var _ = session;
        var heard = new List<string>();
        root.On<PointerMoveEvent>(EventPhase.Capture, e => heard.Add($"capture {e.Position.Y} {Name(e.Target)} {e.Phase}"));
        list.On<PointerMoveEvent>(e => heard.Add($"bubble {e.Position.Y} {e.Delta.Y} {Name(e.Target)} {Name(e.CurrentTarget)} {e.Phase}"));

        foreach (int row in new[] { 1, 1, 4 })
        {
            var p = In(row);
            session.Window.MoveMouse(p.X, p.Y + row % 2);
        }

        Assert.Equal(
        [
            "capture 46 label1 Capture", "bubble 46 46 label1 list Bubble",
            "capture 46 label1 Capture", "bubble 46 0 label1 list Bubble",
            "capture 135 label4 Capture", "bubble 135 89 label4 list Bubble",
        ], heard);

        string Name(Element e) => e is TextElement t ? "label" + t.Content[^1] : ReferenceEquals(e, list) ? "list" : e.GetType().Name;
    }

    [Fact]
    public void EnterAndLeave_ReachEachListenerOfTheChain_SharingOneEvent()
    {
        var (session, root, list, rows) = Screen();
        using var _ = session;
        var heard = new List<(string What, PointerEvent Event)>();
        // The labels and the root do not listen; the rows and the list do.
        foreach (var (row, i) in rows.Select((r, i) => (r, i)))
        {
            row.OnPointerEnter(e => heard.Add(($"enter row{i}", e)));
            row.OnPointerLeave(e => heard.Add(($"leave row{i}", e)));
        }
        list.OnPointerEnter(e => heard.Add(("enter list", e)));
        list.OnPointerLeave(e => heard.Add(("leave list", e)));

        var p = In(2);
        session.Window.MoveMouse(p.X, p.Y, KeyModifiers.Shift);         // from nothing: the whole chain
        Assert.Equal(["enter row2", "enter list"], heard.Select(h => h.What));
        Assert.Same(heard[0].Event, heard[1].Event);
        Assert.Equal(p, heard[0].Event.Position);
        Assert.Equal(KeyModifiers.Shift, heard[0].Event.Modifiers);

        heard.Clear();
        p = In(5);
        session.Window.MoveMouse(p.X, p.Y);                             // a cousin: up to the list, not past it
        Assert.Equal(["leave row2", "enter row5"], heard.Select(h => h.What));
        Assert.Equal(p, heard[1].Event.Position);

        heard.Clear();
        session.Window.MoveMouse(200, 390);                             // below the rows: onto the root
        Assert.Equal(["leave row5", "leave list"], heard.Select(h => h.What));
        Assert.Same(heard[0].Event, heard[1].Event);
    }

    [Fact]
    public void AHandlerAddedOnTheWayUp_IsHeardWhenItWasBefore()
    {
        var (session, root, list, rows) = Screen();
        using var _ = session;
        var heard = new List<string>();

        // Leave and enter are looked up as the walk reaches each element: a leave handler that
        // gives its parent one (the parent had none) is followed by that one, on the same event.
        PointerLeaveEvent? first = null;
        rows[2].OnPointerLeave(e =>
        {
            first = e;
            heard.Add("leave row2");
            list.OnPointerLeave(e2 => heard.Add(ReferenceEquals(e2, first) ? "leave list, same event" : "leave list, another event"));
        });
        var p = In(2);
        session.Window.MoveMouse(p.X, p.Y);
        session.Window.MoveMouse(200, 390);
        Assert.Equal(["leave row2", "leave list, same event"], heard);

        // A move pins its path first: a handler a move handler adds to an ancestor that had none
        // is heard from the next move on, not by the move that added it.
        heard.Clear();
        bool added = false;
        rows[4].On<PointerMoveEvent>(_ =>
        {
            heard.Add("move row4");
            if (added) return;
            added = true;
            root.On<PointerMoveEvent>(_ => heard.Add("move root"));
        });
        p = In(4);
        session.Window.MoveMouse(p.X, p.Y);
        session.Window.MoveMouse(p.X + 1, p.Y);
        Assert.Equal(["move row4", "move row4", "move root"], heard);
    }
}
