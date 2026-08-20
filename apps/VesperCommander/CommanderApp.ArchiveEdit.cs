using HultUI;

namespace VesperCommander;

/// <summary>
/// F4 on a member of an archive: edit it, and put it back.
///
/// <para>An archive member has no file on disk, so handing its path to an editor opened a
/// document that wasn't there — an empty buffer that, saved, went nowhere. The member is
/// unpacked to a temp file under its real name (editors pick their syntax mode off the name)
/// and the editor is pointed at that.</para>
///
/// <para>Getting the edit back is the harder half. "When the editor closes" is not something
/// Vesper can watch for: VS Code, Notepad++ and most others hand the file to an already-running
/// instance and the process Vesper launched exits immediately, long before anyone has typed
/// anything. So the question is asked from the other end — when Vesper comes back to the
/// foreground, every file it has out for editing is re-stat'd, and the ones that changed are
/// offered back to their archives. That is the moment the user has finished with the editor and
/// is looking at Vesper again, which is as close to "on close" as anything observable gets.</para>
/// </summary>
public partial class CommanderApp
{
    private sealed class ArchiveEdit
    {
        public required string Archive;        // the real archive file on disk
        public required string Folder;         // internal folder path the member sits in ("" at root)
        public required string Internal;       // internal path of the member itself
        public required string Name;
        public required string TempPath;
        public string? Password;
        public string? Blocked;                // why it can't be written back, when it can't
        public DateTime Stamp;                 // last-write time already answered for
        public long Length;
    }

    private readonly List<ArchiveEdit> _edits = [];
    private string? _editRoot;                 // per-session temp folder, removed on exit
    private bool _editPrompting;               // a write-back confirm is on screen
    private bool _editWriting;                 // a write-back is running off-thread

    /// <summary>F4 on an archive member. Unpacks it, opens the editor on the copy, and starts
    /// watching it. A second F4 on the same member reopens the copy already out — a fresh
    /// extraction would be a second file, missing whatever was typed into the first.</summary>
    private void EditArchiveMember(FileEntry e, string template)
    {
        if (ArchivePath.TryParse(e.FullPath) is not { } ap) return;

        var edit = OpenEditFor(ap.ArchiveFile, ap.Internal);

        if (edit is null)
        {
            edit = ExtractForEdit(ap.ArchiveFile, ap.Internal, e.Name, PasswordFor(e.FullPath));
            if (edit is null) { Notice($"Couldn't read {e.Name} out of the archive"); return; }
            _edits.Add(edit);
            // Said now rather than after the editing: a read-only archive is worth knowing
            // about before the work, not after it.
            if (edit.Blocked is { } why) Notice($"Opened a copy — {why.ToLowerInvariant()}");
        }

        FileOps.LaunchEditor(template, edit.TempPath);
    }

    /// <summary>The copy already out for this member, or null. Matched on the archive plus the
    /// internal path, so two members that share a name in different archive folders are two
    /// different edits.</summary>
    private ArchiveEdit? OpenEditFor(string archive, string internalPath) =>
        _edits.FirstOrDefault(x => PathEquals(x.Archive, archive)
            && string.Equals(x.Internal, internalPath, StringComparison.OrdinalIgnoreCase));

    private ArchiveEdit? ExtractForEdit(string archive, string internalPath, string name, string? password)
    {
        try
        {
            _editRoot ??= System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vesper-edit",
                Guid.NewGuid().ToString("N"));
            // One folder per member: two members can share a name across archive folders, and
            // the temp file has to keep its real name for the editor's sake.
            var dir = System.IO.Path.Combine(_editRoot, Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            var temp = System.IO.Path.Combine(dir, name);

            using (var source = ArchiveVfs.OpenEntry(archive, internalPath, password, _sevenZip))
            {
                if (source is null) return null;
                using var target = System.IO.File.Create(temp);
                source.CopyTo(target);
            }

            var info = new System.IO.FileInfo(temp);
            return new ArchiveEdit
            {
                Archive = archive,
                Folder = ArchivePath.ParentOf(archive, internalPath),
                Internal = internalPath,
                Name = name,
                TempPath = temp,
                Password = password,
                Blocked = ArchiveAddBlock(ArchivePath.ParentOf(archive, internalPath)),
                Stamp = info.LastWriteTimeUtc,
                Length = info.Length,
            };
        }
        catch { return null; }
    }

    // ── Coming back to Vesper ────────────────────────────────────────────
    /// <summary>Window.Activated: the user is looking at Vesper again, so anything they took
    /// out to an editor is worth re-checking.</summary>
    private void OnWindowActivated()
    {
        if (_edits.Count == 0) return;
        OfferNextArchiveEdit();
    }

