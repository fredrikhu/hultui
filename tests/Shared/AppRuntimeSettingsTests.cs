using System.Text.Json;
using Xunit;

namespace HultUI.Testing;

/// <summary>
/// The runtime settings every app ships with (apps/AppRuntime.props), read from the app's
/// runtimeconfig.json beside the tests. A NativeAOT publish bakes the same items into the exe, so
/// what this reads is what the shipped app runs with. Linked into each app's test project; the app
/// is the test assembly's name without ".Tests".
/// </summary>
public sealed class AppRuntimeSettingsTests
{
    [Fact]
    public void TheAppShipsWithTheSharedGcSettings()
    {
        var props = ConfigProperties();
        // 512 KB, not the default 85,000 bytes: a text area's text up to about 262,000 characters
        // stays off the large-object heap, so editing it no longer brings gen-2 collections.
        Assert.Equal(524_288, props.GetProperty("System.GC.LOHThreshold").GetInt64());
    }

    private static JsonElement ConfigProperties()
    {
        string tests = typeof(AppRuntimeSettingsTests).Assembly.GetName().Name!;
        string app = tests[..^".Tests".Length];
        string path = Path.Combine(AppContext.BaseDirectory, app + ".runtimeconfig.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties").Clone();
    }
}
