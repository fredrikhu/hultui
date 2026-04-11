using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using GitClient.Components.Shared;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.History;

public static class CommitRow
{
    private const float RowHeight = 36f;
    private const float LaneWidth = 14f;
    private const float DotRadius = 4f;
    private const float EdgeStroke = 1.8f;

    private static readonly Color[] LanePalette =
    [
        Color.FromHex("#60a5fa"), // blue
        Color.FromHex("#a78bfa"), // violet
        Color.FromHex("#34d399"), // green
        Color.FromHex("#fbbf24"), // amber
        Color.FromHex("#f472b6"), // pink
        Color.FromHex("#f87171"), // red
        Color.FromHex("#38bdf8"), // sky
        Color.FromHex("#c084fc"), // purple
    ];

    public static Element Build(
        GitCommit commit,
        GitGraphRow graph,
        bool isSelected,
        Action<GitCommit> onSelect,
        Action<string>? copyToClipboard = null,
        Action<string>? onCheckout = null,
        Action<string>? onCherryPick = null,
        Action<string>? onTag = null)
    {
        var row = Flex.Row(gap: 8)
            .AlignItems(AlignItems.Stretch)
            .Background(isSelected ? GitClientColors.Selection : Color.Transparent)
            .Cursor(CursorType.Pointer);

        // Graph lanes — flush top-to-bottom so edges join across rows.
        int columns = Math.Max(graph.LaneCount, 1);
        float graphWidth = columns * LaneWidth + 4f;
        row.AddChild(
            Canvas.New(ctx => DrawGraph(ctx, graph))
                .Width(graphWidth).FlexShrink(0)
        );

        // All remaining cells sit inside a padded inner row so the graph
        // canvas can occupy the full row height without vertical padding.
        var content = Flex.Row(gap: 8)
            .Padding(new Thickness(6, 4, 6, 12))
            .AlignItems(AlignItems.Center)
            .FlexGrow(1);

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
            content.AddChild(badgeRow);
        }

        // Subject
        content.AddChild(
            Text.New(commit.Subject).FontSize(13f)
                .TextColor(GitClientColors.TextPrimary)
                .FlexGrow(1).FlexShrink(1)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
        );

        // Author
        content.AddChild(
            Text.New(commit.AuthorName).FontSize(12f)
                .TextColor(GitClientColors.TextSecondary)
                .Width(120).FlexShrink(0)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
        );

        // Date
        content.AddChild(
            Text.New(FormatRelativeDate(commit.AuthorDate)).FontSize(12f)
                .TextColor(GitClientColors.TextMuted)
                .Width(80).FlexShrink(0)
                .Tooltip(commit.AuthorDate.ToString("yyyy-MM-dd HH:mm:ss"))
        );

        // Short hash
        content.AddChild(
            Text.New(commit.ShortHash).FontSize(11f)
                .TextColor(GitClientColors.TextMuted)
                .Width(60).FlexShrink(0)
                .Tooltip(commit.Hash)
        );

        row.AddChild(content);

        // Hover
        row.OnPointerEnter(_ => { if (!isSelected) row.BackgroundColor = GitClientColors.SurfaceHover; });
        row.OnPointerLeave(_ => { if (!isSelected) row.BackgroundColor = Color.Transparent; });
        row.OnClick(_ => onSelect(commit));

        // Context menu
        row.ContextMenu(() =>
        [
            new ContextMenuItem { Label = $"Checkout '{commit.ShortHash}'", Action = () => onCheckout?.Invoke(commit.Hash) },
            new ContextMenuItem { Label = "Cherry-pick", Action = () => onCherryPick?.Invoke(commit.Hash) },
            new ContextMenuItem { Label = "Tag this commit\u2026", Action = () => onTag?.Invoke(commit.Hash) },
            ContextMenuItem.Separator(),
            new ContextMenuItem { Label = "Copy SHA", Action = () => copyToClipboard?.Invoke(commit.Hash) },
            new ContextMenuItem { Label = "Copy commit message", Action = () => copyToClipboard?.Invoke(commit.Subject) },
        ]);

        return row;
    }

    private static void DrawGraph(ICanvasDrawingContext ctx, GitGraphRow row)
    {
        float h = ctx.Bounds.Height;
        float centerY = h / 2f;

        var topEdges = row.EdgesFromPrev;
        if (topEdges != null)
        {
            foreach (var e in topEdges)
            {
                var top = new Point(LaneX(e.FromLane), 0);
                var mid = new Point(LaneX(e.ToLane), centerY);
                ctx.DrawLine(top, mid, LaneColor(e.ColorIndex), EdgeStroke);
            }
        }

        var bottomEdges = row.EdgesToNext;
        if (bottomEdges != null)
        {
            foreach (var e in bottomEdges)
            {
                var mid = new Point(LaneX(e.FromLane), centerY);
                var bot = new Point(LaneX(e.ToLane), h);
                ctx.DrawLine(mid, bot, LaneColor(e.ColorIndex), EdgeStroke);
            }
        }

        // The commit dot itself.
        var dotColor = row.IsMerge
            ? GitClientColors.BranchRemote
            : LaneColor(ColorIndexForLane(row));
        var center = new Point(LaneX(row.Lane), centerY);
        ctx.DrawCircle(center, DotRadius, dotColor);
    }

    private static int ColorIndexForLane(GitGraphRow row)
    {
        if (row.EdgesToNext != null)
            foreach (var e in row.EdgesToNext)
                if (e.FromLane == row.Lane) return e.ColorIndex;
        if (row.EdgesFromPrev != null)
            foreach (var e in row.EdgesFromPrev)
                if (e.ToLane == row.Lane) return e.ColorIndex;
        return row.Lane;
    }

    private static float LaneX(int lane) => lane * LaneWidth + LaneWidth / 2f;

    private static Color LaneColor(int colorIndex) =>
        LanePalette[((colorIndex % LanePalette.Length) + LanePalette.Length) % LanePalette.Length];

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
