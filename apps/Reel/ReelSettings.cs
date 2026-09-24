using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reel;

/// <summary>Where a file was left, keyed by the file itself.</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="Length">Its size in bytes — the cheap half of "is this still the same
/// file". A path that now holds a different encode should not resume 40 minutes in.</param>
/// <param name="Position">How far in, in seconds.</param>
/// <param name="Duration">How long the file is, in seconds; 0 when it was never known.</param>
/// <param name="Watched">When it was last opened, so the oldest entries can be dropped.</param>
public sealed record ResumePoint(string Path, long Length, double Position, double Duration, DateTimeOffset Watched);

public sealed class ReelSettingsData
{
    /// <summary>Volume 0–1 as the player last left it, and whether it was muted.</summary>
    public float Volume { get; set; } = 1f;
    public bool Muted { get; set; }

    /// <summary>The folder the Open dialog starts in: wherever the last file came from.</summary>
    public string? LastFolder { get; set; }

    /// <summary>
    /// The audio and subtitle languages last chosen by hand, applied to the next file that
    /// has a track matching. Same rule the Jellyfin client follows, and the same reason:
    /// an evening of episodes should not reset to the file's default every time.
    /// </summary>
    public string? PreferredAudioLanguage { get; set; }
    public string? PreferredSubtitleLanguage { get; set; }

    /// <summary>Whether subtitles are wanted at all. Turning them off is a preference like
    /// any other — a viewer who closed them means it for the next file too.</summary>
    public bool SubtitlesOn { get; set; } = true;

    public List<ResumePoint> Resume { get; set; } = [];

    /// <summary>Whether a second launch hands its file to the running window.</summary>
    public bool SingleInstance { get; set; } = true;

    public void Normalize()
    {
        Resume ??= [];
        Volume = Math.Clamp(Volume, 0f, 1f);
        Resume.RemoveAll(r => string.IsNullOrWhiteSpace(r.Path));
    }
}

/// <summary>
/// What Reel remembers between runs: volume, where each file was left, and where the Open
/// dialog should start. A plain JSON file — there is no account and no secret here, which
/// is the whole difference from the Jellyfin client's split store.
/// </summary>
public sealed class ReelSettings
{
    /// <summary>How many files keep a resume point. Beyond this the oldest are dropped —
    /// the list is a convenience, not a viewing history, and it is written on every stop.</summary>
    private const int MaxResumePoints = 400;

    /// <summary>
    /// Under this, a file counts as "not started" and plays from the beginning; over the
    /// end margin it counts as finished, so the next open does not resume two seconds
    /// before the credits stop.
    /// </summary>
    public static readonly TimeSpan ResumeThreshold = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan EndMargin = TimeSpan.FromSeconds(20);

    private readonly string _path;

    public ReelSettings(string? settingsPath = null)
    {
        _path = settingsPath ?? DefaultPath;
        Data = Read(_path) ?? new ReelSettingsData();
        Data.Normalize();
    }

    public ReelSettingsData Data { get; }

    /// <summary>
    /// Where this file was left, or null when it was never started, was barely started, or
    /// ran to the end. Matched on path and size together: the same name holding a different
    /// file is a different film.
    /// </summary>
    public TimeSpan? ResumeFor(string path)
    {
        var point = Find(path);
        if (point is null) return null;
        var at = TimeSpan.FromSeconds(point.Position);
        if (at < ResumeThreshold) return null;
        if (point.Duration > 0 && at > TimeSpan.FromSeconds(point.Duration) - EndMargin) return null;
        return at;
    }

    /// <summary>Records where a file was left. A position inside the opening or the closing
    /// margin clears the entry instead — those are "start again" and "finished".</summary>
    public void Remember(string path, TimeSpan position, TimeSpan? duration)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        long length = Length(path);
        Data.Resume.RemoveAll(r => Same(r, path, length));

        double seconds = position.TotalSeconds;
        double total = duration?.TotalSeconds ?? 0;
        bool worthKeeping = position >= ResumeThreshold
            && (total <= 0 || position < duration!.Value - EndMargin);
        if (worthKeeping)
            Data.Resume.Add(new ResumePoint(path, length, seconds, total, DateTimeOffset.UtcNow));

        if (Data.Resume.Count > MaxResumePoints)
        {
            Data.Resume.Sort((a, b) => b.Watched.CompareTo(a.Watched));
            Data.Resume.RemoveRange(MaxResumePoints, Data.Resume.Count - MaxResumePoints);
        }
        Save();
    }

    private ResumePoint? Find(string path)
    {
        long length = Length(path);
        return Data.Resume.FirstOrDefault(r => Same(r, path, length));
    }

    private static bool Same(ResumePoint point, string path, long length) =>
        string.Equals(point.Path, path, StringComparison.OrdinalIgnoreCase)
        && (length <= 0 || point.Length <= 0 || point.Length == length);

    private static long Length(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return 0; }
    }

    public void Save()
    {
        var pending = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(pending, JsonSerializer.Serialize(Data, ReelJsonContext.Default.ReelSettingsData));
            if (File.Exists(_path)) File.Replace(pending, _path, null, ignoreMetadataErrors: true);
            else File.Move(pending, _path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the resume point is not worth failing playback over.
            try { File.Delete(pending); } catch (IOException) { /* nothing further to try */ }
        }
    }

    private static ReelSettingsData? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var data = JsonSerializer.Deserialize(File.ReadAllText(path), ReelJsonContext.Default.ReelSettingsData);
            data?.Normalize();
            return data;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string DefaultPath => Path.Combine(ConfigDirectory, "settings.json");

    /// <summary>XDG on Linux, <c>%APPDATA%\Reel</c> on Windows — the lookup the other apps use.</summary>
    public static string ConfigDirectory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdg)) return Path.Combine(xdg, "Reel");
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData)) return Path.Combine(appData, "Reel");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Reel");
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ReelSettingsData))]
internal partial class ReelJsonContext : JsonSerializerContext;
