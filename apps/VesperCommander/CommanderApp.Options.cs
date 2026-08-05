using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    private static readonly (string id, string label, string glyph)[] OptionPages =
    [
        ("General", "General", "◇"),
        ("Panes", "Panes", "▤"),
        ("Keyboard", "Keyboard", "⌘"),
        ("Tools", "Tools", "⚒"),
        ("Colors", "Colors", "◐"),
    ];

    private static readonly string[] AccentPalette = ["#9184d9", "#7fb2d9", "#8fd0b0", "#d9a06b"];

    // ── Options window ───────────────────────────────────────────────────
    private Element BuildOptions()
    {
        var card = DialogCard(940);

        // Header: gear · "Options" · current page · ✕.
        var header = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(10, 14, 10, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Ui.Icon("⚙", 14f, Nocturne.Accent));
        header.AddChild(Text.New("Options").FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Nocturne.Text));
        header.AddChild(Text.New(OptionPages[_optionsPage].label).FontSize(11f).TextColor(Nocturne.On(0.45)));
        header.AddChild(Box.New().FlexGrow(1));
        var close = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(1, 5, 1, 5)).CornerRadius(4f)
            .Children(Ui.Icon("✕", 12f, Nocturne.Neutral600));
        close.OnClick(_ => CloseOverlays());
        header.AddChild(close.HoverBg(Nocturne.Neutral900, Color.Transparent));
        card.AddChild(header);

        // Body: 184px nav | page content. A fixed height + a 1fr row makes both cells
        // (notably the nav's surface) fill the dialog regardless of the page's content,
        // and keeps the window a stable size as you switch pages.
        var body = Grid.Of("184px 1fr", "1fr").Height(460);
        body.AddChild(OptionsNav().GridCell(0, 0));
        var page = Flex.Column(gap: 14).Padding(new Thickness(16, 20, 16, 20)).GridCell(0, 1);
        OptionsPage(page);
        body.AddChild(page);
        card.AddChild(body);

        // Footer: Reset · Cancel · Save.
        var footer = DialogFooter();
        footer.AddChild(GhostButton("Reset to defaults", ResetOptions));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(GhostButton("Cancel", CloseOverlays));
        footer.AddChild(PrimaryButton("Save", SaveOptions));
        card.AddChild(footer);

        return Backdrop(card, topAligned: false);
    }

    private Element OptionsNav()
    {
        var nav = Flex.Column(gap: 0).Padding(new Thickness(10, 0, 10, 0))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 0, 1));
        for (int i = 0; i < OptionPages.Length; i++)
        {
            int idx = i;
            bool on = _optionsPage == i;
            var item = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Height(30)
                .Padding(new Thickness(6, 14, 6, 14)).Cursor(CursorType.Pointer)
                .BorderColor(on ? Nocturne.Accent : Color.Transparent).BorderWidth(new Thickness(0, 0, 0, 2));
            if (on) item.Background(Nocturne.Accent900);
            item.AddChild(Text.New(OptionPages[i].glyph).FontSize(11f).Width(14)
                .TextAlign(TextAlign.Center).TextColor(on ? Nocturne.Accent200 : Nocturne.Neutral400));
            item.AddChild(Text.New(OptionPages[i].label).FontSize(12f)
                .TextColor(on ? Nocturne.Accent200 : Nocturne.Neutral400));
            item.OnClick(_ => { _optionsPage = idx; MarkDirty(); });
            if (!on) item.HoverBg(Nocturne.Neutral900, Color.Transparent);
            nav.AddChild(item);
        }
        return nav;
    }

    private void OptionsPage(BoxElement page)
    {
        switch (_optionsPage)
        {
            case 0: GeneralPage(page); break;
            case 1: PanesPage(page); break;
            case 2: KeyboardPage(page); break;
            case 3: ToolsPage(page); break;
            case 4: ColorsPage(page); break;
        }
    }

    // ── Pages ────────────────────────────────────────────────────────────
    private void GeneralPage(BoxElement page)
    {
        page.AddChild(SectionHeader("Startup & behavior", "How Vesper opens and what it remembers."));
        var toggles = Flex.Column(gap: 2);
        toggles.AddChild(ToggleRow(_draft.RestoreTabs, "Restore tabs and pane paths on start", () => Flip(ref _draft.RestoreTabs)));
        toggles.AddChild(ToggleRow(_draft.SingleInstance, "Single instance — a second launch is prevented", () => Flip(ref _draft.SingleInstance)));
        toggles.AddChild(ToggleRow(_draft.ConfirmDelete, "Confirm before delete", () => Flip(ref _draft.ConfirmDelete), "F8"));
        toggles.AddChild(ToggleRow(_draft.Recycle, "Delete to Recycle Bin instead of permanently", () => Flip(ref _draft.Recycle)));
        page.AddChild(toggles);
        page.AddChild(Divider());
        page.AddChild(FieldRow("On copy conflict", Seg(
            ("Ask", _draft.Conflict == ConflictMode.Ask, () => SetDraft(() => _draft.Conflict = ConflictMode.Ask)),
            ("Keep newer", _draft.Conflict == ConflictMode.KeepNewer, () => SetDraft(() => _draft.Conflict = ConflictMode.KeepNewer)),
            ("Auto-rename", _draft.Conflict == ConflictMode.AutoRename, () => SetDraft(() => _draft.Conflict = ConflictMode.AutoRename)))));
    }

    private void PanesPage(BoxElement page)
    {
        page.AddChild(SectionHeader("Panes & columns", "Applies to both panes."));
        page.AddChild(GroupLabel("Visible columns"));
        var cols = Flex.Row(gap: 20);
        cols.AddChild(CheckItem("Name", true, null));
        cols.AddChild(CheckItem("Size", _draft.ShowSize, () => Flip(ref _draft.ShowSize)));
        cols.AddChild(CheckItem("Modified", _draft.ShowDate, () => Flip(ref _draft.ShowDate)));
        page.AddChild(cols);
        page.AddChild(Divider());
        page.AddChild(FieldRow("Sort by", Seg(
            ("Name", _draft.Sort == SortMode.Name, () => SetDraft(() => _draft.Sort = SortMode.Name)),
            ("Ext", _draft.Sort == SortMode.Ext, () => SetDraft(() => _draft.Sort = SortMode.Ext)),
            ("Size", _draft.Sort == SortMode.Size, () => SetDraft(() => _draft.Sort = SortMode.Size)),
            ("Date", _draft.Sort == SortMode.Date, () => SetDraft(() => _draft.Sort = SortMode.Date)))));
        page.AddChild(FieldRow("Row density", Seg(
            ("Compact", _draft.Density == Density.Compact, () => SetDraft(() => _draft.Density = Density.Compact)),
            ("Comfortable", _draft.Density == Density.Comfortable, () => SetDraft(() => _draft.Density = Density.Comfortable)))));
        page.AddChild(FieldRow("Preview width", Stepper(_draft.PreviewWidth, "px",
            () => SetDraft(() => _draft.PreviewWidth = Math.Max(220, _draft.PreviewWidth - 20)),
            () => SetDraft(() => _draft.PreviewWidth = Math.Min(460, _draft.PreviewWidth + 20)))));
        page.AddChild(FieldRow("Preview panel", Seg(
            ("Off", _draft.Preview == PreviewMode.Off, () => SetDraft(() => _draft.Preview = PreviewMode.Off)),
            ("Side", _draft.Preview == PreviewMode.Normal, () => SetDraft(() => _draft.Preview = PreviewMode.Normal)),
            ("Full", _draft.Preview == PreviewMode.Full, () => SetDraft(() => _draft.Preview = PreviewMode.Full)))));
        var toggles = Flex.Column(gap: 2);
        toggles.AddChild(ToggleRow(_draft.DirsFirst, "Directories first", () => Flip(ref _draft.DirsFirst)));
        toggles.AddChild(ToggleRow(_draft.ShowHidden, "Show hidden and system files", () => Flip(ref _draft.ShowHidden)));
        page.AddChild(toggles);
    }

    private void KeyboardPage(BoxElement page)
    {
        page.AddChild(SectionHeader("Keyboard", "Click a shortcut to rebind it; press the new keys, or Esc to cancel."));
        var list = ScrollView.Vertical().MaxHeight(340);
        var col = Flex.Column(gap: 0);
        foreach (var (cmd, label) in Keybindings.Order)
        {
            bool capturing = _rebindCmd == cmd;
            var row = Grid.Of("1fr auto").Height(28).AlignItems(AlignItems.Center)
                .Padding(new Thickness(0, 8, 0, 8)).CornerRadius(5f).Cursor(CursorType.Pointer);
            row.AddChild(Text.New(label).FontSize(12f).TextColor(Nocturne.Text).GridCell(0, 0));

            BoxElement chip = capturing
                ? Box.New().Padding(new Thickness(1, 6, 2, 6)).CornerRadius(4f)
                    .Background(Nocturne.Accent900).BorderColor(Nocturne.Accent).BorderWidth(1f)
                    .Children(Ui.Mono("press keys…", 10f, Nocturne.Accent200))
                : Ui.Kbd(Keybindings.Format(CurrentChord(cmd, _draft)));
            row.AddChild(chip.GridCell(0, 1));

            var target = cmd;
            row.OnClick(_ => { _rebindCmd = target; MarkDirty(); });
            if (!capturing) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
            col.AddChild(row);
        }
        list.AddChild(col);
        page.AddChild(list);

        var footer = Flex.Row(gap: 10).AlignItems(AlignItems.Center).Padding(new Thickness(6, 0, 0, 0));
        footer.AddChild(Text.New($"{Keybindings.Order.Length} rebindable commands").FontSize(11f).TextColor(Nocturne.Neutral500));
        footer.AddChild(Box.New().FlexGrow(1));
        if (_draft.KeyBindings.Count > 0)
        {
            var reset = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(4, 11, 4, 11)).CornerRadius(Nocturne.RadiusSm)
                .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
                .Children(Text.New($"Reset {_draft.KeyBindings.Count} to defaults").FontSize(11f).TextColor(Nocturne.Neutral300));
            reset.OnClick(_ => { _draft.KeyBindings.Clear(); _rebindCmd = null; MarkDirty(); });
            footer.AddChild(reset.HoverBg(Nocturne.Neutral900, Color.Transparent));
        }
        page.AddChild(footer);
    }

    private void ToolsPage(BoxElement page)
    {
        page.AddChild(SectionHeader("External tools", "Where Vesper hands off — diffs, git, editing, terminal."));
        page.AddChild(FieldRow("Diff tool", Seg(
            ("Built-in", _draft.DiffTool == DiffToolKind.Builtin, () => SetDraft(() => _draft.DiffTool = DiffToolKind.Builtin)),
            ("VS Code", _draft.DiffTool == DiffToolKind.VSCode, () => SetDraft(() => _draft.DiffTool = DiffToolKind.VSCode)),
            ("Junction", _draft.DiffTool == DiffToolKind.Junction, () => SetDraft(() => _draft.DiffTool = DiffToolKind.Junction)))));
        page.AddChild(FieldRow("Editor", ReadonlyField(_draft.Editor)));
        page.AddChild(FieldRow("Image editor", ReadonlyField(_draft.ImageEditor)));
        page.AddChild(FieldRow("Terminal", ReadonlyField(_draft.Terminal)));
        page.AddChild(FieldRow("Git client", ReadonlyField(_draft.GitClient.Length > 0 ? _draft.GitClient : "— not set —")));
        page.AddChild(FieldRow("Archiver", Seg(
            ("Built-in zip", _draft.Archiver == ArchiverKind.Builtin, () => SetDraft(() => _draft.Archiver = ArchiverKind.Builtin)),
            ("7-Zip", _draft.Archiver == ArchiverKind.SevenZip, () => SetDraft(() => _draft.Archiver = ArchiverKind.SevenZip)))));
        page.AddChild(FieldRow("7-Zip path", ReadonlyField(_draft.SevenZipPath.Length > 0 ? _draft.SevenZipPath : "— using PATH —")));
        page.AddChild(Text.New(_sevenZip
                ? "7-Zip found — 7z, rar (read), and encryption are available."
                : "7-Zip not found — only built-in zip works. Set the path in settings.json.")
            .FontSize(11f).TextColor(_sevenZip ? Nocturne.On(0.45) : Nocturne.MarkRedDim));
    }

    private void ColorsPage(BoxElement page)
    {
        page.AddChild(SectionHeader("Colors", "Marked files stay red — the one convention we don't make configurable."));
        var accents = Flex.Row(gap: 8);
        for (int i = 0; i < AccentPalette.Length; i++)
        {
            int idx = i;
            accents.AddChild(Swatch(Color.FromHex(AccentPalette[i]), _draft.Accent == i,
                () => SetDraft(() => _draft.Accent = idx)));
        }
        page.AddChild(FieldRow("Accent", accents));
        page.AddChild(FieldRow("Marked files", SwatchStatic(Nocturne.MarkRed, "fixed")));
        page.AddChild(FieldRow("Directory names", SwatchStatic(Nocturne.Accent200, "accent-200")));
        page.AddChild(Divider());
        var toggles = Flex.Column(gap: 2);
        toggles.AddChild(ToggleRow(_draft.Glow, "Glow on the active pane border", () => Flip(ref _draft.Glow)));
        toggles.AddChild(ToggleRow(_draft.Zebra, "Alternating row tint", () => Flip(ref _draft.Zebra)));
        page.AddChild(toggles);
    }

    // ── Reusable controls ────────────────────────────────────────────────
    private void Flip(ref bool field) { field = !field; MarkDirty(); }
    private void SetDraft(Action mutate) { mutate(); MarkDirty(); }

    private Element Toggle(bool value, Action onToggle)
    {
        var sw = Flex.Row(gap: 0).Width(30).Height(17).FlexShrink(0)
            .Padding(new Thickness(2)).CornerRadius(9f).AlignItems(AlignItems.Center)
            .JustifyContent(value ? JustifyContent.FlexEnd : JustifyContent.FlexStart)
            .Background(value ? Nocturne.Accent700 : Nocturne.Neutral800).Cursor(CursorType.Pointer);
        sw.AddChild(Box.New().Width(13).Height(13).CornerRadius(7f)
            .Background(value ? Nocturne.Accent200 : Nocturne.Neutral500));
        sw.OnClick(_ => onToggle());
        return sw;
    }

    private Element ToggleRow(bool value, string label, Action onToggle, string? kbd = null)
    {
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center).Height(28).Cursor(CursorType.Pointer);
        row.AddChild(Toggle(value, onToggle));
        var lbl = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        lbl.AddChild(Text.New(label).FontSize(12f).TextColor(Nocturne.Text));
        if (kbd != null) lbl.AddChild(Ui.Kbd(kbd));
        row.AddChild(lbl);
        row.OnClick(_ => onToggle());
        return row;
    }

    private Element Seg(params (string label, bool on, Action onClick)[] opts)
    {
        var seg = Flex.Row(gap: 2).FlexShrink(0)
            .Padding(new Thickness(2)).CornerRadius(6f)
            .BorderColor(Nocturne.Neutral800).BorderWidth(1f);
        foreach (var (label, on, onClick) in opts)
        {
            var o = Box.New().Padding(new Thickness(3, 9, 3, 9)).CornerRadius(5f).Cursor(CursorType.Pointer)
                .BorderColor(on ? Nocturne.Accent700 : Color.Transparent).BorderWidth(1f)
                .Children(Text.New(label).FontSize(11.5f).TextColor(on ? Nocturne.Accent200 : Nocturne.Neutral400));
            if (on) o.Background(Nocturne.Accent900);
            o.OnClick(_ => onClick());
            seg.AddChild(o);
        }
        return seg;
    }

    private Element Swatch(Color c, bool selected, Action onClick)
    {
        var s = Box.New().Width(22).Height(22).CornerRadius(6f).Background(c).Cursor(CursorType.Pointer)
            .BorderColor(selected ? Nocturne.Text : Color.Transparent).BorderWidth(2f);
        s.OnClick(_ => onClick());
        return s;
    }

    private static Element SwatchStatic(Color c, string note)
    {
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center);
        row.AddChild(Box.New().Width(22).Height(22).CornerRadius(6f).Background(c));
        row.AddChild(Ui.Mono(note, 11f, Nocturne.On(0.45)));
        return row;
    }

    private static Element Stepper(int value, string unit, Action dec, Action inc)
    {
        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        row.AddChild(StepBtn("−", dec));
        row.AddChild(Ui.Mono($"{value} {unit}", 11.5f, Nocturne.Text).Width(56).TextAlign(TextAlign.Center));
        row.AddChild(StepBtn("+", inc));
        return row;
    }

    private static Element StepBtn(string glyph, Action onClick)
    {
        var b = Box.New().Padding(new Thickness(3, 9, 3, 9)).CornerRadius(5f).Cursor(CursorType.Pointer)
            .BorderColor(Nocturne.Neutral800).BorderWidth(1f)
            .Children(Text.New(glyph).FontSize(12f).TextColor(Nocturne.Neutral400));
        b.OnClick(_ => onClick());
        return b.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }

    private static Element ReadonlyField(string value)
    {
        return Box.New().FlexGrow(1)
            .Padding(new Thickness(4, 8, 4, 8)).CornerRadius(3f)
            .Background(Nocturne.Bg).BorderColor(Nocturne.Neutral800).BorderWidth(1f)
            .Children(Ui.Mono(value, 11.5f, Nocturne.Text).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
    }

    private static Element CheckItem(string label, bool on, Action? onToggle)
    {
        var row = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
            .Cursor(onToggle != null ? CursorType.Pointer : CursorType.Default);
        var box = Box.New().Width(16).Height(16).CornerRadius(4f)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .BorderColor(on ? Nocturne.Accent : Nocturne.On(0.35)).BorderWidth(1f);
        if (on) box.Background(Nocturne.Accent).Children(Ui.Icon("✓", 10f, Nocturne.Accent100));
        row.AddChild(box);
        row.AddChild(Text.New(label).FontSize(12f).TextColor(onToggle != null ? Nocturne.Text : Nocturne.On(0.55)));
        if (onToggle != null) row.OnClick(_ => onToggle());
        return row;
    }

    private static Element FieldRow(string label, Element control)
    {
        var row = Flex.Row(gap: 16).AlignItems(AlignItems.Center);
        row.AddChild(Text.New(label).FontSize(12f).TextColor(Nocturne.On(0.55)).Width(150).FlexShrink(0));
        row.AddChild(control);
        return row;
    }

    private static Element SectionHeader(string title, string hint)
    {
        var c = Flex.Column(gap: 2);
        c.AddChild(Text.New(title).FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Nocturne.Text));
        c.AddChild(Text.New(hint).FontSize(11f).TextColor(Nocturne.Neutral500));
        return c;
    }

    private static Element GroupLabel(string text) =>
        Text.New(text.ToUpperInvariant()).FontSize(10f).LetterSpacing(0.06f * 10f).TextColor(Nocturne.Neutral600);

    private static Element Divider() =>
        Box.New().Height(1).Background(Nocturne.Divider);
}