    /// <summary>Ask about the next changed file, one at a time. Several can have changed —
    /// a stack of dialogs is worse than a queue, and each answer leads to the next.</summary>
    private void OfferNextArchiveEdit()
    {
        // Don't talk over another dialog, or over a transfer that is already rewriting things.
        if (_editPrompting || _editWriting || _confirm != null || _op != null || AnyOverlayOpen) return;

        foreach (var edit in _edits)
        {
            if (!HasChanged(edit)) continue;

            if (edit.Blocked is { } why)
            {
                // Nothing to ask: there is no way to put it back. Say so once and stop
                // watching the file, so it isn't raised again on every return to the window.
                Settle(edit);
                Notice($"{edit.Name} changed, but {why.ToLowerInvariant()} — the edited copy is at {edit.TempPath}");
                continue;
            }

            _editPrompting = true;
            _confirm = new ConfirmSpec(
                $"{edit.Name} changed",
                $"Update {System.IO.Path.GetFileName(edit.Archive)} with the edit?",
                "Update", Danger: false,
                OnConfirm: () =>
                {
                    _confirm = null; _editPrompting = false;
                    CommitArchiveEdit(edit);
                },
                OnCancel: () =>
                {
                    _editPrompting = false;
                    // Answered, so it doesn't ask again until the file changes once more. The
                    // copy stays where it is until Vesper exits, which is the only chance to
                    // rescue it — hence the path in the notice.
                    Settle(edit);
                    Notice($"Left {System.IO.Path.GetFileName(edit.Archive)} alone — the edited copy is at {edit.TempPath}");
                    OfferNextArchiveEdit();
                });
            MarkDirty();
            return;
        }
    }

    private static bool HasChanged(ArchiveEdit edit)
    {
        try
        {
            var info = new System.IO.FileInfo(edit.TempPath);
            if (!info.Exists) return false;
            return info.LastWriteTimeUtc != edit.Stamp || info.Length != edit.Length;
        }
        catch { return false; }
    }

    /// <summary>Record the file as it stands now, so this state is not asked about again.</summary>
    private static void Settle(ArchiveEdit edit)
    {
        try
        {
            var info = new System.IO.FileInfo(edit.TempPath);
            if (!info.Exists) return;
            edit.Stamp = info.LastWriteTimeUtc;
            edit.Length = info.Length;
        }
        catch { /* gone or locked — the next check will settle it */ }
    }

    /// <summary>Write the edited copy back as the archive member it came from.
    ///
    /// <para>Off the UI thread: both writers rewrite the whole archive, and 7-Zip does it in
    /// another process, so a large archive would otherwise freeze the window for as long as the
    /// repack takes. There is no progress overlay — it is one small file going back into a
    /// place it already had — so the window stays usable and a notice reports the result.</para></summary>
    private void CommitArchiveEdit(ArchiveEdit edit)
    {
        Settle(edit);
        _editWriting = true;
        Notice($"Updating {edit.Name} in {System.IO.Path.GetFileName(edit.Archive)}…");

        var archive = edit.Archive;
        var folder = ArchivePath.TryParse(edit.Folder) is { } fp ? fp.Internal : "";
        var (temp, name, password) = (edit.TempPath, edit.Name, edit.Password);

        System.Threading.Tasks.Task.Run(() =>
        {
            var err = ArchiveVfs.AddMembers(archive, folder, [(temp, name)], password, _sevenZip);
            Application.InvokeOnMainThread(() =>
            {
                _editWriting = false;
                if (err != null)
                {
                    Notice($"Couldn't update {System.IO.Path.GetFileName(archive)}: {FileOps.Clean(err)}");
                }
                else
                {
                    Notice($"Updated {name} in {System.IO.Path.GetFileName(archive)}");
                    _genL++; _genR++;      // either pane may be showing the archive
                }
                MarkDirty();
                OfferNextArchiveEdit();    // whatever else changed while that ran
            });
        });
    }

    /// <summary>Drop the session's edit copies. Anything still unanswered goes with them —
    /// which is why declining to write one back names the file it is leaving behind.</summary>
    private void CleanupArchiveEdits()
    {
        _edits.Clear();
        if (_editRoot is null) return;
        try { if (System.IO.Directory.Exists(_editRoot)) System.IO.Directory.Delete(_editRoot, true); }
        catch { /* temp, best effort */ }
        _editRoot = null;
    }
}
