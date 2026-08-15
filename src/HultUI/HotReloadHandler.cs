#if !NATIVE_AOT
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(HultUI.HotReloadHandler))]

namespace HultUI;

/// <summary>
/// Integrates with .NET hot reload. When dotnet watch applies a code change,
/// the runtime calls ClearCache then UpdateApplication, triggering a full
/// rebuild of all mounted components.
/// </summary>
internal static class HotReloadHandler
{
    /// <summary>Called by the runtime to clear any caches before updating.</summary>
    public static void ClearCache(Type[]? updatedTypes) { }

    /// <summary>Called by the runtime after metadata has been updated.</summary>
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        // The runtime invokes update handlers from its own thread, not the UI thread,
        // so rebuilding every mounted component directly here would run Build() and
        // reconcile the live tree off-thread. Queue it instead; the run loop drains
        // the queue on its next pass (at worst one 16 ms wait away).
        Application.InvokeOnMainThread(() => Application.Current?.RebuildAll());
    }
}
#endif
