namespace VesperCommander;

public partial class CommanderApp
{
    // ── Archive browsing ─────────────────────────────────────────────────
    // Whether an external 7-Zip is available (probed once at startup — the probe spawns a
    // process, so it must not run per-listing). Passwords for encrypted archives live only
    // in memory for the session, keyed by archive file; never persisted or logged.
    private bool _sevenZip;
    private readonly Dictionary<string, string> _archivePasswords = new(StringComparer.OrdinalIgnoreCase);

    private bool InArchive(char side) => ArchivePath.InArchive(Dir(side));

    /// <summary>The cached password for the archive that <paramref name="dir"/> is inside
    /// (or null). Used so a listing/extract can reuse a password entered this session.</summary>
    private string? PasswordFor(string dir) =>
        ArchivePath.TryParse(dir) is { } ap && _archivePasswords.TryGetValue(ap.ArchiveFile, out var p) ? p : null;

    /// <summary>True (and posts a notice) when a write is attempted inside an archive —
    /// archives are read-only in v1. Mirrors <see cref="LockedBlock"/>.</summary>
    private bool InArchiveBlock(char side)
    {
        if (!InArchive(side)) return false;
        _notice = "Can't write inside an archive — F5 copies out";
        MarkDirty();
        return true;
    }
}
