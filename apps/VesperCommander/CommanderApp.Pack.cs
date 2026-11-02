using System.IO.Compression;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Pack (Alt+F5) ────────────────────────────────────────────────────
    private enum PackFmt { Zip, SevenZip, TarGz }

    private bool _packOpen;
    private char _packSource;
    private List<FileEntry> _packItems = new();
    private string _packName = "";
    private PackFmt _packFmt = PackFmt.Zip;
    private int _packLevel = 2;              // 0 Store · 1 Fast · 2 Normal · 3 Max
    private bool _packMove, _packEachOwn, _packEncrypt;
    private string _packPassword = "";
    private bool _packOther = true;          // destination: other pane (else this pane)
    private bool _packAddToExisting;
    private bool _packAutoRename;            // "one archive per item": rename around collisions
    private bool _packClash;                 // _packError is a collision, so offer the two ways out
    private string? _packError;
    private bool _pendingPackFocus;
    private readonly Ref<TextInputElement> _packNameRef = new();

    private static string PackExt(PackFmt f) => f switch { PackFmt.Zip => ".zip", PackFmt.SevenZip => ".7z", _ => ".tar.gz" };

    private void OpenPack()
    {
        if (AnyOverlayOpen) return;
        char side = _active;
        if (InArchive(side)) { _notice = "Can't pack from inside an archive"; MarkDirty(); return; }
        var items = OperationTargets(side).ToList();
        if (items.Count == 0) return;

        _packSource = side;
        _packItems = items;
        _packFmt = PackFmt.Zip;
        _packName = SeedPackName(items) + ".zip";
        _packLevel = 2;
        _packMove = _packEachOwn = _packEncrypt = false;
        _packPassword = ""; _packOther = true; _packError = null;
        _packAddToExisting = _packAutoRename = _packClash = false;
        _packOpen = true; _pendingPackFocus = true;
        MarkDirty();
    }

    private string SeedPackName(List<FileEntry> items) =>
        items.Count == 1 ? StemOf(items[0].Name)
                         : new System.IO.DirectoryInfo(Dir(_packSource)).Name;

    /// <summary>The name without its archive extension. <c>.tar.gz</c> is two extensions as
    /// far as the BCL is concerned, so it needs saying explicitly or the stem keeps a
    /// <c>.tar</c> that the next call adds a second extension behind.</summary>
    private static string StemOf(string name) =>
        name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            ? name[..^7] : System.IO.Path.GetFileNameWithoutExtension(name);

    private void ClosePack() { _packOpen = false; PaneRef(_packSource).Current?.Focus(); MarkDirty(); }

    private void SetPackFormat(PackFmt fmt)
    {
        // Preserve the stem, swap the extension.
        string stem = StemOf(_packName);
        _packFmt = fmt;
        _packName = stem + PackExt(fmt);
        _packError = null;
        MarkDirty();
    }

    private void HandlePackKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Escape: ClosePack(); e.StopPropagation = true; break;
            case KeyCode.Enter: CommitPack(); e.StopPropagation = true; break;
        }
    }

    private void CommitPack()
    {
        string ext = PackExt(_packFmt);
        string stem = StemOf(_packName);
        _packClash = false;
        if (stem.Length == 0) { _packError = "Enter an archive name"; MarkDirty(); return; }

        // The destination pane must be a folder: PackDestDir would otherwise resolve to the
        // folder a fed-in result set was searched from, which is not what "this pane" says.
        if (InResults(_packOther ? (_packSource == 'L' ? 'R' : 'L') : _packSource))
        {
            _packError = "That pane is showing search results — pick a folder to pack into";
            MarkDirty(); return;
        }
        string destDir = PackDestDir();
        bool needs7z = _packFmt != PackFmt.Zip || _packEncrypt;
        if (needs7z && !_sevenZip)
        {
            _packError = "7z / tar.gz / encryption need 7-Zip — set it in Tools → Archiver";
            MarkDirty(); return;
        }

        var level = _packLevel switch
        {
            0 => CompressionLevel.NoCompression,
            1 => CompressionLevel.Fastest,
            3 => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal,
        };
        string? password = _packEncrypt && _packPassword.Length > 0 ? _packPassword : null;
        bool move = _packMove;
        // Captured here rather than read in the worker, like `move`, so a later dialog
        // interaction can't change it mid-pack.
        bool addToExisting = _packAddToExisting;
        var sources = _packItems.Select(e => (e.FullPath, e.IsDir)).ToList();

        // Build the pack jobs: one archive, or one per item.
        var jobs = new List<(string archivePath, List<(string Source, string Entry)> items)>();
        if (_packEachOwn)
            foreach (var it in _packItems)
                jobs.Add((System.IO.Path.Combine(destDir, StemOf(it.Name) + ext), PackItemsFor([it], needs7z)));
        else
            jobs.Add((System.IO.Path.Combine(destDir, stem + ext), PackItemsFor(_packItems, needs7z)));

        if (ResolveCollisions(jobs, destDir) is { } clash)
        {
            _packError = clash; _packClash = true;
            MarkDirty(); return;
        }

        // One progress op at a time. A copy that was sent to the background is still running
        // with _op set and the overlay hidden, so AnyOverlayOpen doesn't catch it — and pack
        // would take _op out from under it, leaving that transfer with nothing to report to.
        if (_op != null)
        {
            _packError = "A transfer is already running — let it finish first";
            MarkDirty(); return;
        }

        var op = new TransferOp(PackRows(jobs))
        {
            IsPack = true,
            Source = _packSource,
            DestDir = destDir,
            PackTarget = System.IO.Path.GetFileName(jobs[0].archivePath),
            PackWholeRunOnly = needs7z,
        };
        _packOpen = false;
        _op = op;
        _progressOpen = true;
        // Notices only clear on the next keypress, so whatever was on the command line would sit
        // there behind the overlay. The overlay is the report now; the notice comes at the end.
        _notice = null;
        MarkDirty();

        System.Threading.Tasks.Task.Run(() =>
            RunPack(op, jobs, level, password, needs7z, addToExisting, move, sources));
    }

    /// <summary>The queue rows for the progress overlay: every entry of every job, in the order
    /// they will be written, so a job's item index plus its row offset addresses one row.
    /// <para>Sizes are read here rather than left to <see cref="FileOps.EntrySize"/>, which
    /// trusts a file's recorded <c>Size</c> — these rows are synthesised, so nothing has filled
    /// that in yet. Folders keep <c>Size</c> 0 and are measured by walking them, which is what
    /// EntrySize is for.</para></summary>
    private static List<FileEntry> PackRows(
        List<(string archivePath, List<(string Source, string Entry)> items)> jobs)
    {
        var rows = new List<FileEntry>();
        foreach (var (_, items) in jobs)
            foreach (var (source, entry) in items)
            {
                bool isDir = System.IO.Directory.Exists(source);
                long size = 0;
                if (!isDir) { try { size = new System.IO.FileInfo(source).Length; } catch { } }
                rows.Add(new FileEntry(entry, source, isDir, size, DateTime.Now));
            }
        return rows;
    }

    /// <summary>Write every job, reporting into the shared progress overlay as it goes.
    /// Runs off the UI thread; the overlay is repainted through <see cref="Repaint"/> and the
    /// result is marshalled back through <see cref="FinishPack"/>.</summary>
    private void RunPack(TransferOp op,
        List<(string archivePath, List<(string Source, string Entry)> items)> jobs,
        CompressionLevel level, string? password, bool needs7z, bool addToExisting,
        bool move, List<(string FullPath, bool IsDir)> sources)
    {
        for (int i = 0; i < op.Items.Count; i++) op.ItemBytes[i] = FileOps.EntrySize(op.Items[i]);
        op.TotalBytes = op.ItemBytes.Sum();
        op.StartTick = Environment.TickCount64;
        Repaint();

        // The one archive currently being written — the only thing a cancel may remove.
        // Deliberately not a list of everything this run created: "one archive per item" cancelled
        // at the fifth would then also take the four that had already finished, which is data the
        // user watched succeed. Null while appending, because an archive they already had is not
        // ours to throw away even half-modified.
        string? inFlight = null;
        string? err = null;
        int offset = 0;

        foreach (var (archivePath, items) in jobs)
        {
            if (op.Cancelled) break;
            inFlight = addToExisting && FileOps.Exists(archivePath) ? null : archivePath;
            op.PackTarget = System.IO.Path.GetFileName(archivePath);
            Repaint();

            err = ArchiveVfs.Create(items, archivePath, level, password, needs7z, addToExisting,
                PackWatcher(op, offset, items.Count));
            offset += items.Count;
            if (err != null) break;
            inFlight = null;                    // this one landed whole
        }

        bool cancelled = op.Cancelled || err == ArchiveVfs.CancelledError;
        if (cancelled)
        {
            // A part-written archive is not a usable one: it lists and extracts as though it
            // were whole, so leaving it behind is worse than leaving nothing.
            if (inFlight != null) FileOps.DeletePermanent(inFlight, isDir: false);
        }
        else if (err == null && move)
            foreach (var (path, isDir) in sources) FileOps.DeletePermanent(path, isDir);

        string notice =
            cancelled ? "Pack cancelled"
            : err != null ? $"Pack failed: {err}"
            : jobs.Count == 1 ? $"Packed {System.IO.Path.GetFileName(jobs[0].archivePath)}"
            : $"Packed {jobs.Count} archives";
        Application.InvokeOnMainThread(() => FinishPack(notice));
    }

    /// <summary>Wire one job's <see cref="PackProgress"/> to the overlay's op.
    /// <paramref name="offset"/> shifts the job's item indices onto its rows.
    ///
    /// <para>The two byte routes are exclusive by backend, not merged: the built-in writer
    /// streams and reports <c>Bytes</c> deltas, while 7-Zip only ever gives a percentage for
    /// the run — so the percentage is scaled into this job's share of the total and added to
    /// what earlier jobs already accounted for, or the second archive's "10%" would drag the
    /// overall bar backwards.</para></summary>
    private PackProgress PackWatcher(TransferOp op, int offset, int count)
    {
        long jobBase = op.ItemBytes.Take(offset).Sum();
        long jobBytes = op.ItemBytes.Skip(offset).Take(count).Sum();
        return new PackProgress
        {
            EntryStart = (i, length) =>
            {
                int row = offset + i;
                op.Index = row;
                op.Status[row] = 1;                                  // active
                op.FileBytes = length > 0 ? length : op.ItemBytes[row];
                op.FileBytesDone = 0;
                Repaint();
            },
            Bytes = n => { op.FileBytesDone += n; op.TotalBytesDone += n; Repaint(); },
            EntryDone = (i, ok) =>
            {
                int row = offset + i;
                op.Status[row] = (byte)(ok ? 2 : 3);                 // done · missing
                if (ok) op.Done++;
                else
                {
                    // Its bytes were never streamed, so the total bar has to be credited them
                    // here or it can never reach the end.
                    op.Skipped++;
                    op.TotalBytesDone += op.ItemBytes[row];
                }
                Repaint();
            },
            Percent = pct =>
            {
                op.TotalBytesDone = jobBase + (long)(jobBytes * Math.Clamp(pct, 0, 100) / 100.0);
                Repaint();
            },
            Cancelled = () => op.Cancelled,
            WaitWhilePaused = () => WaitWhilePaused(op),
        };
    }

    /// <summary>Back on the UI thread: drop the op, close the overlay, and refresh both panes.
    /// Deliberately not <c>FinishTransfer</c>, which carries copy-only business — the elevated
    /// retry of denied items, and handing over to sync's second batch.</summary>
    private void FinishPack(string notice)
    {
        var op = _op;
        _op = null;
        _progressOpen = false;
        _notice = notice;
        if (op != null)
        {
            Marks(op.Source).Clear();
            ClearMeasuredIn(op.DestDir);        // the new archive changes the folder's total
            ClearMeasuredIn(Dir(op.Source));    // and "move to archive" took the originals out
        }
        _genL++; _genR++;
        RefreshResultsBothSides();
        MarkDirty();
    }

    /// <summary>Apply the overwrite rule to every job. Returns the message to put in front of
    /// the user, or null when the jobs are clear to run — "Auto-rename" having rewritten the
    /// paths that needed it.
    /// <para>"Put each item in its own archive" used to skip this: it wrote straight over any
    /// archive of that name, silently, which is the same overwrite the single-archive path has
    /// always stopped to ask about.</para></summary>
    private string? ResolveCollisions(
        List<(string archivePath, List<(string Source, string Entry)> items)> jobs, string destDir)
    {
        if (_packAddToExisting) return null;   // already answered: merge into whatever is there

        // Names claimed earlier in this same run count as taken alongside what is on disk.
        // Two items called "notes.txt" and "notes.md" both pack to "notes.zip", so the second
        // job would otherwise overwrite the first before anyone could see either.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clashes = new List<string>();

        bool Free(string name) =>
            !FileOps.Exists(System.IO.Path.Combine(destDir, name))
            && !taken.Contains(System.IO.Path.Combine(destDir, name));

        for (int i = 0; i < jobs.Count; i++)
        {
            string name = System.IO.Path.GetFileName(jobs[i].archivePath);
            if (Free(name)) { taken.Add(jobs[i].archivePath); continue; }
            if (!_packAutoRename) { clashes.Add(name); continue; }

            // FileOps.UniqueName only consults the disk, and the disk doesn't yet know about
            // the archives this run is about to write.
            string stem = StemOf(name), ext = name[stem.Length..];
            string renamed = name;
            for (int n = 2; !Free(renamed); n++) renamed = $"{stem} ({n}){ext}";
            var path = System.IO.Path.Combine(destDir, renamed);
            jobs[i] = (path, jobs[i].items);
            taken.Add(path);
        }

        if (clashes.Count == 0) return null;
        return clashes.Count == 1
            ? $"{clashes[0]} already exists in {destDir}"
            : $"{clashes.Count} of these archives already exist in {destDir}";
    }

    /// <summary>Expand the selection into (source, entry-name) pairs. Built-in zip needs
    /// folders flattened with relative entry names; 7-Zip recurses folders itself.</summary>
    private static List<(string Source, string Entry)> PackItemsFor(IReadOnlyList<FileEntry> items, bool sevenZip)
    {
        var result = new List<(string, string)>();
        foreach (var e in items)
        {
            if (sevenZip || !e.IsDir) { result.Add((e.FullPath, e.Name)); continue; }
            var baseDir = System.IO.Path.GetDirectoryName(e.FullPath) ?? "";
            foreach (var f in System.IO.Directory.EnumerateFiles(e.FullPath, "*", System.IO.SearchOption.AllDirectories))
                result.Add((f, System.IO.Path.GetRelativePath(baseDir, f).Replace('\\', '/')));
        }
        return result;
    }

    // ── Pack overlay ─────────────────────────────────────────────────────
    private Element BuildPack()
    {
        long bytes = _packItems.Sum(e => e.IsDir ? 0 : e.Size);
        var card = Card(520, "Pack", $"{_packItems.Count} item{(_packItems.Count == 1 ? "" : "s")} · {FileSystem.ByteLabel(bytes)}", Glyphs.Pack);

        // Archive name.
        var nameRow = PackSection("ARCHIVE NAME");
        var input = Ui.Field(TextInput.New(_packName));
        input.PlaceholderText("archive name");
        input.OnTextChanged(v => { _packName = v; _packError = null; });
        input.WithRef(_packNameRef);
        nameRow.AddChild(input);
        card.AddChild(nameRow);
        if (_packEachOwn)
            card.AddChild(PackHint("One archive per item — names come from each item"));
        if (_packError != null)
            card.AddChild(_packClash ? PackCollision() : PackProblem(_packError));

        // Format.
        var fmt = PackSection("FORMAT");
        fmt.AddChild(Seg(
            ("Zip", _packFmt == PackFmt.Zip, () => SetPackFormat(PackFmt.Zip)),
            ("7z", _packFmt == PackFmt.SevenZip, () => { if (_sevenZip) SetPackFormat(PackFmt.SevenZip); }),
            ("tar.gz", _packFmt == PackFmt.TarGz, () => { if (_sevenZip) SetPackFormat(PackFmt.TarGz); })));
        card.AddChild(fmt);
        if (!_sevenZip)
            card.AddChild(PackHint("7z and tar.gz need 7-Zip — set it in Tools → Archiver"));

        // Level.
        var lvl = PackSection("LEVEL");
        lvl.AddChild(Seg(
            ("Store", _packLevel == 0, () => { _packLevel = 0; MarkDirty(); }),
            ("Fast", _packLevel == 1, () => { _packLevel = 1; MarkDirty(); }),
            ("Normal", _packLevel == 2, () => { _packLevel = 2; MarkDirty(); }),
            ("Max", _packLevel == 3, () => { _packLevel = 3; MarkDirty(); })));
        card.AddChild(lvl);

        // Options.
        var opts = Flex.Column(gap: 4).Padding(new Thickness(6, 14, 8, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        opts.AddChild(FindCheck("Move to archive — delete originals after", _packMove, () => { _packMove = !_packMove; MarkDirty(); }));
        opts.AddChild(FindCheck("Put each item in its own archive", _packEachOwn, () => { _packEachOwn = !_packEachOwn; MarkDirty(); }));
        bool canEncrypt = _sevenZip;
        opts.AddChild(FindCheck("Encrypt with password" + (canEncrypt ? "" : " (needs 7-Zip)"), _packEncrypt && canEncrypt,
            () => { if (canEncrypt) { _packEncrypt = !_packEncrypt; MarkDirty(); } }));
        if (_packEncrypt && canEncrypt)
        {
            var pwRow = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Padding(new Thickness(2, 22, 2, 0));
            var pw = Ui.Field(TextInput.New(_packPassword));
            pw.Masked = true;
            pw.PlaceholderText("password");
            pw.OnTextChanged(v => _packPassword = v);
            pwRow.AddChild(pw);
            opts.AddChild(pwRow);
            opts.AddChild(Ui.Mono("AES-256 · names stay visible · never stored or logged", 10f, Nocturne.On(0.40)));
        }
        card.AddChild(opts);

        // Destination.
        var dst = PackSection("DESTINATION");
        dst.AddChild(Seg(
            ("This pane", !_packOther, () => { _packOther = false; _packError = null; MarkDirty(); }),
            ("Other pane", _packOther, () => { _packOther = true; _packError = null; MarkDirty(); })));
        dst.AddChild(Ui.Mono(PackDestDir(), 10.5f, Nocturne.On(0.45))
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        card.AddChild(dst);

        // Footer.
        var footer = Flex.Row(gap: 10).AlignItems(AlignItems.Center).Padding(new Thickness(8, 14, 8, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0))
            .CornerRadius(new CornerRadius(0, 0, Nocturne.RadiusLg, Nocturne.RadiusLg));
        footer.AddChild(Ui.Kbd("↵")); footer.AddChild(Ui.Mono("pack · Esc cancel", 10.5f, Nocturne.On(0.45)));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(SecondaryButton("Cancel", ClosePack));
        footer.AddChild(PrimaryButton($"Pack {_packItems.Count} item{(_packItems.Count == 1 ? "" : "s")}", CommitPack));
        card.AddChild(footer);

        return Backdrop(card, "Pack", topAligned: true, topPad: 70f);
    }

    private static Element PackSection(string label)
    {
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 14, 8, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        row.AddChild(Ui.Mono(label, 9.5f, Nocturne.On(0.40)).Width(90).FlexShrink(0));
        return row;
    }

    private static Element PackHint(string text) =>
        Box.New().Padding(new Thickness(0, 14, 6, 14))
            .Children(Ui.Mono(text, 10f, Nocturne.On(0.42)));

    /// <summary>A problem that has no way out but changing something — an empty name, a
    /// format with no 7-Zip behind it. Said in red, without the collision dialog's buttons:
    /// "Add to it" answers a question this message never asked.</summary>
    private static Element PackProblem(string message) =>
        Box.New().Padding(new Thickness(2, 14, 8, 14))
            .Children(Ui.Mono(message, 10.5f, Nocturne.MarkRed));

    private Element PackCollision()
    {
        var box = Flex.Column(gap: 6).Padding(new Thickness(2, 14, 8, 14));
        box.AddChild(Ui.Mono(_packError!, 10.5f, Nocturne.MarkRed));
        var actions = Flex.Row(gap: 8);

        // One archive per item has no single name to rewrite, so both ways out re-run the
        // pack with the answer set. The single-archive prompt keeps putting the new name in
        // the field instead, where it can be read and edited before committing to it.
        if (_packEachOwn)
        {
            actions.AddChild(ClashButton("pack-add-existing", "Add to them",
                () => { _packAddToExisting = true; _packError = null; CommitPack(); }));
            actions.AddChild(ClashButton("pack-auto-rename", "Auto-rename",
                () => { _packAutoRename = true; _packError = null; CommitPack(); }));
        }
        else
        {
            actions.AddChild(ClashButton("pack-add-existing", "Add to it",
                () => { _packAddToExisting = true; _packError = null; CommitPack(); }));
            actions.AddChild(ClashButton("pack-auto-rename", "Auto-rename", () =>
            {
                _packName = FileOps.UniqueName(PackDestDir(), StemOf(_packName) + PackExt(_packFmt));
                _packError = null; MarkDirty();
            }));
        }
        box.AddChild(actions);
        return box;
    }

    /// <summary>A way out of a collision, named so assistive tech (and the tests) can reach
    /// it — the two are the only controls in this dialog that decide what happens to a file
    /// that is already on disk.</summary>
    private static Element ClashButton(string id, string label, Action onClick)
    {
        var b = GhostButton(label, onClick);
        b.AutomationId = id;
        b.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        b.AccessibleName = label;
        return b;
    }

    private string PackDestDir() => _packOther ? Dir(_packSource == 'L' ? 'R' : 'L') : Dir(_packSource);
}
