using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
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
/// Mutable state driving the interactive-rebase modal. The owner mutates this
/// in response to UI events and calls <see cref="RefreshDialog"/> to redraw.
/// Focus restoration after a rebuild is driven by <see cref="PendingRowFocus"/>
/// and <see cref="PendingDropdownFocus"/>, which the host (App) consumes in its
/// <see cref="HultUI.Core.Components.Component.OnAfterBuild"/> hook.
/// </summary>
public sealed class InteractiveRebaseState
{
    public required string BaseRef { get; init; }
    public required string BaseShortHash { get; init; }
    public required List<RebaseEntry> Entries { get; init; }
    public required Action<IReadOnlyList<RebaseEntry>> OnConfirm { get; init; }
    public Action? OnCancel { get; init; }
    public Action? RefreshDialog { get; set; }

    /// <summary>Per-entry refs to the focusable row elements. Repopulated on
    /// every <see cref="InteractiveRebaseDialog.Build"/>; refs are repointed
    /// by <see cref="Element.PatchFrom"/> to the live tree, so arrow-key
    /// handlers can call <c>.Current?.Focus()</c> without hunting.</summary>
    internal Dictionary<string, Ref<BoxElement>> RowRefs = new();

    /// <summary>If non-null, the next App rebuild should focus the row with
    /// this hash. Used to restore focus after rebuilds where the row that
    /// should be focused isn't necessarily the previously-focused element
    /// (e.g. when the dropdown closes).</summary>
    public string? PendingRowFocus { get; set; }

    /// <summary>If true, focus the open dropdown after the next build.</summary>
    public bool PendingDropdownFocus { get; set; }

    /// <summary>Ref the dropdown overlay attaches itself to, populated during
    /// build and consumed by <see cref="PendingDropdownFocus"/>.</summary>
    internal Ref<BoxElement> DropdownRef = new();

    /// <summary>The entry whose op-dropdown is currently open, or null if closed.</summary>
    internal RebaseEntry? OpenDropdownEntry;

    /// <summary>Anchor box of the chip that opened the dropdown, in window coords.</summary>
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
///   - Tab / Shift+Tab — move between rows + dialog buttons (framework).
///   - Up / Down on a focused row — move focus to the previous/next row.
///   - p/r/e/s/f/d on a focused row — set the op directly.
///   - Space / Enter on a focused row — open the op dropdown.
///   - Arrow keys + letter keys inside the dropdown — navigate / pick.
///   - Escape — close the dropdown if open, otherwise dismiss the dialog.
/// </summary>
public static class InteractiveRebaseDialog
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

    public static Element Build(InteractiveRebaseState state, Action dismiss)
    {
        // Reset per-build artifacts. Entries can have been reordered or dropped
        // since the last frame, so stale row-ref keys must go.
        state.RowRefs.Clear();

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
            if (state.OpenDropdownEntry is not null)
            {
                CloseDropdown(state);
                return;
            }
            state.OnCancel?.Invoke();
            dismiss();
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
            Text.New($"Rebase {state.Entries.Count} commit{(state.Entries.Count == 1 ? "" : "s")} onto {state.BaseShortHash}. "
                   + "Use ↑/↓ to move between rows, Space to open the action menu, or press p/r/e/s/f/d to set the action directly.")
                .FontSize(12f).TextColor(GitClientColors.TextSecondary)
        );

