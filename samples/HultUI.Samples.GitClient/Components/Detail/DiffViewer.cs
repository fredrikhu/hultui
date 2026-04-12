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
public enum DiffViewMode { Unified, SideBySide }

/// <summary>
/// Displays unified or side-by-side diff content using a VirtualList for large diffs.
/// When in working-copy mode, hunk headers include stage/unstage/discard buttons.
/// </summary>
public partial class DiffViewer : Component
{
    [Reactive] public partial List<GitFileDiff> Diffs { get; set; }
    [Reactive] public partial DiffViewMode ViewMode { get; set; }

    public Action<GitFileDiff, GitDiffHunk, HunkActionKind>? OnHunkAction { get; set; }
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

        var mode = ViewMode;
        var displayLines = mode == DiffViewMode.Unified
            ? FlattenUnified(diffs)
            : FlattenSideBySide(diffs);

        Func<DiffDisplayLine, Element> renderer = mode == DiffViewMode.Unified
            ? RenderUnifiedLine
            : RenderSideBySideLine;

        return Flex.Column(gap: 0).FlexGrow(1).Children(
            BuildToolbar(mode),
            VirtualListElement<DiffDisplayLine>.New()
                .ItemHeight(22)
                .Items(displayLines)
                .RenderWith(renderer)
                .FlexGrow(1)
                .Background(GitClientColors.Background)
        );
    }

    // ── Toolbar ──

    private Element BuildToolbar(DiffViewMode mode)
    {
        return Flex.Row(gap: 2)
            .Padding(new Thickness(4, 8, 4, 8))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .FlexShrink(0)
            .Children(
                Box.New().FlexGrow(1),
                ModeButton("Unified", mode == DiffViewMode.Unified,
                    () => ViewMode = DiffViewMode.Unified),
                ModeButton("Split", mode == DiffViewMode.SideBySide,
                    () => ViewMode = DiffViewMode.SideBySide)
            );
    }

    private static Element ModeButton(string label, bool active, Action onClick)
    {
        var bg = active ? GitClientColors.ButtonPrimary : GitClientColors.ButtonSecondary;
        var fg = active ? Color.White : GitClientColors.TextSecondary;

        var btn = Box.New()
            .Padding(new Thickness(3, 10, 3, 10))
            .CornerRadius(3f)
            .Background(bg)
            .Cursor(CursorType.Pointer)
            .Children(Text.New(label).FontSize(10f).FontWeight(FontWeight.SemiBold).TextColor(fg));

        if (!active)
        {
            btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondaryHover);
            btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondary);
        }
        btn.On<ClickEvent>(_ => onClick());

        return btn;
    }

    // ── Unified mode ──

    private static List<DiffDisplayLine> FlattenUnified(List<GitFileDiff> diffs)
    {
        var lines = new List<DiffDisplayLine>();
        foreach (var fileDiff in diffs)
        {
            lines.Add(new DiffDisplayLine(DiffLineKind.FileHeader, fileDiff.Path, null, null));

            foreach (var hunk in fileDiff.Hunks)
            {
                lines.Add(new DiffDisplayLine(DiffLineKind.HunkHeader, hunk.Header, null, null,
                    FileDiff: fileDiff, Hunk: hunk));

                foreach (var line in hunk.Lines)
                {
                    if (line.Kind == DiffLineKind.HunkHeader) continue;
                    lines.Add(new DiffDisplayLine(line.Kind, line.Content, line.OldLineNumber, line.NewLineNumber));
                }
            }
        }
        return lines;
    }

    private Element RenderUnifiedLine(DiffDisplayLine line)
    {
        var (bgColor, textColor, prefix) = LineColors(line.Kind);

        if (line.Kind == DiffLineKind.FileHeader)
            return RenderFileHeader(line.Content);

        if (line.Kind == DiffLineKind.HunkHeader)
            return RenderHunkHeader(line, bgColor, textColor);

        var row = Flex.Row(gap: 0)
            .Padding(new Thickness(0, 4, 0, 0))
            .AlignItems(AlignItems.Center)
            .Background(bgColor);

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
        row.AddChild(Text.New(prefix).FontSize(12f).TextColor(textColor).Width(14));
        row.AddChild(
            Text.New(line.Content).FontSize(12f).TextColor(textColor)
                .FlexGrow(1).OverflowMode(TextOverflow.Clip).MaxLines(1)
        );

        return row;
    }

    // ── Side-by-side mode ──

    private static List<DiffDisplayLine> FlattenSideBySide(List<GitFileDiff> diffs)
    {
        var lines = new List<DiffDisplayLine>();
        foreach (var fileDiff in diffs)
        {
            lines.Add(new DiffDisplayLine(DiffLineKind.FileHeader, fileDiff.Path, null, null));

            foreach (var hunk in fileDiff.Hunks)
            {
                lines.Add(new DiffDisplayLine(DiffLineKind.HunkHeader, hunk.Header, null, null,
                    FileDiff: fileDiff, Hunk: hunk));

                var contentLines = hunk.Lines.Where(l => l.Kind != DiffLineKind.HunkHeader).ToList();
                int i = 0;
                while (i < contentLines.Count)
                {
                    var cur = contentLines[i];
                    if (cur.Kind == DiffLineKind.Context)
                    {
                        lines.Add(new DiffDisplayLine(
                            DiffLineKind.Context, cur.Content, cur.OldLineNumber, null,
                            RightKind: DiffLineKind.Context, RightContent: cur.Content,
                            RightLine: cur.NewLineNumber));
                        i++;
                        continue;
                    }

                    // Collect a contiguous block of deletions then additions
                    var dels = new List<GitDiffLine>();
                    var adds = new List<GitDiffLine>();
                    while (i < contentLines.Count && contentLines[i].Kind == DiffLineKind.Deletion)
                        dels.Add(contentLines[i++]);
                    while (i < contentLines.Count && contentLines[i].Kind == DiffLineKind.Addition)
                        adds.Add(contentLines[i++]);

                    // Zip them into paired rows
                    int maxLen = Math.Max(dels.Count, adds.Count);
                    for (int j = 0; j < maxLen; j++)
                    {
                        var del = j < dels.Count ? dels[j] : null;
                        var add = j < adds.Count ? adds[j] : null;

                        lines.Add(new DiffDisplayLine(
                            del?.Kind ?? DiffLineKind.Context,
                            del?.Content ?? "",
                            del?.OldLineNumber,
                            null,
                            RightKind: add?.Kind ?? DiffLineKind.Context,
                            RightContent: add?.Content ?? "",
                            RightLine: add?.NewLineNumber));
                    }
                }
            }
        }
        return lines;
    }

    private Element RenderSideBySideLine(DiffDisplayLine line)
    {
        if (line.Kind == DiffLineKind.FileHeader)
            return RenderFileHeader(line.Content);

        if (line.Kind == DiffLineKind.HunkHeader)
            return RenderHunkHeader(line, GitClientColors.DiffHunkBg, GitClientColors.DiffHunkText);

        var row = Flex.Row(gap: 0).AlignItems(AlignItems.Stretch);

        row.AddChild(RenderSideColumn(line.Kind, line.Content, line.OldLine));
        row.AddChild(Box.New().Width(1).Background(GitClientColors.Border).FlexShrink(0));
        row.AddChild(RenderSideColumn(line.RightKind, line.RightContent ?? "", line.RightLine));

        return row;
    }

    private static Element RenderSideColumn(DiffLineKind kind, string content, int? lineNo)
    {
        bool isPadding = lineNo == null && string.IsNullOrEmpty(content);

        var (bgColor, textColor, prefix) = kind switch
        {
            DiffLineKind.Addition => (GitClientColors.DiffAddBg, GitClientColors.DiffAddText, "+"),
            DiffLineKind.Deletion => (GitClientColors.DiffDelBg, GitClientColors.DiffDelText, "-"),
            _ => (Color.Transparent, GitClientColors.TextSecondary, " "),
        };

        if (isPadding)
            bgColor = GitClientColors.DiffEmptyBg;

        var col = Flex.Row(gap: 0)
            .FlexGrow(1).FlexBasis(0)
            .Padding(new Thickness(0, 4, 0, 0))
            .AlignItems(AlignItems.Center)
            .Background(bgColor);

        col.AddChild(
            Text.New(lineNo?.ToString() ?? "").FontSize(11f)
                .TextColor(GitClientColors.DiffLineNumber).Width(40)
        );
        col.AddChild(
            Text.New(isPadding ? "" : prefix).FontSize(12f).TextColor(textColor).Width(14)
        );
        col.AddChild(
            Text.New(content).FontSize(12f).TextColor(textColor)
                .FlexGrow(1).OverflowMode(TextOverflow.Clip).MaxLines(1)
        );

        return col;
    }

    // ── Shared rendering ──

    private static Element RenderFileHeader(string path)
    {
        return Flex.Row(gap: 8)
            .Padding(new Thickness(4, 12, 4, 12))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .Children(
                Text.New("\u2501\u2501").FontSize(11f).TextColor(GitClientColors.TextMuted),
                Text.New(path).FontSize(13f).FontWeight(FontWeight.SemiBold)
                    .TextColor(GitClientColors.TextPrimary)
            );
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

    private static (Color bg, Color text, string prefix) LineColors(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Addition => (GitClientColors.DiffAddBg, GitClientColors.DiffAddText, "+"),
        DiffLineKind.Deletion => (GitClientColors.DiffDelBg, GitClientColors.DiffDelText, "-"),
        DiffLineKind.HunkHeader => (GitClientColors.DiffHunkBg, GitClientColors.DiffHunkText, ""),
        DiffLineKind.FileHeader => (GitClientColors.Surface, GitClientColors.TextPrimary, ""),
        _ => (Color.Transparent, GitClientColors.TextSecondary, " "),
    };
}

internal record DiffDisplayLine(
    DiffLineKind Kind, string Content, int? OldLine, int? NewLine,
    GitFileDiff? FileDiff = null, GitDiffHunk? Hunk = null,
    // Side-by-side: right column data (left uses Kind/Content/OldLine)
    DiffLineKind RightKind = default, string? RightContent = null, int? RightLine = null);
