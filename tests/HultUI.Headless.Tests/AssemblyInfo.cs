using Xunit;

// A HeadlessSession owns process-global state: Application.Current, the static
// AnimationManager tween list, ReactiveRuntime's scheduler, and the thread's
// SynchronizationContext. Two sessions alive at once would corrupt each other,
// so all tests in this assembly run sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
