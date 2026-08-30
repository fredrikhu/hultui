// HeadlessSession installs a synchronization context and the reactive runtime keeps a
// static pending-effect queue, so two sessions running at once cross their wires.
// Serialize the suite, the same call the app suites make.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
