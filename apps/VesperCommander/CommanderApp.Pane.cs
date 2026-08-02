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
    // Shared grid template for the header and every file row: icon · name · size · date.

    /// <summary>A 1px full-height divider between panes (the design's per-pane
    /// <c>border-right</c>, pulled out so the active-pane ring can stay layout-stable).</summary>
    private static Element VDivider() =>
        Box.New().Width(1).FlexShrink(0).Background(Nocturne.Divider);

    // ── Chrome (draggable strip + window controls) ───────────────────────
    private Element BuildChrome()
    {
        var bar = Flex.Row(gap: 0).Height(30).FlexShrink(0)
            .AlignItems(AlignItems.Stretch)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        var drag = Flex.Row(gap: 8).FlexGrow(1).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 12, 0, 12));
        drag.AddChild(Text.New("Vesper Commander").FontSize(12f)
            .FontWeight(FontWeight.Medium).TextColor(Nocturne.On(0.70)));
        drag.WindowDrag();
        bar.AddChild(drag);

        var controls = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch);
        controls.AddChild(WinButton("–", "Minimize", () => _window.Minimize(), Nocturne.Neutral800));
        controls.AddChild(WinButton("□", "Maximize", () => _window.ToggleMaximize(), Nocturne.Neutral800)
            .WindowMaximizeButton());
        controls.AddChild(WinButton("✕", "Close", () => _window.Close(), new Color(232, 17, 35)));
        bar.AddChild(controls);
        return bar;
    }

    private BoxElement WinButton(string glyph, string tooltip, Action onClick, Color hoverBg)
    {
        var b = Flex.Row(gap: 0).Width(42).Height(30)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Cursor(CursorType.Pointer).Tooltip(tooltip);
        b.AddChild(Text.New(glyph).FontSize(11f).TextColor(Nocturne.On(0.75)));
        b.OnClick(_ => onClick());
        return b.HoverBg(hoverBg, Color.Transparent);
    }

    // ── Left rail (app glyph · bookmark jumps · + manager · palette) ─────
    private Element BuildRail()
    {
        var rail = Flex.Column(gap: 4).Width(RailWidth).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(10, 0, 10, 0))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        rail.AddChild(Text.New("V").FontSize(15f).FontWeight(FontWeight.SemiBold)
            .TextColor(Nocturne.Accent).Margin(new Thickness(2, 0, 4, 0)));
        rail.AddChild(Box.New().Width(18).Height(1).Background(Nocturne.Divider)
            .Margin(new Thickness(2, 0, 4, 0)));

        // Up to three quick bookmark jumps (initial letter), then the "+" manager.
        for (int i = 0; i < Math.Min(3, _bookmarks.Count); i++)
        {
            int idx = i;
            var b = _bookmarks[i];
            rail.AddChild(RailButton(b.Name[..1].ToUpperInvariant(),
                $"{b.Name} ({HotkeyLabel(idx)})", () => JumpBookmark(idx)));
        }
        rail.AddChild(RailButton("+", "Bookmarks (Ctrl+D)", OpenBookmarks));

        rail.AddChild(Box.New().FlexGrow(1));
        rail.AddChild(RailButton("⌘", "Commands (Ctrl+P)", TogglePalette));
        return rail;
    }

    private BoxElement RailButton(string glyph, string tooltip, Action onClick)
    {
        var b = Box.New().Width(26).Height(24)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .CornerRadius(5f).Cursor(CursorType.Pointer).Tooltip(tooltip)
            .Children(Text.New(glyph).FontSize(12f).TextColor(Nocturne.Neutral400));
        b.OnClick(_ => onClick());
        return b.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }

    // ── Pane (breadcrumb · list header · list · footer) ──────────────────
    // Design: the pane never changes background. Active is signalled only by an
    // inset 1px accent-800 ring. BoxShadow is outset-only here, so the ring is a
    // reserved 1px border (present on both panes for layout stability), coloured
    // accent-800 when active and transparent otherwise; the inter-pane divider is
    // pulled out into VDivider() so it doesn't fight the ring.
    private Element BuildPane(char side)
    {
        bool active = _active == side;
        var list = Listing(side);
        var state = PaneStateOf(side);

        var pane = Flex.Column(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch)
            .BorderColor(active ? Nocturne.Accent800 : Color.Transparent)
            .BorderWidth(1f);
        pane.Key = "pane-" + side;
        // The pane is a Tab stop: Tab traverses panes (and, in Full mode, the preview),
        // and clicking anywhere focuses the nearest focusable ancestor — this pane. The
        // active side follows focus, so Tab and click share one path. Defer the repaint:
        // focus can change during reconciliation (element removal), and a synchronous
        // rebuild there re-enters the differ.
        pane.IsFocusable = true;
        pane.WithRef(PaneRef(side));
        pane.On<FocusEvent>(_ =>
        {
            if (_active != side || _previewFocused)
            {
                _active = side; _previewFocused = false;
                HultUI.Application.InvokeOnMainThread(MarkDirty);
            }
        });

        // Tab strip on top, then breadcrumb + column header as one block on the
        // pane ground; the header's bottom border is the only divider to the list.
        pane.AddChild(BuildTabStrip(side));
        pane.AddChild(BuildBreadcrumb(side, active));
        pane.AddChild(BuildListHeader());
        // Non-ok states replace the row list with a centered status block.
        pane.AddChild(state == PaneState.Ok
            ? BuildList(side, list, active)
            : BuildStateBlock(side, state));
        pane.AddChild(BuildFooter(side, list));
        return pane;
    }

    // ── Tab strip ────────────────────────────────────────────────────────
    private Element BuildTabStrip(char side)
    {
        var strip = Flex.Row(gap: 2).FlexShrink(0).AlignItems(AlignItems.FlexEnd)
            .Padding(new Thickness(6, 8, 0, 8))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        var tabs = Tabs(side);
        for (int i = 0; i < tabs.Count; i++)
            strip.AddChild(TabChip(side, i, i == TabIndex(side)));

        // "+" opens a new tab on the active tab's folder.
        var plus = Box.New()
            .Padding(new Thickness(4, 9, 4, 9)).CornerRadius(new CornerRadius(6, 6, 0, 0))
            .Cursor(CursorType.Pointer).Tooltip("New tab (Ctrl+T)")
            .Children(Ui.Icon("+", 13f, Nocturne.On(0.45)));
        plus.OnClick(_ => NewTab(side));
        strip.AddChild(plus.HoverBg(Nocturne.Neutral900, Color.Transparent));

        // History cluster, right-aligned in the tabs row (design §A). Back / Forward
        // arrows; right-click either for the recent-folder list.
        strip.AddChild(Box.New().FlexGrow(1));
        strip.AddChild(BuildHistoryCluster(side));
        return strip;
    }

    private Element BuildHistoryCluster(char side)
    {
        var cluster = Flex.Row(gap: 0).AlignItems(AlignItems.Center);
        cluster.AddChild(HistoryButton(side, "‹", CanGoBack(side), () => GoBack(side), "Back · Alt+←"));
        cluster.AddChild(HistoryButton(side, "›", CanGoForward(side), () => GoForward(side), "Forward · Alt+→"));
        cluster.ContextMenu(() => HistoryMenu(side));
        return cluster;
    }

    private Element HistoryButton(char side, string glyph, bool enabled, Action onClick, string tooltip)
    {
        var btn = Box.New()
            .Padding(new Thickness(2, 8, 2, 8)).CornerRadius(5f)
            .Cursor(enabled ? CursorType.Pointer : CursorType.Default)
            .Tooltip(tooltip)
            .Children(Ui.Icon(glyph, 15f, enabled ? Nocturne.Neutral400 : Nocturne.Neutral700));
        if (enabled)
        {
            btn.OnClick(_ => { _active = side; onClick(); });
            btn.HoverBg(Nocturne.Neutral900, Color.Transparent);
        }
        return btn;
    }

    /// <summary>Recent-folder list for the history cluster's right-click menu — the
    /// tab's Back stack, most-recent first. Selecting one navigates there (recording
    /// it as a fresh move, so the current folder joins Back).</summary>
    private IReadOnlyList<ContextMenuItem> HistoryMenu(char side)
    {
        var tab = Tabs(side)[TabIndex(side)];
        var items = new List<ContextMenuItem>();
        for (int i = tab.Back.Count - 1; i >= 0 && items.Count < 12; i--)
        {
            string path = tab.Back[i];
            items.Add(new() { Glyph = "‹", Label = TabLabel(path), Action = () => { _active = side; NavigateTo(side, path); } });
        }
        if (items.Count == 0)
            items.Add(new() { Label = "No history yet", IsDisabled = true });
        return items;
    }

    private Element TabChip(char side, int index, bool on)
    {
        var tab = Tabs(side)[index];
        // Active tab connects to the content ground (bg) with a 2px accent top bar;
        // every tab reserves that 2px border so activating one doesn't shift the row.
        var chip = Flex.Row(gap: 5).AlignItems(AlignItems.Center)
            .Padding(new Thickness(2, 10, 4, 10))
            .CornerRadius(new CornerRadius(6, 6, 0, 0))
            .BorderColor(on ? Nocturne.Accent : Color.Transparent).BorderWidth(new Thickness(2, 0, 0, 0))
            .Cursor(CursorType.Pointer);
        if (on) chip.Background(Nocturne.Bg);

        if (tab.Locked)
            chip.AddChild(Ui.Icon("⚿", 10f, on ? Nocturne.Accent300 : Nocturne.On(0.40)));
        chip.AddChild(Text.New(TabLabel(tab.Dir)).FontSize(11.5f)
            .FontWeight(on ? FontWeight.Medium : FontWeight.Regular)
            .TextColor(on ? Nocturne.Text : Nocturne.Neutral500));

        chip.OnClick(_ => SwitchTab(side, index));
        chip.ContextMenu(() => TabMenu(side, index));
        // Middle-click closes the tab (a common convenience).
        chip.On<PointerDownEvent>(e => { if (e.Button == 1) CloseTab(side, index); });
        if (!on)
        {
            chip.OnPointerEnter(_ => chip.BackgroundColor = Nocturne.Neutral900);
            chip.OnPointerLeave(_ => chip.BackgroundColor = Color.Transparent);
        }
        return chip;
    }

    private IReadOnlyList<ContextMenuItem> TabMenu(char side, int index)
    {
        var tab = Tabs(side)[index];
        var canClose = Tabs(side).Count > 1;
        return
        [
            new() { Glyph = "⧉", Label = "Duplicate tab", Action = () => DuplicateTab(side, index) },
            new() { Glyph = "⇄", Label = "Open in other pane", Action = () => OpenTabInOtherPane(side, index) },
            new() { Glyph = tab.Locked ? "⚿" : "⚿", Label = tab.Locked ? "Unlock tab" : "Lock tab (read-only)", Action = () => ToggleTabLock(side, index) },
            ContextMenuItem.Separator(),
            new() { Label = "Close tab", ShortcutHint = "Ctrl+W", Action = () => CloseTab(side, index), IsDisabled = !canClose },
            new() { Label = "Close others", Action = () => CloseOtherTabs(side, index), IsDisabled = !canClose },
        ];
    }

    private Element BuildBreadcrumb(char side, bool active)
    {
        var dir = Dir(side);
        // Transparent, no border — groups with the header below it. Spacing is
        // uniform via the row gap alone (no per-separator margins), so every "/"
        // sits the same distance from its neighbours.
        var row = Flex.Row(gap: 3).FlexShrink(0).AlignItems(AlignItems.Center)
            .FlexWrap(FlexWrap.NoWrap).Overflow(Overflow.Hidden)
            .Padding(new Thickness(7, 12, 5, 12));

        // Drive segment: monospace accent crumb → jumps the pane to the volume root.
        // Trim the trailing separator so the first "/" divider isn't doubled ("C:\ /").
        string root = System.IO.Path.GetPathRoot(dir) ?? dir;
        string driveLabel = root.TrimEnd(System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar);
        row.AddChild(DriveCrumb(side, driveLabel, root, active));

        var rest = dir.Length > root.Length ? dir[root.Length..] : "";
        var segs = rest.Split(
            new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        // Full navigation target for each segment (its path prefix).
        var prefixes = new string[segs.Length];
        var p = root;
        for (int i = 0; i < segs.Length; i++) { p = System.IO.Path.Combine(p, segs[i]); prefixes[i] = p; }

        // Left-truncate by WHOLE folders: keep the drive + as many trailing segments
        // as fit, and if any leading ones are dropped show a single "…" after the
        // drive → "C: / … / folder 3 / folder 4". The leaf is always kept.
        float budget = BreadcrumbBudget() - EstTextWidth(driveLabel, mono: true) - EstSepWidth();
        int start = segs.Length;              // first kept segment index
        float used = 0;
        for (int i = segs.Length - 1; i >= 0; i--)
        {
            float w = EstSepWidth() + EstTextWidth(segs[i], mono: false);
            if (i < segs.Length - 1 && used + w > budget) break;   // always keep the leaf
            used += w;
            start = i;
        }
        bool truncated = start > 0;
        if (truncated) budget -= EstSepWidth() + EstTextWidth("…", mono: false);
        // Dropping the ellipsis's width may push the last kept segment over; trim one more.
        while (truncated && start < segs.Length - 1 && used > budget)
            used -= EstSepWidth() + EstTextWidth(segs[start++], mono: false);

        if (truncated)
        {
            row.AddChild(Sep());
            // "…" is a shortcut to the deepest hidden folder, not a no-op.
            row.AddChild(Crumb(side, "…", prefixes[start - 1], leaf: false));
        }
        for (int i = start; i < segs.Length; i++)
        {
            row.AddChild(Sep());
            row.AddChild(Crumb(side, segs[i], prefixes[i], leaf: i == segs.Length - 1));
        }
        return row;
    }

    private static Element Sep() =>
        Text.New("/").FlexShrink(0).FontSize(11.5f).TextColor(Nocturne.Neutral600);

    private BoxElement DriveCrumb(char side, string label, string root, bool active)
    {
        var d = Box.New().FlexShrink(0)
            .Padding(new Thickness(1, 5, 1, 5)).CornerRadius(4f).Cursor(CursorType.Pointer)
            .Children(Ui.Mono(label, 11.5f, active ? Nocturne.Accent300 : Nocturne.On(0.55)));
        d.OnClick(_ => { _active = side; OpenDrivePicker(side); });
        return d.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }

    private BoxElement Crumb(char side, string label, string target, bool leaf)
    {
        var c = Box.New().FlexShrink(0)
            .Padding(new Thickness(1, 5, 1, 5)).CornerRadius(4f)
            .Cursor(CursorType.Pointer)
            .Children(Text.New(label).FontSize(11.5f)
                .FontWeight(leaf ? FontWeight.Medium : FontWeight.Regular)
                .TextColor(leaf ? Nocturne.Text : Nocturne.Neutral400));
        c.OnClick(_ => { _active = side; NavigateTo(side, target); });
        return c.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }

    // Approximate widths for breadcrumb truncation (layout runs after Build, so we
    // budget from estimates). Deliberately a little generous so a kept crumb never
    // clips mid-word; if the estimate is off the row still clips gracefully.
    private float BreadcrumbBudget()
    {
        float win = _window.LayoutWidth > 0 ? _window.LayoutWidth : 1240f;
        // Side: two panes + 300px preview. Off: two panes. Full: one pane + preview
        // (both 1fr) — same per-pane width as Off.
        float paneW = _settings.Preview == PreviewMode.Normal
            ? (win - RailWidth - PreviewWidth - 2f) / 2f
            : (win - RailWidth - 1f) / 2f;
        return paneW - 2f /*pane border*/ - 24f /*breadcrumb h-padding*/ - 6f;
    }

    private static float EstTextWidth(string s, bool mono) =>
        s.Length * (mono ? 7.2f : 6.6f) + 10f /*crumb h-padding*/;

    private static float EstSepWidth() => 6f + 2 * 3f /*slash + row gaps*/;

    // Column layout depends on which optional columns (Size / Date) are enabled.
    private (string cols, int sizeCol, int dateCol) ListLayout()
    {
        var cols = "16px 1fr";
        int next = 2, sizeCol = -1, dateCol = -1;
        if (_settings.ShowSize) { cols += " 60px"; sizeCol = next++; }
        if (_settings.ShowDate) { cols += " 116px"; dateCol = next++; }
        return (cols, sizeCol, dateCol);
    }

    private Element BuildListHeader()
    {
        var (cols, sizeCol, dateCol) = ListLayout();
        var h = Grid.Of(cols, gap: 10).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 10, 5, 10))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        h.AddChild(Box.New().GridColumn(0));
        h.AddChild(HeaderCell("Name", SortMode.Name, rightAlign: false).GridColumn(1));
        if (sizeCol >= 0) h.AddChild(HeaderCell("Size", SortMode.Size, rightAlign: true).GridColumn(sizeCol));
        if (dateCol >= 0) h.AddChild(HeaderCell("Modified", SortMode.Date, rightAlign: false).GridColumn(dateCol));
        return h;
    }

    /// <summary>A clickable, sortable column header. The active column shows an accent
    /// direction triangle and its label lifts to full-strength text; inactive labels
    /// stay Neutral600. Click cycles asc↔desc on the same column or switches column
    /// (see <see cref="SetSort"/>). The icon column is not built through here (unsortable).</summary>
    private Element HeaderCell(string label, SortMode col, bool rightAlign)
    {
        bool active = _settings.Sort == col;
        // No explicit width: the grid stretches the cell child to the track, and
        // JustifyContent aligns the label+triangle within it (a percent width would
        // resolve to 0 in the fixed 60px Size track and hide the label).
        var row = Flex.Row(gap: 4).AlignItems(AlignItems.Center)
            .JustifyContent(rightAlign ? JustifyContent.FlexEnd : JustifyContent.FlexStart)
            .Padding(new Thickness(0, 4, 0, 4)).CornerRadius(4f)
            .Cursor(CursorType.Pointer);
        row.AddChild(Text.New(label.ToUpperInvariant()).FontSize(10f).LetterSpacing(0.08f * 10f)
            .TextColor(active ? Nocturne.Text : Nocturne.Neutral600));
        if (active)
            row.AddChild(Ui.Icon(_settings.SortDescending ? "▾" : "▴", 9f, Nocturne.Accent));
        row.OnClick(_ => SetSort(col));
        return row.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }

    private Element BuildList(char side, IReadOnlyList<FileEntry> list, bool active)
    {
        // Cursor is tracked by index; compare by name so it survives row recycling.
        // Only the active pane renders a cursor (design: the caret follows focus).
        int cur = Math.Clamp(Cursor(side), 0, Math.Max(0, list.Count - 1));
        string? cursorName = active && list.Count > 0 && cur < list.Count ? list[cur].Name : null;
        var marks = Marks(side);

        // VirtualListElement only re-realizes its rows when the Items *reference*
        // changes (PatchFrom checks ReferenceEquals). Our cached listing keeps the
        // same reference across rebuilds, so a mark/cursor/search change would leave
        // stale (or blank) rows. Hand it a fresh zero-copy snapshot each build to
        // force re-realization of the ~viewport visible rows against current state.
        return VirtualListElement<FileEntry>.New()
            .ItemHeight((int)RowHeight)
            .Items(new ListSnapshot(list))
            .RenderWith(e => FileRow(side, e, cursorName, marks.Contains(e.Name)))
            .WithRef(VList(side))
            .FlexGrow(1);
    }

    /// <summary>A distinct-reference, zero-copy view over the cached listing, so the
    /// VirtualList treats each build as new items and re-renders visible rows.</summary>
    private sealed class ListSnapshot(IReadOnlyList<FileEntry> inner) : IReadOnlyList<FileEntry>
    {
        public FileEntry this[int i] => inner[i];
        public int Count => inner.Count;
        public IEnumerator<FileEntry> GetEnumerator() => inner.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => inner.GetEnumerator();
    }

    private Element FileRow(char side, FileEntry e, string? cursorName, bool marked)
    {
        bool cursor = e.Name == cursorName;
        // Marked (red) overrides the dir/file colour; metadata dims to the red tint.
        Color fg = marked ? Nocturne.MarkRed : e.IsDir ? Nocturne.DirText : Nocturne.Text;
        Color metaFg = marked ? Nocturne.MarkRedDim : Nocturne.On(0.50);
        // A cut-pending row ghosts to ~45% (icon + name) with dimmed metadata — reads as
        // "leaving", distinct from a mark (red) and the cursor ring (which paints over it).
        bool cut = !marked && _cutPending.Contains(e.FullPath);
        if (cut) { fg = Nocturne.Alpha(fg, 0.45); metaFg = Nocturne.Neutral700; }
        var (cols, sizeCol, dateCol) = ListLayout();

        // Every row reserves a 1px border so the cursor ring never shifts siblings.
        var row = Grid.Of(cols, gap: 10).Height(RowHeight)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 10, 0, 10))
            .BorderWidth(1f).BorderColor(cursor ? Nocturne.Accent600 : Color.Transparent)
            .Cursor(CursorType.Default);

        if (cursor)
            row.Background(Nocturne.Alpha(Nocturne.Accent500, 0.13));
        else if (_settings.Zebra && !marked && !e.IsParent
                 && IndexOfName(Listing(side), e.Name) % 2 == 1)
            row.Background(Nocturne.Alpha(Nocturne.Text, 0.03));   // alternating tint

        // Canvas-drawn type icon; coloured by category (marked rows stay red).
        var kind = Icons.KindFor(e);
        Color iconColor = marked ? Nocturne.MarkRed : kind switch
        {
            IconKind.Folder => Nocturne.DirText,
            IconKind.Code => Nocturne.Accent300,
            IconKind.Image => Color.FromHex("#8fd0b0"),
            IconKind.Archive => Color.FromHex("#d9a06b"),
            _ => Nocturne.On(0.55),
        };
        if (cut) iconColor = Nocturne.Alpha(iconColor, 0.45);
        row.AddChild(Icons.Draw(kind, iconColor).GridColumn(0));
        row.AddChild(Ui.Mono(e.Name, 12f, fg).TextAlign(TextAlign.Left)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis).GridColumn(1));
        if (sizeCol >= 0)
        {
            // A Space-measured folder shows its byte total in place of "<DIR>"; while the
            // walk is still running it counts up live with a trailing "…".
            string sizeText = MeasuredDir(e) is (var b, var done)
                ? FileSystem.ByteCount(b) + (done ? "" : "…")
                : FileSystem.SizeLabel(e);
            row.AddChild(Ui.Mono(sizeText, 11f, metaFg).TextAlign(TextAlign.Right).GridColumn(sizeCol));
        }
        if (dateCol >= 0)
            row.AddChild(Ui.Mono(FileSystem.DateLabel(e), 11f, metaFg).GridColumn(dateCol));

        // Select on left mouse-DOWN (not click), so the cursor tracks the press
        // immediately — matches file-manager feel and makes press-and-act snappier.
        row.On<PointerDownEvent>(ev =>
        {
            // Right button starts a mark-drag (no cursor move, no context menu here).
            if (ev.Button == 2) { BeginRightMark(side, e); return; }
            if (ev.Button != 0) return;
            _active = side;
            int idx = IndexOfName(Listing(side), e.Name);
            if (idx >= 0) SetCursor(side, idx);
            // Detect a double-press here rather than via DoubleClickEvent: selecting a
            // row MarkDirty()s, and the VirtualList re-realizes rows with fresh element
            // identities, so the framework's click synthesis (down-target == up-target
            // by reference) never fires for recycled rows. Two presses on the same
            // entry within the window activate it, like Enter.
            if (IsDoublePress(side, e.Name)) ActivateCursor(side);
            MarkDirty();
        });
        // While a right-drag is active, mark every row the pointer moves onto. Moves
        // hit-test fresh each sample, so recycled rows still receive this.
        row.On<PointerMoveEvent>(_ => { if (_rmarkActive) ApplyRightMark(side, e); });
        return row;
    }

    private Element BuildFooter(char side, IReadOnlyList<FileEntry> list)
    {
        int files = 0, dirs = 0;
        foreach (var e in list)
        {
            if (e.IsParent) continue;
            if (e.IsDir) dirs++; else files++;
        }
        int marked = Marks(side).Count;
        var free = FileSystem.FreeLabel(Dir(side));

        var footer = Flex.Row(gap: 0).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4, 12, 4, 12))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        // "✂ N cut · " when this pane holds cut-pending rows, then the mark counter.
        int cut = _cutPending.Count(p => PathEquals(System.IO.Path.GetDirectoryName(p) ?? "", Dir(side)));
        if (cut > 0)
            footer.AddChild(Ui.Mono($"✂ {cut} cut — Ctrl+V moves · Esc cancels · ", 10.5f, Nocturne.Accent300));
        // "<n> marked · " in red, then the plain counts + free space.
        if (marked > 0)
            footer.AddChild(Ui.Mono($"{marked} marked · ", 10.5f, Nocturne.MarkRed));
        var label = $"{files} files, {dirs} dirs" + (free.Length > 0 ? $" · {free}" : "");
        footer.AddChild(Ui.Mono(label, 10.5f, Nocturne.On(0.50)));
        return footer;
    }

    // ── Pane content states (empty · offline · denied) ──────────────────
    private Element BuildStateBlock(char side, PaneState state)
    {
        var wrap = Flex.Column(gap: 0).FlexGrow(1)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        var block = Flex.Column(gap: 8).Height(120)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);

        switch (state)
        {
            case PaneState.Empty:
                block.AddChild(Text.New("This folder is empty").FontSize(12f).TextColor(Nocturne.Neutral500));
                var hint = Flex.Row(gap: 6).AlignItems(AlignItems.Center);
                hint.AddChild(Ui.Kbd("F7"));
                hint.AddChild(Text.New("new folder").FontSize(11f).TextColor(Nocturne.Neutral500));
                block.AddChild(hint);
                break;

            case PaneState.Offline:
                block.AddChild(Ui.Icon("⊘", 20f, Nocturne.Neutral600));
                var drive = (System.IO.Path.GetPathRoot(Dir(side)) ?? Dir(side))
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                var line = Flex.Row(gap: 5).AlignItems(AlignItems.Center);
                line.AddChild(Ui.Mono(drive, 12f, Nocturne.Neutral300));
                line.AddChild(Text.New("is not available").FontSize(12f).TextColor(Nocturne.Neutral400));
                block.AddChild(line);
                block.AddChild(GhostButton("Reconnect", () => RetryPane(side)));
                break;

            case PaneState.Denied:
                block.AddChild(Ui.Icon("⚿", 20f, Nocturne.Neutral600));
                block.AddChild(Text.New("Access denied").FontSize(12f).TextColor(Nocturne.Neutral400));
                block.AddChild(Ui.Mono(Dir(side), 10.5f, Nocturne.On(0.35))
                    .MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
                block.AddChild(GhostButton("Retry as administrator", () => RetryPane(side)));
                break;
        }
        wrap.AddChild(block);
        return wrap;
    }

    // ── Preview column (kicker · text excerpt · metadata) ────────────────
    // full: the panel fills the right pane's space (Ctrl+Q "full" mode) rather than
    // sitting as a fixed 300px column.
    private Element BuildPreview(bool full)
    {
        var side = _active;
        var list = Listing(side);
        int cur = Cursor(side);
        var focused = cur >= 0 && cur < list.Count ? list[cur] : null;

        var col = Flex.Column(gap: 0)
            .AlignItems(AlignItems.Stretch).Overflow(Overflow.Hidden)
            .Background(Nocturne.Surface);
        // Full mode reserves a 1px ring (accent when the preview holds keyboard focus)
        // so tabbing into it never shifts the layout.
        if (full)
            col.FlexGrow(1).BorderWidth(1f)
                .BorderColor(_previewFocused ? Nocturne.Accent800 : Color.Transparent);
        else
            col.Width(PreviewWidth).FlexShrink(0);

        var kicker = "Preview" + (focused != null && !focused.IsParent ? " · " + focused.Name : "");
        col.AddChild(Text.New(kicker.ToUpperInvariant()).FontSize(10f).FlexShrink(0)
            .LetterSpacing(0.08f * 10f).TextColor(Nocturne.Neutral600)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis)
            .Padding(new Thickness(8, 12, 6, 12)));

        if (focused == null || focused.IsParent)
            return col;

        var key = $"{focused.FullPath}#{focused.Modified.Ticks}#{focused.Size}";
        HultUI.Core.Rendering.IImageSource? image = null;

        if (focused.IsDir)
        {
            col.AddChild(Box.New().FlexGrow(1));
        }
        else if (Icons.KindFor(focused) == IconKind.Image)
        {
            // Off-thread image decode; render it scaled to fit, aspect preserved.
            _imageLoader.Load(key, () => LoadImage(focused.FullPath, focused.Size), onLoaded: img =>
            {
                if (!ReferenceEquals(_lastImage, img)) { (_lastImage as IDisposable)?.Dispose(); _lastImage = img; }
                MarkDirty();
            });
            image = _imageLoader.Value;
            var body = Box.New().FlexGrow(1).Overflow(Overflow.Hidden)
                .Padding(new Thickness(8, 12, 8, 12))
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center);
            if (image is { IsLoaded: true } src && src.IntrinsicSize.Width > 0)
            {
                // Contain fills the box and letterboxes; works at any panel width.
                var img = Image.New(src);
                img.Fit = ImageFit.Contain;
                body.Children(img.Size(Dimension.Percent(100), Dimension.Percent(100)));
            }
            else
            {
                body.Children(Text.New(_imageLoader.IsLoading ? "Loading…" : "Cannot preview")
                    .FontSize(11f).TextColor(Nocturne.On(0.40)));
            }
            col.AddChild(body);
        }
        else
        {
            // Text excerpt (off-thread; empty for binary/large). A read-only, selectable
            // TextArea: drag or Shift+arrows select, Ctrl+C / the context menu copies,
            // and it scrolls itself (wheel, and — in Full mode, once Tab-focused — the
            // arrow / page / home / end keys). Keyed per file so a new file starts at the
            // top. Wrapped in a padded box because the theme owns the TextArea's padding.
            _previewLoader.Load(key, () => FileSystem.PreviewText(focused.FullPath, focused.Size),
                onLoaded: _ => MarkDirty());
            var excerpt = _previewLoader.Value;
            if (excerpt.Length == 0)
            {
                col.AddChild(Box.New().FlexGrow(1));
            }
            else
            {
                var wrap = Box.New().FlexGrow(1).Overflow(Overflow.Hidden)
                    .Padding(new Thickness(4, 4, 8, 12));
                var body = TextArea.New(excerpt).ReadOnly().FlexGrow(1)
                    .FontFamily(Fonts.Mono).FontSize(10.5f).TextColor(Nocturne.Neutral400)
                    .LineHeight(1.6f);
                body.ScrollbarWidth = 7f;
                body.Key = "pv-text#" + key;
                if (Icons.KindFor(focused) == IconKind.Code)
                    body.Spans(SyntaxHighlight.Spans(excerpt));   // tint keywords/types/comments
                // Focus drives the Full-mode ring. Attach handlers UNCONDITIONALLY —
                // reconciliation doesn't copy event handlers, and this element is keyed
                // per file, so a handler added only in a Full build would never appear
                // when Side→Full patches the same element. Handlers capture only `this`.
                // Blur can fire mid-reconcile (element removed while focused, e.g. Ctrl+Q
                // away), so defer the repaint to avoid re-entering the differ.
                body.On<FocusEvent>(_ => { _previewFocused = true; HultUI.Application.InvokeOnMainThread(MarkDirty); });
                body.On<BlurEvent>(_ => { _previewFocused = false; HultUI.Application.InvokeOnMainThread(MarkDirty); });
                // Click always focuses to select; only Full mode joins the Tab cycle
                // (TabIndex -1 keeps the side preview clickable but out of Tab traversal).
                body.IsFocusable = true;
                body.TabIndex = full ? 0 : -1;
                wrap.AddChild(body);
                col.AddChild(wrap);
            }
        }

        // Metadata grid (Size / Modified / Attrs), separated by a top divider.
        var meta = Grid.Of("auto 1fr", gap: 3).FlexShrink(0)
            .ColumnGap(12)
            .Padding(new Thickness(8, 12, 8, 12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        int mr = 0;
        string sizeMeta = MeasuredDir(focused) is (var mb, var mdone)
            ? FileSystem.ByteCount(mb) + (mdone ? "" : " · calculating…")
            : focused.IsDir ? "Directory" : FileSystem.SizeLabel(focused);
        AddMeta(meta, mr++, "Size", sizeMeta);
        AddMeta(meta, mr++, "Modified", FileSystem.DateLabel(focused));
        if (image is { IsLoaded: true } isrc && isrc.IntrinsicSize.Width > 0)
            AddMeta(meta, mr++, "Dimensions", $"{(int)isrc.IntrinsicSize.Width} × {(int)isrc.IntrinsicSize.Height}");
        AddMeta(meta, mr, "Attrs", FileSystem.AttrLabel(focused.Attributes));
        col.AddChild(meta);
        return col;
    }

    private static void AddMeta(BoxElement grid, int row, string label, string value)
    {
        grid.AddChild(Text.New(label).FontSize(11f).TextColor(Nocturne.On(0.45))
            .GridCell(row, 0));
        grid.AddChild(Ui.Mono(value, 11f, Nocturne.On(0.80)).GridCell(row, 1));
    }

    // ── Command line (idle status for now) ───────────────────────────────
    private Element BuildCommandLine()
    {
        var row = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(7, 12, 7, 12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        row.AddChild(Ui.Mono("❯", 12.5f, Nocturne.Accent));
        // A backgrounded transfer shows a compact indicator here.
        if (_op is { } op && !_progressOpen)
        {
            int pct = op.TotalBytes > 0 ? (int)(100 * op.TotalBytesDone / op.TotalBytes) : 0;
            row.AddChild(Ui.Mono($"▶ copying {pct}% · {op.Done}/{op.Items.Count}", 11.5f, Nocturne.Accent300));
        }
        // While a quick-search is active, show it with a block caret; else an idle hint.
        else if (_search.Length > 0)
        {
            row.AddChild(Ui.Mono($"search: {_search}", 11.5f, Nocturne.Text));
            row.AddChild(Box.New().Width(7).Height(14).Background(Nocturne.Accent));
        }
        else
        {
            row.AddChild(Ui.Mono("Tab switch pane · ↑↓ move · Enter open · Space mark · type to find",
                11f, Nocturne.On(0.40)));
        }

        row.AddChild(Box.New().FlexGrow(1));
        // Right-aligned key hints.
        foreach (var (k, label) in new[] { ("F5", "copy"), ("F6", "move"), ("F8", "delete"), ("Space", "mark") })
        {
            row.AddChild(Ui.Kbd(k));
            row.AddChild(Text.New(label).FontSize(10f).TextColor(Nocturne.On(0.40)));
        }
        return row;
    }
}
