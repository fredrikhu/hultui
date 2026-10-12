using System.Buffers;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
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
        var path = PinPath(target, evt.GetType(), out var pinned, out int depth, out _);
        Walk(evt, target, path, pinned, depth);
    }

    /// <summary>
    /// Dispatches a <see cref="PointerMoveEvent"/> to <paramref name="target"/> as
    /// <see cref="Dispatch"/> does, making the event only when an element on the path listens
    /// for one. Most moves reach no move handler at all — hover styles, cursors and tooltips
    /// need no event — and the event was 64 bytes a move, the bulk of what a 1,000 Hz mouse
    /// left behind. Nothing else differs: the path is pinned first, so whether anything listens
    /// is asked of the same handler lists the walk would run. A handler registered with a
    /// listening condition (<see cref="Element.OnPassive{TEvent}(Action{TEvent}, Func{bool})"/>)
    /// counts only while it holds; if a move is made, every handler on the path runs as ever.
    /// </summary>
    internal static void DispatchPointerMove(Element target, Point position, Point delta, KeyModifiers modifiers)
    {
        var path = PinPath(target, typeof(PointerMoveEvent), out var pinned, out int depth, out bool listened);
        if (!listened || !AnyListening(pinned, depth))
        {
            Release(path, pinned, depth);
            return;
        }
        Walk(new PointerMoveEvent { Position = position, Delta = delta, Modifiers = modifiers }, target, path, pinned, depth);
    }

    /// <summary>Whether a pinned handler wants the event now: one with no listening condition,
    /// or one whose condition holds.</summary>
    private static bool AnyListening(List<EventHandlerEntry>?[] pinned, int depth)
    {
        for (int i = 0; i < depth; i++)
        {
            if (pinned[i] is not { } list) continue;
            for (int j = 0; j < list.Count; j++)
                if (list[j].Handler.Target is not IListeningHandler handler || handler.Listening) return true;
        }
        return false;
    }

    /// <summary>
    /// The path from the root down to <paramref name="target"/>, each element pinned (see
    /// <see cref="Element.PinHandlers"/>) with its handler list for <paramref name="type"/> in
    /// <paramref name="pinned"/>; <paramref name="listened"/> says whether any of them has one.
    /// Always handed on to <see cref="Walk"/> or <see cref="Release"/>.
    /// </summary>
    private static Element[] PinPath(Element target, Type type, out List<EventHandlerEntry>?[] pinned, out int depth,
        out bool listened)
    {
        // Both arrays are pooled rather than allocated: pointer moves and drags dispatch
        // continuously, and this runs once per event. Rent can hand back longer arrays;
        // only [0, depth) is ever touched, and that span is cleared before returning them
        // so the pool holds no element or handler list alive.
        depth = 0;
        for (var e = target; e is not null; e = e.Parent) depth++;

        var path = ArrayPool<Element>.Shared.Rent(depth);
        pinned = ArrayPool<List<EventHandlerEntry>?>.Shared.Rent(depth);
        // Root first, filled back to front — the walk only goes up.
        {
            int i = depth - 1;
            for (var e = target; e is not null; e = e.Parent) path[i--] = e;
        }
        listened = false;
        for (int i = 0; i < depth; i++)
            listened |= (pinned[i] = path[i].PinHandlers(type)) is not null;
        return path;
    }

    /// <summary>Unpins a path from <see cref="PinPath"/> and gives its arrays back.</summary>
    private static void Release(Element[] path, List<EventHandlerEntry>?[] pinned, int depth)
    {
        for (int i = 0; i < depth; i++)
            path[i].UnpinHandlers();
        Array.Clear(path, 0, depth);
        Array.Clear(pinned, 0, depth);
        ArrayPool<Element>.Shared.Return(path);
        ArrayPool<List<EventHandlerEntry>?>.Shared.Return(pinned);
    }

    /// <summary>The capture, target and bubble phases over a pinned path, which it releases.</summary>
    private static void Walk(ElementEvent evt, Element target, Element[] path, List<EventHandlerEntry>?[] pinned, int depth)
    {
        bool batch = ReactiveRuntime.AutoBatch;
        if (batch) ReactiveRuntime.BeginBatch();

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
            Release(path, pinned, depth);
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
