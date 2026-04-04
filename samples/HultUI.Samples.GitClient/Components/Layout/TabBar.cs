using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Layout;

public static class TabBar
{
    public static Element Build(string activeRepo, Action<string>? onTabSelected = null)
    {
        var repoName = System.IO.Path.GetFileName(activeRepo);

        return Flex.Row(gap: 0)
            .AlignItems(AlignItems.Stretch)
            .Height(Dimension.Px(34))
            .Background(GitClientColors.Panel)
            .FlexShrink(0)
            .Children(
                // Active tab
                Flex.Row(gap: 6)
                    .Padding(new Thickness(0, 14, 0, 14))
                    .AlignItems(AlignItems.Center)
                    .Background(GitClientColors.Background)
                    .Children(
                        Text.New("\u2442").FontSize(12f).TextColor(GitClientColors.BranchLocal),
                        Text.New(repoName).FontSize(12f).FontWeight(FontWeight.SemiBold)
                            .TextColor(GitClientColors.TextPrimary)
                    ),

                // Rest of the tab bar (empty space for future tabs)
                Box.New().FlexGrow(1).Background(GitClientColors.Panel)
            );
    }
}
