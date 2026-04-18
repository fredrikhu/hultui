using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Shared;

public enum RemoteBranchMode { Push, Pull }

public sealed class RemoteBranchDialogState
{
    public required RemoteBranchMode Mode { get; init; }
    public required List<string> Remotes { get; init; }
    public required Func<string, List<string>> GetBranches { get; init; }
    public required Action<string, string> OnConfirm { get; init; }

    internal string? SelectedRemote { get; set; }
    internal string? SelectedBranch { get; set; }
    internal List<string> Branches { get; set; } = [];
}

/// <summary>
/// Modal dialog for picking a specific remote and branch for push/pull.
/// </summary>
public static class RemoteBranchDialog
{
    private static readonly Color BackdropColor = new(0, 0, 0, 140);
    private static readonly Color DialogBg = Color.FromHex("#2a2a48");
    private static readonly Color DialogBorder = Color.FromHex("#3a3a5a");
    private static readonly Color ItemHover = Color.FromHex("#353558");
    private static readonly Color ItemSelected = Color.FromHex("#404068");

    public static Element Build(RemoteBranchDialogState state, Action dismiss, Action redraw)
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
            dismiss();
        });

        var title = state.Mode == RemoteBranchMode.Push ? "Push to\u2026" : "Pull from\u2026";

        var panel = Box.New()
            .Width(420)
            .Padding(new Thickness(20, 24, 20, 24))
            .CornerRadius(8f)
            .Background(DialogBg)
            .BorderColor(DialogBorder)
            .BorderWidth(1);
        panel.On<ClickEvent>(e => e.StopPropagation = true);

        var content = Flex.Column(gap: 14);

        content.AddChild(
            Text.New(title).FontSize(16f).FontWeight(FontWeight.Bold)
                .TextColor(GitClientColors.TextPrimary)
        );

        // Remote section
        content.AddChild(
            Text.New("Remote").FontSize(12f).FontWeight(FontWeight.SemiBold)
                .TextColor(GitClientColors.TextSecondary)
        );

        var remoteList = Flex.Column(gap: 2);
        foreach (var remote in state.Remotes)
        {
            var isSelected = remote == state.SelectedRemote;
            remoteList.AddChild(BuildItem(remote, isSelected, () =>
            {
                state.SelectedRemote = remote;
                state.SelectedBranch = null;
                state.Branches = state.GetBranches(remote);
                redraw();
            }));
        }
        content.AddChild(remoteList);

        // Branch section (shown once a remote is picked)
        if (state.SelectedRemote != null && state.Branches.Count > 0)
        {
            content.AddChild(
                Text.New("Branch").FontSize(12f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.TextSecondary)
            );

            var branchScroll = ScrollView.New()
                .MaxHeight(Dimension.Px(200));

            var branchList = Flex.Column(gap: 2);
            foreach (var branch in state.Branches)
            {
                var isSelected = branch == state.SelectedBranch;
                branchList.AddChild(BuildItem(branch, isSelected, () =>
                {
                    state.SelectedBranch = branch;
                    redraw();
                }));
            }

            branchScroll.AddChild(branchList);
            content.AddChild(branchScroll);
        }

        // Buttons
        var buttons = Flex.Row(gap: 8)
            .JustifyContent(JustifyContent.FlexEnd)
            .AlignItems(AlignItems.Center);

        buttons.AddChild(BuildButton("Cancel", false, dismiss));

        var confirmLabel = state.Mode == RemoteBranchMode.Push ? "Push" : "Pull";
        var canConfirm = state.SelectedRemote != null && state.SelectedBranch != null;
        buttons.AddChild(BuildButton(confirmLabel, canConfirm, () =>
        {
            if (state.SelectedRemote != null && state.SelectedBranch != null)
            {
                state.OnConfirm(state.SelectedRemote, state.SelectedBranch);
                dismiss();
            }
        }));

        content.AddChild(buttons);
        panel.AddChild(content);
        backdrop.AddChild(panel);
        return backdrop;
    }

    private static Element BuildItem(string label, bool selected, Action onClick)
    {
        var bg = selected ? ItemSelected : Color.Transparent;
        var item = Box.New()
            .Padding(new Thickness(6, 10, 6, 10))
            .CornerRadius(4f)
            .Background(bg)
            .Cursor(CursorType.Pointer)
            .Children(
                Text.New(label).FontSize(13f).TextColor(GitClientColors.TextPrimary)
            );

        if (!selected)
        {
            item.On<PointerEnterEvent>(_ => item.BackgroundColor = ItemHover);
            item.On<PointerLeaveEvent>(_ => item.BackgroundColor = Color.Transparent);
        }
        item.On<ClickEvent>(e => { e.StopPropagation = true; onClick(); });
        return item;
    }

    private static Element BuildButton(string label, bool primary, Action onClick)
    {
        var bg = primary ? GitClientColors.ButtonPrimary : GitClientColors.ButtonSecondary;
        var hover = primary ? GitClientColors.ButtonPrimaryHover : GitClientColors.ButtonSecondaryHover;
        var textColor = primary ? Color.White : GitClientColors.TextSecondary;

        var btn = Box.New()
            .Padding(new Thickness(8, 16, 8, 16))
            .CornerRadius(4f)
            .Background(bg)
            .Cursor(CursorType.Pointer)
            .Children(
                Text.New(label).FontSize(13f).FontWeight(FontWeight.SemiBold)
                    .TextColor(textColor)
            );
        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = hover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = bg);
        btn.On<ClickEvent>(e => { e.StopPropagation = true; onClick(); });
        return btn;
    }
}
