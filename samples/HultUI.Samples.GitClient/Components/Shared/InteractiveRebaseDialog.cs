using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.Shared;

/// <summary>
/// Mutable state driving the interactive-rebase modal. Owned by <see cref="App"/>
/// so the plan survives across dialog rebuilds; the dialog component reads/mutates
/// it and calls <see cref="RefreshDialog"/> to redraw.
/// </summary>
public sealed class InteractiveRebaseState
{
    public required string BaseRef { get; init; }
    public required string BaseShortHash { get; init; }
    public required List<RebaseEntry> Entries { get; init; }
    public required Action<IReadOnlyList<RebaseEntry>> OnConfirm { get; init; }
    public Action? OnCancel { get; init; }
    public Action? RefreshDialog { get; set; }

    // ── UI-only state, reset on each rebuild as needed ──

    /// <summary>Per-entry refs to the focusable row elements. Refilled on every
    /// build; entries are repointed by <see cref="Element.PatchFrom"/> to the
    /// live tree, so arrow-key handlers can call <c>.Current?.Focus()</c>
    /// without hunting through the tree.</summary>
    internal Dictionary<string, Ref<BoxElement>> RowRefs = new();

    /// <summary>The entry whose op-dropdown is currently open, or null if closed.</summary>
    internal RebaseEntry? OpenDropdownEntry;

    /// <summary>Anchor box of the chip that opened the dropdown, in window coordinates.</summary>
    internal Rect OpenDropdownAnchor;

    /// <summary>Op currently highlighted inside the dropdown (driven by arrow keys).</summary>
    internal RebaseOp DropdownHighlight;

    /// <summary>Hash of the currently focused row, or null if no row is focused.
    /// Read by <see cref="InteractiveRebaseDialog.Build"/> so the focus ring
    /// survives <see cref="Element.PatchFrom"/>, which would otherwise wipe
    /// the row's border on every rebuild.</summary>
    internal string? FocusedHash;
}

/// <summary>
/// Modal dialog for planning an interactive rebase: a list of focusable commit
/// rows, each with an op-dropdown chip, and move-up/down buttons.
///
/// Keyboard model:
///   - Tab / Shift+Tab — move between rows + dialog buttons (handled by the framework).
///   - Up / Down on a focused row — move focus to the previous/next row.
///   - p/r/e/s/f/d on a focused row — set the op directly without opening the menu.
///   - Space / Enter on a focused row — open the op dropdown.
///   - Arrow keys + letter keys inside the dropdown — navigate / pick.
///   - Escape — close the dropdown if open, otherwise dismiss the dialog.
/// </summary>
public sealed class InteractiveRebaseDialog : Component
{
    private static readonly Color BackdropColor = new(0, 0, 0, 140);
    private static readonly Color DialogBg = Color.FromHex("#2a2a48");
    private static readonly Color DialogBorder = Color.FromHex("#3a3a5a");
    private static readonly Color DropIndicator = Color.FromHex("#60a5fa");
    private static readonly Color FocusRingColor = Color.FromHex("#6366f1");

    private static readonly RebaseOp[] AllOps =
    [
        RebaseOp.Pick, RebaseOp.Reword, RebaseOp.Edit,
        RebaseOp.Squash, RebaseOp.Fixup, RebaseOp.Drop,
    ];

    private readonly InteractiveRebaseState _state;
    private readonly Action _dismiss;

    /// <summary>If non-null, focus this row's element after the next build.
    /// Used by handlers that mutate state and refresh — the row that should
    /// receive focus may not have been focusable in the live tree before the
    /// rebuild (e.g. closing the dropdown re-focuses the originating row).</summary>
    private string? _pendingRowFocus;

    /// <summary>If true, focus the dropdown overlay element after the next build.</summary>
    private bool _pendingDropdownFocus;

    private Ref<BoxElement>? _dropdownRef;

    public InteractiveRebaseDialog(InteractiveRebaseState state, Action dismiss)
    {
        _state = state;
        _dismiss = dismiss;
        _state.RefreshDialog = MarkDirty;
    }

