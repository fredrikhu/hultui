using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.Shared;

/// <summary>
/// Mutable state driving the interactive-rebase modal. The owner mutates this in
/// response to UI events and calls <see cref="RefreshDialog"/> to redraw.
/// </summary>
public sealed class InteractiveRebaseState
{
    public required string BaseRef { get; init; }
    public required string BaseShortHash { get; init; }
    public required List<RebaseEntry> Entries { get; init; }
    public required Action<IReadOnlyList<RebaseEntry>> OnConfirm { get; init; }
    public Action? OnCancel { get; init; }
    public Action? RefreshDialog { get; set; }
}

/// <summary>
/// Modal dialog for planning an interactive rebase: a list of commits with
/// per-commit action dropdowns and move-up/down buttons. Confirming the plan
/// triggers <c>git rebase -i</c> under <c>GIT_SEQUENCE_EDITOR</c>.
///
/// Drag-and-drop reorder isn't implemented here — arrow buttons cover the same
/// ground and avoid the need for drag event infrastructure in this sample.
/// </summary>
public static class InteractiveRebaseDialog
{
    private static readonly Color BackdropColor = new(0, 0, 0, 140);
    private static readonly Color DialogBg = Color.FromHex("#2a2a48");
    private static readonly Color DialogBorder = Color.FromHex("#3a3a5a");

    public static Element Build(InteractiveRebaseState state, Action dismiss)
    {
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
                   + "Reorder with \u25B2/\u25BC, change the action per commit, then Confirm.")
                .FontSize(12f).TextColor(GitClientColors.TextSecondary)
        );

        // Commit list — rows stack vertically inside a bounded ScrollView.
        // The MaxHeight must live on the ScrollView itself (not the inner
        // Flex.Column) so overflow actually triggers scrolling instead of
        // just clamping the list and leaving the viewport the same size.
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

        // Buttons
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
        return backdrop;
    }

    private static Element BuildEntryRow(InteractiveRebaseState state, int index)
    {
        var entry = state.Entries[index];
        var isDropped = entry.Op == RebaseOp.Drop;

        // FlexShrink(0) keeps the row at its natural height even when the
        // list is inside a height-bounded ScrollView — without it, the
        // column would try to compress rows to fit.
        var row = Flex.Row(gap: 8)
            .Padding(new Thickness(4, 8, 4, 8))
            .AlignItems(AlignItems.Center)
            .FlexShrink(0)
            .Background(isDropped ? Color.Transparent : GitClientColors.Surface)
            .CornerRadius(3f);
        row.Key = $"rebase-entry-{entry.Hash}";

        // Move up/down
        row.AddChild(MoveButton("\u25B2", enabled: index > 0, () =>
        {
            if (index > 0)
            {
                (state.Entries[index - 1], state.Entries[index]) =
                    (state.Entries[index], state.Entries[index - 1]);
                state.RefreshDialog?.Invoke();
            }
        }));
        row.AddChild(MoveButton("\u25BC", enabled: index < state.Entries.Count - 1, () =>
        {
            if (index < state.Entries.Count - 1)
            {
                (state.Entries[index + 1], state.Entries[index]) =
                    (state.Entries[index], state.Entries[index + 1]);
                state.RefreshDialog?.Invoke();
            }
        }));

        // Op cycler (click to cycle: pick → squash → fixup → drop → pick)
        row.AddChild(OpChip(entry, state));

        // Hash + subject
        row.AddChild(
            Text.New(entry.ShortHash).FontSize(11f)
                .TextColor(GitClientColors.DiffHunkText)
                .Width(60).FlexShrink(0)
        );
        row.AddChild(
            Text.New(entry.Subject).FontSize(12f)
                .TextColor(isDropped ? GitClientColors.TextMuted : GitClientColors.TextPrimary)
                .FlexGrow(1).FlexShrink(1)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
        );

        return row;
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

    private static Element OpChip(RebaseEntry entry, InteractiveRebaseState state)
    {
        var (label, color) = entry.Op switch
        {
            RebaseOp.Pick => ("pick", GitClientColors.StatusAdded),
            RebaseOp.Squash => ("squash", GitClientColors.BranchLocal),
            RebaseOp.Fixup => ("fixup", GitClientColors.BranchRemote),
            RebaseOp.Drop => ("drop", GitClientColors.StatusDeleted),
            _ => ("pick", GitClientColors.StatusAdded),
        };

        // Explicit Width + Height pins the chip to pill proportions. Without
        // Height, the default AlignItems=Stretch on the parent layout chain
        // could let the chip grow vertically to match row height.
        var chip = Box.New()
            .Width(66)
            .Height(22)
            .FlexShrink(0)
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonSecondary)
            .BorderColor(color)
            .BorderWidth(1)
            .Cursor(CursorType.Pointer)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Tooltip("Click to cycle: pick \u2192 squash \u2192 fixup \u2192 drop")
            .Children(
                Text.New(label).FontSize(10f).FontWeight(FontWeight.SemiBold).TextColor(color)
            );

        chip.On<PointerEnterEvent>(_ => chip.BackgroundColor = GitClientColors.ButtonSecondaryHover);
        chip.On<PointerLeaveEvent>(_ => chip.BackgroundColor = GitClientColors.ButtonSecondary);
        chip.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            entry.Op = entry.Op switch
            {
                RebaseOp.Pick => RebaseOp.Squash,
                RebaseOp.Squash => RebaseOp.Fixup,
                RebaseOp.Fixup => RebaseOp.Drop,
                _ => RebaseOp.Pick,
            };
            state.RefreshDialog?.Invoke();
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
