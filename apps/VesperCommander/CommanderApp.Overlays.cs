using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Overlay chrome ───────────────────────────────────────────────────
    // A full-shell dim backdrop (click-to-dismiss) hosting a centred Nocturne card
    // that stops click propagation. `topAligned` drops the card near the top (drive
    // picker / palette) instead of centring it.
    private Element Backdrop(Element card, bool topAligned, float topPad = 70f)
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(Nocturne.Alpha(Color.FromHex("#05060c"), 0.62))
            .JustifyContent(topAligned ? JustifyContent.FlexStart : JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .ZIndex(50);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        if (topAligned) backdrop.Padding(new Thickness(topPad, 0, 0, 0));
        backdrop.On<ClickEvent>(ev => { ev.StopPropagation = true; CloseOverlays(); });

        card.On<ClickEvent>(ev => ev.StopPropagation = true);
        backdrop.AddChild(card);
        return backdrop;
    }

    /// <summary>A Nocturne card shell: the shared <see cref="DialogCard"/> frame plus a
    /// header. With a <paramref name="icon"/> it's Junction's accent app-mark tile +
    /// stacked title/subtitle + bordered close (the family look for titled dialogs);
    /// without one it falls back to the compact title-row header.</summary>
    private BoxElement Card(float width, string title, string? subtitle, string? icon = null)
    {
        var card = DialogCard(width);

        var header = Flex.Row(gap: icon != null ? 12 : 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(icon != null ? 12 : 10, 14, icon != null ? 12 : 10, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        if (icon != null)
        {
            header.AddChild(Ui.HeaderTile(icon));
            var titleCol = Flex.Column(gap: 2).FlexShrink(1).FlexGrow(1);
            titleCol.AddChild(Text.New(title).FontSize(15f).FontWeight(FontWeight.Medium)
                .TextColor(Nocturne.Text).MaxLines(1));
            if (subtitle != null)
                titleCol.AddChild(Ui.Mono(subtitle, 11f, Nocturne.On(0.5))
                    .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
            header.AddChild(titleCol);
            header.AddChild(Ui.IconButton(Glyphs.Close, CloseOverlays, "Close", 28f));
        }
        else
        {
            // Title never shrinks/wraps; a long subtitle (e.g. a path) clips with an ellipsis
            // rather than pushing the title onto a second line.
            header.AddChild(Text.New(title).FontSize(13f).FontWeight(FontWeight.Medium)
                .TextColor(Nocturne.Text).FlexShrink(0).MaxLines(1));
            if (subtitle != null)
                header.AddChild(Ui.Mono(subtitle, 11f, Nocturne.On(0.45))
                    .FlexShrink(1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
            header.AddChild(Box.New().FlexGrow(1));
            var close = Box.New().Cursor(CursorType.Pointer)
                .Padding(new Thickness(1, 5, 1, 5)).CornerRadius(4f)
                .Children(Ui.Icon(Glyphs.Close, 12f, Nocturne.Neutral600));
            close.OnClick(_ => CloseOverlays());
            header.AddChild(close.HoverBg(Nocturne.Neutral900, Color.Transparent));
        }
        card.AddChild(header);
        return card;
    }

    // ── Drive picker (Alt+F1 / Alt+F2 or the breadcrumb drive crumb) ─────
    private Element BuildDrivePicker(char side)
    {
        var card = Card(420, "Select drive", side == 'L' ? "left pane" : "right pane", Glyphs.Drives);

        var listBox = Flex.Column(gap: 0).Padding(new Thickness(6, 6, 6, 6));
        for (int i = 0; i < _drives.Count; i++)
            listBox.AddChild(DriveRow(side, _drives[i], i == _driveCur));
        card.AddChild(listBox);

        return Backdrop(card, topAligned: true);
    }

    private Element DriveRow(char side, DriveEntry d, bool cursor)
    {
        var row = Grid.Of("28px 1fr auto", gap: 10).Height(34)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 10, 0, 10)).CornerRadius(Nocturne.RadiusSm)
            .Cursor(d.Ready ? CursorType.Pointer : CursorType.Default);
        if (cursor)
            row.Background(Nocturne.Alpha(Nocturne.Accent500, 0.13))
               .BorderColor(Nocturne.Accent600).BorderWidth(1f);

        Color letterColor = d.Ready ? Nocturne.Accent300 : Nocturne.On(0.30);
        Color textColor = d.Ready ? Nocturne.Text : Nocturne.On(0.35);

        // Drive letter (e.g. "C:").
        var letter = d.Root.TrimEnd(System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar);
        row.AddChild(Ui.Mono(letter, 12f, letterColor).GridCell(0, 0));
        row.AddChild(Text.New(d.Label).FontSize(12f).TextColor(textColor)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridCell(0, 1));

        string detail = d.Ready
            ? $"{FileSystem.ByteLabel(d.Free)} free of {FileSystem.ByteLabel(d.Total)} · {d.Format}"
            : "not available";
        row.AddChild(Ui.Mono(detail, 10.5f, d.Ready ? Nocturne.On(0.45) : Nocturne.On(0.30)).GridCell(0, 2));

        if (d.Ready)
            row.OnClick(_ => SelectDrive(side, IndexOfDriveEntry(d)));
        return row;
    }

    private int IndexOfDriveEntry(DriveEntry d)
    {
        for (int i = 0; i < _drives.Count; i++) if (ReferenceEquals(_drives[i], d)) return i;
        return -1;
    }

    // ── Buttons (footer / detail actions) ────────────────────────────────
    // Thin adapters over the shared Ui.* button family so every call site here and
    // across the app renders one consistent, Junction-matching button set.
    private static ButtonElement GhostButton(string label, Action onClick, bool enabled = true)
        => Ui.GhostButton(label, null, onClick, enabled);

    private static ButtonElement SecondaryButton(string label, Action onClick)
        => Ui.SecondaryButton(label, null, onClick);

    private static ButtonElement PrimaryButton(string label, Action onClick)
        => Ui.PrimaryButton(label, null, onClick);

    private static ButtonElement DangerButton(string label, Action onClick)
        => Ui.DangerButton(label, null, onClick);

    // The single dialog frame: a raised Surface panel with a hairline Neutral700
    // border and a large radius — Junction's modal shell, so both apps read as one
    // family. (Was a darker Bg card + drop shadow; the raised Surface no longer
    // needs the shadow to lift off the dim backdrop.)
    private BoxElement DialogCard(float width)
    {
        return Flex.Column(gap: 0).Width(width)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg)
            .Overflow(Overflow.Hidden);
    }

    // Transparent footer (same Surface as the card) separated by a top hairline —
    // Junction's footer idiom. The bottom radius matches the card so nothing squares
    // off the rounded corners.
    private BoxElement DialogFooter()
    {
        return Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(11, 14, 11, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0))
            .CornerRadius(new CornerRadius(0, 0, Nocturne.RadiusLg, Nocturne.RadiusLg));
    }

    // ── Confirm dialog (delete, and copy/move go-ahead) ──────────────────
    private Element BuildConfirm(ConfirmSpec spec)
    {
        var card = DialogCard(440);

        var body = Flex.Column(gap: 6).Padding(new Thickness(16, 18, 14, 18));
        body.AddChild(Text.New(spec.Title).FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Nocturne.Text));
        foreach (var line in spec.Message.Split('\n'))
        {
            bool pathish = line.Contains('/') || line.Contains('\\');
            body.AddChild(pathish
                ? Ui.Mono(line, 11.5f, Nocturne.On(0.70)).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)
                : Text.New(line).FontSize(12f).TextColor(Nocturne.On(0.70)));
        }
        card.AddChild(body);

        var footer = DialogFooter();
        footer.AddChild(Box.New().FlexGrow(1));
        if (spec.CancelLabel is { } cancel) footer.AddChild(SecondaryButton(cancel, CancelConfirm));
        footer.AddChild(spec.Danger
            ? DangerButton(spec.ConfirmLabel, spec.OnConfirm)
            : PrimaryButton(spec.ConfirmLabel, spec.OnConfirm));
        card.AddChild(footer);

        return Backdrop(card, topAligned: false);
    }

    // ── Name prompt (F7 new folder · Shift+F6 rename) ────────────────────
    private Element BuildNamePrompt(NamePromptSpec spec)
    {
        var card = DialogCard(460);

        var head = Flex.Column(gap: 4).Padding(new Thickness(14, 16, 8, 16));
        head.AddChild(Text.New(spec.Title).FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Nocturne.Text));
        head.AddChild(Ui.Mono(spec.Subtitle, 11f, Nocturne.On(0.45)).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        card.AddChild(head);

        // A bordered field on the app ground (darker than the Surface card, so it reads),
        // outlined in the accent because it is the one thing in the dialog to type into.
        // Explicit height: a bare flex child here can lay out with a collapsed content box
        // and paint nothing; the wrapper's 3px and the theme's 6px of inset become 36.
        var input = TextInput.New(_namePromptValue)
            .Style(Ui.Bordered(Nocturne.Accent700, radius: Nocturne.RadiusSm, sidePadding: 6f))
            .Margin(new Thickness(0, 16, 14, 16))
            .Height(Ui.FieldHeight + 6f);
        input.PlaceholderText(spec.Placeholder);
        // Clear a stale error as soon as the user edits; keep the value in sync so
        // Enter (handled globally) commits the current text.
        input.OnTextChanged(v =>
        {
            _namePromptValue = v;
            if (_namePromptError != null) { _namePromptError = null; MarkDirty(); }
        });
        input.WithRef(_namePromptRef);
        card.AddChild(input);

        if (_namePromptError != null)
            card.AddChild(Text.New(_namePromptError).FontSize(11.5f).TextColor(Nocturne.MarkRed)
                .Margin(new Thickness(0, 16, 14, 0)));

        var footer = DialogFooter();
        // While a typed remote path is being resolved the dialog stays up and says so: the
        // confirm button goes inert behind a spinner, and Cancel (or Esc) abandons the probe.
        bool busy = _gotoProbe != null;
        if (busy)
        {
            footer.AddChild(Spinner.New(12f));
            footer.AddChild(Ui.Mono("Checking…", 11f, Nocturne.On(0.45)));
        }
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(SecondaryButton("Cancel", CloseNamePrompt));
        footer.AddChild(busy
            ? Ui.PrimaryButton(spec.ConfirmLabel, null, () => { }, enabled: false)
            : PrimaryButton(spec.ConfirmLabel, CommitNamePrompt));
        card.AddChild(footer);

        return Backdrop(card, topAligned: false);
    }

    // ── Overwrite conflict dialog ────────────────────────────────────────
    private Element BuildOverwrite(TransferOp op)
    {
        var e = op.Items[op.Index];
        long tSize = 0; DateTime tDate = default;
        // An archive member has nothing on disk to stat, so what it would replace was recorded
        // when the conflict was found. Only a real destination is measured here.
        if (op.ArchiveTargets is { } targets && targets.TryGetValue(op.Index, out var member))
            (tSize, tDate) = member;
        else
        {
            var destPath = System.IO.Path.Combine(op.DestDir, e.Name);
            try
            {
                if (File.Exists(destPath)) { var fi = new FileInfo(destPath); tSize = fi.Length; tDate = fi.LastWriteTime; }
                else if (Directory.Exists(destPath)) tDate = new DirectoryInfo(destPath).LastWriteTime;
            }
            catch { /* target stat failed — show what we have */ }
        }

        var card = DialogCard(440);

        var head = Flex.Column(gap: 4).Padding(new Thickness(14, 16, 4, 16));
        head.AddChild(Text.New(op.ArchiveTargets != null ? "Already in the archive" : "File already exists")
            .FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Nocturne.Text));
        var line = Flex.Row(gap: 5).AlignItems(AlignItems.Center);
        line.AddChild(Ui.Mono(e.Name, 12f, Nocturne.Text).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        line.AddChild(Text.New(op.ArchiveTargets != null ? "is already in this archive" : "already exists in the target")
            .FontSize(12f).TextColor(Nocturne.On(0.60)));
        head.AddChild(line);
        card.AddChild(head);

        // Two-column Source / Target compare.
        var cmp = Grid.Of("1fr 1fr")
            .Padding(new Thickness(10, 16, 10, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        cmp.AddChild(CompareCell("Source", FileSystem.SizeLabel(e), Iso(e.Modified), rightBorder: true).GridCell(0, 0));
        cmp.AddChild(CompareCell("Target", e.IsDir ? "Directory" : FileSystem.ByteLabel(tSize), Iso(tDate), rightBorder: false).GridCell(0, 1));
        card.AddChild(cmp);

        var footer = DialogFooter();
        footer.AddChild(GhostButton("Skip", () => OverwriteDecision(false, false)));
        footer.AddChild(GhostButton("Skip all", () => OverwriteDecision(false, true)));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(GhostButton("Overwrite", () => OverwriteDecision(true, false)));
        footer.AddChild(PrimaryButton("Overwrite all", () => OverwriteDecision(true, true)));
        card.AddChild(footer);

        return Backdrop(card, topAligned: false);
    }

    private static Element CompareCell(string label, string size, string date, bool rightBorder)
    {
        var col = Flex.Column(gap: 4).Padding(new Thickness(0, 12, 0, 12));
        if (rightBorder)
            col.BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        col.AddChild(Text.New(label.ToUpperInvariant()).FontSize(10f)
            .LetterSpacing(0.08f * 10f).TextColor(Nocturne.Neutral600));
        col.AddChild(Ui.Mono(size, 11f, Nocturne.Text));
        col.AddChild(Ui.Mono(date, 11f, Nocturne.On(0.45)));
        return col;
    }

    private static string Iso(DateTime d) =>
        d == default ? "—" : d.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    // ── Copy-progress overlay (F5) ───────────────────────────────────────
    private static readonly Color DoneGreen = Color.FromHex("#7fbf79");

    /// <summary>How many queue rows the progress overlay shows at once. Rows are 22px; the
    /// clipped box is sized from this so the window and the height can't drift apart.</summary>
    private const int QueueWindow = 9;

    private Element BuildProgress(TransferOp op)
    {
        int processed = op.Done + op.Skipped + op.Failed;
        string subtitle = op.IsPack && op.PackTarget.Length > 0
            ? $"{processed} of {op.Items.Count} · {op.PackTarget}"
            : $"{processed} of {op.Items.Count}";
        var card = Card(560,
            op.IsPack ? op.PackVerb : op.IsMove ? "Moving" : "Copying", subtitle,
            op.IsPack ? Glyphs.Pack : op.IsMove ? Glyphs.Swap : Glyphs.Copy);

        var body = Flex.Column(gap: 12).Padding(new Thickness(12, 16, 12, 16));

        // Current-file and total progress bars.
        var curName = op.Index >= 0 && op.Index < op.Items.Count ? op.Items[op.Index].Name : "";
        double fileFrac = op.FileBytes > 0 ? (double)op.FileBytesDone / op.FileBytes : 0;
        double totalFrac = op.TotalBytes > 0 ? (double)op.TotalBytesDone / op.TotalBytes : 0;
        if (!op.PackWholeRunOnly)
            body.AddChild(TransferBar(op.Paused ? "Paused" : "File", curName, fileFrac, Nocturne.Accent500));
        body.AddChild(TransferBar(op.PackWholeRunOnly && op.Paused ? "Paused" : "Total",
            $"{FileSystem.ByteLabel(op.TotalBytesDone)} of {FileSystem.ByteLabel(op.TotalBytes)}{TransferRate(op)}",
            totalFrac, Nocturne.Accent700));

        // Queue: one row per item with its status, in a window that follows the active item.
        // The box is clipped to a fixed height, so laying every row out from the first meant a
        // batch longer than the window showed its opening rows for the whole run and never the
        // item actually being worked on — which is the one thing the queue is there to say.
        // The card's subtitle carries the "72 of 400", so the window needs no count of its own.
        var queue = Flex.Column(gap: 0).MaxHeight(QueueWindow * 22).Overflow(Overflow.Hidden)
            .Margin(new Thickness(2, 0, 0, 0));
        int first = Math.Clamp(op.Index - QueueWindow / 2, 0, Math.Max(0, op.Items.Count - QueueWindow));
        int last = Math.Min(op.Items.Count, first + QueueWindow);
        for (int i = first; i < last; i++) queue.AddChild(ProgressRow(op, i));
        body.AddChild(queue);
        card.AddChild(body);

        var footer = DialogFooter();
        footer.AddChild(GhostButton(op.Paused ? "Resume  P" : "Pause  P", ToggleTransferPause));
        // No per-entry skip while packing: a member abandoned half-written would still be in
        // the archive, listing and extracting as though it were whole. See PackProgress.
        if (!op.IsPack) footer.AddChild(GhostButton("Skip file  S", SkipCurrentFile));
        footer.AddChild(GhostButton("Background  B", BackgroundTransfer));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(SecondaryButton("Cancel", CancelTransfer));
        card.AddChild(footer);

        return Backdrop(card, topAligned: false);
    }

    /// <summary>" · 42 MB/s · 8s left" once there's enough signal, else "".</summary>
    /// <param name="op">The transfer.</param>
    /// <param name="now">The current <see cref="Environment.TickCount64"/>; null reads it. A test
    /// passes the one it set the transfer up with, or the clock moving between its read and this
    /// one turns "1000 B/s" into "999 B/s" (seen on the Linux runner).</param>
    private static string TransferRate(TransferOp op, long? now = null)
    {
        if (op.Paused) return " · paused";
        // Time spent paused is not time spent transferring: leaving it in would make a run that
        // was held for a while report a rate and an ETA it never had.
        long elapsedMs = (now ?? Environment.TickCount64) - op.StartTick - op.PausedTotal;
        if (elapsedMs < 400 || op.TotalBytesDone <= 0) return "";
        double bps = op.TotalBytesDone / (elapsedMs / 1000.0);
        if (bps < 1) return "";
        long remaining = Math.Max(0, op.TotalBytes - op.TotalBytesDone);
        int etaSec = (int)Math.Ceiling(remaining / bps);
        string eta = etaSec >= 60 ? $"{etaSec / 60}m {etaSec % 60}s" : $"{etaSec}s";
        return $" · {FileSystem.ByteLabel((long)bps)}/s · {eta} left";
    }

    private static Element TransferBar(string label, string sub, double frac, Color fill)
    {
        frac = Math.Clamp(frac, 0, 1);
        var col = Flex.Column(gap: 5);
        var top = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        top.AddChild(Text.New(label.ToUpperInvariant()).FontSize(10f)
            .LetterSpacing(0.08f * 10f).TextColor(Nocturne.Neutral600).FlexShrink(0));
        top.AddChild(Ui.Mono(sub, 11f, Nocturne.On(0.60)).FlexGrow(1)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        top.AddChild(Ui.Mono($"{(int)(frac * 100)}%", 10.5f, Nocturne.On(0.60)).FlexShrink(0));
        col.AddChild(top);

        // Two bars in one panel, coloured apart: the fill is set on the bar itself, which
        // wins over the theme's.
        var bar = ProgressBar.New(frac);
        bar.FillColor = fill;
        col.AddChild(bar);
        return col;
    }

    private Element ProgressRow(TransferOp op, int i)
    {
        var e = op.Items[i];
        byte st = op.Status[i];
        (string icon, Color color, string text) = st switch
        {
            1 => (Glyphs.Play, Nocturne.Accent, op.IsPack ? op.PackRowVerb : "copying"),
            2 => (Glyphs.Check, DoneGreen, "done"),
            3 => (Glyphs.Minus, Nocturne.On(0.35), op.IsPack ? "missing" : "skipped"),
            4 => (Glyphs.Close, Nocturne.MarkRed, "failed"),
            _ => (Glyphs.Dot, Nocturne.On(0.35), "queued"),
        };
        var row = Grid.Of("14px 1fr 64px 96px", gap: 10).Height(22)
            .AlignItems(AlignItems.Center).Padding(new Thickness(0, 10, 0, 10));
        // JustifySelf rather than TextAlign: the status mark is artwork now, and an image
        // fills the box it is given instead of centring text inside a wider one.
        row.AddChild(Ui.Icon(icon, 10f, color).JustifySelf(JustifySelf.Center).GridCell(0, 0));
        row.AddChild(Ui.Mono(e.Name, 11f, st == 1 ? Nocturne.Text : Nocturne.On(0.70))
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridCell(0, 1));
        row.AddChild(Ui.Mono(FileSystem.ByteLabel(op.ItemBytes[i]), 10.5f, Nocturne.On(0.45))
            .TextAlign(TextAlign.Right).GridCell(0, 2));
        row.AddChild(Ui.Mono(text, 10.5f, color).GridCell(0, 3));
        return row;
    }

    // ── Bookmark manager (Ctrl+D / rail +) ───────────────────────────────
    private Element BuildBookmarks()
    {
        if (_bookmarkSel >= _bookmarks.Count) _bookmarkSel = Math.Max(0, _bookmarks.Count - 1);
        var card = Card(900, "Bookmarks", "Hotkeys jump the active pane", Glyphs.Bookmarks);

        var body = Grid.Of("260px 1fr").MinHeight(240);

        // Left: the bookmark list.
        var listBox = Flex.Column(gap: 0).Padding(new Thickness(6, 6, 6, 6))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0))
            .GridCell(0, 0);
        for (int i = 0; i < _bookmarks.Count; i++)
            listBox.AddChild(BookmarkRow(i, i == _bookmarkSel));
        if (_bookmarks.Count == 0)
            listBox.AddChild(Text.New("No bookmarks yet").FontSize(12f)
                .TextColor(Nocturne.On(0.40)).Padding(new Thickness(8, 8, 8, 8)));
        body.AddChild(listBox);

        // Right: detail for the selected bookmark.
        body.AddChild(BuildBookmarkDetail().GridCell(0, 1));
        card.AddChild(body);

        // Footer: Add · Remove · (spacer) · Done.
        var footer = DialogFooter();
        footer.AddChild(GhostButton("Add current folder", AddBookmark));
        footer.AddChild(GhostButton("Remove", () => RemoveBookmark(_bookmarkSel), _bookmarks.Count > 0));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(PrimaryButton("Done", CloseOverlays));
        card.AddChild(footer);

        return Backdrop(card, topAligned: false);
    }

    private Element BookmarkRow(int index, bool selected)
    {
        var b = _bookmarks[index];
        var row = Grid.Of("auto 1fr", rows: "1fr", gap: 8).AlignItems(AlignItems.Center).Height(30)
            .Padding(new Thickness(0, 8, 0, 8)).CornerRadius(Nocturne.RadiusSm)
            .Cursor(CursorType.Pointer);
        if (selected)
            row.Background(Nocturne.Alpha(Nocturne.Accent500, 0.13))
               .BorderColor(Nocturne.Accent600).BorderWidth(1f);
        row.AddChild(Ui.Icon(Glyphs.CaretRight, 11f, Nocturne.DirText).GridColumn(0));
        row.AddChild(Ui.Mono(b.Name, 11.5f, selected ? Nocturne.Text : Nocturne.On(0.80)).GridColumn(1)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        row.OnClick(_ => { _bookmarkSel = index; MarkViewDirty(); });
        row.On<DoubleClickEvent>(e => { e.StopPropagation = true; OpenBookmarkInPane(index, _active); });
        return row;
    }

    private Element BuildBookmarkDetail()
    {
        var col = Flex.Column(gap: 12).Padding(new Thickness(14, 16, 14, 16));
        if (_bookmarkSel < 0 || _bookmarkSel >= _bookmarks.Count)
        {
            col.AddChild(Text.New("Select a bookmark").FontSize(12f).TextColor(Nocturne.On(0.40)));
            return col;
        }
        var b = _bookmarks[_bookmarkSel];

        col.AddChild(DetailField("Name", b.Name));
        col.AddChild(DetailField("Path", b.Path));

        var hk = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        hk.AddChild(Text.New("Hotkey").FontSize(11.5f).TextColor(Nocturne.On(0.45)).Width(70));
        var label = HotkeyLabel(_bookmarkSel);
        hk.AddChild(label.Length > 0 ? Ui.Kbd(label) : Ui.Mono("—", 11f, Nocturne.On(0.35)));
        col.AddChild(hk);

        var actions = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Margin(new Thickness(4, 0, 0, 0));
        actions.AddChild(GhostButton("Open in left pane", () => OpenBookmarkInPane(_bookmarkSel, 'L')));
        actions.AddChild(GhostButton("Open in right pane", () => OpenBookmarkInPane(_bookmarkSel, 'R')));
        col.AddChild(actions);
        return col;
    }

    private static Element DetailField(string label, string value)
    {
        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        row.AddChild(Text.New(label).FontSize(11.5f).TextColor(Nocturne.On(0.45)).Width(70).FlexShrink(0));
        var field = Box.New().FlexGrow(1)
            .Padding(new Thickness(4, 8, 4, 8)).CornerRadius(3f)
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .Children(Ui.Mono(value, 11.5f, Nocturne.Text).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        row.AddChild(field);
        return row;
    }
}
