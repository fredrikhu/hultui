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

        var list = Rows(side);
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

    // ── Password prompt (Card 420) ───────────────────────────────────────
    // The value lives only in memory for the session (cleared on cancel), is never
    // stored or logged, and the field masks it. One prompt per archive per session.
    private bool _pwOpen;
    private string _pwArchive = "";
    private string _pwValue = "";
    private bool _pwWrong;
    private bool _pendingPwFocus;
    private readonly Ref<TextInputElement> _pwRef = new();

    private void OpenPasswordPrompt(string archiveFile)
    {
        _pwArchive = archiveFile; _pwValue = ""; _pwWrong = false;
        _pwOpen = true; _pendingPwFocus = true; MarkDirty();
    }

    private void ClosePasswordPrompt() { _pwOpen = false; _pwValue = ""; MarkDirty(); }

    private void SubmitPassword()
    {
        if (_pwValue.Length == 0) return;
        var test = ArchiveVfs.ListEntries(_pwArchive, "", _pwValue, _sevenZip);
        if (test.Status == ArchiveStatus.Encrypted) { _pwWrong = true; _pwValue = ""; MarkDirty(); return; }
        _archivePasswords[_pwArchive] = _pwValue;   // in-memory only
        _pwOpen = false; _pwValue = "";
        _genL++; _genR++;                            // re-list with the cached password
        MarkDirty();
    }

    private void HandlePasswordKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Escape: ClosePasswordPrompt(); e.StopPropagation = true; break;
            case KeyCode.Enter: SubmitPassword(); e.StopPropagation = true; break;
        }
    }

    private Element BuildPasswordPrompt()
    {
        var card = Card(420, "Password required", System.IO.Path.GetFileName(_pwArchive), "⚿");
        var body = Flex.Column(gap: 8).Padding(new Thickness(10, 14, 10, 14));

        var fieldRow = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        fieldRow.AddChild(Ui.Icon("⚿", 13f, _pwWrong ? Nocturne.MarkRed : ArchiveAmber));
        var input = TextInput.New(_pwValue).FlexGrow(1).Height(30);
        input.Masked = true;
        input.PlaceholderText("password");
        input.OnTextChanged(v => { _pwValue = v; _pwWrong = false; });
        input.WithRef(_pwRef);
        fieldRow.AddChild(input);
        body.AddChild(fieldRow);

        body.AddChild(_pwWrong
            ? Ui.Mono("Wrong password — try again", 10.5f, Nocturne.MarkRed)
            : Ui.Mono("Never stored or logged · asked once per archive this session", 10f, Nocturne.On(0.40)));
        card.AddChild(body);

        var footer = Flex.Row(gap: 10).AlignItems(AlignItems.Center).Padding(new Thickness(8, 14, 8, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0))
            .CornerRadius(new CornerRadius(0, 0, Nocturne.RadiusLg, Nocturne.RadiusLg));
        footer.AddChild(Ui.Kbd("↵")); footer.AddChild(Ui.Mono("unlock · Esc", 10.5f, Nocturne.On(0.45)));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(SecondaryButton("Cancel", ClosePasswordPrompt));
        footer.AddChild(PrimaryButton("Unlock", SubmitPassword));
        card.AddChild(footer);
        return Backdrop(card, topAligned: true, topPad: 90f);
    }

    // ── Archive error state (in-pane, replaces the listing) ──────────────
    private ArchiveStatus? ArchiveStatusOf(char side) => Loader(side).Value.Archive;

    private Element BuildArchiveError(char side, ArchiveStatus status)
    {
        var wrap = Flex.Column(gap: 0).FlexGrow(1).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        var block = Flex.Column(gap: 8).AlignItems(AlignItems.Center);
        string archive = ArchivePath.TryParse(Dir(side)) is { } ap ? System.IO.Path.GetFileName(ap.ArchiveFile) : "archive";

        switch (status)
        {
            case ArchiveStatus.Encrypted:
                block.AddChild(Ui.Icon("⚿", 24f, ArchiveAmber));
                block.AddChild(Text.New("This archive is encrypted").FontSize(13f).TextColor(Nocturne.Text));
                block.AddChild(Ui.Mono(archive, 10.5f, Nocturne.On(0.45)));
                var unlock = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(5, 12, 5, 12))
                    .CornerRadius(Nocturne.RadiusSm).BorderColor(Nocturne.Accent700).BorderWidth(1f)
                    .Children(Text.New("Unlock…").FontSize(11.5f).TextColor(Nocturne.Accent200));
                string arcFile = ArchivePath.TryParse(Dir(side))!.Value.ArchiveFile;
                unlock.OnClick(_ => OpenPasswordPrompt(arcFile));
                block.AddChild(unlock.HoverBg(Nocturne.Neutral900, Color.Transparent));
                break;
            case ArchiveStatus.NeedsSevenZip:
                block.AddChild(Icons.Draw(IconKind.Archive, ArchiveAmber));
                block.AddChild(Text.New("Opening this archive needs 7-Zip").FontSize(13f).TextColor(Nocturne.Text));
                block.AddChild(Ui.Mono("Set the path in Tools → Archiver, or install 7-Zip", 10.5f, Nocturne.On(0.45)));
                break;
            default: // Corrupt / NotFound
                block.AddChild(Ui.Icon("⚠", 22f, Nocturne.MarkRed));
                block.AddChild(Text.New("Can't open this archive — it may be corrupt").FontSize(13f).TextColor(Nocturne.Text));
                block.AddChild(Ui.Mono(archive, 10.5f, Nocturne.On(0.45)));
                break;
        }
        block.AddChild(Ui.Mono("Backspace to go back", 10f, Nocturne.On(0.35)));
        wrap.AddChild(block);
        return wrap;
    }
}
