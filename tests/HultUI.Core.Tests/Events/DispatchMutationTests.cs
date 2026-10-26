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
}
