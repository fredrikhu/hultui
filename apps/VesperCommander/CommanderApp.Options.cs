using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Themes;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    private static readonly (string id, string label, string icon)[] OptionPages =
    [
        ("General", "General", Glyphs.PageGeneral),
        ("Panes", "Panes", Glyphs.PagePanes),
        ("Keyboard", "Keyboard", Glyphs.PageKeyboard),
        ("Tools", "Tools", Glyphs.PageTools),
        ("Colors", "Colors", Glyphs.PageColors),
    ];

    private const string PreviewPanelLabel = "Preview panel — takes over the pane you are not using";

    // ── Options window ───────────────────────────────────────────────────
    private Element BuildOptions()
    {
        var card = DialogCard(940);
        bool searching = _optSearch.Trim().Length > 0;

        // Header: accent tile · "Options" · search field · ✕.
        var header = Flex.Row(gap: 12).AlignItems(AlignItems.Center)
            .Padding(new Thickness(10, 14, 10, 14))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Ui.HeaderTile(Glyphs.Settings));
        header.AddChild(Text.New("Options").FontSize(15f).FontWeight(FontWeight.Medium).TextColor(Nocturne.Text));
        header.AddChild(Box.New().FlexGrow(1));
        header.AddChild(OptionsSearchField());
        header.AddChild(Ui.IconButton(Glyphs.Close, CloseOverlays, "Close", 28f));
        card.AddChild(header);

        // Body: 184px nav | page content. A fixed height + a 1fr row makes both cells
        // (notably the nav's surface) fill the dialog regardless of the page's content,
        // and keeps the window a stable size as you switch pages.
        var body = Grid.Of("184px 1fr", "1fr").Height(460);
        body.AddChild(OptionsNav().GridCell(0, 0));
        // Key the page subtree by which page (or the search view) it holds. A keyless
        // switch would patch the new page's controls onto the old ones by position,
        // bleeding retained state (focus, caret) across logically unrelated controls.
        // The page column scrolls. The dialog is a fixed 460 tall so switching pages does
        // not resize the window, and the pages have outgrown that — a page that cannot
        // scroll simply loses whatever falls past the bottom edge, silently, since there is
        // no clipped edge to see. Only this column scrolls: the nav, header and footer stay.
        var pageScroll = ScrollView.Vertical().GridCell(0, 1);
        var page = Flex.Column(gap: 14).Padding(new Thickness(16, 20, 16, 20));
        page.Key = searching ? "opt-page:search" : "opt-page:" + _optionsPage;
        if (searching) OptionsSearchResults(page, _optSearch.Trim());
        else OptionsPage(page);
        pageScroll.AddChild(page);
        body.AddChild(pageScroll);
        card.AddChild(body);

        // Footer: Reset · Cancel · Save.
        var footer = DialogFooter();
        footer.AddChild(GhostButton("Reset to defaults", ResetOptions));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(SecondaryButton("Cancel", CloseOverlays));
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
            item.AddChild(Ui.Icon(OptionPages[i].icon, 13f, on ? Nocturne.Accent200 : Nocturne.Neutral400));
            item.AddChild(Text.New(OptionPages[i].label).FontSize(12f)
                .TextColor(on ? Nocturne.Accent200 : Nocturne.Neutral400));
            // Picking a page also drops out of a search so you land on the real page.
            item.OnClick(_ => { _optionsPage = idx; _optSearch = ""; MarkDirty(); });
            if (!on) item.HoverBg(Nocturne.Neutral900, Color.Transparent);
            nav.AddChild(item);
        }
        return nav;
    }

    // ── Settings search ──────────────────────────────────────────────────
    // One row per setting, tagged with its page and keywords. Both the normal pages
    // and the search results build their controls from these delegates, so a match in
    // the results is the same live control you'd toggle on its page.
    private readonly record struct OptRow(int Page, string Label, string Keywords, Func<Element> Build);

    private Element OptionsSearchField()
    {
        var box = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Width(250)
            .Padding(new Thickness(4, 10, 4, 10)).CornerRadius(6f)
            .Background(Nocturne.Bg).BorderColor(Nocturne.Neutral700).BorderWidth(1f);
        box.AddChild(Ui.Icon(Glyphs.Search, 13f, Nocturne.On(0.45)));
        var input = Ui.Field(TextInput.New(_optSearch));
        input.PlaceholderText("Search settings…");
        input.OnTextChanged(v => { _optSearch = v; MarkDirty(); });
        input.WithRef(_optSearchRef);
        box.AddChild(input);
        if (_optSearch.Length > 0)
        {
            var clear = Box.New().Cursor(CursorType.Pointer).Padding(new Thickness(0, 3, 0, 3))
                .Children(Ui.Icon(Glyphs.Close, 10f, Nocturne.Neutral500));
            clear.OnClick(_ => { _optSearch = ""; MarkDirty(); });
            box.AddChild(clear);
        }
        return box;
    }

    private void OptionsSearchResults(BoxElement page, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Matches(OptRow r)
        {
            var hay = $"{r.Label} {r.Keywords} {OptionPages[r.Page].label}";
            return terms.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        var hits = OptionCatalog().Where(Matches).ToList();

        page.AddChild(SectionHeader("Search",
            hits.Count == 0 ? $"Nothing matches “{query}”"
            : hits.Count == 1 ? "1 setting" : $"{hits.Count} settings"));
        if (hits.Count == 0)
        {
            page.AddChild(Text.New("Try a shorter term, or a page name like “keyboard”.")
                .FontSize(12f).TextColor(Nocturne.Neutral500));
            return;
        }

        var scroll = ScrollView.Vertical().MaxHeight(360);
        var col = Flex.Column(gap: 12);
        foreach (var group in hits.GroupBy(r => r.Page).OrderBy(g => g.Key))
        {
            var section = Flex.Column(gap: 2);
            section.AddChild(GroupLabel(OptionPages[group.Key].label));
            foreach (var r in group) section.AddChild(r.Build());
            col.AddChild(section);
        }
        scroll.AddChild(col);
        page.AddChild(scroll);
    }

    private List<OptRow> OptionCatalog()
    {
        var c = new List<OptRow>
        {
            // General
            new(0, "Restore tabs and pane paths on start", "startup session remember reopen",
                () => ToggleRow(_draft.RestoreTabs, "Restore tabs and pane paths on start", () => Flip(ref _draft.RestoreTabs))),
            new(0, "Single instance", "startup one window prevent second launch",
                () => ToggleRow(_draft.SingleInstance, "Single instance — a second launch is prevented", () => Flip(ref _draft.SingleInstance))),
            new(0, "Confirm before delete", "safety trash",
                () => ToggleRow(_draft.ConfirmDelete, "Confirm before delete", () => Flip(ref _draft.ConfirmDelete), "F8")),
            new(0, "Delete to Recycle Bin", "trash permanent safety",
                () => ToggleRow(_draft.Recycle, "Delete to Recycle Bin instead of permanently", () => Flip(ref _draft.Recycle))),
            new(0, "On copy conflict", "overwrite keep newer auto rename move",
                () => FieldRow("On copy conflict", Seg(
                    ("Ask", _draft.Conflict == ConflictMode.Ask, () => SetDraft(() => _draft.Conflict = ConflictMode.Ask)),
                    ("Keep newer", _draft.Conflict == ConflictMode.KeepNewer, () => SetDraft(() => _draft.Conflict = ConflictMode.KeepNewer)),
                    ("Auto-rename", _draft.Conflict == ConflictMode.AutoRename, () => SetDraft(() => _draft.Conflict = ConflictMode.AutoRename))))),
            new(0, "Find: max search depth", "find files search recursive levels subfolders limit depth",
                () => FieldRow("Max search depth", FindDepthStepper())),
            new(0, "Find: exclude folders", "find files search skip prune ignore git vs node_modules pattern",
                () => FieldRow("Exclude folders", FindExcludeField())),
            // Panes
            new(1, "Size column", "columns visible",
                () => ToggleRow(_draft.ShowSize, "Size column", () => Flip(ref _draft.ShowSize))),
            new(1, "Modified column", "columns visible date",
                () => ToggleRow(_draft.ShowDate, "Modified column", () => Flip(ref _draft.ShowDate))),
            new(1, "Default sort", "order name ext size date",
                () => FieldRow("Default sort", Seg(
                    ("Name", _draft.Sort == SortMode.Name, () => SetDraft(() => _draft.Sort = SortMode.Name)),
                    ("Ext", _draft.Sort == SortMode.Ext, () => SetDraft(() => _draft.Sort = SortMode.Ext)),
                    ("Size", _draft.Sort == SortMode.Size, () => SetDraft(() => _draft.Sort = SortMode.Size)),
                    ("Date", _draft.Sort == SortMode.Date, () => SetDraft(() => _draft.Sort = SortMode.Date))))),
            new(1, "Row density", "compact comfortable spacing height",
                () => FieldRow("Row density", Seg(
                    ("Compact", _draft.Density == Density.Compact, () => SetDraft(() => _draft.Density = Density.Compact)),
                    ("Comfortable", _draft.Density == Density.Comfortable, () => SetDraft(() => _draft.Density = Density.Comfortable))))),
            new(1, "Preview panel", "off on image file inactive pane",
                () => ToggleRow(_draft.PreviewPane, PreviewPanelLabel, () => Flip(ref _draft.PreviewPane), "Ctrl+Q")),
            new(1, "Default view", "brief thumbnail grid detail mode layout",
                () => FieldRow("Default view", DefaultViewSeg())),
            new(1, "Directories first", "folders sort order",
                () => ToggleRow(_draft.DirsFirst, "Directories first", () => Flip(ref _draft.DirsFirst))),
            new(1, "Show hidden and system files", "dotfiles",
                () => ToggleRow(_draft.ShowHidden, "Show hidden and system files", () => Flip(ref _draft.ShowHidden))),
            // Tools
            new(3, "Text font", "viewer diff monospace face family typeface reading code",
                () => FieldRow("Text font", TextFontField())),
            new(3, "Text size", "viewer diff font size points bigger smaller reading code",
                () => FieldRow("Text size", TextFontSizeStepper())),
            new(3, "Diff tool", "compare vscode junction builtin",
                () => FieldRow("Diff tool", Seg(
                    ("Built-in", _draft.DiffTool == DiffToolKind.Builtin, () => SetDraft(() => _draft.DiffTool = DiffToolKind.Builtin)),
                    ("VS Code", _draft.DiffTool == DiffToolKind.VSCode, () => SetDraft(() => _draft.DiffTool = DiffToolKind.VSCode)),
                    ("Junction", _draft.DiffTool == DiffToolKind.Junction, () => SetDraft(() => _draft.DiffTool = DiffToolKind.Junction))))),
            new(3, "Wrap long lines in the diff", "word wrap break scroll sideways horizontal",
                () => ToggleRow(_draft.DiffWrap, "Wrap long lines in the diff", () => Flip(ref _draft.DiffWrap))),
            new(3, "Editor", "external text",
                () => FieldRow("Editor", ReadonlyField(_draft.Editor))),
            new(3, "Image editor", "external",
                () => FieldRow("Image editor", ReadonlyField(_draft.ImageEditor))),
            new(3, "Terminal", "external shell console",
                () => FieldRow("Terminal", ReadonlyField(_draft.Terminal))),
            new(3, "Git client", "external version control",
                () => FieldRow("Git client", ReadonlyField(_draft.GitClient.Length > 0 ? _draft.GitClient : "— not set —"))),
            new(3, "Archiver", "zip 7-zip compression",
                () => FieldRow("Archiver", Seg(
                    ("Built-in zip", _draft.Archiver == ArchiverKind.Builtin, () => SetDraft(() => _draft.Archiver = ArchiverKind.Builtin)),
                    ("7-Zip", _draft.Archiver == ArchiverKind.SevenZip, () => SetDraft(() => _draft.Archiver = ArchiverKind.SevenZip))))),
            new(3, "7-Zip path", "archiver external",
                () => FieldRow("7-Zip path", ReadonlyField(_draft.SevenZipPath.Length > 0 ? _draft.SevenZipPath : "— using PATH —"))),
            // Colors
            new(4, "Colour scheme", "theme gruvbox nord dracula solarized tokyo catppuccin everforest dark light",
                () => ThemeGrid()),
            new(4, "Accent color", "theme swatch palette",
                () => FieldRow("Accent", AccentRow())),
            new(4, "Glow on the active pane border", "highlight ring",
                () => ToggleRow(_draft.Glow, "Glow on the active pane border", () => Flip(ref _draft.Glow))),
            new(4, "Alternating row tint", "zebra stripes",
                () => ToggleRow(_draft.Zebra, "Alternating row tint", () => Flip(ref _draft.Zebra))),
        };
        // Keyboard: one searchable row per rebindable command.
        foreach (var (cmd, label) in Keybindings.Order)
            c.Add(new OptRow(2, label, "shortcut keybinding hotkey key", () => KeyBindRow(cmd, label)));
        return c;
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
        page.AddChild(Divider());
        page.AddChild(GroupLabel("Find files (Alt+F7)"));
        page.AddChild(FieldRow("Max search depth", FindDepthStepper()));
        page.AddChild(FieldRow("Exclude folders", FindExcludeField()));
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
        page.AddChild(FieldRow("Default sort", Seg(
            ("Name", _draft.Sort == SortMode.Name, () => SetDraft(() => _draft.Sort = SortMode.Name)),
            ("Ext", _draft.Sort == SortMode.Ext, () => SetDraft(() => _draft.Sort = SortMode.Ext)),
            ("Size", _draft.Sort == SortMode.Size, () => SetDraft(() => _draft.Sort = SortMode.Size)),
            ("Date", _draft.Sort == SortMode.Date, () => SetDraft(() => _draft.Sort = SortMode.Date)))));
        page.AddChild(FieldRow("Row density", Seg(
            ("Compact", _draft.Density == Density.Compact, () => SetDraft(() => _draft.Density = Density.Compact)),
            ("Comfortable", _draft.Density == Density.Comfortable, () => SetDraft(() => _draft.Density = Density.Comfortable)))));
        page.AddChild(FieldRow("Default view", DefaultViewSeg()));
        var toggles = Flex.Column(gap: 2);
        toggles.AddChild(ToggleRow(_draft.PreviewPane, PreviewPanelLabel, () => Flip(ref _draft.PreviewPane), "Ctrl+Q"));
        toggles.AddChild(ToggleRow(_draft.DirsFirst, "Directories first", () => Flip(ref _draft.DirsFirst)));
        toggles.AddChild(ToggleRow(_draft.ShowHidden, "Show hidden and system files", () => Flip(ref _draft.ShowHidden)));
        page.AddChild(toggles);
    }

    private void KeyboardPage(BoxElement page)
    {
        page.AddChild(SectionHeader("Keyboard", "Click a shortcut to rebind it; press the new keys, or Esc to cancel."));
        // No scroll view of its own: the page column is one now, and a scroll inside a
        // scroll takes the wheel away from the outer one for whichever it happens to be over.
        var col = Flex.Column(gap: 0);
        foreach (var (cmd, label) in Keybindings.Order)
            col.AddChild(KeyBindRow(cmd, label));
        page.AddChild(col);

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

    /// <summary>One rebindable-command row: label + current chord (or a capture chip while
    /// this command is being rebound). Shared by the Keyboard page and settings search.</summary>
    private Element KeyBindRow(Cmd cmd, string label)
    {
        bool capturing = _rebindCmd == cmd;
        var row = Grid.Of("1fr auto").Height(28).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 8, 0, 8)).CornerRadius(5f).Cursor(CursorType.Pointer);
        row.AddChild(Text.New(label).FontSize(12f).TextColor(Nocturne.Text).GridCell(0, 0));

        BoxElement chip = capturing
            ? Box.New().Padding(new Thickness(1, 6, 2, 6)).CornerRadius(4f)
                .Background(Nocturne.Accent900).BorderColor(Nocturne.Accent).BorderWidth(1f)
                .Children(Ui.Mono("press keys…", 10f, Nocturne.Accent200))
            : CurrentChord(cmd, _draft) is { } ch
                ? Ui.Kbd(Keybindings.Format(ch))
                : Box.New().Padding(new Thickness(1, 6, 2, 6))   // stolen chord's former owner
                    .Children(Ui.Mono("unassigned", 10f, Nocturne.Neutral600));
        row.AddChild(chip.GridCell(0, 1));

        row.OnClick(_ => { _rebindCmd = cmd; MarkDirty(); });
        if (!capturing) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
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

        page.AddChild(Divider());
        page.AddChild(SectionHeader("Reading files",
            "The viewer (Shift+F3) and the built-in diff. Leave the face empty for the system's monospace."));
        page.AddChild(FieldRow("Text font", TextFontField()));
        page.AddChild(FieldRow("Text size", TextFontSizeStepper()));
        page.AddChild(TextFontPreview());
    }

    // ── Reading-file font (shared by the Tools page and settings search) ──

    /// <summary>The face the viewer and the diff read files in. Editable, so it takes the
    /// bordered wrapper and an explicit height (a bare flex TextInput paints nothing), and
    /// no MarkDirty per keystroke — rebuilding with new text is what loses the caret. The
    /// preview under it is rebuilt on save, which is when the choice takes effect.</summary>
    private Element TextFontField()
    {
        var box = Box.New().FlexGrow(1)
            .Padding(new Thickness(0, 8, 0, 8)).CornerRadius(3f)
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f);
        box.Key = "opt-text-font";
        var input = TextInput.New(_draft.TextFont)
            .Width(Dimension.Percent(100)).Height(Ui.FieldHeight);
        input.PlaceholderText(Fonts.Mono);
        input.OnTextChanged(v => _draft.TextFont = v);
        input.AutomationId = "opt-text-font-input";
        box.AddChild(input);
        return box;
    }

    private Element TextFontSizeStepper()
    {
        var n = NumericUpDown.New(_draft.TextFontSize, Settings.MinTextFontSize, Settings.MaxTextFontSize, step: 0.5)
            .Format(v => $"{v:0.#} pt")
            .OnValueChanged(v => SetDraft(() => _draft.TextFontSize = (float)v));
        n.AutomationId = "opt-text-size";
        n.AccessibleName = "Text size";
        return n;
    }

    /// <summary>A line of the real thing, in the face and size being chosen — the only
    /// honest way to say whether this machine actually has the font that was typed.</summary>
    private Element TextFontPreview()
    {
        var family = string.IsNullOrWhiteSpace(_draft.TextFont) ? Fonts.Mono : _draft.TextFont;
        return Box.New()
            .Padding(new Thickness(7, 10, 7, 10)).CornerRadius(3f)
            .Background(Nocturne.Bg).Overflow(Overflow.Hidden)
            .Children(Ui.Code("for (i = 0; i < n; i++) { total += size[i]; }  0O1lI",
                    _draft.TextFontSize, family, Nocturne.On(0.78))
                .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
    }

    private void ColorsPage(BoxElement page)
    {
        page.AddChild(SectionHeader("Colour scheme",
            "Each card is painted in its own colours. Marked files stay red in all of them."));
        page.AddChild(ThemeGrid());
        page.AddChild(Divider());
        page.AddChild(FieldRow("Accent", AccentRow()));
        var toggles = Flex.Column(gap: 2);
        toggles.AddChild(ToggleRow(_draft.Glow, "Glow on the active pane border", () => Flip(ref _draft.Glow)));
        toggles.AddChild(ToggleRow(_draft.Zebra, "Alternating row tint", () => Flip(ref _draft.Zebra)));
        page.AddChild(toggles);
    }

    /// <summary>
    /// The scheme picker: one card per theme, each drawn in that theme's own ground, text and
    /// accents. A row of names would make the user apply each one to find out what it looks
    /// like, which is four clicks per guess.
    /// </summary>
    private Element ThemeGrid()
    {
        var scroll = ScrollView.Vertical().MaxHeight(224);
        // Room on the right for the scrollbar, which otherwise sits on top of the last column.
        var col = Flex.Column(gap: 8).Padding(new Thickness(0, 10, 0, 0));
        var themes = ThemeCatalog.All;

        for (int i = 0; i < themes.Count; i += 3)
        {
            var row = Flex.Row(gap: 8).AlignItems(AlignItems.Stretch);
            for (int c = i; c < Math.Min(i + 3, themes.Count); c++)
                row.AddChild(ThemeCard(themes[c]).FlexGrow(1).FlexBasis(Dimension.Px(0)));
            // Pad the last row so two cards don't stretch to the width of three.
            for (int pad = themes.Count; pad < i + 3; pad++)
                row.AddChild(Box.New().FlexGrow(1).FlexBasis(Dimension.Px(0)));
            col.AddChild(row);
        }

        scroll.AddChild(col);
        return scroll;
    }

    private Element ThemeCard(NamedTheme t)
    {
        bool on = _draft.Theme == t.Id;
        // The card previews the palette the app would actually use, accent override included,
        // rather than the theme's raw seeds — otherwise picking an accent leaves every card
        // showing a colour the app is not going to paint.
        var p = Palette.For(t, _draft.Accent);

        var card = Flex.Column(gap: 6)
            .Padding(new Thickness(8, 9, 8, 9)).CornerRadius(Nocturne.RadiusMd)
            .Background(p.Bg).Cursor(CursorType.Pointer)
            // Selection reads off the card's own palette, so it stays visible on a light
            // scheme where the app's accent would not be.
            .BorderWidth(1f)
            .BorderColor(on ? p.Accents[3] : ColorMath.Alpha(p.Text, 0.18))
            // The second px of the selected card's edge is a ring outside it. As a
            // border it made the card taller than its unselected neighbours, and the
            // row stretches to the tallest, so the whole row grew when selection moved.
            .Outline(on ? 1f : 0f, on ? p.Accents[3] : Color.Transparent);

        var title = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        title.AddChild(Text.New(t.Name).FontSize(11.5f)
            .FontWeight(on ? FontWeight.Medium : FontWeight.Regular)
            .TextColor(p.Text).MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexGrow(1));
        if (on) title.AddChild(Ui.Icon(Glyphs.Check, 11f, p.Accents[3]));
        card.AddChild(title);

        // A strip of surface with a few dots on it: the raised bar every view in the app
        // has, and the colours that land on top of it.
        var strip = Flex.Row(gap: 5).AlignItems(AlignItems.Center).Height(20)
            .Padding(new Thickness(0, 6, 0, 6)).CornerRadius(Nocturne.RadiusSm)
            .Background(p.Surface);
        foreach (var dot in new[] { p.Accent, p.Accents[1], p.DiffGreen, p.MarkRed, p.MarkAmber })
            strip.AddChild(Box.New().Width(9).Height(9).CornerRadius(5f).Background(dot).FlexShrink(0));
        card.AddChild(strip);

        card.AddChild(Text.New(t.Blurb).FontSize(9.5f)
            .TextColor(ColorMath.Alpha(p.Text, 0.55))
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));

        card.OnClick(_ => SetDraft(() => _draft.Theme = t.Id));
        card.AutomationId = "opt-theme:" + t.Id;
        card.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        card.AccessibleName = t.Name;
        return card;
    }

    private Element DefaultViewSeg() => Seg(
        ("Detail", _draft.DefaultView == ViewMode.Detail, () => SetDraft(() => _draft.DefaultView = ViewMode.Detail)),
        ("Brief", _draft.DefaultView == ViewMode.Brief, () => SetDraft(() => _draft.DefaultView = ViewMode.Brief)),
        ("Thumbnails", _draft.DefaultView == ViewMode.Thumbnails, () => SetDraft(() => _draft.DefaultView = ViewMode.Thumbnails)));

    /// <summary>Accent swatches, applied on top of whichever scheme is selected. The first is
    /// the scheme's own — so there is always a way back from an override.</summary>
    private Element AccentRow()
    {
        var theme = ThemeCatalog.ById(_draft.Theme);
        var accents = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        for (int i = 0; i < Palette.AccentOverrides.Length; i++)
        {
            int idx = i;
            var color = i == 0 ? Palette.For(theme, 0).Accent : Color.FromHex(Palette.AccentOverrides[i]);
            var sw = Swatch(color, _draft.Accent == i, () => SetDraft(() => _draft.Accent = idx));
            sw.AutomationId = "opt-accent:" + idx;
            sw.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
            sw.AccessibleName = i == 0 ? $"{theme.Name}'s own accent" : "Accent " + i;
            accents.AddChild(sw);
        }
        accents.AddChild(Text.New(_draft.Accent == 0 ? "the scheme's own" : "overriding the scheme")
            .FontSize(10.5f).TextColor(Nocturne.Neutral500));
        return accents;
    }

    // ── Reusable controls ────────────────────────────────────────────────
    private void Flip(ref bool field) { field = !field; MarkDirty(); }
    private void SetDraft(Action mutate) { mutate(); MarkDirty(); }

    /// <summary>The framework's switch (sized by <see cref="Nocturne.ToHultTheme"/>) with the
    /// label and optional key hint beside it. The row's click covers the label; the switch
    /// handles its own and stops it there, or a click on the switch toggled twice.</summary>
    private Element ToggleRow(bool value, string label, Action onToggle, string? kbd = null)
    {
        var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center).Height(28).Cursor(CursorType.Pointer);
        var sw = ToggleSwitch.New(value).FlexShrink(0).OnToggled(_ => onToggle());
        sw.On<ClickEvent>(e => e.StopPropagation = true);
        sw.AutomationId = "opt-toggle:" + label;
        sw.AccessibleName = label;
        row.AddChild(sw);
        var lbl = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
        lbl.AddChild(Text.New(label).FontSize(12f).TextColor(Nocturne.Text));
        if (kbd != null) lbl.AddChild(Ui.Kbd(kbd));
        row.AddChild(lbl);
        row.OnClick(_ => onToggle());
        return row;
    }

    private Element Seg(params (string label, bool on, Action onClick)[] opts)
    {
        // Divider (text at 16 %) rather than Neutral800: the ramp step sits on the ground
        // in schemes with a short neutral ramp, and the control lost its outline.
        var seg = Flex.Row(gap: 2).FlexShrink(0)
            .Padding(new Thickness(2)).CornerRadius(6f)
            .BorderColor(Nocturne.Divider).BorderWidth(1f);
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

    // ── Find-files defaults (shared by the General page and settings search) ──
    /// <summary>Default depth bound for Alt+F7 — folder levels below the searched folder,
    /// 0 = unlimited. See <see cref="DepthLabel"/> for the wording.</summary>
    private Element FindDepthStepper() =>
        DepthField(_draft.FindMaxDepth, v => SetDraft(() => _draft.FindMaxDepth = v), "opt-find-depth");

    /// <summary>Default folder-exclude list for Alt+F7. Unlike the read-only tool fields this
    /// one is editable, so it needs the bordered wrapper and an explicit height (a bare flex
    /// TextInput paints nothing). No MarkDirty per keystroke — rebuilding with new text is
    /// what loses the caret. Keyed because search results place it in a different subtree.</summary>
    private Element FindExcludeField()
    {
        var box = Box.New().FlexGrow(1)
            .Padding(new Thickness(0, 8, 0, 8)).CornerRadius(3f)
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f);
        box.Key = "opt-find-exclude";
        var input = TextInput.New(_draft.FindExcludeFolders)
            .Width(Dimension.Percent(100)).Height(Ui.FieldHeight);
        input.PlaceholderText(".git;.vs");
        input.OnTextChanged(v => _draft.FindExcludeFolders = v);
        box.AddChild(input);
        return box;
    }


    private static Element ReadonlyField(string value)
    {
        return Box.New().FlexGrow(1)
            .Padding(new Thickness(4, 8, 4, 8)).CornerRadius(3f)
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f)
            .Children(Ui.Mono(value, 11.5f, Nocturne.Text).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
    }

    private static Element CheckItem(string label, bool on, Action? onToggle)
    {
        var row = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
            .Cursor(onToggle != null ? CursorType.Pointer : CursorType.Default);
        var box = Box.New().Width(16).Height(16).CornerRadius(4f)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .BorderColor(on ? Nocturne.Accent : Nocturne.On(0.35)).BorderWidth(1f);
        if (on) box.Background(Nocturne.Accent).Children(Ui.Icon(Glyphs.Check, 10f, Nocturne.Accent100));
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