        var list = Flex.Column(gap: 4);
        for (int i = 0; i < state.Entries.Count; i++)
            list.AddChild(BuildEntryRow(state, i));

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
            state.OnCancel?.Invoke();
            dismiss();
        }));

        var effective = state.Entries.Count(e => e.Op != RebaseOp.Drop);
        buttons.AddChild(DialogButton(
            effective == 0 ? "Nothing to do" : "Confirm",
            primary: effective > 0,
            () =>
            {
                if (effective == 0) return;
                state.OnConfirm(state.Entries);
                dismiss();
            }));

        content.AddChild(buttons);
        panel.AddChild(content);
        backdrop.AddChild(panel);

        // Dropdown overlay sits as an absolute sibling of the panel inside the
        // backdrop so its position is interpreted in window coordinates.
        if (state.OpenDropdownEntry is { } openEntry)
            backdrop.AddChild(BuildDropdownOverlay(state, openEntry));

        return backdrop;
    }

    private static Element BuildEntryRow(InteractiveRebaseState state, int index)
    {
        var entry = state.Entries[index];
        var isDropped = entry.Op == RebaseOp.Drop;

        var rowRef = new Ref<BoxElement>();
        state.RowRefs[entry.Hash] = rowRef;

        bool isFocused = state.FocusedHash == entry.Hash;
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

        // Track focus and paint the border directly on the *live* row — not on
        // the closure's `row`, which can be an orphan after a panel-replace
        // rebuild (e.g. when the dropdown opens). state.RowRefs[hash].Current
        // is repointed to the live tree by PatchFrom each rebuild, so it always
        // refers to the element actually rendered on screen.
        //
        // We deliberately do NOT call RefreshDialog from these handlers:
        // every focus change would trigger a synchronous nested App rebuild,
        // and a single arrow-key press would cascade through Blur+Focus events
        // each spawning their own rebuild, freezing the UI.
        row.On<FocusEvent>(_ =>
        {
            state.FocusedHash = entry.Hash;
            var live = state.RowRefs.GetValueOrDefault(entry.Hash)?.Current;
            if (live is null) return;
            live.BorderColor = FocusRingColor;
            if (live.Box.BorderWidth == new Thickness(0))
                live.Box.BorderWidth = new Thickness(1);
            live.MarkPaintDirty();
        });
        row.On<BlurEvent>(_ =>
        {
            if (state.FocusedHash == entry.Hash) state.FocusedHash = null;
            var live = state.RowRefs.GetValueOrDefault(entry.Hash)?.Current;
            if (live is null) return;
            // Only clear if it's the focus ring, not a drag indicator.
            if (live.Box.BorderWidth != new Thickness(1)) return;
            live.BorderColor = Color.Transparent;
            live.Box.BorderWidth = new Thickness(0);
            live.MarkPaintDirty();
        });

        // Keyboard shortcuts only fire when the row itself is the target —
        // letting child TextAreas (in reword mode) handle their own keys.
        row.On<KeyDownEvent>(EventPhase.Target, e =>
        {
            // Look up the index dynamically: PatchFrom doesn't copy handlers,
            // so this closure is created on first build. Re-orderings since
            // then mean the captured `index` from build time is stale.
            int currentIdx = state.Entries.IndexOf(entry);
            if (currentIdx < 0) return;

            switch (e.Key)
            {
                case KeyCode.Up when currentIdx > 0:
                    state.RowRefs[state.Entries[currentIdx - 1].Hash].Current?.Focus();
                    e.StopPropagation = true;
                    break;

                case KeyCode.Down when currentIdx < state.Entries.Count - 1:
                    state.RowRefs[state.Entries[currentIdx + 1].Hash].Current?.Focus();
                    e.StopPropagation = true;
                    break;

                case KeyCode.Space or KeyCode.Enter:
                    OpenDropdown(state, entry, row);
                    e.StopPropagation = true;
                    break;

                default:
                    if (TryOpFromKey(e.Key, out var op))
                    {
                        SetOp(state, entry, op);
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
                (state.Entries[index - 1], state.Entries[index]) =
                    (state.Entries[index], state.Entries[index - 1]);
                state.RefreshDialog?.Invoke();
            }
        }));
        row.AddChild(MoveButton("▼", enabled: index < state.Entries.Count - 1, () =>
        {
            if (index < state.Entries.Count - 1)
            {
                (state.Entries[index + 1], state.Entries[index]) =
                    (state.Entries[index], state.Entries[index + 1]);
                state.RefreshDialog?.Invoke();
            }
        }));

        // Op chip — click opens the dropdown. The chip itself is not focusable;
        // clicking inside the row routes focus to the row via FindFocusableAncestor.
        row.AddChild(BuildOpChip(state, entry));

        // Hash
        row.AddChild(
            Text.New(entry.ShortHash).FontSize(11f)
                .TextColor(GitClientColors.DiffHunkText)
                .Width(60).FlexShrink(0)
        );

        // Subject — editable in reword mode, static otherwise.
        if (entry.Op == RebaseOp.Reword)
            row.AddChild(BuildRewordInput(state, entry));
        else
            row.AddChild(
                Text.New(entry.Subject).FontSize(12f)
                    .TextColor(isDropped ? GitClientColors.TextMuted : GitClientColors.TextPrimary)
                    .FlexGrow(1).FlexShrink(1)
                    .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
            );

        WireDragReorder(row, entry, state);
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

    private static void SetOp(InteractiveRebaseState state, RebaseEntry entry, RebaseOp op)
    {
        if (entry.Op == op) return;
        entry.Op = op;
        // Seed the new message the first time Reword is selected so users tweak
        // the existing subject rather than typing from scratch.
        if (op == RebaseOp.Reword && entry.NewMessage is null)
            entry.NewMessage = entry.Subject;
        state.RefreshDialog?.Invoke();
    }

    private static void OpenDropdown(InteractiveRebaseState state, RebaseEntry entry, BoxElement chipAnchor)
    {
        state.OpenDropdownEntry = entry;
        state.OpenDropdownAnchor = chipAnchor.Layout.AbsoluteBorderBox;
        state.DropdownHighlight = entry.Op;
        state.PendingDropdownFocus = true;
        state.RefreshDialog?.Invoke();
    }

    private static void CloseDropdown(InteractiveRebaseState state)
    {
        if (state.OpenDropdownEntry is null) return;
        state.PendingRowFocus = state.OpenDropdownEntry.Hash;
        state.OpenDropdownEntry = null;
        state.RefreshDialog?.Invoke();
    }

    private static Element BuildDropdownOverlay(InteractiveRebaseState state, RebaseEntry entry)
    {
        const float DropdownWidth = 130f;
        float x = state.OpenDropdownAnchor.X;
        float y = state.OpenDropdownAnchor.Y + state.OpenDropdownAnchor.Height + 2f;

        // Reuse the same Ref instance each build so refs registered on prior
        // builds keep working even before reconciliation re-points them.
        var dropdownRef = state.DropdownRef;

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
        dropdown.WithRef(dropdownRef);
        // Catch clicks both at Target (clicks landing directly on the panel)
        // and Bubble (clicks on items) so the backdrop's outside-click handler
        // never fires for clicks inside the dropdown.
        dropdown.On<ClickEvent>(EventPhase.Target, e => e.StopPropagation = true);
        dropdown.On<ClickEvent>(e => e.StopPropagation = true);
        dropdown.Key = $"rebase-dropdown-{entry.Hash}";

        dropdown.On<KeyDownEvent>(e =>
        {
            int idx = Array.IndexOf(AllOps, state.DropdownHighlight);
            if (idx < 0) idx = 0;

            switch (e.Key)
            {
                case KeyCode.Up:
                    state.DropdownHighlight = AllOps[(idx - 1 + AllOps.Length) % AllOps.Length];
                    state.RefreshDialog?.Invoke();
                    e.StopPropagation = true;
                    break;
                case KeyCode.Down:
                    state.DropdownHighlight = AllOps[(idx + 1) % AllOps.Length];
                    state.RefreshDialog?.Invoke();
                    e.StopPropagation = true;
                    break;
                case KeyCode.Home:
                    state.DropdownHighlight = AllOps[0];
                    state.RefreshDialog?.Invoke();
                    e.StopPropagation = true;
                    break;
                case KeyCode.End:
                    state.DropdownHighlight = AllOps[^1];
                    state.RefreshDialog?.Invoke();
                    e.StopPropagation = true;
                    break;
                case KeyCode.Enter:
                    SetOp(state, entry, state.DropdownHighlight);
                    CloseDropdown(state);
                    e.StopPropagation = true;
                    break;
                case KeyCode.Escape:
                    CloseDropdown(state);
                    e.StopPropagation = true;
                    break;
                default:
                    if (TryOpFromKey(e.Key, out var op))
                    {
                        SetOp(state, entry, op);
                        CloseDropdown(state);
                        e.StopPropagation = true;
                    }
                    break;
            }
        });

        var col = Flex.Column(gap: 0).Padding(new Thickness(4, 0, 4, 0));
        foreach (var op in AllOps)
            col.AddChild(BuildDropdownItem(state, op, entry, op == state.DropdownHighlight));
        dropdown.AddChild(col);

        return dropdown;
    }

    private static Element BuildDropdownItem(InteractiveRebaseState state, RebaseOp op, RebaseEntry entry, bool highlighted)
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
            if (state.DropdownHighlight == op) return;
            state.DropdownHighlight = op;
            state.RefreshDialog?.Invoke();
        });

        item.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            SetOp(state, entry, op);
            CloseDropdown(state);
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

    private static Element BuildRewordInput(InteractiveRebaseState state, RebaseEntry entry)
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
                    state.RefreshDialog?.Invoke();
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
    private static void WireDragReorder(BoxElement row, RebaseEntry entry, InteractiveRebaseState state)
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

            int sourceIdx = state.Entries.IndexOf(src!);
            int targetIdx = state.Entries.IndexOf(entry);
            if (sourceIdx == -1 || targetIdx == -1) return;

            var bounds = row.Layout.AbsoluteBorderBox;
            bool above = e.Position.Y < bounds.Y + bounds.Height / 2;

            state.Entries.RemoveAt(sourceIdx);
            int insertIdx = above ? targetIdx : targetIdx + 1;
            // Removing the source before the target shifts the target down by one.
            if (sourceIdx < insertIdx) insertIdx--;
            state.Entries.Insert(Math.Clamp(insertIdx, 0, state.Entries.Count), src!);

            row.Box.BorderWidth = new Thickness(0);
            state.RefreshDialog?.Invoke();
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

    private static Element BuildOpChip(InteractiveRebaseState state, RebaseEntry entry)
    {
        var (label, color) = OpAppearance(entry.Op);

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
            OpenDropdown(state, entry, chip);
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
