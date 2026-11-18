namespace HultUI.Platform.SDL3;

/// <summary>The checks on the app's metadata, apart from SDL so they can be tested without it.</summary>
internal static class AppMetadata
{
    internal const int MaxIdentifierLength = 255;

    /// <summary>Letters, digits, '.', '_' and '-'; 1 to 255 characters; no empty part between dots.</summary>
    internal static bool IsValidIdentifier(string identifier)
    {
        if (identifier.Length is 0 or > MaxIdentifierLength) return false;
        if (identifier[0] == '.' || identifier[^1] == '.' || identifier.Contains("..", StringComparison.Ordinal)) return false;
        foreach (char c in identifier)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')) return false;
        return true;
    }

    /// <summary>Throws unless the metadata can be given to SDL now.</summary>
    internal static void Validate(string name, string identifier, bool windowExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(identifier);
        if (!IsValidIdentifier(identifier))
            throw new ArgumentException(
                $"\"{identifier}\" is not an app id: use letters, digits, '.', '_' and '-', with no empty part between dots and at most 255 characters, as in \"org.example.App\".",
                nameof(identifier));
        if (windowExists)
            throw new InvalidOperationException(
                "Set the app metadata before the first window is created: a window keeps the app id it was created with.");
    }
}
