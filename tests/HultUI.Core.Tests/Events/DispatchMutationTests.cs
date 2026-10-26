using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Testing;
using Xunit;

namespace HultUI.Core.Tests.Events;

/// <summary>
/// Dispatch used to snapshot the handler list on every event, which made it safe for a
/// handler to change the handlers underneath it. The snapshot is gone — it was the
/// highest-frequency allocation in the framework, running continuously through any drag —
/// and copy-on-write replaces it. These cover what the snapshot used to guarantee.
/// </summary>
public class DispatchMutationTests
{
    private static PointerDownEvent Click() => new();

    [Fact]
    public void AHandlerAddedDuringDispatch_DoesNotRunInThatDispatch()
    {
        var box = Box.New();
        int added = 0;
        box.On<PointerDownEvent>(_ => box.On<PointerDownEvent>(_ => added++));

        box.InvokeHandlers(Click(), EventPhase.Bubble);

        Assert.Equal(0, added);

        // …but it is registered for the next one.
        box.InvokeHandlers(Click(), EventPhase.Bubble);
        Assert.Equal(1, added);
    }

    [Fact]
    public void AHandlerRemovedDuringDispatch_StillRuns()
    {
        // The reconciler's shape: a handler marks dirty, the rebuild patches this element,
        // and CopyAppHandlersFrom drops every app handler. The dispatch in flight must
        // still run the ones that were attached when it started.
        var live = Box.New();
        var order = new List<string>();

        live.On<PointerDownEvent>(_ =>
        {
            order.Add("first");
            live.PatchFrom(Box.New());   // wipes app handlers
        });
        live.On<PointerDownEvent>(_ => order.Add("second"));

        live.InvokeHandlers(Click(), EventPhase.Bubble);

        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public void HandlersFromTheRebuild_ApplyToTheNextDispatch()
    {
        var live = Box.New();
        var order = new List<string>();

        var incoming = Box.New();
        incoming.On<PointerDownEvent>(_ => order.Add("rebuilt"));

        live.On<PointerDownEvent>(_ => { order.Add("original"); live.PatchFrom(incoming); });

        live.InvokeHandlers(Click(), EventPhase.Bubble);
        Assert.Equal(["original"], order);

        live.InvokeHandlers(Click(), EventPhase.Bubble);
        Assert.Equal(["original", "rebuilt"], order);
    }

    [Fact]
    public void NestedDispatchOnTheSameElement_DoesNotDisturbTheOuterOne()
    {
        // A click handler that moves focus fires blur synchronously, so an element can be
        // walking two handler lists at once.
        var box = Box.New();
        var order = new List<string>();

        box.On<PointerDownEvent>(_ =>
        {
            order.Add("outer-1");
            box.InvokeHandlers(new BlurEvent(), EventPhase.Bubble);
        });
        box.On<PointerDownEvent>(_ => order.Add("outer-2"));
        box.On<BlurEvent>(_ => order.Add("inner"));

        box.InvokeHandlers(Click(), EventPhase.Bubble);

        Assert.Equal(["outer-1", "inner", "outer-2"], order);
    }

    [Fact]
    public void StopPropagation_StillEndsTheWalk()
    {
        var box = Box.New();
        int ran = 0;
        box.On<PointerDownEvent>(e => { ran++; e.StopPropagation = true; });
        box.On<PointerDownEvent>(_ => ran++);

        box.InvokeHandlers(Click(), EventPhase.Bubble);

        Assert.Equal(1, ran);
    }

    [Fact]
    public void OwnedHandlersSurviveAPatch_AppHandlersDoNot()
    {
        // The distinction CopyAppHandlersFrom exists for, re-checked through the
        // copy-on-write path.
        var live = Box.New();
        int owned = 0, app = 0;
        live.OnOwn<PointerDownEvent>(_ => owned++);
        live.On<PointerDownEvent>(_ => app++);

        live.PatchFrom(Box.New());
        live.InvokeHandlers(Click(), EventPhase.Bubble);

        Assert.Equal(1, owned);
        Assert.Equal(0, app);
    }

    [Fact]
    public void APatch_KeepsOwnedHandlersFirst_ThenTheBuildsInItsOrder()
    {
        var live = Box.New();
        var order = new List<string>();
        live.On<PointerDownEvent>(_ => order.Add("old app"));
        live.OnOwn<PointerDownEvent>(_ => order.Add("owned"));
        live.On<BlurEvent>(_ => order.Add("old blur"));

        var incoming = Box.New();
        incoming.On<PointerDownEvent>(_ => order.Add("new 1"));
        incoming.OnOwn<PointerDownEvent>(_ => order.Add("incoming owned"));
        incoming.On<PointerDownEvent>(_ => order.Add("new 2"));
        live.PatchFrom(incoming);
        live.InvokeHandlers(Click(), EventPhase.Bubble);

        Assert.Equal(["owned", "new 1", "new 2"], order);
        // A kind of event the build no longer listens for is let go.
        Assert.False(live.HasEventHandler<BlurEvent>());
        Assert.True(live.HasEventHandler<PointerDownEvent>());
    }

    /// <summary>
    /// A rebuild patches every element it keeps with handlers of the same kinds as before, every
    /// key. The live element's lists take the new handlers where they are: making them again cost
    /// 208 B a patched element with one click handler (Vesper's Detail view, 13 KB a key).
    /// </summary>
    [Fact]
    public void APatchWithTheSameKindsOfHandler_AllocatesNothing()
    {
        const int Count = 64;
        var live = new BoxElement[Count];
        var twins = new BoxElement[Count];
        for (int i = 0; i < Count; i++)
        {
            live[i] = Box.New();
            live[i].OnOwn<KeyDownEvent>(_ => { });
            live[i].On<ClickEvent>(_ => { });
            twins[i] = Box.New();
            twins[i].On<ClickEvent>(_ => { });
            twins[i].On<PointerEnterEvent>(_ => { });
            live[i].PatchFrom(twins[i]);
        }

        long bytes = Allocations.Least(() =>
        {
            for (int i = 0; i < Count; i++)
                live[i].PatchFrom(twins[i]);
        });

        Assert.Equal(0, bytes);
        Assert.True(live[0].HasEventHandler<PointerEnterEvent>());
    }

    [Fact]
    public void HandlersOfDifferentKinds_RunOnlyForTheirOwnEvent_InTheOrderTheyCame()
    {
        // An element keeps its handlers of every kind together, so registering them in turn
        // interleaves them; each event runs exactly its own type's, in order.
        var box = Box.New();
        var order = new List<string>();
        box.On<PointerDownEvent>(_ => order.Add("down 1"));
        box.On<BlurEvent>(_ => order.Add("blur"));
        box.OnOwn<PointerDownEvent>(_ => order.Add("down 2"));
        box.On<PointerUpEvent>(_ => order.Add("up"));
        box.On<PointerDownEvent>(_ => order.Add("down 3"));
        // A handler is filed under the type it names, not the types above it.
        box.On<PointerEvent>(_ => order.Add("any pointer"));

        box.InvokeHandlers(Click(), EventPhase.Bubble);
        Assert.Equal(["down 1", "down 2", "down 3"], order);

        order.Clear();
        box.InvokeHandlers(new BlurEvent(), EventPhase.Bubble);
        Assert.Equal(["blur"], order);
        Assert.False(box.HasEventHandler<ClickEvent>());
        Assert.True(box.HasEventHandler<PointerUpEvent>());
    }

    [Fact]
    public void APatchToFewerHandlers_LeavesNoneOfTheOldOnesBehind()
    {
        var live = Box.New();
        var order = new List<string>();
        live.On<PointerDownEvent>(_ => order.Add("old 1"));
        live.On<PointerUpEvent>(_ => order.Add("old up"));
        live.On<PointerDownEvent>(_ => order.Add("old 2"));

        var incoming = Box.New();
        incoming.On<PointerDownEvent>(_ => order.Add("new"));
        live.PatchFrom(incoming);

        live.InvokeHandlers(Click(), EventPhase.Bubble);
        live.InvokeHandlers(new PointerUpEvent(), EventPhase.Bubble);
        Assert.Equal(["new"], order);
        Assert.False(live.HasEventHandler<PointerUpEvent>());

        // And back to more than it had room for.
        var more = Box.New();
        for (int i = 0; i < 5; i++)
        {
            int n = i;
            more.On<PointerDownEvent>(_ => order.Add($"more {n}"));
        }
        live.PatchFrom(more);
        order.Clear();
        live.InvokeHandlers(Click(), EventPhase.Bubble);
        Assert.Equal(["more 0", "more 1", "more 2", "more 3", "more 4"], order);
    }

    [Fact]
    public void AHandlerOfAnotherKindAddedDuringDispatch_LeavesTheWalkAlone()
    {
        // Room for it in the element's storage or not, a walk in progress keeps what it had.
        foreach (int before in new[] { 1, 2, 3 })
        {
            var box = Box.New();
            var order = new List<string>();
            int blurs = 0;
            box.On<PointerDownEvent>(_ =>
            {
                order.Add("first");
                box.On<PointerDownEvent>(_ => order.Add("added"));
                box.On<BlurEvent>(_ => blurs++);
            });
            for (int i = 1; i < before; i++)
            {
                int n = i;
                box.On<PointerDownEvent>(_ => order.Add($"then {n}"));
            }

            box.InvokeHandlers(Click(), EventPhase.Bubble);

            Assert.Equal(["first", .. Enumerable.Range(1, before - 1).Select(n => $"then {n}")], order);
            box.InvokeHandlers(new BlurEvent(), EventPhase.Bubble);
            Assert.Equal(1, blurs);
        }
    }

    /// <summary>
    /// A build makes all its elements' handlers again on every key. An element's first two
    /// handlers, of any kinds, take one small array of entries that hold the handlers as typed:
    /// with a dictionary of lists and a closure around each handler they cost 424 B and then
    /// 208 B (18–28 KB a key in Junction, Vesper and Meridian).
    /// </summary>
    [Fact]
    public void AnElementsFirstTwoHandlers_TakeOneSmallArray()
    {
        Action<ClickEvent> click = _ => { };
        Action<PointerEnterEvent> enter = _ => { };
        const int Count = 64;
        var boxes = new BoxElement[Count * Allocations.MaxRounds];
        for (int i = 0; i < boxes.Length; i++)
            boxes[i] = Box.New();
        int next = 0;

        double perElement = Allocations.PerCall(Count, () =>
        {
            var box = boxes[next++];
            box.On(click);
            box.On(enter);
        }, goal: 80);

        Assert.True(perElement <= 80, $"two handlers allocated {perElement} B an element");
    }
}
