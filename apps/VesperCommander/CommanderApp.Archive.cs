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

    // ── Extract (copy-out) ───────────────────────────────────────────────
    private void StartArchiveTransfer(List<FileEntry> items, string dest, char source)
    {
        var op = new TransferOp(items) { IsMove = false, Source = source, DestDir = dest };
        _op = op;
        _progressOpen = true;
        MarkDirty();
        System.Threading.Tasks.Task.Run(() => RunTransfer(op));
    }

    /// <summary>F5 while inside an archive — extract the selection to the other pane, via
    /// the copy confirm (verb "Extract").</summary>
    private void StartExtract()
    {
        char side = _active, other = side == 'L' ? 'R' : 'L';
        var items = OperationTargets(side).ToList();
        if (items.Count == 0) return;
        var dest = Dir(other);
        if (InArchiveBlock(other) || LockedBlock(other, "extract into a locked tab")) return;
        string from = ArchivePath.TryParse(Dir(side)) is { } a ? System.IO.Path.GetFileName(a.ArchiveFile) : "the archive";
        string label = items.Count == 1 ? $"\"{items[0].Name}\"" : $"{items.Count} items";
        _confirm = new ConfirmSpec("Extract", $"Extract {label} from {from} to\n{dest}", "Extract", Danger: false,
            () => { _confirm = null; StartArchiveTransfer(items, dest, side); });
        MarkDirty();
    }

    /// <summary>Alt+F9 — extract the archive under the cursor (or, when already inside one,
    /// the selection) to the other pane. A subfolder named after the archive is used when
    /// its root holds more than one top-level entry (avoids double-nesting a single folder).</summary>
    private void ExtractCursorArchive()
    {
        char side = _active, other = side == 'L' ? 'R' : 'L';
        if (InArchive(side)) { StartExtract(); return; }

        var list = Listing(side);
        int cur = Cursor(side);
        if (cur < 0 || cur >= list.Count) return;
        var e = list[cur];
        if (!(ArchiveVfs.IsArchiveFile(e.FullPath) && File.Exists(e.FullPath))) return;

        var dest = Dir(other);
        if (InArchiveBlock(other) || LockedBlock(other, "extract into a locked tab")) return;

        var root = ArchiveVfs.ListEntries(e.FullPath, "", PasswordFor(e.FullPath), _sevenZip);
        var members = root.Entries.Where(x => !x.IsParent).ToList();
        if (members.Count == 0) return;
        bool subfolder = members.Count > 1;
        string target = subfolder ? System.IO.Path.Combine(dest, System.IO.Path.GetFileNameWithoutExtension(e.Name)) : dest;
        _confirm = new ConfirmSpec("Extract", $"Extract {e.Name} to\n{target}", "Extract", Danger: false,
            () => { _confirm = null; StartArchiveTransfer(members, target, side); });
        MarkDirty();
    }
}
