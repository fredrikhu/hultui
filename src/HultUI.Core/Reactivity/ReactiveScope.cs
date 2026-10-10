namespace HultUI.Core.Reactivity;

/// <summary>
/// Thread-local dependency tracker. When a reactive source (Signal/Computed) is read
/// inside an active scope, it registers itself as a dependency.
/// </summary>
/// <remarks>
/// The scope belongs to the thread that runs the Effect or Computed, and lasts while its
/// callback is on the stack: a read on another thread, or after an <c>await</c> inside the
/// callback, subscribes nothing. It was an <c>AsyncLocal</c>, which flowed into whatever the
/// callback started — a <c>Task.Run</c>, the rest of an <c>async</c> lambda — so such a read
/// subscribed the effect from a thread the reactive graph does not lock for; and every set
/// made a new ExecutionContext, 72 bytes an effect run (160 with another AsyncLocal live,
/// as setting <c>CultureInfo.CurrentCulture</c> makes one). Reactive state lives on the UI
/// thread (<see cref="Threading.UIThread"/>), so nothing that keeps that rule could tell.
/// </remarks>
public static class ReactiveScope
{
    [ThreadStatic] private static IReactiveNode? t_currentSubscriber;

    internal static IReactiveNode? CurrentSubscriber => t_currentSubscriber;

    internal static IReactiveNode? SetSubscriber(IReactiveNode? subscriber)
    {
        var previous = t_currentSubscriber;
        t_currentSubscriber = subscriber;
        return previous;
    }

    /// <summary>
    /// Runs the given action without tracking any dependencies.
    /// Useful when reading signals without establishing a reactive relationship.
    /// </summary>
    public static void Untracked(Action action)
    {
        var previous = SetSubscriber(null);
        try
        {
            action();
        }
        finally
        {
            SetSubscriber(previous);
        }
    }

    /// <summary>
    /// Runs the given function without tracking any dependencies.
    /// </summary>
    public static T Untracked<T>(Func<T> func)
    {
        var previous = SetSubscriber(null);
        try
        {
            return func();
        }
        finally
        {
            SetSubscriber(previous);
        }
    }

    internal static void TrackRead(IReactiveSource source)
    {
        var subscriber = t_currentSubscriber;
        if (subscriber is not null)
        {
            source.Subscribe(subscriber);
            if (subscriber is IDependencyTracker tracker)
                tracker.AddDependency(source);
        }
    }
}
