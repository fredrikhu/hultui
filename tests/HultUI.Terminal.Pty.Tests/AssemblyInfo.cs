using Xunit;

// A HeadlessSession owns process-global state (Application.Current, the UI-thread
// binding, the reactive scheduler), so all tests in this assembly run sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]