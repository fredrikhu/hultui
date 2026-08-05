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
        _packPassword = ""; _packOther = true; _packAddToExisting = false; _packError = null;
        _packOpen = true; _pendingPackFocus = true;
        MarkDirty();
    }

    private string SeedPackName(List<FileEntry> items) =>
        items.Count == 1 ? System.IO.Path.GetFileNameWithoutExtension(items[0].Name)
                         : new System.IO.DirectoryInfo(Dir(_packSource)).Name;

    private void ClosePack() { _packOpen = false; PaneRef(_packSource).Current?.Focus(); MarkDirty(); }

    private void SetPackFormat(PackFmt fmt)
    {
        // Preserve the stem, swap the extension.
        string stem = _packName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            ? _packName[..^7] : System.IO.Path.GetFileNameWithoutExtension(_packName);
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
        string stem = _packName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            ? _packName[..^7] : System.IO.Path.GetFileNameWithoutExtension(_packName);
        if (stem.Length == 0) { _packError = "Enter an archive name"; MarkDirty(); return; }

        char other = _packSource == 'L' ? 'R' : 'L';
        string destDir = _packOther ? Dir(other) : Dir(_packSource);
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
        var sources = _packItems.Select(e => (e.FullPath, e.IsDir)).ToList();

        // Build the pack jobs: one archive, or one per item.
        var jobs = new List<(string archivePath, List<(string Source, string Entry)> items)>();
        if (_packEachOwn)
        {
            foreach (var it in _packItems)
            {
                string n = System.IO.Path.GetFileNameWithoutExtension(it.Name) + ext;
                jobs.Add((System.IO.Path.Combine(destDir, n), PackItemsFor([it], needs7z)));
            }
        }
        else
        {
            string archivePath = System.IO.Path.Combine(destDir, stem + ext);
            if (System.IO.File.Exists(archivePath) && !_packAddToExisting)
            {
                _packError = $"{stem + ext} already exists in {destDir}";
                MarkDirty(); return;
            }
            jobs.Add((archivePath, PackItemsFor(_packItems, needs7z)));
        }

        _packOpen = false;
        _notice = jobs.Count == 1 ? "Packing…" : $"Packing {jobs.Count} archives…";
        MarkDirty();

        System.Threading.Tasks.Task.Run(() =>
        {
            string? err = null;
            foreach (var (archivePath, items) in jobs)
            {
                err = ArchiveVfs.Create(items, archivePath, level, password, needs7z);
                if (err != null) break;
            }
            if (err == null && move)
                foreach (var (p, isDir) in sources) FileOps.DeletePermanent(p, isDir);

            Application.InvokeOnMainThread(() =>
            {
                _notice = err == null
                    ? (jobs.Count == 1 ? $"Packed {System.IO.Path.GetFileName(jobs[0].archivePath)}" : $"Packed {jobs.Count} archives")
                    : $"Pack failed: {err}";
                _genL++; _genR++;
                MarkDirty();
            });
        });
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
        var card = Card(520, "Pack", $"{_packItems.Count} item{(_packItems.Count == 1 ? "" : "s")} · {FileSystem.ByteLabel(bytes)}");

        // Archive name.
        var nameRow = PackSection("ARCHIVE NAME");
        var input = TextInput.New(_packName).FlexGrow(1).Height(30);
        input.PlaceholderText("archive name");
        input.OnTextChanged(v => { _packName = v; _packError = null; });
        input.WithRef(_packNameRef);
        nameRow.AddChild(input);
        card.AddChild(nameRow);
        if (_packEachOwn)
            card.AddChild(PackHint("One archive per item — names come from each item"));
        if (_packError != null)
            card.AddChild(PackCollision());

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
            var pw = TextInput.New(_packPassword).FlexGrow(1).Height(28);
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
        dst.AddChild(Ui.Mono(_packOther ? Dir(_packSource == 'L' ? 'R' : 'L') : Dir(_packSource), 10.5f, Nocturne.On(0.45))
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        card.AddChild(dst);

        // Footer.
        var footer = Flex.Row(gap: 10).AlignItems(AlignItems.Center).Padding(new Thickness(8, 14, 8, 14));
        footer.AddChild(Ui.Kbd("↵")); footer.AddChild(Ui.Mono("pack · Esc cancel", 10.5f, Nocturne.On(0.45)));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(GhostButton("Cancel", ClosePack));
        footer.AddChild(PrimaryButton($"Pack {_packItems.Count} item{(_packItems.Count == 1 ? "" : "s")}", CommitPack));
        card.AddChild(footer);

        return Backdrop(card, topAligned: true, topPad: 70f);
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

    private Element PackCollision()
    {
        var box = Flex.Column(gap: 6).Padding(new Thickness(2, 14, 8, 14));
        box.AddChild(Ui.Mono(_packError!, 10.5f, Nocturne.MarkRed));
        var actions = Flex.Row(gap: 8);
        actions.AddChild(GhostButton("Add to it", () => { _packAddToExisting = true; _packError = null; CommitPack(); }));
        actions.AddChild(GhostButton("Auto-rename", () =>
        {
            string ext = PackExt(_packFmt);
            string stem = System.IO.Path.GetFileNameWithoutExtension(_packName);
            char other = _packSource == 'L' ? 'R' : 'L';
            string destDir = _packOther ? Dir(other) : Dir(_packSource);
            _packName = FileOps.UniqueName(destDir, stem + ext);
            _packError = null; MarkDirty();
        }));
        box.AddChild(actions);
        return box;
    }
}
