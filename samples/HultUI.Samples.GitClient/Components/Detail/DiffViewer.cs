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

namespace GitClient.Components.Detail;

public enum HunkActionKind { Stage, Unstage, Discard }

/// <summary>
/// Displays unified diff content using a VirtualList for large diffs.
/// When in working-copy mode, hunk headers include stage/unstage/discard buttons.
/// </summary>
public partial class DiffViewer : Component
{
    [Reactive] public partial List<GitFileDiff> Diffs { get; set; }

    /// <summary>
    /// When set, the diff viewer is in working-copy mode and hunk headers
    /// show action buttons. The callback receives the file, hunk, and action.
    /// </summary>
    public Action<GitFileDiff, GitDiffHunk, HunkActionKind>? OnHunkAction { get; set; }

    /// <summary>
    /// Whether the currently displayed diff is from the staged (cached) side.
    /// Controls which buttons appear: staged → Unstage; unstaged → Stage + Discard.
    /// </summary>
    public bool IsStaged { get; set; }

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
                // For hunk headers, attach file+hunk references so the renderer can build action buttons
                displayLines.Add(new DiffDisplayLine(DiffLineKind.HunkHeader, hunk.Header, null, null, fileDiff, hunk));

                foreach (var line in hunk.Lines)
                {
                    if (line.Kind == DiffLineKind.HunkHeader) continue; // already emitted above
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

    private Element RenderDiffLine(DiffDisplayLine line)
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

        if (line.Kind == DiffLineKind.HunkHeader)
            return RenderHunkHeader(line, bgColor, textColor);

        var row = Flex.Row(gap: 0)
            .Padding(new Thickness(0, 4, 0, 0))
            .AlignItems(AlignItems.Center)
            .Background(bgColor);

        // Line numbers
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

        // Content
        row.AddChild(
            Text.New(line.Content).FontSize(12f).TextColor(textColor)
                .FlexGrow(1)
                .OverflowMode(TextOverflow.Clip).MaxLines(1)
        );

        return row;
    }

    private Element RenderHunkHeader(DiffDisplayLine line, Color bgColor, Color textColor)
    {
        var row = Flex.Row(gap: 0)
            .Padding(new Thickness(0, 4, 0, 0))
            .AlignItems(AlignItems.Center)
            .Background(bgColor);

        row.AddChild(Box.New().Width(104));
        row.AddChild(
            Text.New(line.Content).FontSize(12f).TextColor(textColor).FlexGrow(1)
                .OverflowMode(TextOverflow.Clip).MaxLines(1)
        );

        // Hunk action buttons (only in working-copy mode)
        if (OnHunkAction != null && line.FileDiff != null && line.Hunk != null)
        {
            var fileDiff = line.FileDiff;
            var hunk = line.Hunk;

            if (IsStaged)
            {
                row.AddChild(HunkButton("Unstage", GitClientColors.StatusModified, () =>
                    OnHunkAction(fileDiff, hunk, HunkActionKind.Unstage)));
            }
            else
            {
                row.AddChild(HunkButton("Discard", GitClientColors.StatusDeleted, () =>
                    OnHunkAction(fileDiff, hunk, HunkActionKind.Discard)));
                row.AddChild(HunkButton("Stage", GitClientColors.StatusAdded, () =>
                    OnHunkAction(fileDiff, hunk, HunkActionKind.Stage)));
            }

            row.AddChild(Box.New().Width(4));
        }

        return row;
    }

    private static Element HunkButton(string label, Color textColor, Action onClick)
    {
        var btn = Box.New()
            .Padding(new Thickness(2, 6, 2, 6))
            .Margin(new Thickness(0, 0, 0, 2))
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonSecondary)
            .Cursor(CursorType.Pointer)
            .Children(Text.New(label).FontSize(10f).FontWeight(FontWeight.SemiBold).TextColor(textColor));

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondaryHover);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondary);
        btn.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            onClick();
        });

        return btn;
    }
}

internal record DiffDisplayLine(
    DiffLineKind Kind, string Content, int? OldLine, int? NewLine,
    GitFileDiff? FileDiff = null, GitDiffHunk? Hunk = null);
