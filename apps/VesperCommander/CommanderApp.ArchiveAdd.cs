using System.IO.Compression;
using HultUI;

namespace VesperCommander;

/// <summary>
/// Writing <em>into</em> an archive: F5, F6 and paste with an archive pane as the destination.
/// Reading out of one (F5 to extract) and deleting from one (F8) were already there; adding was
/// refused outright by <see cref="CommanderApp.InArchiveBlock"/>.
/// </summary>
public partial class CommanderApp
{
    /// <summary>True when <paramref name="side"/> is inside an archive that can actually be
    /// written to, so a copy/move/paste aimed at it should add rather than be refused. Posts the
    /// reason and returns false when it is an archive but not a writable one — the caller has
    /// already decided this is the destination, so the refusal belongs here.</summary>
    private bool CanAddToArchive(char side)
    {
        if (!InArchive(side)) return false;
        if (ArchiveAddBlock(Dir(side)) is { } why) { _notice = why; MarkDirty(); return false; }
        return true;
    }

    /// <summary>Why a write into <paramref name="dir"/> — a path pointing inside an archive —
    /// can't happen, or null when it can. Stated over a path rather than a pane because the
    /// F5 / F6 destination box can name an archive folder neither pane is showing.</summary>
    private string? ArchiveAddBlock(string dir)
    {
        if (ArchivePath.TryParse(dir) is not { } ap) return "That folder is not inside an archive";
        if (!System.IO.File.Exists(ap.ArchiveFile)) return "That archive is no longer there";
        // What a nested archive shows is a temp extraction of the inner file, so an add would
        // go into the copy and leave the real archive untouched.
        if (ap.Internal.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Any(ArchivePath.IsArchiveFile))
            return "Can't add to a nested archive — extract it first";
        string ext = System.IO.Path.GetExtension(ap.ArchiveFile).ToLowerInvariant();
        if (!ArchiveVfs.Caps(ext, _sevenZip).Write)
            return ext is ".rar"
                ? "rar archives are read-only"
                : "Adding to this format needs 7-Zip — set it in Tools → Archiver";
        return null;
    }

    /// <summary>Begin adding <paramref name="items"/> to the archive folder at
    /// <paramref name="destDir"/>. <paramref name="move"/> deletes the sources afterwards, and
    /// only after a clean add.</summary>
    private void StartArchiveAdd(char from, string destDir, List<FileEntry> items, bool move)
    {
        if (items.Count == 0) return;
        if (ArchivePath.TryParse(destDir) is not { } ap) return;
        if (_op != null)
        {
            _notice = "A transfer is already running — let it finish first";
            MarkDirty(); return;
        }

        string? password = PasswordFor(destDir);
        bool sevenZipWriter = ArchiveVfs.WritesThroughSevenZip(ap.ArchiveFile, password);

        var op = new TransferOp(items)
        {
            IsPack = true,                       // shares the pack overlay: no per-entry skip
            IsMove = move,
            Source = from,
            DestDir = destDir,
            PackTarget = System.IO.Path.GetFileName(ap.ArchiveFile),
            PackVerb = "Adding to",
            PackRowVerb = "adding",
            // 7-Zip reports one percentage for the run, so the overlay drops its per-file bar.
            PackWholeRunOnly = sevenZipWriter,
            ArchiveTargets = new(),
        };
        _op = op;
        _progressOpen = true;
        _notice = null;
        MarkDirty();

        System.Threading.Tasks.Task.Run(() =>
            RunArchiveAdd(op, ap.ArchiveFile, ap.Internal, password, sevenZipWriter, move));
    }

