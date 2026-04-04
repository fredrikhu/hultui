using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.Detail;

/// <summary>
/// Displays unified diff content using a VirtualList for large diffs.
/// </summary>
public partial class DiffViewer : Component
{
    [Reactive] public partial List<GitFileDiff> Diffs { get; set; }

    public DiffViewer()
    {
        Diffs = [];
    }

    public override Element Build()
    {
        var diffs = Diffs;

        if (diffs.Count == 0)
        {
            return Box.New().FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Background(GitClientColors.Background)
                .Children(Text.New("Select a commit to view diff").FontSize(13f).TextColor(GitClientColors.TextMuted));
        }

        // Flatten all diff lines into a single list for the virtual list
        var displayLines = new List<DiffDisplayLine>();
        foreach (var fileDiff in diffs)
        {
            // File header
            displayLines.Add(new DiffDisplayLine(DiffLineKind.FileHeader, fileDiff.Path, null, null));

            foreach (var hunk in fileDiff.Hunks)
            {
                foreach (var line in hunk.Lines)
                {
                    displayLines.Add(new DiffDisplayLine(line.Kind, line.Content, line.OldLineNumber, line.NewLineNumber));
                }
            }
        }

        return VirtualListElement<DiffDisplayLine>.New()
            .ItemHeight(22)
            .Items(displayLines)
            .RenderWith(RenderDiffLine)
            .FlexGrow(1)
            .Background(GitClientColors.Background);
    }

    private static Element RenderDiffLine(DiffDisplayLine line)
    {
        var (bgColor, textColor, prefix) = line.Kind switch
        {
            DiffLineKind.Addition => (GitClientColors.DiffAddBg, GitClientColors.DiffAddText, "+"),
            DiffLineKind.Deletion => (GitClientColors.DiffDelBg, GitClientColors.DiffDelText, "-"),
            DiffLineKind.HunkHeader => (GitClientColors.DiffHunkBg, GitClientColors.DiffHunkText, ""),
            DiffLineKind.FileHeader => (GitClientColors.Surface, GitClientColors.TextPrimary, ""),
            _ => (Color.Transparent, GitClientColors.TextSecondary, " "),
        };

        if (line.Kind == DiffLineKind.FileHeader)
        {
            return Flex.Row(gap: 8)
                .Padding(new Thickness(4, 12, 4, 12))
                .AlignItems(AlignItems.Center)
                .Background(bgColor)
                .Children(
                    Text.New("\u2501\u2501").FontSize(11f).TextColor(GitClientColors.TextMuted),
                    Text.New(line.Content).FontSize(13f).FontWeight(FontWeight.SemiBold).TextColor(textColor)
                );
        }

        var row = Flex.Row(gap: 0)
            .Padding(new Thickness(0, 4, 0, 0))
            .AlignItems(AlignItems.Center)
            .Background(bgColor);

        // Line numbers
        if (line.Kind != DiffLineKind.HunkHeader)
        {
            row.AddChild(
                Text.New(line.OldLine?.ToString() ?? "").FontSize(11f)
                    .TextColor(GitClientColors.DiffLineNumber)
                    .Width(45).Padding(new Thickness(0, 0, 0, 4))
            );
            row.AddChild(
                Text.New(line.NewLine?.ToString() ?? "").FontSize(11f)
                    .TextColor(GitClientColors.DiffLineNumber)
                    .Width(45)
            );
            row.AddChild(
                Text.New(prefix).FontSize(12f).TextColor(textColor).Width(14)
            );
        }
        else
        {
            row.AddChild(Box.New().Width(104));
        }

        // Content
        row.AddChild(
            Text.New(line.Content).FontSize(12f).TextColor(textColor)
                .FlexGrow(1)
                .OverflowMode(TextOverflow.Clip).MaxLines(1)
        );

        return row;
    }
}

internal record DiffDisplayLine(DiffLineKind Kind, string Content, int? OldLine, int? NewLine);
