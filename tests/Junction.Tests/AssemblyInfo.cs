// Two kinds of test here touch process-global state: the headless host mutates the
// Nocturne accent ramp and Ui.DisplayScale and installs a synchronization context,
// and the git-backed tests set HOME / GIT_CONFIG_GLOBAL for the child process
// environment. Running either pair concurrently races. The suite is small, so
// serialize it — the same call VesperCommander.Tests makes.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