    /// <summary>
    /// The add worker. Two phases, deliberately: every conflict is settled before a single byte
    /// is written.
    ///
    /// <para>A copy onto disk can ask about each file as it reaches it, because the files it has
    /// already written stay written. An archive is one file — both writers rewrite it as a whole,
    /// and 7-Zip does it in another process — so stopping half way through to ask leaves the
    /// archive mid-rewrite with a dialog on top of it. Resolving first also means "Skip all"
    /// costs nothing: the skipped members were never handed to the writer.</para>
    /// </summary>
    private void RunArchiveAdd(TransferOp op, string archive, string internalPath,
        string? password, bool sevenZipWriter, bool move)
    {
        for (int i = 0; i < op.Items.Count; i++) op.ItemBytes[i] = FileOps.EntrySize(op.Items[i]);
        op.TotalBytes = op.ItemBytes.Sum();
        op.StartTick = Environment.TickCount64;
        Repaint();

        var members = ArchiveVfs.AllMembers(archive, password, _sevenZip);
        if (members == null)
        {
            Application.InvokeOnMainThread(() => FinishArchiveAdd(op, "Add failed: couldn't read the archive", move, 0));
            return;
        }

        // ── phase 1: settle the conflicts ────────────────────────────────
        string prefix = internalPath.Replace('\\', '/').Trim('/');
        var approved = new List<int>();
        for (int i = 0; i < op.Items.Count && !op.Cancelled; i++)
        {
            var e = op.Items[i];
            if (MemberClash(members, prefix, e) is { } clash)
            {
                op.ArchiveTargets![i] = clash;
                op.Index = i;
                bool overwrite;
                if (op.OverwriteAll == null)
                {
                    op.Gate = new System.Threading.ManualResetEventSlim(false);
                    Application.InvokeOnMainThread(() => { _progressOpen = true; _overwriteOpen = true; MarkDirty(); });
                    op.Gate.Wait();
                    op.Gate.Dispose(); op.Gate = null;
                    if (op.Cancelled) break;
                    overwrite = op.ConflictOverwrite;
                }
                else overwrite = op.OverwriteAll.Value;

                if (!overwrite)
                {
                    op.Status[i] = 3; op.Skipped++;
                    op.TotalBytesDone += op.ItemBytes[i];   // keep the total bar reachable
                    Repaint();
                    continue;
                }
            }
            approved.Add(i);
        }

        if (op.Cancelled || approved.Count == 0)
        {
            string why = op.Cancelled ? "Add cancelled"
                : $"Nothing added — {(op.Skipped == 1 ? "the item was" : "all items were")} skipped";
            Application.InvokeOnMainThread(() => FinishArchiveAdd(op, why, move: false, 0));
            return;
        }

        // ── phase 2: write ───────────────────────────────────────────────
        var (entries, rowOf) = ExpandForArchive(op, approved, sevenZipWriter);
        var err = ArchiveVfs.AddMembers(archive, internalPath, entries, password, _sevenZip,
            AddWatcher(op, rowOf, entries.Count));

        bool cancelled = op.Cancelled || err == ArchiveVfs.CancelledError;
        int added = cancelled || err != null ? 0 : approved.Count;

        string notice =
            cancelled ? "Add cancelled"
            : err != null ? $"Add failed: {err}"
            : $"Added {added} item{(added == 1 ? "" : "s")} to {System.IO.Path.GetFileName(archive)}"
              + (op.Skipped > 0 ? $" · {op.Skipped} skipped" : "");

        Application.InvokeOnMainThread(() =>
            FinishArchiveAdd(op, notice, move && !cancelled && err == null, added));
    }

