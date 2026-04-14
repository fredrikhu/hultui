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

public partial class FileTree : Component
{
    [Reactive] public partial List<GitFileChange> Files { get; set; }
    [Reactive] public partial GitFileChange? SelectedFile { get; set; }

    public Action<GitFileChange>? OnFileSelected { get; set; }
    public Action<GitFileChange>? OnBlameRequested { get; set; }

    public FileTree()
    {
        Files = [];
    }

    public override Element Build()
    {
        var files = Files;
        var selected = SelectedFile;

        if (files.Count == 0)
        {
            return Box.New().FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New("No files").FontSize(12f).TextColor(GitClientColors.TextMuted));
        }

        var container = Flex.Column(gap: 0);
        var children = new Element[files.Count];

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var isSelected = file == selected;

            var row = Flex.Row(gap: 6)
                .Padding(new Thickness(4, 8, 4, 8))
                .AlignItems(AlignItems.Center)
                .FlexShrink(0)
                .Background(isSelected ? GitClientColors.Selection : Color.Transparent)
                .Cursor(CursorType.Pointer);
            row.Key = $"file-{file.Path}";

            // Status badge
            row.AddChild(Badge.FileStatus(file.Kind));

            // File name (just the filename, not the full path)
            var fileName = Path.GetFileName(file.Path);
            var dirName = Path.GetDirectoryName(file.Path)?.Replace('\\', '/');

            row.AddChild(
                Text.New(fileName).FontSize(12f)
                    .TextColor(isSelected ? GitClientColors.TextPrimary : GitClientColors.TextSecondary)
                    .FlexShrink(1)
                    .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
            );

            if (!string.IsNullOrEmpty(dirName))
            {
                row.AddChild(
                    Text.New(dirName).FontSize(11f)
                        .TextColor(GitClientColors.TextMuted)
                        .FlexShrink(1)
                        .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
                );
            }

            // +/- counts
            if (file.Additions > 0 || file.Deletions > 0)
            {
                row.AddChild(Box.New().FlexGrow(1));
                if (file.Additions > 0)
                    row.AddChild(Text.New($"+{file.Additions}").FontSize(11f).TextColor(GitClientColors.DiffAddText));
                if (file.Deletions > 0)
                    row.AddChild(Text.New($"-{file.Deletions}").FontSize(11f).TextColor(GitClientColors.DiffDelText));
            }

            // Events
            var fileRef = file;
            row.OnPointerEnter(_ => { if (!isSelected) row.BackgroundColor = GitClientColors.SurfaceHover; });
            row.OnPointerLeave(_ => { if (!isSelected) row.BackgroundColor = Color.Transparent; });
            row.OnClick(_ =>
            {
                SelectedFile = fileRef;
                OnFileSelected?.Invoke(fileRef);
            });

            if (OnBlameRequested != null && fileRef.Kind != FileChangeKind.Deleted)
            {
                row.ContextMenu(() =>
                [
                    new ContextMenuItem { Label = "Blame this file", Action = () => OnBlameRequested?.Invoke(fileRef) },
                ]);
            }

            children[i] = row;
        }

        container.ReplaceChildren(children);
        return ScrollView.New().FlexGrow(1).Children(container);
    }
}
