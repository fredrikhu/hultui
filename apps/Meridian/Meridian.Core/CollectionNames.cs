namespace Meridian.Core;

/// <summary>
/// The rules for a name the app turns into a file or folder of the collection — an
/// environment, a request, a folder, a new collection. A collection is cloned to every OS, so
/// a name has to be one all of them take: no separators or <c>..</c>, nothing Windows refuses
/// in a file name (its reserved characters, a device name such as <c>CON</c>, a trailing dot),
/// and no leading dot, which would hide it (the scanner never enters dot-folders).
/// </summary>
public static class CollectionNames
{
    private static readonly HashSet<string> WindowsDevices = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Why <paramref name="name"/> (already trimmed) can't name a <paramref name="noun"/>,
    /// or null when it can. Says nothing about clashes: see <see cref="Taken"/>.</summary>
    public static string? Problem(string name, string noun)
    {
        if (name.Length == 0) return $"Give the {noun} a name.";
        if (name.Contains('/') || name.Contains('\\') || name.Contains(".."))
            return "A name can't contain /, \\ or two dots in a row.";
        if (name.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c)))
            return "A name can't contain < > : \" | ? or *";
        if (name.StartsWith('.')) return "A name can't start with a dot.";
        if (name.EndsWith('.')) return "A name can't end with a dot.";
        // Windows refuses a device name with any extension too: con.req is CON.
        int dot = name.IndexOf('.');
        if (WindowsDevices.Contains(dot < 0 ? name : name[..dot]))
            return $"{name} is a device name on Windows.";
        return null;
    }

    /// <summary>The name of the file or folder in <paramref name="directory"/> that
    /// <paramref name="entryName"/> would land on in any letter case — on Windows and macOS
    /// they are the same entry — or null when there is none (or the folder can't be read, in
    /// which case creating the entry reports the problem).</summary>
    public static string? Taken(string directory, string entryName)
    {
        try
        {
            if (!Directory.Exists(directory)) return null;
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var existing = Path.GetFileName(entry);
                if (string.Equals(existing, entryName, StringComparison.OrdinalIgnoreCase)) return existing;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>"list-users.req" and " list-users " both mean list-users: the
    /// <paramref name="suffix"/> (".req", ".env") and the spaces around go.</summary>
    public static string Stem(string input, string suffix)
    {
        string name = input.Trim();
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? name[..^suffix.Length].TrimEnd() : name;
    }
}
