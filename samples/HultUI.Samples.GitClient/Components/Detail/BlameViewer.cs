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

/// <summary>
/// Displays blame output for a file — each line annotated with the commit that last touched it.
/// Consecutive lines from the same commit are grouped visually: only the first line in a run
/// shows the author/date gutter, so long runs of unchanged lines read as a single block.
/// Clicking the gutter jumps to that commit in the history view.
/// </summary>
public partial class BlameViewer : Component
{
    [Reactive] public partial List<GitBlameLine> Lines { get; set; }
    [Reactive] public partial string? FilePath { get; set; }

    public Action<string>? OnCommitSelected { get; set; }
    public Action? OnClose { get; set; }

    public BlameViewer()
    {
        Lines = [];
    }

    public override Element Build()
    {
        var lines = Lines;

        if (lines.Count == 0)
        {
            return Box.New().FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Background(GitClientColors.Background)
                .Children(Text.New("No blame data").FontSize(13f).TextColor(GitClientColors.TextMuted));
        }

        // Precompute which lines start a new commit-group — only these show gutter metadata.
        var startsGroup = new bool[lines.Count];
        startsGroup[0] = true;
        for (int i = 1; i < lines.Count; i++)
            startsGroup[i] = lines[i].CommitHash != lines[i - 1].CommitHash;

        var displayItems = new List<BlameDisplayLine>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
            displayItems.Add(new BlameDisplayLine(lines[i], startsGroup[i]));

        return Flex.Column(gap: 0).FlexGrow(1).Children(
            BuildToolbar(),
            VirtualListElement<BlameDisplayLine>.New()
                .ItemHeight(22)
                .Items(displayItems)
                .RenderWith(RenderLine)
                .FlexGrow(1)
                .Background(GitClientColors.Background)
        );
    }

    private Element BuildToolbar()
    {
        var row = Flex.Row(gap: 8)
            .Padding(new Thickness(4, 12, 4, 8))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .FlexShrink(0);

        row.AddChild(
            Text.New("Blame").FontSize(12f).FontWeight(FontWeight.SemiBold)
                .TextColor(GitClientColors.TextPrimary)
        );
        if (!string.IsNullOrEmpty(FilePath))
        {
            row.AddChild(
                Text.New(FilePath).FontSize(11f).TextColor(GitClientColors.TextMuted)
                    .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
            );
        }
        row.AddChild(Box.New().FlexGrow(1));

        if (OnClose != null)
        {
            var closeBtn = Box.New()
                .Padding(new Thickness(3, 10, 3, 10))
                .CornerRadius(3f)
                .Background(GitClientColors.ButtonSecondary)
                .Cursor(CursorType.Pointer)
                .Children(
                    Text.New("Back to Diff").FontSize(10f).FontWeight(FontWeight.SemiBold)
                        .TextColor(GitClientColors.TextSecondary)
                );
            closeBtn.On<PointerEnterEvent>(_ => closeBtn.BackgroundColor = GitClientColors.ButtonSecondaryHover);
            closeBtn.On<PointerLeaveEvent>(_ => closeBtn.BackgroundColor = GitClientColors.ButtonSecondary);
            closeBtn.On<ClickEvent>(_ => OnClose?.Invoke());
            row.AddChild(closeBtn);
        }

        return row;
    }

    private Element RenderLine(BlameDisplayLine item)
    {
        var line = item.Line;

        var row = Flex.Row(gap: 0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 4, 0, 0));

        // Gutter: short hash, author, date. Clickable — navigates to commit.
        var gutter = Flex.Row(gap: 8)
            .Width(Dimension.Px(260))
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 8, 0, 6))
            .Cursor(CursorType.Pointer)
            .Background(item.StartsGroup ? GitClientColors.Surface : Color.Transparent);

        if (item.StartsGroup)
        {
            gutter.AddChild(
                Text.New(line.ShortHash).FontSize(11f)
                    .TextColor(GitClientColors.DiffHunkText)
                    .Width(60).FlexShrink(0)
                    .Tooltip(line.CommitHash)
            );
            gutter.AddChild(
                Text.New(line.AuthorName).FontSize(11f)
                    .TextColor(GitClientColors.TextSecondary)
                    .FlexGrow(1).FlexShrink(1)
                    .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
            );
            gutter.AddChild(
                Text.New(FormatDate(line.AuthorDate)).FontSize(11f)
                    .TextColor(GitClientColors.TextMuted)
                    .FlexShrink(0)
                    .Tooltip(line.AuthorDate.ToString("yyyy-MM-dd HH:mm") + "\n" + line.Summary)
            );
        }
        else
        {
            // Spacer row inside the same commit-group
            gutter.AddChild(Box.New().FlexGrow(1));
        }

        var hash = line.CommitHash;
        gutter.OnPointerEnter(_ => gutter.BackgroundColor = GitClientColors.SurfaceHover);
        gutter.OnPointerLeave(_ => gutter.BackgroundColor =
            item.StartsGroup ? GitClientColors.Surface : Color.Transparent);
        gutter.OnClick(_ => OnCommitSelected?.Invoke(hash));

        row.AddChild(gutter);
        row.AddChild(Box.New().Width(1).Background(GitClientColors.Border).FlexShrink(0));

        // Line number
        row.AddChild(
            Text.New(line.LineNumber.ToString()).FontSize(11f)
                .TextColor(GitClientColors.DiffLineNumber)
                .Width(50).FlexShrink(0)
                .Padding(new Thickness(0, 0, 0, 8))
        );

        // Line content
        row.AddChild(
            Text.New(line.Content).FontSize(12f)
                .TextColor(GitClientColors.TextPrimary)
                .FlexGrow(1).OverflowMode(TextOverflow.Clip).MaxLines(1)
        );

        return row;
    }

    private static string FormatDate(DateTimeOffset date)
    {
        if (date == DateTimeOffset.MinValue) return "";
        var span = DateTimeOffset.Now - date;
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";
        return $"{(int)(span.TotalDays / 365)}y ago";
    }
}

internal record BlameDisplayLine(GitBlameLine Line, bool StartsGroup);
