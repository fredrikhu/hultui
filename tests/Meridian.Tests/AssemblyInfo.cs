// The headless host tests touch process-global state: HeadlessSession installs a
// synchronization context, Application.InvokeOnMainThread posts to the *current*
// application (which the collection watcher relies on), and Ui.DisplayScale is a
// static. Two sessions running concurrently cross their wires — a watcher callback
// lands on the other test's main-thread queue. Serialize the suite, the same call
// the other app suites make.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
