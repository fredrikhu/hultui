using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Components.Shared;

public class TreeNode
{
    public string Label { get; init; } = "";
    public string? Icon { get; init; }
    public Color? IconColor { get; init; }
    public List<TreeNode> Children { get; init; } = [];
    public bool IsExpanded { get; set; } = true;
    public Action? OnClick { get; init; }
    public Action? OnDoubleClick { get; init; }
    public Func<List<ContextMenuItem>>? ContextMenuItems { get; init; }
    public bool IsSelected { get; set; }
    public string? Tooltip { get; init; }
    public object? Tag { get; init; }
}

public partial class TreeView : Component
{
    [Reactive] public partial ReactiveList<TreeNode> Roots { get; set; }
    [Reactive] public partial TreeNode? SelectedNode { get; set; }
    [Reactive] public partial int Version { get; set; }

    public Action<TreeNode>? OnSelectionChanged { get; set; }

    public TreeView()
    {
        Roots = new ReactiveList<TreeNode>();
    }

    public override Element Build()
    {
        _ = Roots.Count; // track changes
        _ = Version; // track expand/collapse
        var container = Flex.Column(gap: 0);
        var children = new List<Element>();

        foreach (var root in Roots)
            BuildNodeElements(root, 0, children);

        container.ReplaceChildren(children.ToArray());
        return container;
    }

    private void BuildNodeElements(TreeNode node, int depth, List<Element> elements)
    {
        var isSelected = SelectedNode == node;
        var row = Flex.Row(gap: 4)
            .Padding(new Thickness(4, 8, 4, 4 + depth * 16))
            .AlignItems(AlignItems.Center)
            .FlexShrink(0)
            .Background(isSelected ? GitClientColors.Selection : Color.Transparent)
            .Cursor(CursorType.Pointer);
        row.Key = $"tree-{depth}-{node.Label}";

        var hasChildren = node.Children.Count > 0;

        // Expand/collapse chevron or spacer
        if (hasChildren)
        {
            var chevron = Text.New(node.IsExpanded ? "\u25BE" : "\u25B8")
                .FontSize(11f).TextColor(GitClientColors.TextMuted)
                .Width(14);
            row.AddChild(chevron);
        }
        else
        {
            row.AddChild(Box.New().Width(14));
        }

        // Icon
        if (node.Icon != null)
        {
            row.AddChild(
                Text.New(node.Icon).FontSize(12f)
                    .TextColor(node.IconColor ?? GitClientColors.TextSecondary)
                    .Width(18)
            );
        }

        // Label
        var label = Text.New(node.Label).FontSize(13f)
            .TextColor(isSelected ? GitClientColors.TextPrimary : GitClientColors.TextSecondary);
        if (node.Tooltip != null)
            label.Tooltip(node.Tooltip);
        row.AddChild(label);

        // Events
        row.OnPointerEnter(_ => { if (!isSelected) row.BackgroundColor = GitClientColors.SurfaceHover; });
        row.OnPointerLeave(_ => { if (!isSelected) row.BackgroundColor = Color.Transparent; });
        row.OnClick(_ =>
        {
            if (hasChildren)
            {
                node.IsExpanded = !node.IsExpanded;
                Version++;
            }
            SelectedNode = node;
            OnSelectionChanged?.Invoke(node);
            node.OnClick?.Invoke();
        });

        if (node.ContextMenuItems != null)
        {
            var ctxFactory = node.ContextMenuItems;
            row.ContextMenu(ctxFactory);
        }

        elements.Add(row);

        // Children
        if (hasChildren && node.IsExpanded)
        {
            foreach (var child in node.Children)
                BuildNodeElements(child, depth + 1, elements);
        }
    }
}
