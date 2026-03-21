using HultUI.Core.Elements;

namespace HultUI.Core.Events;

public static class EventDispatcher
{
    /// <summary>
    /// Dispatches an event through the capture → target → bubble phases.
    /// </summary>
    public static void Dispatch(ElementEvent evt, Element target)
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

    /// <summary>
    /// Dispatches a non-bubbling event directly to the target only.
    /// Used for Focus, Blur, PointerEnter, PointerLeave.
    /// </summary>
    public static void DispatchDirect(ElementEvent evt, Element target)
    {
        evt.Target = target;
        evt.CurrentTarget = target;
        evt.Phase = EventPhase.Target;
        target.InvokeHandlers(evt, EventPhase.Target);
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
