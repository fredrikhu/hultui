using System;
using System.IO;
using System.Runtime.CompilerServices;

// Several tests host the real CommanderApp in a headless window, which touches
// process-global state — the mutable Nocturne accent ramp, the on-disk settings /
// session store, and the headless Application + its synchronization context. Running
// two such hosts on parallel threads races. The suite is tiny, so serialize it.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace VesperCommander.Tests;

internal static class TestConfig
{
    /// <summary>
    /// Points the app's settings / session / bookmarks at a scratch directory before any
    /// test constructs it. Hosting the real app otherwise reads the developer's own
    /// configuration — and, now that the diff's wrap toggle persists, rewrites it.
    /// </summary>
    /// <para>Emptied at the start of every run, not merely redirected: the diff's wrap
    /// toggle persists, so a run that leaves it on would have the next run start wrapped and
    /// every "press w and expect wrapping" assertion would mean the opposite.</para>
    [ModuleInitializer]
    internal static void RedirectConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vesper-tests-config");
        try { Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("VESPER_CONFIG_DIR", dir);
    }
}
