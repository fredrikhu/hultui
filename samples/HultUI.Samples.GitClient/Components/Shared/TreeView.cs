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
    public Element? Trailing { get; init; }
}

public partial class TreeView : Component
{
    [Reactive] public partial ReactiveList<TreeNode> Roots { get; set; }
    [Reactive] public partial TreeNode? SelectedNode { get; set; }
    [Reactive] public partial int Version { get; set; }

    public Action<TreeNode>? OnSelectionChanged { get; set; }

    // Stable key-based lookups so handlers attached on the first Build can
    // find the current node and expansion state across rebuilds.  The framework
    // patches old elements in-place and keeps their original handlers, so
    // capturing per-build locals in closures would go stale.
    private Dictionary<string, TreeNode> _nodesByKey = new();
    private readonly Dictionary<string, bool> _expansionByKey = new();

    public TreeView()
    {
        Roots = new ReactiveList<TreeNode>();
    }

    public override Element Build()
    {
        _ = Roots.Count; // track changes
        _ = Version; // track expand/collapse
        _nodesByKey = new Dictionary<string, TreeNode>();
        var container = Flex.Column(gap: 0);
        var children = new List<Element>();

        foreach (var root in Roots)
            BuildNodeElements(root, 0, children);

        container.ReplaceChildren(children.ToArray());
        return container;
    }

    private bool GetExpanded(string key, TreeNode node)
    {
        return _expansionByKey.TryGetValue(key, out var e) ? e : node.IsExpanded;
    }

    private void BuildNodeElements(TreeNode node, int depth, List<Element> elements)
    {
        var key = $"tree-{depth}-{node.Label}";
        _nodesByKey[key] = node;

        var isSelected = node.IsSelected || SelectedNode == node;
        var expanded = GetExpanded(key, node);
        var hasChildren = node.Children.Count > 0;

        var row = Flex.Row(gap: 4)
            .Padding(new Thickness(4, 8, 4, 4 + depth * 16))
            .AlignItems(AlignItems.Center)
            .FlexShrink(0)
            .Background(isSelected ? GitClientColors.Selection : Color.Transparent)
            .Cursor(CursorType.Pointer);
        row.Key = key;

        // Expand/collapse chevron or spacer
        if (hasChildren)
        {
            var chevron = Text.New(expanded ? "\u25BE" : "\u25B8")
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
            .TextColor(isSelected ? GitClientColors.TextPrimary : GitClientColors.TextSecondary)
            .FlexShrink(1)
            .OverflowMode(TextOverflow.Ellipsis)
            .MaxLines(1);
        if (node.Tooltip != null)
            label.Tooltip(node.Tooltip);
        row.AddChild(label);

        if (node.Trailing != null)
        {
            row.AddChild(Box.New().FlexGrow(1));
            row.AddChild(node.Trailing);
        }

        // Events — closures capture `key` (a stable string) and read everything
        // else through `this` (the TreeView instance) so lookups survive rebuilds.
        row.OnPointerEnter(_ =>
        {
            var selected = _nodesByKey.TryGetValue(key, out var current)
                           && (current.IsSelected || SelectedNode == current);
            if (!selected) row.BackgroundColor = GitClientColors.SurfaceHover;
        });
        row.OnPointerLeave(_ =>
        {
            var selected = _nodesByKey.TryGetValue(key, out var current)
                           && (current.IsSelected || SelectedNode == current);
            row.BackgroundColor = selected ? GitClientColors.Selection : Color.Transparent;
        });
        row.OnClick(args =>
        {
            if (!_nodesByKey.TryGetValue(key, out var current)) return;
            if (current.Children.Count > 0)
            {
                _expansionByKey[key] = !GetExpanded(key, current);
                Version++;
            }
            SelectedNode = current;
            OnSelectionChanged?.Invoke(current);
            if (args.ClickCount == 2)
                current.OnDoubleClick?.Invoke();
            else
                current.OnClick?.Invoke();
        });

        if (node.ContextMenuItems != null)
        {
            var ctxFactory = node.ContextMenuItems;
            row.ContextMenu(ctxFactory);
        }

        elements.Add(row);

        // Children
        if (hasChildren && expanded)
        {
            foreach (var child in node.Children)
                BuildNodeElements(child, depth + 1, elements);
        }
    }
}
