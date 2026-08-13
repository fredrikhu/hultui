using System.Runtime.CompilerServices;

namespace Junction.Tests;

/// <summary>
/// Points Junction's config directory at a throwaway location for the whole test process.
///
/// <para>The hosted tests build a <see cref="JunctionApp"/> over an in-memory
/// <see cref="JunctionSettingsData"/>, which reads as harmless — but the app persists on
/// its own initiative. Toggling diff wrap, switching repo or workspace, editing options:
/// around twenty call sites reach <c>Persist()</c>, and every one of them wrote the test
/// fixture over the developer's real <c>settings.json</c>. Pressing <c>W</c> in the diff
/// tests was enough. The damage was silent in both directions, because <c>Save</c> swallows
/// its exceptions and <c>Load</c> treats an unreadable file as a first run — so the only
/// symptom was an installed Junction that had "forgotten" its repositories and come back
/// pointing at a deleted temp fixture.</para>
///
/// <para><see cref="TempRepo"/> already isolates the <i>git</i> environment for the same
/// reason; this is the same idea for Junction's own config. It runs as a module initializer
/// rather than from <c>TempRepo</c> so that it also covers any test that hosts the app
/// without creating a repository.</para>
/// </summary>
internal static class SettingsIsolation
{
    /// <summary>The directory the config is redirected to, for tests that want to assert
    /// on the files themselves.</summary>
    internal static string ConfigHome { get; } =
        Path.Combine(Path.GetTempPath(), "junction-test-config");

    [ModuleInitializer]
    internal static void Redirect()
    {
        // JunctionSettings.GetConfigDirectory checks XDG_CONFIG_HOME first on every
        // platform, Windows included, and both Load and Save go through it — so this one
        // variable moves the whole config, not just the reads.
        //
        // Left in place for the run rather than set per test: the point is that no test can
        // reach the real file, and a redirect that has to be remembered is one a future test
        // will forget. Starting clean each run keeps one run's leftovers from being read as
        // another's stored state. GIT_CONFIG_GLOBAL (see TempRepo) already overrides git's
        // own XDG lookup, so this cannot leak into the fixtures' git behaviour.
        try { Directory.Delete(ConfigHome, recursive: true); } catch { /* first run, or in use */ }
        Directory.CreateDirectory(ConfigHome);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", ConfigHome);
    }
}
