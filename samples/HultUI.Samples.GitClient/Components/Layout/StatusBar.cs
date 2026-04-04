using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Layout;

public static class StatusBar
{
    public static Element Build(
        string branchName,
        int ahead,
        int behind,
        int changedFiles,
        Action onFetch,
        Action onPull,
        Action onPush)
    {
        var aheadBehind = "";
        if (ahead > 0) aheadBehind += $" \u2191{ahead}";
        if (behind > 0) aheadBehind += $" \u2193{behind}";

        return Flex.Row(gap: 16)
            .AlignItems(AlignItems.Center)
            .Height(Dimension.Px(32))
            .Background(GitClientColors.StatusBar)
            .Padding(new Thickness(0, 12, 0, 12))
            .FlexShrink(0)
            .Children(
                // Branch indicator
                Text.New("\u25CF").FontSize(10f).TextColor(GitClientColors.BranchCurrent),
                Text.New(branchName).FontSize(12f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.TextPrimary),

                // Ahead/behind
                aheadBehind.Length > 0
                    ? Text.New(aheadBehind.Trim()).FontSize(12f).TextColor(GitClientColors.TextSecondary)
                    : Box.New().Width(0),

                // Separator
                Box.New().Width(1).Height(16).Background(GitClientColors.Border),

                // Changed files
                Text.New($"{changedFiles} changed").FontSize(12f)
                    .TextColor(changedFiles > 0 ? GitClientColors.StatusModified : GitClientColors.TextMuted),

                // Spacer
                Box.New().FlexGrow(1),

                // Action buttons
                ActionButton("Fetch", onFetch),
                ActionButton("Pull", onPull),
                ActionButton("Push", onPush)
            );
    }

    private static Element ActionButton(string label, Action onClick)
    {
        var btn = Box.New()
            .Padding(new Thickness(4, 10, 4, 10))
            .CornerRadius(4f)
            .Background(GitClientColors.ButtonSecondary)
            .Cursor(CursorType.Pointer)
            .Children(
                Text.New(label).FontSize(11f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.TextPrimary)
            );

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondaryHover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondary);
        btn.On<ClickEvent>(_ => onClick());

        return btn;
    }
}
