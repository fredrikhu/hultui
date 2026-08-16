using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
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
}
