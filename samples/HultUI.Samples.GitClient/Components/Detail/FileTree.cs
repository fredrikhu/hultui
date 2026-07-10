using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Components.Shared;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.Detail;

/// <summary>
/// Shows the files changed in a commit as a folder tree. Folders carry aggregate +/- counts
/// so you can see at a glance where in the codebase a commit landed.
/// </summary>
public partial class FileTree : Component
{
    [Reactive] public partial List<GitFileChange> Files { get; set; }
    [Reactive] public partial GitFileChange? SelectedFile { get; set; }
    [Reactive] public partial int Version { get; set; }

    public Action<GitFileChange>? OnFileSelected { get; set; }
    public Action<GitFileChange>? OnBlameRequested { get; set; }
    public Action<GitFileChange>? OnHistoryRequested { get; set; }

    // Expansion state keyed by folder FullPath. Default (missing) is expanded; we only
    // store explicit user toggles. Survives rebuilds so the tree doesn't re-expand every
    // time Files changes.
    private readonly Dictionary<string, bool> _expanded = new();

    private const int IndentPx = 14;
    private const int ChevronWidth = 14;

    public FileTree()
    {
        Files = [];
    }

    public override Element Build()
    {
        var files = Files;
        var selectedPath = SelectedFile?.Path;
        _ = Version;

        if (files.Count == 0)
        {
            return Box.New().FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New("No files").FontSize(12f).TextColor(GitClientColors.TextMuted));
        }

        var tree = PathTree.Build(files);
        var container = Flex.Column(gap: 0);
        var rows = new List<Element>();
        foreach (var node in tree) AppendNode(node, depth: 0, selectedPath, rows);
        container.ReplaceChildren(rows.ToArray());
        return ScrollView.New().FlexGrow(1).Children(container);
    }

    private bool IsExpanded(string fullPath) =>
        !_expanded.TryGetValue(fullPath, out var v) || v;

    private void AppendNode(PathTreeNode node, int depth, string? selectedPath, List<Element> rows)
    {
        if (node.IsFile)
        {
            rows.Add(BuildFileRow(node, depth, selectedPath));
            return;
        }

        rows.Add(BuildFolderRow(node, depth));
        if (IsExpanded(node.FullPath))
            foreach (var child in node.Children) AppendNode(child, depth + 1, selectedPath, rows);
    }

    private Element BuildFolderRow(PathTreeNode node, int depth)
    {
        var expanded = IsExpanded(node.FullPath);
        var leftPad = 8 + depth * IndentPx;

        var row = Flex.Row(gap: 4)
            .Padding(new Thickness(4, 8, 4, leftPad))
            .AlignItems(AlignItems.Center)
            .FlexShrink(0)
            .AccessibleRole(AccessibilityRole.TreeItem)
            .AccessibleName(node.Segment)
            .AccessibleExpanded(expanded)
            .Cursor(CursorType.Pointer);
        row.Key = $"file-tree-dir-{node.FullPath}";

        row.AddChild(Text.New(expanded ? "\u25BE" : "\u25B8")
            .FontSize(11f).TextColor(GitClientColors.TextMuted).Width(ChevronWidth));

        row.AddChild(Text.New(node.Segment).FontSize(12f)
            .TextColor(GitClientColors.TextSecondary)
            .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));

        row.AddChild(Text.New($"({node.FileCount})").FontSize(11f).TextColor(GitClientColors.TextMuted));

        if (node.Additions > 0 || node.Deletions > 0)
        {
            row.AddChild(Box.New().FlexGrow(1));
            if (node.Additions > 0)
                row.AddChild(Text.New($"+{node.Additions}").FontSize(11f).TextColor(GitClientColors.DiffAddText));
            if (node.Deletions > 0)
                row.AddChild(Text.New($"-{node.Deletions}").FontSize(11f).TextColor(GitClientColors.DiffDelText));
        }

        var fullPath = node.FullPath;
        row.OnPointerEnter(_ => row.BackgroundColor = GitClientColors.SurfaceHover);
        row.OnPointerLeave(_ => row.BackgroundColor = Color.Transparent);
        row.OnClick(_ =>
        {
            _expanded[fullPath] = !IsExpanded(fullPath);
            Version++;
        });

        return row;
    }

    private Element BuildFileRow(PathTreeNode node, int depth, string? selectedPath)
    {
        var file = node.File!;
        var isSelected = file.Path == selectedPath;
        // File rows reserve the chevron column so badges line up with folder labels.
        var leftPad = 8 + depth * IndentPx + ChevronWidth;

        var row = Flex.Row(gap: 6)
            .Padding(new Thickness(4, 8, 4, leftPad))
            .AlignItems(AlignItems.Center)
            .FlexShrink(0)
            .Background(isSelected ? GitClientColors.Selection : Color.Transparent)
            .Cursor(CursorType.Pointer);
        row.Key = $"file-tree-file-{file.Path}";

        row.AddChild(Badge.FileStatus(file.Kind));

        row.AddChild(
            Text.New(node.Segment).FontSize(12f)
                .TextColor(isSelected ? GitClientColors.TextPrimary : GitClientColors.TextSecondary)
                .FlexShrink(1)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
                .Tooltip(file.Path)
        );

        if (file.Additions > 0 || file.Deletions > 0)
        {
            row.AddChild(Box.New().FlexGrow(1));
            if (file.Additions > 0)
                row.AddChild(Text.New($"+{file.Additions}").FontSize(11f).TextColor(GitClientColors.DiffAddText));
            if (file.Deletions > 0)
                row.AddChild(Text.New($"-{file.Deletions}").FontSize(11f).TextColor(GitClientColors.DiffDelText));
        }

        var fileRef = file;
        row.OnPointerEnter(_ =>
        {
            if (SelectedFile?.Path != fileRef.Path)
                row.BackgroundColor = GitClientColors.SurfaceHover;
        });
        row.OnPointerLeave(_ =>
        {
            row.BackgroundColor = SelectedFile?.Path == fileRef.Path
                ? GitClientColors.Selection
                : Color.Transparent;
        });
        row.OnClick(_ => SelectFile(fileRef));

        var hasContextMenu = OnBlameRequested != null || OnHistoryRequested != null;
        if (hasContextMenu)
        {
            // Context menu actions implicitly select the file first — users opening
            // a right-click menu on a row expect it to be the target, even if they
            // hadn't left-clicked it yet.
            row.ContextMenu(() =>
            {
                var items = new List<ContextMenuItem>();
                if (OnHistoryRequested != null)
                    items.Add(new ContextMenuItem
                    {
                        Label = "Show history of this file",
                        Action = () => { SelectFile(fileRef); OnHistoryRequested?.Invoke(fileRef); },
                    });
                if (OnBlameRequested != null && fileRef.Kind != FileChangeKind.Deleted)
                    items.Add(new ContextMenuItem
                    {
                        Label = "Blame this file",
                        Action = () => { SelectFile(fileRef); OnBlameRequested?.Invoke(fileRef); },
                    });
                return items;
            });
        }

        return row;
    }

    private void SelectFile(GitFileChange file)
    {
        SelectedFile = file;
        OnFileSelected?.Invoke(file);
    }
}
