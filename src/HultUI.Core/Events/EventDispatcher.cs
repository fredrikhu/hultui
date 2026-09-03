using System.Buffers;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;

namespace HultUI.Core.Events;

public static class EventDispatcher
{
    /// <summary>
    /// Monotonic count of user event-handler invocations, bumped by
    /// <see cref="Element.RunHandlers"/> each time a handler actually runs.
    /// The input pipeline compares it across a dispatch to know whether any
    /// handler executed (a handler may mutate visuals without marking dirty,
    /// so the window renders a frame whenever one ran). UI-thread only.
    /// </summary>
    internal static long HandlerInvocationCount;

    /// <summary>
    /// Dispatches an event through the capture → target → bubble phases.
    /// </summary>
    /// <remarks>
    /// <para>The whole dispatch runs inside one batch (when <see cref="ReactiveRuntime.AutoBatch"/>
    /// is enabled), so multiple signal writes across the handler chain coalesce
    /// into a single rebuild that flushes when the event finishes. A handler
    /// that must observe its own rebuild mid-dispatch can call
    /// <see cref="ReactiveRuntime.FlushNow"/> or wrap the writes in
    /// <see cref="ReactiveRuntime.SuspendBatching"/>.</para>
    ///
    /// <para>An event runs the handlers that were live when it was dispatched. The path is
    /// elements, and reconciliation keeps elements while changing what they <i>are</i>: a
    /// handler that marks dirty can have the tree patched underneath the rest of its own
    /// walk, and an ancestor still to be bubbled through then carries the new build's
    /// handlers. That is how one click both created a row and deleted it — the button ran
    /// "add", the rebuild patched its parent into the new row's remove button, and the same
    /// click bubbled into "remove". So each element on the path is pinned up front (see
    /// <see cref="Element.PinHandlers"/>): the walk sees the handler lists as they were, and
    /// a patch mid-dispatch installs the new ones for the <i>next</i> event. This already
    /// held for the element in flight; pinning the path is the same rule applied to the
    /// ancestors, and it means a handler attached to an ancestor mid-dispatch does not run
    /// for the event that attached it.</para>
    /// </remarks>
    public static void Dispatch(ElementEvent evt, Element target)
    {
        bool batch = ReactiveRuntime.AutoBatch;
        if (batch) ReactiveRuntime.BeginBatch();

        // Both arrays are pooled rather than allocated: pointer moves and drags dispatch
        // continuously, and this runs once per event. Rent can hand back longer arrays;
        // only [0, depth) is ever touched, and that span is cleared before returning them
        // so the pool holds no element or handler list alive.
        int depth = 0;
        for (var e = target; e is not null; e = e.Parent) depth++;

        var path = ArrayPool<Element>.Shared.Rent(depth);
        var pinned = ArrayPool<List<EventHandlerEntry>?>.Shared.Rent(depth);
        // Root first, filled back to front — the walk only goes up.
        {
            int i = depth - 1;
            for (var e = target; e is not null; e = e.Parent) path[i--] = e;
        }
        var type = evt.GetType();
        for (int i = 0; i < depth; i++)
            pinned[i] = path[i].PinHandlers(type);

        try
        {
            evt.Target = target;

            // Phase 1: Capture (root → target, excluding target)
            evt.Phase = EventPhase.Capture;
            for (int i = 0; i < depth - 1; i++)
            {
                evt.CurrentTarget = path[i];
                path[i].InvokeHandlers(evt, EventPhase.Capture, pinned[i]);
                if (evt.StopPropagation) return;
            }

            // Phase 2: Target
            evt.Phase = EventPhase.Target;
            evt.CurrentTarget = target;
            target.InvokeHandlers(evt, EventPhase.Target, pinned[depth - 1]);
            if (evt.StopPropagation) return;

            // Phase 3: Bubble (target parent → root)
            evt.Phase = EventPhase.Bubble;
            for (int i = depth - 2; i >= 0; i--)
            {
                evt.CurrentTarget = path[i];
                path[i].InvokeHandlers(evt, EventPhase.Bubble, pinned[i]);
                if (evt.StopPropagation) return;
            }
        }
        finally
        {
            for (int i = 0; i < depth; i++)
                path[i].UnpinHandlers();
            Array.Clear(path, 0, depth);
            Array.Clear(pinned, 0, depth);
            ArrayPool<Element>.Shared.Return(path);
            ArrayPool<List<EventHandlerEntry>?>.Shared.Return(pinned);
            if (batch) ReactiveRuntime.EndBatch();
        }
    }

    /// <summary>
    /// Dispatches a non-bubbling event directly to the target only.
    /// Used for Focus, Blur, PointerEnter, PointerLeave.
    /// </summary>
    public static void DispatchDirect(ElementEvent evt, Element target)
    {
        bool batch = ReactiveRuntime.AutoBatch;
        if (batch) ReactiveRuntime.BeginBatch();
        try
        {
            evt.Target = target;
            evt.CurrentTarget = target;
            evt.Phase = EventPhase.Target;
            target.InvokeHandlers(evt, EventPhase.Target);
        }
        finally
        {
            if (batch) ReactiveRuntime.EndBatch();
        }
    }

}
