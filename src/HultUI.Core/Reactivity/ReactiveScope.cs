namespace HultUI.Core.Reactivity;

/// <summary>
/// Thread-local dependency tracker. When a reactive source (Signal/Computed) is read
/// inside an active scope, it registers itself as a dependency.
/// </summary>
public static class ReactiveScope
{
    private static readonly AsyncLocal<IReactiveNode?> _currentSubscriber = new();

    internal static IReactiveNode? CurrentSubscriber => _currentSubscriber.Value;

    internal static IReactiveNode? SetSubscriber(IReactiveNode? subscriber)
    {
        var previous = _currentSubscriber.Value;
        _currentSubscriber.Value = subscriber;
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
        var subscriber = _currentSubscriber.Value;
        if (subscriber is not null)
        {
            source.Subscribe(subscriber);
            if (subscriber is IDependencyTracker tracker)
                tracker.AddDependency(source);
        }
    }
}
