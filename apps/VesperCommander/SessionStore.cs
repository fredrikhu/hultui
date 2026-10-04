using System.Text.Json.Serialization;
using HultUI.Core.Storage;

namespace VesperCommander;

/// <summary>The open tabs per pane + active index, persisted for "Restore tabs on start".
/// The view-mode lists are parallel to the dir lists (index-aligned) and optional — an
/// older session file without them restores every tab in Detail. <see cref="Window"/> is
/// where the window was, restored whatever the tabs option says; null in an older file.</summary>
public sealed record SessionData(
    List<string> Left, int LeftActive, List<string> Right, int RightActive,
    List<int>? LeftViews = null, List<int>? RightViews = null,
    HultUI.Core.Platform.WindowPlacement? Window = null);

// Source-generated (de)serialization — reflection-based System.Text.Json is not
// trim/AOT-safe, and this app publishes AOT.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SessionData))]
internal partial class SessionJsonContext : JsonSerializerContext;

/// <summary>Persists the tab session to <c>session.json</c> in <see cref="ConfigDir"/>. Best-effort:
/// written beside and swapped in, the previous kept as a backup (see <see cref="JsonFile{T}"/>).</summary>
public static class SessionStore
{
    private static JsonFile<SessionData> File => new(ConfigDir.Path, "session.json", SessionJsonContext.Default.SessionData);

    public static SessionData? Load() => File.Load();

    public static void Save(SessionData session) => File.Save(session);
}
