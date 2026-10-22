using Xunit;

// A HeadlessSession switches process-global state to itself when a test drives it:
// Application.Current, the static AnimationManager tween list, the thread's
// SynchronizationContext; and ReactiveRuntime's scheduler is static. Sessions may
// live side by side but are driven one thread at a time, so all tests in this
// assembly run sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
