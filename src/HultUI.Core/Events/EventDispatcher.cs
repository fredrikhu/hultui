using HultUI.Core.Elements;
using HultUI.Core.Reactivity;

namespace HultUI.Core.Events;

public static class EventDispatcher
{
    /// <summary>
    /// Dispatches an event through the capture → target → bubble phases.
    /// </summary>
    /// <remarks>
    /// The whole dispatch runs inside one batch (when <see cref="ReactiveRuntime.AutoBatch"/>
    /// is enabled), so multiple signal writes across the handler chain coalesce
    /// into a single rebuild that flushes when the event finishes. A handler
    /// that must observe its own rebuild mid-dispatch can call
    /// <see cref="ReactiveRuntime.FlushNow"/> or wrap the writes in
    /// <see cref="ReactiveRuntime.SuspendBatching"/>.
    /// </remarks>
    public static void Dispatch(ElementEvent evt, Element target)
    {
        bool batch = ReactiveRuntime.AutoBatch;
        if (batch) ReactiveRuntime.BeginBatch();
        try
        {
            evt.Target = target;

            // Build path from root to target
            var path = BuildPath(target);

            // Phase 1: Capture (root → target, excluding target)
            evt.Phase = EventPhase.Capture;
            for (int i = 0; i < path.Count - 1; i++)
            {
                evt.CurrentTarget = path[i];
                path[i].InvokeHandlers(evt, EventPhase.Capture);
                if (evt.StopPropagation) return;
            }

            // Phase 2: Target
            evt.Phase = EventPhase.Target;
            evt.CurrentTarget = target;
            target.InvokeHandlers(evt, EventPhase.Target);
            if (evt.StopPropagation) return;

            // Phase 3: Bubble (target parent → root)
            evt.Phase = EventPhase.Bubble;
            for (int i = path.Count - 2; i >= 0; i--)
            {
                evt.CurrentTarget = path[i];
                path[i].InvokeHandlers(evt, EventPhase.Bubble);
                if (evt.StopPropagation) return;
            }
        }
        finally
        {
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

    private static List<Element> BuildPath(Element target)
    {
        var path = new List<Element>();
        var current = target;
        while (current is not null)
        {
            path.Add(current);
            current = current.Parent;
        }
        path.Reverse(); // root first
        return path;
    }
}