    /// <summary>The member an item would replace, or null when its place in the archive is free.
    /// A folder collides when anything at all is stored beneath it, which is why the whole member
    /// map is needed rather than the one level the pane is showing.</summary>
    private static (long Size, DateTime Modified)? MemberClash(
        Dictionary<string, (long Size, DateTime Modified)> members, string prefix, FileEntry e)
    {
        string target = prefix.Length == 0 ? e.Name : prefix + "/" + e.Name;
        if (members.TryGetValue(target, out var exact)) return exact;
        if (!e.IsDir) return null;
        // The folder itself is not a member; something inside it standing in for it is.
        foreach (var (path, info) in members)
            if (path.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase)) return info;
        return null;
    }

    /// <summary>Expand the approved rows into the (source, entry) pairs the writer wants, plus
    /// the row each pair belongs to so progress lands on the right queue line.
    /// <para>The built-in writer needs folders flattened into their files with relative names;
    /// 7-Zip recurses a folder itself and wants it handed over whole.</para></summary>
    private static (List<(string Source, string Entry)> Entries, List<int> RowOf) ExpandForArchive(
        TransferOp op, List<int> approved, bool sevenZip)
    {
        var entries = new List<(string, string)>();
        var rowOf = new List<int>();
        foreach (int row in approved)
        {
            var e = op.Items[row];
            if (sevenZip || !e.IsDir)
            {
                entries.Add((e.FullPath, e.Name));
                rowOf.Add(row);
                continue;
            }
            var baseDir = System.IO.Path.GetDirectoryName(e.FullPath) ?? "";
            foreach (var f in System.IO.Directory.EnumerateFiles(e.FullPath, "*", System.IO.SearchOption.AllDirectories))
            {
                entries.Add((f, System.IO.Path.GetRelativePath(baseDir, f).Replace('\\', '/')));
                rowOf.Add(row);
            }
        }
        return (entries, rowOf);
    }

    /// <summary>Wire the writer's per-entry reports onto the overlay's rows. One row can own many
    /// entries — a folder is one queue line and any number of members — so a row is only marked
    /// done once the last entry belonging to it has gone in.</summary>
    private PackProgress AddWatcher(TransferOp op, List<int> rowOf, int count)
    {
        // The final entry of each row, so a multi-entry row isn't ticked off at its first file.
        var lastOfRow = new Dictionary<int, int>();
        for (int i = 0; i < count; i++) lastOfRow[rowOf[i]] = i;

        return new PackProgress
        {
            EntryStart = (i, length) =>
            {
                if (i >= rowOf.Count) return;
                int row = rowOf[i];
                op.Index = row;
                op.Status[row] = 1;
                op.FileBytes = length > 0 ? length : op.ItemBytes[row];
                op.FileBytesDone = 0;
                Repaint();
            },
            Bytes = n => { op.FileBytesDone += n; op.TotalBytesDone += n; Repaint(); },
            EntryDone = (i, ok) =>
            {
                if (i >= rowOf.Count) return;
                int row = rowOf[i];
                if (!ok)
                {
                    // A source that vanished between the selection and the write. The row is
                    // only "missing" if nothing of it landed at all.
                    if (op.Status[row] != 2) { op.Status[row] = 3; }
                    op.TotalBytesDone += op.ItemBytes[row];
                }
                else if (lastOfRow.TryGetValue(row, out var last) && last == i)
                {
                    op.Status[row] = 2; op.Done++;
                }
                Repaint();
            },
            Percent = pct =>
            {
                op.TotalBytesDone = (long)(op.TotalBytes * Math.Clamp(pct, 0, 100) / 100.0);
                Repaint();
            },
            Cancelled = () => op.Cancelled,
            WaitWhilePaused = () => WaitWhilePaused(op),
        };
    }

    /// <summary>Back on the UI thread: drop the op, close the overlay, delete the sources if this
    /// was a move, and re-read both panes so the archive listing shows what went in.</summary>
    private void FinishArchiveAdd(TransferOp op, string notice, bool move, int added)
    {
        _op = null;
        _overwriteOpen = false;
        _progressOpen = false;
        _notice = notice;

        // Only the rows that actually landed are moved; a skipped item keeps its original.
        if (move && added > 0)
            for (int i = 0; i < op.Items.Count; i++)
                if (op.Status[i] == 2) FileOps.DeletePermanent(op.Items[i].FullPath, op.Items[i].IsDir);

        Marks(op.Source).Clear();
        ClearMeasuredIn(Dir(op.Source));
        _genL++; _genR++;
        RefreshResultsBothSides();
        MarkDirty();
    }
}