    public override Element Build()
    {
        // Reset row refs at the start of each build — entries can have been
        // reordered or dropped since the last frame, so stale keys must go.
        _state.RowRefs.Clear();

        var backdrop = Box.New()
            .Width(Dimension.Percent(100))
            .Height(Dimension.Percent(100))
            .Background(BackdropColor)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .ZIndex(100);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            // Backdrop click while dropdown is open just closes the dropdown.
            if (_state.OpenDropdownEntry is not null)
            {
                CloseDropdown();
                return;
            }
            _state.OnCancel?.Invoke();
            _dismiss();
        });

        var panel = Box.New()
            .Width(640)
            .MaxHeight(Dimension.Percent(80))
            .Padding(new Thickness(20, 24, 20, 24))
            .CornerRadius(8f)
            .Background(DialogBg)
            .BorderColor(DialogBorder)
            .BorderWidth(1);
        panel.On<ClickEvent>(e => e.StopPropagation = true);

        var content = Flex.Column(gap: 16);

        content.AddChild(
            Text.New("Interactive Rebase").FontSize(16f).FontWeight(FontWeight.Bold)
                .TextColor(GitClientColors.TextPrimary)
        );
        content.AddChild(
            Text.New($"Rebase {_state.Entries.Count} commit{(_state.Entries.Count == 1 ? "" : "s")} onto {_state.BaseShortHash}. "
                   + "Use ↑/↓ to move between rows, Space to open the action menu, or press p/r/e/s/f/d to set the action directly.")
                .FontSize(12f).TextColor(GitClientColors.TextSecondary)
        );

        // Commit list. MaxHeight lives on the ScrollView (not the inner column)
        // so overflow actually triggers scrolling.
        var list = Flex.Column(gap: 4);
        for (int i = 0; i < _state.Entries.Count; i++)
            list.AddChild(BuildEntryRow(i));

        content.AddChild(
            ScrollView.New()
                .MaxHeight(Dimension.Px(360))
                .Background(GitClientColors.Background)
                .CornerRadius(4f)
                .Padding(new Thickness(4, 4, 4, 4))
                .Children(list)
        );

        var buttons = Flex.Row(gap: 8)
            .JustifyContent(JustifyContent.FlexEnd)
            .AlignItems(AlignItems.Center);

        buttons.AddChild(DialogButton("Cancel", primary: false, () =>
        {
            _state.OnCancel?.Invoke();
            _dismiss();
        }));

        var effective = _state.Entries.Count(e => e.Op != RebaseOp.Drop);
        buttons.AddChild(DialogButton(
            effective == 0 ? "Nothing to do" : "Confirm",
            primary: effective > 0,
            () =>
            {
                if (effective == 0) return;
                _state.OnConfirm(_state.Entries);
                _dismiss();
            }));

        content.AddChild(buttons);
        panel.AddChild(content);
        backdrop.AddChild(panel);

        // Dropdown overlay sits as an absolute sibling of the panel inside
        // the backdrop, so its position is interpreted in window coordinates.
        if (_state.OpenDropdownEntry is { } openEntry)
            backdrop.AddChild(BuildDropdownOverlay(openEntry));

        return backdrop;
    }

    protected override void OnMount()
    {
        // Push focus into the dialog so keyboard navigation works without
        // an initial Tab. Done in OnMount (not OnAfterBuild) because the
        // ComponentHost's first Build runs in its constructor — before the
        // host is added to the App tree, so FocusManager isn't reachable yet.
        if (_state.Entries.Count > 0)
            _state.RowRefs.GetValueOrDefault(_state.Entries[0].Hash)?.Current?.Focus();
    }

    protected override void OnAfterBuild()
    {
        if (_pendingRowFocus is not null
            && _state.RowRefs.TryGetValue(_pendingRowFocus, out var rowRef))
        {
            rowRef.Current?.Focus();
            _pendingRowFocus = null;
        }

        if (_pendingDropdownFocus)
        {
            _pendingDropdownFocus = false;
            _dropdownRef?.Current?.Focus();
        }
    }

    private Element BuildEntryRow(int index)
    {
        var entry = _state.Entries[index];
        var isDropped = entry.Op == RebaseOp.Drop;

        var rowRef = new Ref<BoxElement>();
        _state.RowRefs[entry.Hash] = rowRef;

        // The row itself is the keyboard-focus target. AlignItems flips for
        // Reword (which hosts a tall TextArea) so the small fixed-height
        // controls sit on top instead of being centered against the editor.
        bool isFocused = _state.FocusedHash == entry.Hash;
        var row = Flex.Row(gap: 8)
            .Padding(new Thickness(4, 8, 4, 8))
            .AlignItems(entry.Op == RebaseOp.Reword ? AlignItems.FlexStart : AlignItems.Center)
            .FlexShrink(0)
            .Background(isDropped ? Color.Transparent : GitClientColors.Surface)
            .CornerRadius(3f)
            .BorderColor(isFocused ? FocusRingColor : Color.Transparent)
            .BorderWidth(isFocused ? 1 : 0);
        row.Key = $"rebase-entry-{entry.Hash}";
        row.IsFocusable = true;
        row.AccessibleName = $"Commit {entry.ShortHash}: {entry.Subject}. Action {entry.Op}.";
        row.WithRef(rowRef);

        // Track focus in state so the focus ring survives rebuilds (PatchFrom
        // would otherwise reset BorderColor/BorderWidth from the new tree). The
        // border is also applied directly here so a focus change without a
        // rebuild repaints immediately.
        row.On<FocusEvent>(_ =>
        {
            _state.FocusedHash = entry.Hash;
            row.BorderColor = FocusRingColor;
            if (row.Box.BorderWidth == new Thickness(0))
                row.Box.BorderWidth = new Thickness(1);
            row.MarkPaintDirty();
        });
        row.On<BlurEvent>(_ =>
        {
            if (_state.FocusedHash == entry.Hash) _state.FocusedHash = null;
            // Only clear the border if it's the focus ring (not a drag indicator).
            if (row.Box.BorderWidth == new Thickness(1))
            {
                row.BorderColor = Color.Transparent;
                row.Box.BorderWidth = new Thickness(0);
            }
            row.MarkPaintDirty();
        });

        // Keyboard shortcuts only fire when the row itself is the target —
        // letting child TextAreas (in reword mode) handle their own keys.
        row.On<KeyDownEvent>(EventPhase.Target, e =>
        {
            // Look up the index dynamically: PatchFrom doesn't copy handlers,
            // so this closure is created on first build. Re-orderings since
            // then mean the captured `index` from build time is stale.
            int currentIdx = _state.Entries.IndexOf(entry);
            if (currentIdx < 0) return;

            switch (e.Key)
            {
                case KeyCode.Up when currentIdx > 0:
                    _state.RowRefs[_state.Entries[currentIdx - 1].Hash].Current?.Focus();
                    e.StopPropagation = true;
                    break;

                case KeyCode.Down when currentIdx < _state.Entries.Count - 1:
                    _state.RowRefs[_state.Entries[currentIdx + 1].Hash].Current?.Focus();
                    e.StopPropagation = true;
                    break;

                case KeyCode.Space or KeyCode.Enter:
                    OpenDropdown(entry, row);
                    e.StopPropagation = true;
                    break;

                default:
                    if (TryOpFromKey(e.Key, out var op))
                    {
                        SetOp(entry, op);
                        e.StopPropagation = true;
                    }
                    break;
            }
        });

        // Move up/down
        row.AddChild(MoveButton("▲", enabled: index > 0, () =>
        {
            if (index > 0)
            {
                (_state.Entries[index - 1], _state.Entries[index]) =
                    (_state.Entries[index], _state.Entries[index - 1]);
                _state.RefreshDialog?.Invoke();
            }
        }));
        row.AddChild(MoveButton("▼", enabled: index < _state.Entries.Count - 1, () =>
        {
            if (index < _state.Entries.Count - 1)
            {
                (_state.Entries[index + 1], _state.Entries[index]) =
                    (_state.Entries[index], _state.Entries[index + 1]);
                _state.RefreshDialog?.Invoke();
            }
        }));

        // Op chip — click opens the dropdown. The chip itself is not focusable;
        // clicking inside the row routes focus to the row via FindFocusableAncestor.
        row.AddChild(BuildOpChip(entry, row));

        // Hash
        row.AddChild(
            Text.New(entry.ShortHash).FontSize(11f)
                .TextColor(GitClientColors.DiffHunkText)
                .Width(60).FlexShrink(0)
        );

        // Subject — editable in reword mode, static otherwise.
        if (entry.Op == RebaseOp.Reword)
            row.AddChild(BuildRewordInput(entry));
        else
            row.AddChild(
                Text.New(entry.Subject).FontSize(12f)
                    .TextColor(isDropped ? GitClientColors.TextMuted : GitClientColors.TextPrimary)
                    .FlexGrow(1).FlexShrink(1)
                    .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
            );

        WireDragReorder(row, entry);
        return row;
    }

    private static bool TryOpFromKey(KeyCode key, out RebaseOp op)
    {
        switch (key)
        {
            case KeyCode.P: op = RebaseOp.Pick;   return true;
            case KeyCode.R: op = RebaseOp.Reword; return true;
            case KeyCode.E: op = RebaseOp.Edit;   return true;
            case KeyCode.S: op = RebaseOp.Squash; return true;
            case KeyCode.F: op = RebaseOp.Fixup;  return true;
            case KeyCode.D: op = RebaseOp.Drop;   return true;
            default:        op = default;         return false;
        }
    }

    private void SetOp(RebaseEntry entry, RebaseOp op)
    {
        if (entry.Op == op) return;
        entry.Op = op;
        // Seed the new message the first time Reword is selected so users tweak
        // the existing subject rather than typing from scratch.
        if (op == RebaseOp.Reword && entry.NewMessage is null)
            entry.NewMessage = entry.Subject;
        _state.RefreshDialog?.Invoke();
    }

    private void OpenDropdown(RebaseEntry entry, BoxElement chipAnchor)
    {
        // Use the row's bounds as the anchor — the chip is a child but the
        // row's bounds are well-defined and stable across reflows.
        _state.OpenDropdownEntry = entry;
        _state.OpenDropdownAnchor = chipAnchor.Layout.AbsoluteBorderBox;
        _state.DropdownHighlight = entry.Op;
        _pendingDropdownFocus = true;
        _state.RefreshDialog?.Invoke();
    }

    private void CloseDropdown()
    {
        if (_state.OpenDropdownEntry is null) return;
        var hashToFocus = _state.OpenDropdownEntry.Hash;
        _state.OpenDropdownEntry = null;
        _pendingRowFocus = hashToFocus;
        _state.RefreshDialog?.Invoke();
    }

    private Element BuildDropdownOverlay(RebaseEntry entry)
    {
        // Position the dropdown directly under the chip. Width is fixed so
        // the user gets the same target on every row.
        const float DropdownWidth = 130f;
        float x = _state.OpenDropdownAnchor.X;
        float y = _state.OpenDropdownAnchor.Y + _state.OpenDropdownAnchor.Height + 2f;

        _dropdownRef = new Ref<BoxElement>();

        var dropdown = Box.New()
            .Width(DropdownWidth)
            .Padding(new Thickness(4, 0, 4, 0))
            .CornerRadius(4f)
            .Background(DialogBg)
            .BorderColor(DialogBorder)
            .BorderWidth(1)
            .ZIndex(200);
        dropdown.Position.Type = PositionType.Absolute;
        dropdown.Position.Top = Dimension.Px(y);
        dropdown.Position.Left = Dimension.Px(x);
        dropdown.IsFocusable = true;
        dropdown.Role = AccessibilityRole.List;
        dropdown.AccessibleName = "Rebase action";
        dropdown.WithRef(_dropdownRef);
        // Catch clicks both at Target (clicks landing directly on the panel)
        // and Bubble (clicks on items) so the backdrop's outside-click handler
        // never fires for clicks inside the dropdown.
        dropdown.On<ClickEvent>(EventPhase.Target, e => e.StopPropagation = true);
        dropdown.On<ClickEvent>(e => e.StopPropagation = true);

        // Stable key so reconciler reuses the same overlay element across
        // dropdown rebuilds (e.g. when arrow keys move the highlight) and
        // keyboard focus stays put.
        dropdown.Key = $"rebase-dropdown-{entry.Hash}";

        dropdown.On<KeyDownEvent>(e =>
        {
            int idx = Array.IndexOf(AllOps, _state.DropdownHighlight);
            if (idx < 0) idx = 0;

            switch (e.Key)
            {
                case KeyCode.Up:
                    _state.DropdownHighlight = AllOps[(idx - 1 + AllOps.Length) % AllOps.Length];
                    _state.RefreshDialog?.Invoke();
                    e.StopPropagation = true;
                    break;
                case KeyCode.Down:
                    _state.DropdownHighlight = AllOps[(idx + 1) % AllOps.Length];
                    _state.RefreshDialog?.Invoke();
                    e.StopPropagation = true;
                    break;
                case KeyCode.Home:
                    _state.DropdownHighlight = AllOps[0];
                    _state.RefreshDialog?.Invoke();
                    e.StopPropagation = true;
                    break;
                case KeyCode.End:
                    _state.DropdownHighlight = AllOps[^1];
                    _state.RefreshDialog?.Invoke();
                    e.StopPropagation = true;
                    break;
                case KeyCode.Enter:
                    SetOp(entry, _state.DropdownHighlight);
                    CloseDropdown();
                    e.StopPropagation = true;
                    break;
                case KeyCode.Escape:
                    CloseDropdown();
                    e.StopPropagation = true;
                    break;
                default:
                    if (TryOpFromKey(e.Key, out var op))
                    {
                        SetOp(entry, op);
                        CloseDropdown();
                        e.StopPropagation = true;
                    }
                    break;
            }
        });

        var col = Flex.Column(gap: 0).Padding(new Thickness(4, 0, 4, 0));
        foreach (var op in AllOps)
            col.AddChild(BuildDropdownItem(op, entry, op == _state.DropdownHighlight));
        dropdown.AddChild(col);

        return dropdown;
    }

    private Element BuildDropdownItem(RebaseOp op, RebaseEntry entry, bool highlighted)
    {
        var (label, color) = OpAppearance(op);
        var bg = highlighted ? GitClientColors.SurfaceHover : Color.Transparent;
        var hint = OpHint(op);

        var item = Flex.Row(gap: 8)
            .Padding(new Thickness(4, 8, 4, 8))
            .AlignItems(AlignItems.Center)
            .CornerRadius(3f)
            .Background(bg)
            .Cursor(CursorType.Pointer);
        item.Key = $"rebase-dropdown-item-{op}";

        item.On<PointerEnterEvent>(_ =>
        {
            if (_state.DropdownHighlight == op) return;
            _state.DropdownHighlight = op;
            _state.RefreshDialog?.Invoke();
        });

        item.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            SetOp(entry, op);
            CloseDropdown();
        });

        item.AddChild(
            Text.New(label).FontSize(12f).FontWeight(FontWeight.SemiBold)
                .TextColor(color).Width(50).FlexShrink(0)
        );
        item.AddChild(
            Text.New(hint).FontSize(10f).TextColor(GitClientColors.TextMuted)
                .FlexGrow(1)
        );
        return item;
    }

    private static string OpHint(RebaseOp op) => op switch
    {
        RebaseOp.Pick   => "P",
        RebaseOp.Reword => "R",
        RebaseOp.Edit   => "E",
        RebaseOp.Squash => "S",
        RebaseOp.Fixup  => "F",
        RebaseOp.Drop   => "D",
        _ => "",
    };

    private static (string Label, Color Color) OpAppearance(RebaseOp op) => op switch
    {
        RebaseOp.Pick   => ("pick",   GitClientColors.StatusAdded),
        RebaseOp.Reword => ("reword", GitClientColors.TagColor),
        RebaseOp.Edit   => ("edit",   GitClientColors.BranchCurrent),
        RebaseOp.Squash => ("squash", GitClientColors.BranchLocal),
        RebaseOp.Fixup  => ("fixup",  GitClientColors.BranchRemote),
        RebaseOp.Drop   => ("drop",   GitClientColors.StatusDeleted),
        _ => ("pick", GitClientColors.StatusAdded),
    };

    private Element BuildRewordInput(RebaseEntry entry)
    {
        // Multi-line editor so users can add a body below the subject. Height
        // is derived from the explicit newline count — wrap-based growth would
        // need pixel-width measurement that isn't available during Build().
        var text = entry.NewMessage ?? entry.Subject;
        int lineCount = CountLines(text);
        float lineHeight = 16f;
        float chrome = 10f;
        float height = Math.Max(1, lineCount) * lineHeight + chrome;

        var area = TextArea.New(text)
            .OnTextChanged(newText =>
            {
                var prevLines = CountLines(entry.NewMessage ?? entry.Subject);
                entry.NewMessage = newText;
                if (CountLines(newText) != prevLines)
                    _state.RefreshDialog?.Invoke();
            })
            .FontSize(12f)
            .FlexGrow(1).FlexShrink(1)
            .Height(Dimension.Px(height))
            .Padding(new Thickness(3, 6, 3, 6))
            .Background(GitClientColors.Background)
            .CornerRadius(3f)
            .BorderColor(GitClientColors.Border)
            .TextColor(GitClientColors.TextPrimary);
        area.Key = $"reword-input-{entry.Hash}";
        return area;
    }

    private static int CountLines(string text) =>
        string.IsNullOrEmpty(text) ? 1 : text.Count(c => c == '\n') + 1;

    /// <summary>
    /// Makes a row draggable and a drop target for reorder. Reuses the row's
    /// BorderWidth as the insertion indicator (top or bottom edge depending
    /// on which half of the row the cursor is in).
    /// </summary>
    private void WireDragReorder(BoxElement row, RebaseEntry entry)
    {
        row.Draggable();

        row.On<DragStartEvent>(e => e.DragData.Set("rebase-entry", entry));

        row.On<DragOverEvent>(e =>
        {
            if (!e.DragData.TryGet<RebaseEntry>("rebase-entry", out var src) || src == entry)
                return;

            e.Accepted = true;

            var bounds = row.Layout.AbsoluteBorderBox;
            bool above = e.Position.Y < bounds.Y + bounds.Height / 2;
            row.Box.BorderWidth = above
                ? new Thickness(2, 0, 0, 0)
                : new Thickness(0, 0, 2, 0);
            row.BorderColor = DropIndicator;
            row.MarkPaintDirty();
        });

        row.On<DragLeaveEvent>(_ =>
        {
            row.Box.BorderWidth = new Thickness(0);
            row.MarkPaintDirty();
        });

        row.On<DropEvent>(e =>
        {
            if (!e.DragData.TryGet<RebaseEntry>("rebase-entry", out var src) || src == entry)
                return;

            int sourceIdx = _state.Entries.IndexOf(src!);
            int targetIdx = _state.Entries.IndexOf(entry);
            if (sourceIdx == -1 || targetIdx == -1) return;

            var bounds = row.Layout.AbsoluteBorderBox;
            bool above = e.Position.Y < bounds.Y + bounds.Height / 2;

            _state.Entries.RemoveAt(sourceIdx);
            int insertIdx = above ? targetIdx : targetIdx + 1;
            // Removing the source before the target shifts the target down by one.
            if (sourceIdx < insertIdx) insertIdx--;
            _state.Entries.Insert(Math.Clamp(insertIdx, 0, _state.Entries.Count), src!);

            row.Box.BorderWidth = new Thickness(0);
            _state.RefreshDialog?.Invoke();
        });
    }

    private static Element MoveButton(string glyph, bool enabled, Action onClick)
    {
        var btn = Box.New()
            .Width(22).Height(22)
            .CornerRadius(3f)
            .Background(enabled ? GitClientColors.ButtonSecondary : Color.Transparent)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(enabled ? CursorType.Pointer : CursorType.Default)
            .Children(
                Text.New(glyph).FontSize(9f)
                    .TextColor(enabled ? GitClientColors.TextPrimary : GitClientColors.TextMuted)
            );

        if (enabled)
        {
            btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondaryHover);
            btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondary);
            btn.On<ClickEvent>(e => { e.StopPropagation = true; onClick(); });
        }
        return btn;
    }

    private Element BuildOpChip(RebaseEntry entry, BoxElement row)
    {
        var (label, color) = OpAppearance(entry.Op);

        // Explicit Width + Height pins the chip to pill proportions and keeps
        // the dropdown horizontally aligned from row to row.
        var chip = Box.New()
            .Width(78)
            .Height(22)
            .FlexShrink(0)
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonSecondary)
            .BorderColor(color)
            .BorderWidth(1)
            .Cursor(CursorType.Pointer)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Tooltip("Click to choose action: pick / reword / edit / squash / fixup / drop")
            .Children(
                Flex.Row(gap: 4).AlignItems(AlignItems.Center).Children(
                    Text.New(label).FontSize(10f).FontWeight(FontWeight.SemiBold).TextColor(color),
                    Text.New("▾").FontSize(8f).TextColor(GitClientColors.TextSecondary)
                )
            );

        chip.On<PointerEnterEvent>(_ => chip.BackgroundColor = GitClientColors.ButtonSecondaryHover);
        chip.On<PointerLeaveEvent>(_ => chip.BackgroundColor = GitClientColors.ButtonSecondary);
        chip.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            // Anchor on the chip itself so the dropdown lines up with it.
            OpenDropdown(entry, chip);
        });
        return chip;
    }

    private static Element DialogButton(string label, bool primary, Action onClick)
    {
        var bg = primary ? GitClientColors.ButtonPrimary : GitClientColors.ButtonSecondary;
        var hover = primary ? GitClientColors.ButtonPrimaryHover : GitClientColors.ButtonSecondaryHover;

        var btn = Box.New()
            .Padding(new Thickness(8, 16, 8, 16))
            .CornerRadius(4f)
            .Background(bg)
            .Cursor(CursorType.Pointer)
            .Children(
                Text.New(label).FontSize(13f).FontWeight(FontWeight.SemiBold)
                    .TextColor(Color.White)
            );
        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = hover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = bg);
        btn.On<ClickEvent>(e => { e.StopPropagation = true; onClick(); });
        return btn;
    }
}
