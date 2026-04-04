using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Layout;

public static class TitleBar
{
    public static Element Build(Window window, string repoName, string branchName)
    {
        return Flex.Row(gap: 0)
            .WindowDrag()
            .AlignItems(AlignItems.Center)
            .Height(Dimension.Px(38))
            .Background(GitClientColors.TitleBar)
            .Padding(new Thickness(0, 12, 0, 12))
            .FlexShrink(0)
            .Children(
                // App name
                Text.New("HultGit").FontSize(13f).FontWeight(FontWeight.Bold)
                    .TextColor(GitClientColors.SelectionBorder),

                // Separator
                Text.New("  \u2022  ").FontSize(11f).TextColor(GitClientColors.TextMuted),

                // Repo + branch
                Text.New(repoName).FontSize(13f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.TextPrimary),
                Text.New($"  \u2192  {branchName}").FontSize(12f)
                    .TextColor(GitClientColors.BranchCurrent),

                // Spacer
                Box.New().FlexGrow(1),

                // Window controls
                WindowButton("\u2212", GitClientColors.SurfaceHover, () => window.Minimize()),
                WindowButton("\u25A1", GitClientColors.SurfaceHover, () => window.ToggleMaximize()),
                WindowButton("\u00D7", new Color(200, 60, 60), () => window.Close())
            );
    }

    private static Element WindowButton(string label, Color hoverColor, Action onClick)
    {
        var btn = Box.New()
            .Width(Dimension.Px(36)).Height(Dimension.Px(28))
            .CornerRadius(4f)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Children(
                Text.New(label).FontSize(14f).TextColor(GitClientColors.TextSecondary)
            );

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = hoverColor);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = Color.Transparent);
        btn.On<ClickEvent>(_ => onClick());

        return btn;
    }
}
