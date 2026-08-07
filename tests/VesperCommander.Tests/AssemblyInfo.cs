// Several tests host the real CommanderApp in a headless window, which touches
// process-global state — the mutable Nocturne accent ramp, the on-disk settings /
// session store, and the headless Application + its synchronization context. Running
// two such hosts on parallel threads races. The suite is tiny, so serialize it.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
