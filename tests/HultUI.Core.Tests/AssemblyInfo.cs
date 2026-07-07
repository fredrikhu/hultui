using Xunit;

// The reactive runtime — Signals, Effects, and the batch/flush queue in
// ReactiveRuntime — is process-global and single-threaded by design: the whole
// UI framework runs on one thread. Event dispatch now enters/exits reactive
// batches too, so any test that touches elements or dispatches events shares
// that global state with the reactivity tests. Run the assembly serially so
// tests don't race on it — this matches how the framework actually runs.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
