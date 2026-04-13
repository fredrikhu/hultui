using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.Shared;

public static class Badge
{
    public static Element FileStatus(FileChangeKind kind)
    {
        var (letter, color) = kind switch
        {
            FileChangeKind.Added => ("A", GitClientColors.StatusAdded),
            FileChangeKind.Modified => ("M", GitClientColors.StatusModified),
            FileChangeKind.Deleted => ("D", GitClientColors.StatusDeleted),
            FileChangeKind.Renamed => ("R", GitClientColors.StatusRenamed),
            FileChangeKind.Copied => ("C", GitClientColors.StatusRenamed),
            FileChangeKind.Untracked => ("?", GitClientColors.StatusUntracked),
            FileChangeKind.Conflicted => ("!", GitClientColors.StatusConflicted),
            _ => ("?", GitClientColors.TextMuted),
        };

        return Box.New()
            .Width(20).Height(18)
            .FlexShrink(0)
            .CornerRadius(3f)
            .Background(new Color(color.R, color.G, color.B, 30))
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Children(
                Text.New(letter).FontSize(11f).FontWeight(FontWeight.Bold).TextColor(color)
            );
    }

    public static Element Ref(string name, Color color)
    {
        return Box.New()
            .Padding(new Thickness(2, 6, 2, 6))
            .CornerRadius(3f)
            .Background(new Color(color.R, color.G, color.B, 30))
            .BorderColor(new Color(color.R, color.G, color.B, 60))
            .Children(
                Text.New(name).FontSize(10f).FontWeight(FontWeight.SemiBold).TextColor(color)
            );
    }
}
