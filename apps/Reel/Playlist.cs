namespace Reel;

/// <summary>
/// The folder a file sits in, as the thing to play next. There is no playlist to build and
/// no library to browse: opening one episode of a season means the rest are already in
/// order beside it, and that ordering is what a player has to get right.
///
/// <para>Sorted the way a file manager sorts, not the way <see cref="string.CompareTo(string?)"/>
/// does: "Episode 2" comes before "Episode 10", which ordinal comparison gets backwards on
/// exactly the files people watch in sequence.</para>
/// </summary>
public sealed class Playlist
{
    /// <summary>What counts as something to play. Deliberately the containers mpv handles
    /// without argument; anything else in the folder is not part of the running order.</summary>
    public static readonly string[] Extensions =
    [
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".webm", ".ts", ".m2ts", ".mpg", ".mpeg",
        ".wmv", ".flv", ".ogv", ".3gp",
    ];

    private readonly List<string> _files;

    private Playlist(List<string> files, int index)
    {
        _files = files;
        Index = index;
    }

    /// <summary>The playable files beside <paramref name="path"/>, in viewing order, with
    /// the cursor on that file. A file whose folder cannot be read is a playlist of one:
    /// the thing still plays, it just has no next.</summary>
    public static Playlist ForFile(string path)
    {
        var self = new List<string> { path };
        string? folder;
        try { folder = Path.GetDirectoryName(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        { return new Playlist(self, 0); }
        if (string.IsNullOrEmpty(folder)) return new Playlist(self, 0);

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder)
                .Where(IsPlayable)
                .OrderBy(Path.GetFileName, NaturalComparer.Instance)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Playlist(self, 0);
        }

        int index = files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0) { files.Insert(0, path); index = 0; }
        return new Playlist(files, index);
    }

    public static bool IsPlayable(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Files => _files;
    public int Index { get; private set; }
    public int Count => _files.Count;

    public string Current => _files[Math.Clamp(Index, 0, _files.Count - 1)];
    public bool HasNext => Index + 1 < _files.Count;
    public bool HasPrevious => Index > 0;

    /// <summary>Moves the cursor and returns what to play, or null at either end — the end
    /// of a folder is the end, not a wrap around to the first episode again.</summary>
    public string? Next() => HasNext ? _files[++Index] : null;
    public string? Previous() => HasPrevious ? _files[--Index] : null;

    /// <summary>"3 of 12", for the window title.</summary>
    public string Position => _files.Count > 1 ? $"{Index + 1} of {_files.Count}" : "";
}

/// <summary>
/// Compares file names the way a person reads them: runs of digits as numbers, everything
/// else case-insensitively. Without it "Episode 10" sorts before "Episode 2" and a folder
/// of episodes plays in the wrong order.
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                // Compared as numbers, so leading zeroes and length do not decide it.
                var left = x.AsSpan(si, i - si).TrimStart('0');
                var right = y.AsSpan(sj, j - sj).TrimStart('0');
                if (left.Length != right.Length) return left.Length - right.Length;
                int digits = left.SequenceCompareTo(right);
                if (digits != 0) return digits;
                continue;
            }

            int one = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (one != 0) return one;
            i++; j++;
        }
        return (x.Length - i) - (y.Length - j);
    }
}
