using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Icons;
using VesperCommander.Theme;

namespace VesperCommander;

public partial class CommanderApp
{
    // Shared grid template for the header and every file row: icon · name · size · date.

    /// <summary>A 1px full-height divider between panes (the design's per-pane
    /// <c>border-right</c>, pulled out so the active-pane ring can stay layout-stable).</summary>
    private static Element VDivider() =>
        Box.New().Width(1).FlexShrink(0).Background(Nocturne.Divider);

    // ── Chrome (brand · search & commands · window controls) ─────────────
    // Mirrors Junction's title bar so the two apps read as one family: a
    // text brand pinned left (the app-mark tile lives in the rail, as in
    // Junction's sidebar), a centred "Search & commands" box that opens the
    // palette, and icon-glyph window controls flush right.
    private Element BuildChrome()
    {
        var bar = Flex.Row(gap: 0).Height(40).FlexShrink(0)
            .AlignItems(AlignItems.Stretch)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        // Brand pinned left with a fixed inset; draggable (no handlers on children).
        var crumb = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 0, 0, 14));
        crumb.AddChild(Text.New("Vesper Commander").FontSize(13f)
            .FontWeight(FontWeight.Medium).TextColor(Nocturne.On(0.70)));
        crumb.WindowDrag();
        bar.AddChild(crumb);

        // Right group: search box + window controls, flush right.
        var right = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Center);
        // Leading draggable filler so the gap between the brand and the search box
        // drags the window too (a plain grow spacer has no WindowDrag ancestor).
        right.AddChild(Flex.Row(gap: 0).FlexGrow(1).AlignSelf(AlignSelf.Stretch).WindowDrag());

        var search = Flex.Row(gap: 8)
            .Height(26).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 8, 0, 10))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer)
            .Tooltip(KeyText(Cmd.Palette) is { Length: > 0 } pk ? $"Search & commands ({pk})" : "Search & commands");
        search.AddChild(Ui.Icon(Glyphs.Search, 13f, Nocturne.On(0.55)));
        search.AddChild(Text.New("Search & commands").FontSize(12f).TextColor(Nocturne.On(0.55)));
        if (KeyText(Cmd.Palette) is { Length: > 0 } paletteKeys) search.AddChild(Ui.Kbd(paletteKeys));
        search.OnClick(_ => TogglePalette());
        search.HoverBg(Nocturne.Alpha(Nocturne.Accent, 0.08), Nocturne.Bg);
        right.AddChild(search);

        right.AddChild(Box.New().Width(10));

        var controls = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch);
        controls.AddChild(WinButton(Glyphs.WinMinimize, "Minimize", () => _window.Minimize(), Nocturne.Neutral800));
        controls.AddChild(WinButton(Glyphs.WinMaximize, "Maximize", () => _window.ToggleMaximize(), Nocturne.Neutral800)
            .WindowMaximizeButton());
        controls.AddChild(WinButton(Glyphs.WinClose, "Close", () => _window.Close(), new Color(232, 17, 35)));
        right.AddChild(controls);

        bar.AddChild(right);
        return bar;
    }

    private BoxElement WinButton(string icon, string tooltip, Action onClick, Color hoverBg)
    {
        // No pointer cursor: Windows caption buttons keep the arrow, and the maximize
        // button here has to anyway — it is non-client so Snap Layouts works, which puts
        // the cursor under the OS. Setting it on the other two only made the trio
        // inconsistent with each other and with every other window on the desktop.
        var b = Flex.Row(gap: 0).Width(46).Height(40)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Tooltip(tooltip);
        // 13 rather than 11: the caption marks are hairlines, and below this the bar in
        // the minimize glyph lands under one device pixel and washes out.
        b.AddChild(Ui.Icon(icon, 13f, Nocturne.On(0.75)));
        b.OnClick(_ => onClick());
        return b.HoverBg(hoverBg, Color.Transparent);
    }

    // ── Left rail (app-mark · bookmark jumps · + manager) ────────────────
    // The left panel adopts Junction's sidebar treatment: it sits on the app
    // ground (Bg) with a hairline right divider, and the app identity is an
    // accent-tile mark (Junction's sidebar app-mark). Commands moved to the
    // title-bar search box, so the rail is a top-aligned bookmark strip.
    private Element BuildRail()
    {
        var rail = Flex.Column(gap: 6).Width(RailWidth).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(10, 0, 10, 0))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        rail.AddChild(Box.New().Width(26).Height(26).FlexShrink(0)
            .CornerRadius(Nocturne.RadiusMd).Background(Nocturne.Accent800)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Margin(new Thickness(2, 0, 4, 0))
            .Children(Text.New("V").FontSize(13f).FontWeight(FontWeight.SemiBold)
                .TextColor(Nocturne.Accent200)));
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
        rail.AddChild(RailTile(Ui.Icon(Glyphs.Plus, 13f, Nocturne.Neutral300),
            "Bookmarks (Ctrl+D)", OpenBookmarks));
        return rail;
    }

    // A bookmark's initial letter. Text, not an icon — it is the bookmark's name, and
    // there is no glyph for "D".
    private BoxElement RailButton(string letter, string tooltip, Action onClick) =>
        RailTile(Text.New(letter).FontSize(12f).FontWeight(FontWeight.SemiBold)
            .TextColor(Nocturne.Neutral300), tooltip, onClick);

    // A rail action as a Junction-style tile: a rounded neutral square with a
    // hairline border, sized to match the accent app-mark.
    private BoxElement RailTile(Element content, string tooltip, Action onClick)
    {
        var b = Box.New().Width(26).Height(26).FlexShrink(0)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Neutral900)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer).Tooltip(tooltip)
            .Children(content);
        b.OnClick(_ => onClick());
        return b.HoverBg(Nocturne.Neutral800, Nocturne.Neutral900);
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
        var list = Rows(side);   // filtered view while a type-to-find is active
        var state = PaneStateOf(side);
        BeginFit(side);

        var pane = Flex.Column(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch)
            .BorderColor(active ? Nocturne.Accent800 : Color.Transparent)
            .BorderWidth(1f);
        // Optional accent glow around the active pane (Colors → "Glow on the active pane").
        if (active && _settings.Glow)
            pane.BoxShadow(0, 0, 14, Nocturne.Alpha(Nocturne.Accent, 0.3));
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
        // Mouse back / forward buttons navigate this pane's history — the same as
        // Alt+←/→. The press targets whichever pane it lands on.
        pane.On<PointerDownEvent>(ev =>
        {
            if (ev.Button is not (PointerEvent.BackButton or PointerEvent.ForwardButton) || AnyOverlayOpen) return;
            _active = side;
            if (ev.Button == PointerEvent.BackButton) GoBack(side); else GoForward(side);
            ev.StopPropagation = true;
        });

        // The whole pane takes a drop into its own folder; a folder row inside it takes
        // one into that folder instead (AttachEntryPointer), stopping the event so the more
        // specific target wins. A drag hovering here rings the pane in the accent colour.
        AttachDropTarget(pane, side, Dir(side), isRow: false);
        if (IsDropHighlighted(side, Dir(side)))
            pane.BorderColor(Nocturne.Accent).BorderWidth(1f);

        // Tab strip on top, then breadcrumb + column header as one block on the
        // pane ground; the header's bottom border is the only divider to the list.
        pane.AddChild(BuildTabStrip(side));
        // A fed-in result set replaces the path trail outright — it is not a place, so a
        // trail of folders would be a lie about where the pane is.
        var crumb = ResultsOf(side) is { } results
            ? BuildResultsBreadcrumb(side, active, results)
            : BuildBreadcrumb(side, active);
        // Keyed: the two are different elements at one position, and the reconciler does not
        // copy handlers across a keyless swap.
        crumb.Key = ResultsOf(side) != null ? "crumb-results" : "crumb-path";
        pane.AddChild(crumb);
        // The column header belongs to Detail only — Brief and Thumbnails have no columns.
        if (ViewOf(side) == ViewMode.Detail)
            pane.AddChild(BuildListHeader(side, InArchive(side)));
        // Non-ok states replace the list with a centered status block. An archive that
        // couldn't be read (encrypted / corrupt / needs-7-Zip) shows its own message. A
        // filter that hides everything shows a "no matches" hint instead of a bare ".." row.
        bool noMatches = _search.Length > 0 && active && !list.Any(e => !e.IsParent);
        pane.AddChild(state != PaneState.Ok
            // While connecting, the held archive status belongs to the folder being left —
            // showing its error over the wait would be a message about somewhere else.
            ? (state != PaneState.Connecting
                    && ArchiveStatusOf(side) is { } arcStatus && arcStatus is not ArchiveStatus.Empty
                ? BuildArchiveError(side, arcStatus)
                : BuildStateBlock(side, state))
            : noMatches
                ? BuildNoMatches()
                : BuildViewArea(side, list, active));
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
            .Children(Ui.Icon(Glyphs.Plus, 13f, Nocturne.On(0.45)));
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
        // Design §A: back · caret · forward as one visual unit — 26/16/26 × 24, gap 0,
        // radius Sm, right-aligned so the cluster's right edge sits at the pane's 8px
        // padding. Right-click anywhere (or left-click the caret) opens the recents list.
        // The tab strip aligns its children to the bottom (tabs meet the content border),
        // so the cluster opts out with AlignSelf.Center to sit centered in the strip.
        var cluster = Flex.Row(gap: 0).AlignItems(AlignItems.Center).AlignSelf(AlignSelf.Center);
        cluster.AddChild(NavBtn(side, IconKind.NavBack, CanGoBack(side),
            $"hist-back-{side}", "Back", "Back · Alt+←", () => GoBack(side)));
        cluster.AddChild(CaretBtn(side));
        cluster.AddChild(NavBtn(side, IconKind.NavForward, CanGoForward(side),
            $"hist-fwd-{side}", "Forward", "Forward · Alt+→", () => GoForward(side)));
        // Right-click anywhere in the cluster also opens the recent-folder popover.
        cluster.On<PointerDownEvent>(e =>
        {
            if (e.Button != PointerEvent.RightButton || ActiveTab(side).Back.Count == 0) return;
            e.StopPropagation = true;
            _active = side;
            OpenHistoryPopover(side, e.CurrentTarget);   // live cluster, not the captured build's copy
        });
        return cluster;
    }

    // A back/forward chevron: a fixed 26×24 hit target with a Neutral400 glyph when
    // enabled (Neutral700 + no handler otherwise). Acts on ClickEvent and stops
    // propagation so a pane-focus rebuild can't swallow the gesture. Carries an
    // AutomationId + Button role so it's reachable by assistive tech (and tests).
    private Element NavBtn(char side, IconKind icon, bool enabled,
        string autoId, string name, string tooltip, Action onClick)
    {
        var btn = Box.New().Width(26).Height(24)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .CornerRadius(5f)
            .Cursor(enabled ? CursorType.Pointer : CursorType.Default)
            .Tooltip(tooltip)
            .Children(Icons.Draw(icon, enabled ? Nocturne.Neutral400 : Nocturne.Neutral700, 13f));
        btn.AutomationId = autoId;
        btn.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        btn.AccessibleName = enabled ? name : $"{name} (unavailable)";
        if (enabled)
        {
            btn.On<ClickEvent>(e => { e.StopPropagation = true; _active = side; onClick(); });
            btn.HoverBg(Nocturne.Neutral900, Color.Transparent);
        }
        return btn;
    }

    // The 16×24 caret: opens the recent-folder dropdown anchored just under itself.
    private Element CaretBtn(char side)
    {
        bool hasHist = ActiveTab(side).Back.Count > 0;
        var btn = Box.New().Width(16).Height(24)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .CornerRadius(5f)
            .Cursor(hasHist ? CursorType.Pointer : CursorType.Default)
            .Tooltip("Recent folders")
            .Children(Icons.Draw(IconKind.NavCaret, hasHist ? Nocturne.Neutral400 : Nocturne.Neutral700, 11f));
        btn.AutomationId = $"hist-caret-{side}";
        btn.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        btn.AccessibleName = "Recent folders";
        if (hasHist)
        {
            btn.On<ClickEvent>(e =>
            {
                e.StopPropagation = true;
                _active = side;
                OpenHistoryPopover(side, e.CurrentTarget.Parent!);   // anchor to the live cluster (right-aligned)
            });
            btn.HoverBg(Nocturne.Neutral900, Color.Transparent);
        }
        return btn;
    }

    // ── Recent-folders popover (caret click / cluster right-click) ───────
    // The framework's popover, right-aligned under the back · caret · forward cluster: the
    // current folder and the Back stack, each row a folder icon + full path (collapsed to
    // C:\…\last\few when it won't fit). It takes the keyboard — ↑ ↓ walk the rows, Enter
    // jumps, Esc closes, Alt+← / Alt+→ still go back and forward — and a press on the cluster
    // while it is open only closes it.
    private Popover? _historyPopover;
    private char _historySide;

    private Popover HistoryPopover => _historyPopover ??= new Popover(BuildHistoryPopover)
    {
        Alignment = PopoverAlignment.End,
        ArrowKeys = true,
        AccessibleName = "Recent folders",
        AutomationId = "history-pop",
    };

    private void OpenHistoryPopover(char side, Element anchor)
    {
        _historySide = side;
        HistoryPopover.Open(anchor);
    }

    private void CloseHistoryPopover() => _historyPopover?.Close();

    private Element BuildHistoryPopover()
    {
        const float w = 300f;
        char side = _historySide;
        var tab = ActiveTab(side);

        var card = Flex.Column(gap: 0).Width(w);
        // Back and forward from the keyboard, as in the pane: they go, and the popover with them.
        card.On<KeyDownEvent>(e =>
        {
            if (!e.Modifiers.HasFlag(KeyModifiers.Alt) || e.Key is not (KeyCode.Left or KeyCode.Right)) return;
            e.StopPropagation = true;
            CloseHistoryPopover();
            if (e.Key == KeyCode.Left) GoBack(side); else GoForward(side);
        });

        float budget = w - 60f;   // room for the folder icon, gaps, padding and trailing bullet

        // Current folder (active, non-navigable), then the Back stack, most-recent first.
        card.AddChild(HistoryRow(side, Dir(side), active: true, budget));
        for (int i = tab.Back.Count - 1, n = 0; i >= 0 && n < 10; i--, n++)
            card.AddChild(HistoryRow(side, tab.Back[i], active: false, budget));

        // Footer key hints.
        card.AddChild(Box.New().Height(1).Background(Nocturne.Divider).Margin(new Thickness(3, 6, 3, 6)));
        var foot = Flex.Row(gap: 5).AlignItems(AlignItems.Center).Padding(new Thickness(2, 10, 5, 10));
        foot.AddChild(Ui.Kbd("Alt+←")); foot.AddChild(Ui.Mono("back", 10.5f, Nocturne.Neutral500));
        foot.AddChild(Ui.Kbd("Alt+→")); foot.AddChild(Ui.Mono("forward", 10.5f, Nocturne.Neutral500));
        foot.AddChild(Box.New().FlexGrow(1));
        foot.AddChild(Ui.Kbd("↵")); foot.AddChild(Ui.Mono("jump", 10.5f, Nocturne.Neutral500));
        card.AddChild(foot);
        return card;
    }

    private Element HistoryRow(char side, string path, bool active, float budget)
    {
        // Folder glyph · path · (the active row's dot).
        var row = Grid.Of(active ? "auto 1fr auto" : "auto 1fr", rows: "1fr", gap: 8)
            .AlignItems(AlignItems.Center).Height(28)
            .Padding(new Thickness(0, 8, 0, 8)).CornerRadius(Nocturne.RadiusSm)
            .Cursor(active ? CursorType.Default : CursorType.Pointer);
        row.AddChild(Icons.Draw(IconKind.Folder, null, 14f).GridColumn(0));
        row.AddChild(Ui.Mono(CollapsePath(path, budget), 11.5f, active ? Nocturne.Accent200 : Nocturne.On(0.80))
            .GridColumn(1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        if (active)
            row.AddChild(Ui.Icon(Glyphs.Dot, 9f, Nocturne.Accent).GridColumn(2));
        else
        {
            void Jump() { CloseHistoryPopover(); _active = side; NavigateTo(side, path); }
            // A row the keyboard can reach: ↑ ↓ (the popover's arrow keys) and Tab land on it,
            // lit like the hover, and Enter or Space jumps.
            row.IsFocusable = true;
            row.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
            row.AccessibleName = path;
            row.OnClick(_ => Jump());
            row.On<KeyDownEvent>(e =>
            {
                if (e.Key is not (KeyCode.Enter or KeyCode.Space) || e.Modifiers.HasFlag(KeyModifiers.Alt)) return;
                e.StopPropagation = true;
                Jump();
            });
            row.HoverBg(Nocturne.Neutral900, Color.Transparent);
            row.OnFocus(r => r.Background(Nocturne.Neutral900));
        }
        return row;
    }

    /// <summary>How wide one tab's label is allowed to get before it ellipsizes. A deep
    /// folder can be named anything, and one such tab used to push the "+" button and the
    /// history cluster off the end of the strip — so the name is what gives, not the row.</summary>
    private const float TabLabelMaxWidth = 140f;

    private Element TabChip(char side, int index, bool on)
    {
        var tab = Tabs(side)[index];
        // Active tab connects to the content ground (bg) with a 2px accent top bar;
        // every tab reserves that 2px border so activating one doesn't shift the row.
        var chip = Flex.Row(gap: 5).AlignItems(AlignItems.Center)
            .Padding(new Thickness(2, 10, 4, 10))
            .CornerRadius(new CornerRadius(6, 6, 0, 0))
            .BorderColor(on ? Nocturne.Accent : Color.Transparent).BorderWidth(new Thickness(2, 0, 0, 0))
            .MinWidth(56).Overflow(Overflow.Hidden)
            .Cursor(CursorType.Pointer);
        if (on) chip.Background(Nocturne.Bg);

        if (tab.Locked)
            chip.AddChild(Ui.Icon(Glyphs.Lock, 10f, on ? Nocturne.Accent300 : Nocturne.On(0.40)));
        // A tab holding a fed-in result set says so: its folder is only where the search ran.
        if (tab.Results != null)
            chip.AddChild(Ui.Icon(Glyphs.Search, 10f, ResultsTeal));
        var label = tab.Results != null ? "Results" : TabLabel(tab.Dir);
        // MaxWidth alone would still let the name wrap to a second line — wrapping is
        // decided at measure time, before any width cap applies — so the one-line cap and
        // the ellipsis have to be asked for as well. The full name stays reachable as the
        // tooltip, which is the only place a truncated tab can still tell you what it is.
        chip.AddChild(Text.New(label).FontSize(11.5f)
            .FontWeight(on ? FontWeight.Medium : FontWeight.Regular)
            .TextColor(on ? Nocturne.Text : Nocturne.Neutral500)
            .MaxWidth(TabLabelMaxWidth).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        chip.Tooltip(tab.Results != null ? $"Results from {tab.Dir}" : tab.Dir);

        chip.OnClick(_ => SwitchTab(side, index));
        chip.ContextMenu(() => TabMenu(side, index));
        // Middle-click closes the tab (a common convenience).
        chip.On<PointerDownEvent>(e => { if (e.Button == 1) CloseTab(side, index); });
        if (!on) chip.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return chip;
    }

    /// <summary>Phosphor artwork for a menu row, tinted to whatever colour the menu
    /// resolved for that item's state (normal, disabled, danger).</summary>
    private static Func<Color, IImageSource?> MenuIcon(string name) =>
        color => Phosphor.Source(name, Ui.IconBox(14f), color, Icons.DisplayScale);

    private IReadOnlyList<ContextMenuItem> TabMenu(char side, int index)
    {
        var tab = Tabs(side)[index];
        var canClose = Tabs(side).Count > 1;
        return
        [
            new() { Icon = MenuIcon(Glyphs.Copy), Label = "Duplicate tab", Action = () => DuplicateTab(side, index) },
            new() { Icon = MenuIcon(Glyphs.Swap), Label = "Open in other pane", Action = () => OpenTabInOtherPane(side, index) },
            new() { Icon = MenuIcon(Glyphs.Lock), Label = tab.Locked ? "Unlock tab" : "Lock tab (read-only)", Action = () => ToggleTabLock(side, index) },
            ContextMenuItem.Separator(),
            new() { Label = "Close tab", ShortcutHint = KeyHint(Cmd.CloseTab), Action = () => CloseTab(side, index), IsDisabled = !canClose },
            new() { Label = "Close others", Action = () => CloseOtherTabs(side, index), IsDisabled = !canClose },
        ];
    }

    private Element BuildBreadcrumb(char side, bool active)
    {
        var dir = Dir(side);
        // Transparent, no border — groups with the header below it. Spacing is
        // uniform via the row gap alone (no per-separator margins), so every "/"
        // sits the same distance from its neighbours. The crumb row grows; the view
        // toggle is pinned flush-right (see the outer wrapper at the end).
        var row = Flex.Row(gap: 3).FlexGrow(1).AlignItems(AlignItems.Center)
            .FlexWrap(FlexWrap.NoWrap).Overflow(Overflow.Hidden)
            .Background(Color.Transparent)   // give the empty strip a hit area
            .Tooltip("Click to type a path (Ctrl+G)");
        row.AutomationId = $"crumb-strip-{side}";
        row.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        row.AccessibleName = "Go to path";
        // Clicking the empty run after the last crumb turns the trail into an editable
        // path box, the way an address bar does. Guarded on the row being the hit target
        // so a click that landed on a crumb still just navigates.
        row.OnClick(e => { if (ReferenceEquals(e.Target, e.CurrentTarget)) OpenGotoPrompt(side); });

        var (root, driveLabel, server, segs, prefixes, archIdx, pin, start, truncated) = PlanCrumbs(dir);
        NoteFitCrumbs(side, start, null);
        row.AddChild(DriveCrumb(side, driveLabel, root, active, server));

        if (pin >= 0)
        {
            row.AddChild(Sep());
            row.AddChild(Crumb(side, segs[pin], prefixes[pin], leaf: segs.Length == 1));
        }
        if (truncated)
        {
            row.AddChild(Sep());
            // "…" is a shortcut to the deepest hidden folder, not a no-op.
            row.AddChild(Crumb(side, "…", prefixes[start - 1], leaf: false));
        }
        for (int i = start; i < segs.Length; i++)
        {
            row.AddChild(Sep());
            row.AddChild(Crumb(side, segs[i], prefixes[i], leaf: i == segs.Length - 1, archive: i == archIdx));
        }

        // The crumb row + the pinned view toggle share the breadcrumb strip's padding.
        var outer = Flex.Row(gap: 8).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(7, 12, 5, 12));
        outer.AddChild(row);
        outer.AddChild(ViewToggle(side));
        return outer;
    }

    /// <summary>What the path trail shows for a folder at the window's present width: the root
    /// (and its label), the segments and the path each one goes to, the archive and share
    /// crumbs that are pinned, and the first segment kept after truncation. The width enters
    /// through <see cref="BreadcrumbBudget"/> alone, so a window resize changes only
    /// <c>Start</c> — which is how a resize tells whether the trail needs a build.</summary>
    private readonly record struct CrumbPlan(string Root, string DriveLabel, bool Server, string[] Segs,
        string[] Prefixes, int ArchIdx, int Pin, int Start, bool Truncated);

    private CrumbPlan PlanCrumbs(string dir)
    {
        // Leading segment: monospace accent crumb. Trim the trailing separator so the first
        // "/" divider isn't doubled ("C:\ /").
        string root = CrumbRoot(dir);
        bool server = PathProbe.HostOf(dir) != null;
        string driveLabel = root.TrimEnd(System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar);

        var rest = dir.Length > root.Length ? dir[root.Length..] : "";
        var segs = rest.Split(
            new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        // Full navigation target for each segment (its path prefix).
        var prefixes = new string[segs.Length];
        var p = root;
        for (int i = 0; i < segs.Length; i++) { p = System.IO.Path.Combine(p, segs[i]); prefixes[i] = p; }

        // The segment that is the archive file, if we're browsing inside one — it renders
        // as an amber crumb and is pinned against truncation.
        int archIdx = -1;
        if (ArchivePath.TryParse(dir) is { } ap)
            for (int i = 0; i < segs.Length; i++)
                if (PathEquals(prefixes[i], ap.ArchiveFile)) { archIdx = i; break; }

        // On a share, the first segment is the share itself, and it is pinned: which share you
        // are on is not a folder among folders, and truncating it away would leave the trail
        // reading "\\server / … / src" on every deep path — the same reason the archive crumb
        // is pinned. -1 when there is no share, which is every local path.
        int pin = server && segs.Length > 0 ? 0 : -1;

        // Left-truncate by WHOLE folders: keep the leading crumbs + as many trailing segments
        // as fit, and if any in between are dropped show a single "…" → "C: / … / folder 3 /
        // folder 4". The leaf is always kept.
        float budget = BreadcrumbBudget() - EstTextWidth(driveLabel, mono: true) - EstSepWidth();
        if (pin >= 0) budget -= EstSepWidth() + EstTextWidth(segs[pin], mono: false);
        int first = pin + 1;                  // lowest segment truncation may drop
        int start = segs.Length;              // first kept segment index
        float used = 0;
        for (int i = segs.Length - 1; i >= first; i--)
        {
            float w = EstSepWidth() + EstTextWidth(segs[i], mono: false);
            if (i < segs.Length - 1 && used + w > budget) break;   // always keep the leaf
            used += w;
            start = i;
        }
        // Never truncate past the archive crumb (design §A: drive + archive are pinned).
        if (archIdx >= first) start = Math.Min(start, archIdx);
        bool truncated = start > first;
        if (truncated) budget -= EstSepWidth() + EstTextWidth("…", mono: false);
        // Dropping the ellipsis's width may push the last kept segment over; trim one more.
        // (Skipped inside an archive so the pinned archive crumb is never trimmed.)
        while (truncated && archIdx < 0 && start < segs.Length - 1 && used > budget)
            used -= EstSepWidth() + EstTextWidth(segs[start++], mono: false);

        return new CrumbPlan(root, driveLabel, server, segs, prefixes, archIdx, pin, start, truncated);
    }

    private static Element Sep() =>
        Text.New("/").FlexShrink(0).FontSize(11.5f).TextColor(Nocturne.Neutral600);

    /// <summary>The trail's leading crumb. A drive letter opens the picker — switching volumes
    /// is what you want from it, and the root is one Backspace away. A server navigates instead:
    /// the picker is built from mounted volumes and never lists one, so opening it there would
    /// be a dead end, while the server is now a place of its own with its shares in it.</summary>
    private BoxElement DriveCrumb(char side, string label, string root, bool active, bool server)
    {
        var d = Box.New().FlexShrink(0)
            .Padding(new Thickness(1, 5, 1, 5)).CornerRadius(4f).Cursor(CursorType.Pointer)
            .Tooltip(server ? $"Go to {label}" : "Select drive (Alt+F1)")
            .Children(Ui.Mono(label, 11.5f, active ? Nocturne.Accent300 : Nocturne.On(0.55)));
        d.OnClick(_ =>
        {
            _active = side;
            if (server) NavigateTo(side, root); else OpenDrivePicker(side);
        });
        return d.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }

    /// <summary>What the trail is rooted at, which is not always the volume. For a drive that is
    /// the path root; for a share it is the <i>server</i>. <c>Path.GetPathRoot</c> answers
    /// "\\server\share" there because that is the volume — free space and same-volume moves both
    /// key on it — but the trail is about navigation, and now that a server browses its own
    /// shares the share is a step in the path rather than part of its root.</summary>
    private static string CrumbRoot(string dir)
    {
        if (PathProbe.HostOf(dir) is { } host)
        {
            var serverRoot = @"\\" + host;
            if (dir.StartsWith(serverRoot, StringComparison.OrdinalIgnoreCase)) return serverRoot;
        }
        return System.IO.Path.GetPathRoot(dir) ?? dir;
    }

    private static readonly Color ArchiveAmber = Color.FromHex("#d9a06b");

    private BoxElement Crumb(char side, string label, string target, bool leaf, bool archive = false)
    {
        // Archive crumb: amber name + zip glyph over an amber @10% fill (design §A) —
        // glyph pairs with the color so it's not color-alone.
        if (archive)
        {
            var box = Box.New().FlexShrink(0)
                .Padding(new Thickness(1, 6, 1, 5)).CornerRadius(Nocturne.RadiusSm)
                .Background(Nocturne.Alpha(ArchiveAmber, 0.10)).Cursor(CursorType.Pointer);
            var inner = Flex.Row(gap: 4).AlignItems(AlignItems.Center);
            inner.AddChild(Icons.Draw(IconKind.Archive, ArchiveAmber));
            inner.AddChild(Ui.Mono(label, 11.5f, ArchiveAmber));
            box.AddChild(inner);
            box.OnClick(_ => { _active = side; NavigateTo(side, target); });
            return box.HoverBg(Nocturne.Alpha(ArchiveAmber, 0.16), Nocturne.Alpha(ArchiveAmber, 0.10));
        }

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
        // Two 1fr columns either way — two panes, or one pane and the preview.
        float paneW = (win - RailWidth - 1f) / 2f;
        return paneW - 2f /*pane border*/ - 24f /*breadcrumb h-padding*/ - 6f - ViewToggleWidth;
    }

    private static float EstTextWidth(string s, bool mono) =>
        s.Length * (mono ? 7.2f : 6.6f) + 10f /*crumb h-padding*/;

    private static float EstSepWidth() => 6f + 2 * 3f /*slash + row gaps*/;

    // Column layout depends on which optional columns (Size / Date) are enabled.
    private (string cols, int sizeCol, int ratioCol, int dateCol) ListLayout(bool inArchive)
    {
        var cols = "16px 1fr";
        int next = 2, sizeCol = -1, ratioCol = -1, dateCol = -1;
        if (_settings.ShowSize) { cols += " 60px"; sizeCol = next++; }
        if (inArchive) { cols += " 40px"; ratioCol = next++; }   // packed/uncompressed %
        if (_settings.ShowDate) { cols += " 116px"; dateCol = next++; }
        return (cols, sizeCol, ratioCol, dateCol);
    }

    // Which header the pointer is over (side + column), so an inactive column can show a
    // dim "would sort" triangle on hover.
    private (char side, SortMode col)? _hoverSort;

    private Element BuildListHeader(char side, bool inArchive)
    {
        var (cols, sizeCol, ratioCol, dateCol) = ListLayout(inArchive);
        var h = Grid.Of(cols, gap: 10).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 10, 5, 10))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        h.AddChild(Box.New().GridColumn(0));
        h.AddChild(HeaderCell(side, "Name", SortMode.Name, rightAlign: false).GridColumn(1));
        if (sizeCol >= 0) h.AddChild(HeaderCell(side, "Size", SortMode.Size, rightAlign: true).GridColumn(sizeCol));
        if (ratioCol >= 0) h.AddChild(Text.New("RATIO").FontSize(10f).LetterSpacing(0.08f * 10f)
            .TextColor(Nocturne.Neutral600).TextAlign(TextAlign.Right).GridColumn(ratioCol));
        if (dateCol >= 0) h.AddChild(HeaderCell(side, "Modified", SortMode.Date, rightAlign: false).GridColumn(dateCol));
        return h;
    }

    /// <summary>A clickable, sortable column header. The active column shows an accent
    /// direction triangle and its label lifts to full-strength text; hovering an inactive
    /// column shows a dim ascending triangle so it reads as sortable. The triangle's width is
    /// always reserved so the label never shifts. Click cycles asc↔desc / switches column.</summary>
    private Element HeaderCell(char side, string label, SortMode col, bool rightAlign)
    {
        bool active = SortOf(side) == col;
        bool hover = !active && _hoverSort is (var hs, var hc) && hs == side && hc == col;
        // No explicit width: the grid stretches the cell child to the track, and
        // JustifyContent aligns the label+triangle within it (a percent width would
        // resolve to 0 in the fixed 60px Size track and hide the label).
        var row = Flex.Row(gap: 4).AlignItems(AlignItems.Center)
            .JustifyContent(rightAlign ? JustifyContent.FlexEnd : JustifyContent.FlexStart)
            .Padding(new Thickness(0, 4, 0, 4)).CornerRadius(4f)
            .Cursor(CursorType.Pointer);
        row.AddChild(Text.New(label.ToUpperInvariant()).FontSize(10f).LetterSpacing(0.08f * 10f)
            .TextColor(active ? Nocturne.Text : Nocturne.Neutral600));
        // Reserve the triangle on every header; colour it accent (active), a dim ghost
        // (hover), or transparent (idle) so hovering never shifts the label.
        Color tri = active ? Nocturne.Accent : hover ? Nocturne.Neutral500 : Color.Transparent;
        row.AddChild(Ui.Icon(active && SortDescOf(side) ? Glyphs.CaretDown : Glyphs.CaretUp, 9f, tri));
        row.OnClick(_ => SetSort(side, col));
        row.OnPointerEnter(_ => { if (_hoverSort != (side, col)) { _hoverSort = (side, col); MarkDirty(); } });
        row.OnPointerLeave(_ => { if (_hoverSort == (side, col)) { _hoverSort = null; MarkDirty(); } });
        return row.HoverBg(Nocturne.Neutral900, Color.Transparent);
    }

    private Element BuildList(char side, IReadOnlyList<FileEntry> list, bool active)
    {
        // The cursor is the list's selection, so a key that only moves it makes the two rows it
        // moved between rather than every row on screen in both panes. Only the active pane
        // renders a cursor (design: the caret follows focus).
        int cur = Math.Clamp(Cursor(side), 0, Math.Max(0, list.Count - 1));
        var marks = Marks(side);

        // The row F3 would compare the active pane's cursor against. Only in the inactive
        // pane — the active one already draws a cursor, and the answer is always over here.
        // Kept only when it names a row here: with nothing to pair, F3's answer is a same-named
        // path that does not exist, which rings nothing, and which changes with every key.
        string? partnerPath = active ? null : DiffTargetPath();
        if (partnerPath is not null && IndexOfPath(list, partnerPath) < 0) partnerPath = null;

        // A kept row shows what it showed when it was made; everything it shows besides its
        // entry and the cursor is in the shape, and a change makes the rows again. The partner
        // is the exception: in a folder of the same names it moves with every key, and only the
        // row it leaves and the row it reaches show it, so those two are made again and the
        // rest are kept. The mounted list is told before this build patches it.
        var rowState = PaneRows(side);
        bool shapeChanged = rowState.Changed(this, side, marks);
        bool partnerMoved = rowState.PartnerMoved(partnerPath, out var partnerWas);
        if (VList(side).Current is { } mounted)
        {
            if (shapeChanged) mounted.InvalidateRows();
            else if (partnerMoved)
            {
                mounted.InvalidateRow(IndexOfPath(list, partnerWas));
                mounted.InvalidateRow(IndexOfPath(list, partnerPath));
            }
        }

        return VirtualListElement<FileEntry>.New()
            .ItemHeight((int)RowHeight)
            .Items(list)
            .SelectedIndex(active && list.Count > 0 ? cur : -1)
            .RenderWith((e, cursor) => FileRow(side, e, cursor, marks.Contains(MarkKey(e)), partnerPath))
            // The list names its rows to assistive tech by this alone, off-screen ones included.
            // Without it each row was read out as the FileEntry record's ToString(). One delegate
            // per pane, reading the marks as they are: the list keeps each entry's label while the
            // entry and the delegate are unchanged, and a change of marks makes them again.
            .AccessibleItemLabel(FileLabelFn(side))
            .WithRef(VList(side))
            .FlexGrow(1);
    }

    private Func<FileEntry, string?>? _fileLabelL, _fileLabelR;

    private Func<FileEntry, string?> FileLabelFn(char side) => side == 'L'
        ? _fileLabelL ??= e => FileLabel(e, Marks('L').Contains(MarkKey(e)))
        : _fileLabelR ??= e => FileLabel(e, Marks('R').Contains(MarkKey(e)));

    private readonly PaneRowState _paneRowsL = new(), _paneRowsR = new();
    private PaneRowState PaneRows(char side) => side == 'L' ? _paneRowsL : _paneRowsR;

    /// <summary>
    /// What a pane's Detail rows show besides their entry and whether the cursor is on it, as
    /// the rows on screen were made with it: the Size and Date columns, the zebra tint, a
    /// fed-in result set's source folders, the scale the icons are cut at, a folder a drag
    /// hovers, and the sets a row looks itself up in — the marks, the files cut for a move, the
    /// folders measured with Space (whose totals count up while they walk). And, apart from
    /// that shape, the F3 partner, which only one row shows.
    /// <para>The rest comes with something the list already handles: another folder, an
    /// archive (and its ratio column) or a filter are other entries; the density is another
    /// row height, which makes every row again; another colour scheme is another Theme object,
    /// which does too.</para>
    /// </summary>
    private sealed class PaneRowState
    {
        private PaneRowShape _shape;
        private readonly HashSet<string> _marks = PathSet();
        private readonly HashSet<string> _cut = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (long Bytes, bool Done)> _sizes = new(StringComparer.Ordinal);
        private bool _made;
        private string? _partner;

        /// <summary>Whether the F3 partner the rows were made with is another one now, and which it
        /// was; takes the new one either way. Kept out of the shape because it moves with every key
        /// in a folder of the same names, and it changes only the row it leaves and the row it
        /// reaches: in the shape, every key made every row of the pane again.</summary>
        public bool PartnerMoved(string? partnerPath, out string? was)
        {
            was = _partner;
            _partner = partnerPath;
            return !string.Equals(was, partnerPath, StringComparison.Ordinal);
        }

        /// <summary>Whether anything the rows show changed since the last build asked; takes
        /// the new state either way.</summary>
        public bool Changed(CommanderApp app, char side, HashSet<string> marks)
        {
            var shape = new PaneRowShape(app._settings.ShowSize, app._settings.ShowDate,
                app._settings.Zebra, app.ResultsOf(side), Icons.DisplayScale, app._dropTarget);
            bool changed = !_made || shape != _shape;
            _made = true;
            _shape = shape;
            if (!_marks.SetEquals(marks)) { changed = true; _marks.Clear(); _marks.UnionWith(marks); }
            if (!_cut.SetEquals(app._cutPending)) { changed = true; _cut.Clear(); _cut.UnionWith(app._cutPending); }
            if (!SameSizes(app._dirSizes))
            {
                changed = true;
                _sizes.Clear();
                foreach (var (path, ds) in app._dirSizes) _sizes[path] = (ds.Bytes, ds.Done);
            }
            return changed;
        }

        private bool SameSizes(Dictionary<string, DirSize> sizes)
        {
            if (sizes.Count != _sizes.Count) return false;
            foreach (var (path, ds) in sizes)
                if (!_sizes.TryGetValue(path, out var kept) || kept != (ds.Bytes, ds.Done)) return false;
            return true;
        }
    }

    private readonly record struct PaneRowShape(
        bool ShowSize, bool ShowDate, bool Zebra, ResultListing? Results,
        float IconScale, (char Side, string Dir)? DropTarget);

    /// <summary>A row as a screen reader says it: the name, what it is, its size and date, and
    /// whether it is marked.</summary>
    private static string FileLabel(FileEntry e, bool marked)
    {
        if (e.IsParent) return "Parent folder";
        var label = e.IsDir
            ? $"{e.Name}, folder"
            : $"{e.Name}, {FileSystem.ByteCount(e.Size)}, modified {e.Modified:g}";
        return marked ? label + ", marked" : label;
    }

    private Element FileRow(char side, FileEntry e, bool cursor, bool marked,
        string? partnerPath = null)
    {
        // The other pane's F3 operand. Matched on the full path rather than the name, because
        // this one comes from DiffPartner as a path and a name could collide with a row in a
        // folder the answer isn't about.
        bool partner = !cursor && partnerPath != null && PathEquals(e.FullPath, partnerPath);
        // Marked (red) overrides the dir/file colour; metadata dims to the red tint.
        Color fg = marked ? Nocturne.MarkRed : e.IsDir ? Nocturne.DirText : Nocturne.Text;
        Color metaFg = marked ? Nocturne.MarkRedDim : Nocturne.On(0.50);
        // A cut-pending row ghosts to ~45% (icon + name) with dimmed metadata — reads as
        // "leaving", distinct from a mark (red) and the cursor ring (which paints over it).
        bool cut = !marked && _cutPending.Contains(e.FullPath);
        if (cut) { fg = Nocturne.Alpha(fg, 0.45); metaFg = Nocturne.Neutral700; }
        var (cols, sizeCol, ratioCol, dateCol) = ListLayout(InArchive(side));

        // Every row reserves a 1px border so the cursor ring never shifts siblings — which is
        // also why the partner marking is a colour on that same reserved ring rather than a
        // thicker or one-sided border, either of which would nudge the row's contents.
        var row = Grid.Of(cols, gap: 10).Height(RowHeight)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 10, 0, 10))
            .BorderWidth(1f)
            .BorderColor(cursor ? Nocturne.Accent600 : partner ? Nocturne.Accent700 : Color.Transparent)
            .Cursor(CursorType.Default);

        // Filled for the cursor, outline-only for the partner. The two differ by more than
        // brightness on purpose: a dimmer ring alone reads as the same thing rendered faintly,
        // whereas filled-versus-hollow is a difference in kind, and the missing fill is what
        // says "this pane doesn't have the keyboard". The hue stays in the accent family
        // because the mark is about what the cursor opposite pairs with.
        if (cursor)
            row.Background(Nocturne.Alpha(Nocturne.Accent500, 0.13));
        else if (_settings.Zebra && !marked && !e.IsParent
                 && IndexOfPath(Rows(side), e.FullPath) % 2 == 1)
            row.Background(Nocturne.Alpha(Nocturne.Text, 0.03));   // alternating tint

        // Type icon rasterized from the design's SVG glyphs; marked rows tint red, a
        // cut-pending row ghosts via element opacity (keeps the design's own colours).
        var kind = Icons.KindFor(e);
        var iconEl = Icons.Draw(kind, marked ? Nocturne.MarkRed : (Color?)null);
        if (cut) iconEl.Opacity = 0.45f;
        row.AddChild(iconEl.GridColumn(0));
        row.AddChild(NameCell(side, e, fg, metaFg).GridColumn(1));
        if (sizeCol >= 0)
        {
            // A Space-measured folder shows its byte total in place of "<DIR>"; while the
            // walk is still running it counts up live with a trailing "…".
            string sizeText = MeasuredDir(e) is (var b, var done)
                ? FileSystem.ByteCount(b) + (done ? "" : "…")
                : FileSystem.SizeLabel(e);
            row.AddChild(Ui.Mono(sizeText, 11f, metaFg).TextAlign(TextAlign.Right).GridColumn(sizeCol));
        }
        if (ratioCol >= 0)
        {
            // packed/uncompressed % for an archive member; "—" for dirs / the parent.
            string ratio = e.IsParent || e.IsDir || e.PackedSize < 0 || e.Size <= 0
                ? "—" : $"{(int)Math.Round(100.0 * e.PackedSize / e.Size)}%";
            row.AddChild(Ui.Mono(ratio, 11f, metaFg).TextAlign(TextAlign.Right).GridColumn(ratioCol));
        }
        if (dateCol >= 0)
            row.AddChild(Ui.Mono(FileSystem.DateLabel(e), 11f, metaFg).GridColumn(dateCol));

        // Select on left mouse-DOWN, double-press to open, right-drag to mark — shared
        // with brief cells and thumbnail tiles. (Double-press rather than DoubleClickEvent
        // because the VirtualList recycles rows with fresh identities between the presses,
        // so the framework's down==up click synthesis never fires for a recycled row.)
        AttachEntryPointer(row, side, e, cursor);
        return row;
    }

    /// <summary>The name column of a Detail row. In a folder that is just the name; in a
    /// fed-in result set it is the name followed by a dimmed source folder, because the rows
    /// come from many folders and the name alone doesn't say which file this is.</summary>
    private Element NameCell(char side, FileEntry e, Color fg, Color metaFg)
    {
        var name = Ui.Mono(e.Name, 12f, fg).TextAlign(TextAlign.Left)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis);
        if (ResultsOf(side) is not { } v || e.IsParent) return name;

        string source = ResultSource(v, e);
        if (source.Length == 0) return name;
        // Both parts are one line and both ellipsize: wrapping is decided at measure time,
        // where FlexShrink alone can't stop it, so each needs MaxLines(1) too.
        var cell = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Overflow(Overflow.Hidden);
        cell.AddChild(name.FlexShrink(1));
        cell.AddChild(Ui.Mono("· " + source, 10.5f, metaFg)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis).FlexShrink(1));
        return cell;
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

        // Inside an archive: amber counts + packed/% instead of free space (meaningless here).
        if (InArchive(side))
        {
            long total = list.Where(e => !e.IsParent && !e.IsDir).Sum(e => e.Size);
            long packed = list.Where(e => !e.IsParent && !e.IsDir && e.PackedSize >= 0).Sum(e => e.PackedSize);
            string pct = total > 0 ? $" · packed {FileSystem.ByteCount(packed)} · {(int)Math.Round(100.0 * packed / total)}%" : "";
            footer.AddChild(Ui.Mono($"{files} files · {dirs} dirs · {FileSystem.ByteCount(total)} uncompressed{pct}",
                10.5f, ArchiveAmber));
            return footer;
        }

        // Folder size: own files always count; a subfolder adds its bytes only once the
        // user has Space-measured it. While any subfolder is unmeasured (or still walking)
        // the total is a lower bound, shown with a leading "≥".
        long size = 0;
        bool partial = false;
        foreach (var e in list)
        {
            if (e.IsParent) continue;
            if (!e.IsDir) { size += e.Size; continue; }
            if (MeasuredDir(e) is { } m) { size += m.bytes; partial |= !m.done; }
            else partial = true;
        }
        string sizeLabel = (partial ? "≥ " : "") + FileSystem.ByteCount(size);

        // While the thumbnail grid is decoding, the right slot counts down instead of free space.
        int decoding = ViewOf(side) == ViewMode.Thumbnails && _thumbsCache != null ? _thumbsCache.PendingCount : 0;
        var tail = decoding > 0 ? $"decoding {decoding}…" : free;
        var label = $"{files} files, {dirs} dirs, {sizeLabel}" + (tail.Length > 0 ? $" · {tail}" : "");
        footer.AddChild(Ui.Mono(label, 10.5f, decoding > 0 ? Nocturne.Accent300 : Nocturne.On(0.50)));

        // Same rule for a fed-in result set that dropped its archive members: the pane says
        // what it is not showing rather than quietly showing fewer hits than the search found.
        if (ResultsOf(side) is { SkippedInArchive: > 0 } res)
        {
            footer.AddChild(Ui.Mono("·", 10.5f, Nocturne.On(0.30)));
            footer.AddChild(Icons.Draw(IconKind.Archive, ArchiveAmber));
            footer.AddChild(Ui.Mono(
                $"{res.SkippedInArchive} inside archives left out", 10.5f, ArchiveAmber)
                .Tooltip("Archive members can't take move, delete or rename — search again and use F5 to extract"));
        }

        // An archive whose members point outside itself is listed short. Say so — a listing
        // that quietly omits files is worse than one that explains why, and the omission is
        // itself worth knowing about.
        int hidden = InResults(side) ? 0 : Loader(side).Value.Unsafe;
        if (hidden > 0)
        {
            footer.AddChild(Ui.Mono("·", 10.5f, Nocturne.On(0.30)));
            footer.AddChild(Ui.Icon(Glyphs.Warning, 11f, Nocturne.MarkRed));
            footer.AddChild(Ui.Mono(
                $"{hidden} unsafe {(hidden == 1 ? "entry" : "entries")} hidden", 10.5f, Nocturne.MarkRed)
                .Tooltip("Their stored paths point outside the archive, so they were not listed"));
        }
        return footer;
    }

    /// <summary>Shown in place of the list when a type-to-find filter matches nothing.</summary>
    private Element BuildNoMatches()
    {
        var wrap = Flex.Column(gap: 8).FlexGrow(1)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        wrap.AddChild(Ui.Icon(Glyphs.Search, 22f, Nocturne.Neutral500));
        wrap.AddChild(Text.New($"No matches for “{_search}”").FontSize(13f).TextColor(Nocturne.Text));
        wrap.AddChild(Ui.Mono("Backspace to widen · Esc to clear", 10.5f, Nocturne.On(0.40)));
        return wrap;
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
                if (KeyText(Cmd.NewFolder) is { Length: > 0 } nf) hint.AddChild(Ui.Kbd(nf));
                hint.AddChild(Text.New("new folder").FontSize(11f).TextColor(Nocturne.Neutral500));
                block.AddChild(hint);
                break;

            case PaneState.Offline:
                block.AddChild(Ui.Icon(Glyphs.Prohibit, 20f, Nocturne.Neutral600));
                var drive = (System.IO.Path.GetPathRoot(Dir(side)) ?? Dir(side))
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                var line = Flex.Row(gap: 5).AlignItems(AlignItems.Center);
                line.AddChild(Ui.Mono(drive, 12f, Nocturne.Neutral300));
                line.AddChild(Text.New("is not available").FontSize(12f).TextColor(Nocturne.Neutral400));
                block.AddChild(line);
                block.AddChild(GhostButton("Reconnect", () => RetryPane(side)));
                break;

            case PaneState.Connecting:
                block.AddChild(Spinner.New(20f));
                var waiting = Flex.Row(gap: 5).AlignItems(AlignItems.Center);
                waiting.AddChild(Text.New("Connecting to").FontSize(12f).TextColor(Nocturne.Neutral400));
                waiting.AddChild(Ui.Mono((System.IO.Path.GetPathRoot(Dir(side)) ?? Dir(side))
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                    12f, Nocturne.Neutral300));
                block.AddChild(waiting);
                // Nothing to go back to on a cold start — the listing resolves to Offline on
                // its own, and a button that cannot act is worse than no button.
                block.AddChild(GhostButton("Cancel", () => CancelPaneLoad(side), CanCancelPaneLoad(side)));
                break;

            case PaneState.Denied:
                block.AddChild(Ui.Icon(Glyphs.Lock, 20f, Nocturne.Neutral600));
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
    // Stands in for the inactive pane and reads the active pane's cursor, so it is always
    // the file you are looking at, on the side you are not.
    private Element BuildPreview()
    {
        var side = _active;
        var list = Rows(side);
        int cur = Cursor(side);
        var focused = cur >= 0 && cur < list.Count ? list[cur] : null;

        // The 1px ring (accent while the preview holds keyboard focus) is always reserved,
        // so tabbing into it never shifts the layout.
        var col = Flex.Column(gap: 0)
            .AlignItems(AlignItems.Stretch).Overflow(Overflow.Hidden)
            .Background(Nocturne.Surface)
            .FlexGrow(1).BorderWidth(1f)
            .BorderColor(_previewFocused ? Nocturne.Accent800 : Color.Transparent);

        var kicker = "Preview" + (focused != null && !focused.IsParent ? " · " + focused.Name : "");
        col.AddChild(Text.New(kicker.ToUpperInvariant()).FontSize(10f).FlexShrink(0)
            .LetterSpacing(0.08f * 10f).TextColor(Nocturne.Neutral600)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis)
            .Padding(new Thickness(8, 12, 6, 12)));

        if (focused == null || focused.IsParent)
            return col;

        var key = $"{focused.FullPath}#{focused.Modified.Ticks}#{focused.Size}";
        bool inArc = InArchive(side);   // the focused entry is an archive member
        HultUI.Core.Rendering.IImageSource? image = null;

        if (focused.IsDir)
        {
            col.AddChild(Box.New().FlexGrow(1));
        }
        else if (Icons.KindFor(focused) == IconKind.Image)
        {
            // Off-thread image decode; render it scaled to fit, aspect preserved.
            _imageLoader.Load(key, () => inArc ? LoadImageMember(focused) : LoadImage(focused.FullPath, focused.Size), onLoaded: img =>
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
            _previewLoader.Load(key, () => inArc ? PreviewTextMember(focused) : FileSystem.PreviewText(focused.FullPath, focused.Size),
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
                // Ask the highlighter, not the icon table: IconKind.Code lists 25
                // extensions for picking a glyph, and gating on it meant .slnx, .csproj,
                // .props and .ps1 previewed uncoloured here while Junction — which has no
                // such gate — coloured them fine.
                if (HultUI.Syntax.SyntaxHighlighter.CanHighlight(focused.Name)
                    && PreviewSpans(focused.Name, excerpt) is { } codeSpans)
                    body.Spans(codeSpans);   // the theme's code colours (null while tokenizing off-thread)
                // Focus drives the ring; handlers capture only `this`, so they stay valid
                // when reconciliation copies them onto the retained element. Blur can fire
                // mid-reconcile (element removed while focused, e.g. Ctrl+Q away), so defer
                // the repaint rather than re-enter the differ.
                body.On<FocusEvent>(_ => { _previewFocused = true; HultUI.Application.InvokeOnMainThread(MarkDirty); });
                body.On<BlurEvent>(_ => { _previewFocused = false; HultUI.Application.InvokeOnMainThread(MarkDirty); });
                body.IsFocusable = true;
                body.TabIndex = 0;
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

        // Active command line: a cwd prompt + editable field. Enter runs the command in a
        // terminal at the active folder (empty → opens the configured terminal); Esc exits.
        if (_cmdActive)
        {
            var dir = Dir(_active);
            var leaf = System.IO.Path.GetFileName(dir.TrimEnd(
                System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            row.AddChild(Ui.Mono((leaf.Length > 0 ? leaf : dir) + " ›", 11.5f, Nocturne.Accent300).FlexShrink(0));
            var input = Ui.Field(TextInput.New(_cmdText));
            input.PlaceholderText("run a command here — Enter opens a terminal, Esc cancels");
            input.OnTextChanged(v => _cmdText = v);
            input.WithRef(_cmdRef);
            row.AddChild(input);
            row.AddChild(Ui.Kbd("Enter"));
            row.AddChild(Ui.Kbd("Esc"));
            return row;
        }

        // A transient notice (e.g. a locked-tab write block) takes the line briefly.
        if (_notice != null)
        {
            row.AddChild(Ui.Icon(Glyphs.Lock, 12f, Nocturne.MarkRed));
            row.AddChild(Ui.Mono(_notice, 11.5f, Nocturne.MarkRed));
        }
        // A backgrounded transfer shows a compact indicator here.
        else if (_op is { } op && !_progressOpen)
        {
            int pct = op.TotalBytes > 0 ? (int)(100 * op.TotalBytesDone / op.TotalBytes) : 0;
            string verb = op.IsPack ? "packing" : op.IsMove ? "moving" : "copying";
            row.AddChild(Ui.Mono($"▶ {verb} {pct}% · {op.Done}/{op.Items.Count}", 11.5f, Nocturne.Accent300));
        }
        // While a type-to-find filter is active, show it with a block caret + match count.
        else if (_search.Length > 0)
        {
            row.AddChild(Ui.Mono($"filter: {_search}", 11.5f, Nocturne.Text));
            row.AddChild(Box.New().Width(7).Height(14).Background(Nocturne.Accent));
            int matches = Rows(_active).Count(e => !e.IsParent);
            row.AddChild(Ui.Mono($"  {matches} match{(matches == 1 ? "" : "es")} · Esc clears", 11f, Nocturne.On(0.45)));
        }
        else
        {
            row.AddChild(Ui.Mono("Tab switch pane · ↑↓ move · Enter open · Space mark · type to filter",
                11f, Nocturne.On(0.40)));
        }

        row.AddChild(Box.New().FlexGrow(1));
        // Right-aligned key hints.
        foreach (var (k, label) in new[] { (KeyText(Cmd.Copy), "copy"), (KeyText(Cmd.Move), "move"), (KeyText(Cmd.Delete), "delete"), ("Space", "mark") })
        {
            if (k.Length == 0) continue;   // rebound away
            row.AddChild(Ui.Kbd(k));
            row.AddChild(Text.New(label).FontSize(10f).TextColor(Nocturne.On(0.40)));
        }
        return row;
    }
}
