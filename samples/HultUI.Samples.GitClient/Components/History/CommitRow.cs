using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Components.Shared;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.History;

public static class CommitRow
{
    public static Element Build(
        GitCommit commit,
        bool isSelected,
        Action<GitCommit> onSelect,
        Action<string>? copyToClipboard = null,
        Action<string>? onCheckout = null,
        Action<string>? onCherryPick = null)
    {
        var row = Flex.Row(gap: 8)
            .Padding(new Thickness(6, 12, 6, 12))
            .AlignItems(AlignItems.Center)
            .Background(isSelected ? GitClientColors.Selection : Color.Transparent)
            .Cursor(CursorType.Pointer);

        // Graph dot
        var isMerge = commit.ParentHashes.Length > 1;
        var dotColor = isMerge ? GitClientColors.BranchRemote : GitClientColors.BranchLocal;
        row.AddChild(
            Box.New().Width(20).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New(isMerge ? "\u25C6" : "\u25CF").FontSize(10f).TextColor(dotColor))
        );

        // Ref badges
        if (commit.Refs.Length > 0)
        {
            var badgeRow = Flex.Row(gap: 4).AlignItems(AlignItems.Center).FlexShrink(0);
            foreach (var r in commit.Refs.Take(3))
            {
                var refName = r.Replace("HEAD -> ", "").Trim();
                var color = r.Contains("HEAD") ? GitClientColors.BranchCurrent
                    : r.Contains("tag:") ? GitClientColors.TagColor
                    : r.Contains("origin/") ? GitClientColors.BranchRemote
                    : GitClientColors.BranchLocal;
                badgeRow.AddChild(Badge.Ref(refName, color));
            }
            row.AddChild(badgeRow);
        }

        // Subject
        row.AddChild(
            Text.New(commit.Subject).FontSize(13f)
                .TextColor(GitClientColors.TextPrimary)
                .FlexGrow(1).FlexShrink(1)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
        );

        // Author
        row.AddChild(
            Text.New(commit.AuthorName).FontSize(12f)
                .TextColor(GitClientColors.TextSecondary)
                .Width(120).FlexShrink(0)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
        );

        // Date
        row.AddChild(
            Text.New(FormatRelativeDate(commit.AuthorDate)).FontSize(12f)
                .TextColor(GitClientColors.TextMuted)
                .Width(80).FlexShrink(0)
                .Tooltip(commit.AuthorDate.ToString("yyyy-MM-dd HH:mm:ss"))
        );

        // Short hash
        row.AddChild(
            Text.New(commit.ShortHash).FontSize(11f)
                .TextColor(GitClientColors.TextMuted)
                .Width(60).FlexShrink(0)
                .Tooltip(commit.Hash)
        );

        // Hover
        row.OnPointerEnter(_ => { if (!isSelected) row.BackgroundColor = GitClientColors.SurfaceHover; });
        row.OnPointerLeave(_ => { if (!isSelected) row.BackgroundColor = Color.Transparent; });
        row.OnClick(_ => onSelect(commit));

        // Context menu
        row.ContextMenu(() =>
        [
            new ContextMenuItem { Label = $"Checkout '{commit.ShortHash}'", Action = () => onCheckout?.Invoke(commit.Hash) },
            new ContextMenuItem { Label = "Cherry-pick", Action = () => onCherryPick?.Invoke(commit.Hash) },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Copy SHA", Action = () => copyToClipboard?.Invoke(commit.Hash) },
            new ContextMenuItem { Label = "Copy commit message", Action = () => copyToClipboard?.Invoke(commit.Subject) },
        ]);

        return row;
    }

    private static string FormatRelativeDate(DateTimeOffset date)
    {
        var span = DateTimeOffset.Now - date;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";
        return $"{(int)(span.TotalDays / 365)}y ago";
    }
}
