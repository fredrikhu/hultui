using System.Globalization;

namespace Meridian.Core;

/// <summary>One send, as the log remembers it.</summary>
public sealed record HistoryEntry(
    DateTimeOffset At,
    string Method,
    string Url,
    int StatusCode,
    double DurationMs,
    string? EnvName,
    string? RequestPath,
    /// <summary>Empty when the send never got a response (an infra failure);
    /// <see cref="StatusCode"/> is 0 then and <see cref="Detail"/> says why.</summary>
    string Detail = "")
{
    public bool Reached => StatusCode > 0;
}

/// <summary>
/// The append-only history: one tab-separated line per send in
/// <c>.meridian/history.log</c>, newest last on disk and newest first in the
/// view. Capped — at <see cref="Cap"/> entries the oldest are dropped, because
/// an unbounded log in a workspace you dogfood daily is a slow leak.
///
/// <para>Line-oriented and plain text like everything else here, so
/// <c>tail -f</c> works and a corrupt line costs one entry rather than the
/// file: anything that doesn't parse is skipped.</para>
/// </summary>
public static class HistoryLog
{
    public const int Cap = 10_000;
    private const char Sep = '\t';

    public static string PathFor(string workspaceRoot) =>
        Path.Combine(workspaceRoot, ".meridian", "history.log");

    /// <summary>Every entry, newest first. A missing or unreadable log is an
    /// empty history, not an error.</summary>
    public static IReadOnlyList<HistoryEntry> Read(string workspaceRoot)
    {
        var path = PathFor(workspaceRoot);
        string[] lines;
        try
        {
            if (!File.Exists(path)) return [];
            lines = File.ReadAllLines(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var entries = new List<HistoryEntry>(lines.Length);
        foreach (var line in lines)
            if (TryParse(line, out var entry))
                entries.Add(entry);
        entries.Reverse();   // newest first
        return entries;
    }

    /// <summary>Appends one send. Trims the file when it grows past the cap —
    /// checked on append rather than by a background sweep, so the cost is one
    /// rewrite every <see cref="Cap"/>/10 sends.</summary>
    public static void Append(string workspaceRoot, HistoryEntry entry)
    {
        var path = PathFor(workspaceRoot);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, Format(entry) + "\n");

            var info = new FileInfo(path);
            // ~180 bytes/line: only count lines when the file is plausibly over.
            if (info.Length > Cap * 180L)
                Trim(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // History is a convenience; failing to write it must never break a send.
        }
    }

    private static void Trim(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length <= Cap) return;
        File.WriteAllLines(path, lines[^Cap..]);
    }

    internal static string Format(HistoryEntry e) => string.Join(Sep,
        e.At.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
        e.Method,
        e.Url,
        e.StatusCode.ToString(CultureInfo.InvariantCulture),
        e.DurationMs.ToString("0", CultureInfo.InvariantCulture),
        e.EnvName ?? "",
        e.RequestPath ?? "",
        e.Detail.Replace('\t', ' ').Replace('\n', ' '));

    internal static bool TryParse(string line, out HistoryEntry entry)
    {
        entry = null!;
        var parts = line.Split(Sep);
        if (parts.Length < 7) return false;
        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)) return false;
        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var status)) return false;
        if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)) return false;

        entry = new HistoryEntry(
            DateTimeOffset.FromUnixTimeMilliseconds(ms),
            parts[1],
            parts[2],
            status,
            duration,
            parts[5].Length == 0 ? null : parts[5],
            parts[6].Length == 0 ? null : parts[6],
            parts.Length > 7 ? parts[7] : "");
        return true;
    }

    /// <summary>"14:03:12" for today, "yesterday 14:03" / "Mon 14:03" further
    /// back — the view dims those rows too.</summary>
    public static string TimeLabel(HistoryEntry entry, DateTimeOffset now)
    {
        var local = entry.At.ToLocalTime();
        var today = now.ToLocalTime().Date;
        int days = (today - local.Date).Days;
        return days switch
        {
            <= 0 => local.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            1 => "yesterday " + local.ToString("HH:mm", CultureInfo.InvariantCulture),
            < 7 => local.ToString("ddd HH:mm", CultureInfo.InvariantCulture),
            _ => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
    }

    public static bool IsToday(HistoryEntry entry, DateTimeOffset now) =>
        entry.At.ToLocalTime().Date == now.ToLocalTime().Date;
}
