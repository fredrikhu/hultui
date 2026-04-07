using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Components.Shared;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Components.Layout;

public partial class Sidebar : Component
{
    private readonly GitRepository _repo;
    private readonly TreeView _treeView;

    public Action<string>? OnBranchSelected { get; set; }
    public Action<string>? CopyToClipboard { get; set; }

    public Sidebar(GitRepository repo)
    {
        _repo = repo;
        _treeView = new TreeView();
    }

    public override Element Build()
    {
        BuildTree();

        return Flex.Column(gap: 0)
            .Width(Dimension.Px(200))
            .Background(GitClientColors.Sidebar)
            .FlexShrink(0)
            .Children(
                // Section header
                Box.New()
                    .Padding(new Thickness(10, 12, 6, 12))
                    .Children(
                        Text.New("BRANCHES").FontSize(10f).FontWeight(FontWeight.Bold)
                            .TextColor(GitClientColors.TextMuted)
                    ),

                // Tree content
                ScrollView.New().FlexGrow(1).Children(
                    _treeView.Mount()
                )
            );
    }

    public void Refresh() => MarkDirty();

    private void BuildTree()
    {
        var branches = _repo.GetBranches();
        var tags = _repo.GetTags();
        var stashes = _repo.GetStashes();
        var currentBranch = _repo.GetCurrentBranch();

        var roots = new ReactiveList<TreeNode>();

        // Local branches
        var localBranches = branches.Where(b => !b.IsRemote).ToList();
        var localNode = new TreeNode
        {
            Label = "Local",
            Icon = "\u2442",
            IconColor = GitClientColors.BranchLocal,
            Children = localBranches.Select(b => new TreeNode
            {
                Label = b.Name,
                Icon = b.IsCurrent || b.Name == currentBranch ? "\u25CF" : "\u25CB",
                IconColor = b.IsCurrent || b.Name == currentBranch
                    ? GitClientColors.BranchCurrent
                    : GitClientColors.BranchLocal,
                Tooltip = FormatBranchTooltip(b),
                OnClick = () => OnBranchSelected?.Invoke(b.Name),
                ContextMenuItems = () =>
                [
                    new ContextMenuItem { Label = $"Checkout '{b.Name}'", Action = () => CheckoutBranch(b.Name) },
                    ContextMenuItem.Separator(),
                    new ContextMenuItem { Label = "Copy name", Action = () => CopyToClipboard?.Invoke(b.Name) },
                ],
                Tag = b,
            }).ToList(),
        };
        roots.Add(localNode);

        // Remote branches — group by remote name
        var remoteBranches = branches.Where(b => b.IsRemote).ToList();
        var remoteGroups = remoteBranches
            .GroupBy(b => b.Name.Contains('/') ? b.Name[..b.Name.IndexOf('/')] : "origin")
            .ToList();

        var remoteNode = new TreeNode
        {
            Label = "Remote",
            Icon = "\u2601",
            IconColor = GitClientColors.BranchRemote,
            Children = remoteGroups.Select(g => new TreeNode
            {
                Label = g.Key,
                Icon = "\u2601",
                IconColor = GitClientColors.BranchRemote,
                Children = g.Select(b => new TreeNode
                {
                    Label = b.Name.Contains('/') ? b.Name[(b.Name.IndexOf('/') + 1)..] : b.Name,
                    Icon = "\u25CB",
                    IconColor = GitClientColors.BranchRemote,
                    Tag = b,
                }).ToList(),
                IsExpanded = false,
            }).ToList(),
        };
        roots.Add(remoteNode);

        // Tags
        if (tags.Count > 0)
        {
            var tagNode = new TreeNode
            {
                Label = "Tags",
                Icon = "\u2691",
                IconColor = GitClientColors.TagColor,
                IsExpanded = false,
                Children = tags.Take(20).Select(t => new TreeNode
                {
                    Label = t,
                    Icon = "\u2691",
                    IconColor = GitClientColors.TagColor,
                }).ToList(),
            };
            roots.Add(tagNode);
        }

        // Stashes
        if (stashes.Count > 0)
        {
            var stashNode = new TreeNode
            {
                Label = "Stashes",
                Icon = "\u2397",
                IconColor = GitClientColors.StashColor,
                IsExpanded = false,
                Children = stashes.Select(s => new TreeNode
                {
                    Label = s,
                    Icon = "\u2397",
                    IconColor = GitClientColors.StashColor,
                }).ToList(),
            };
            roots.Add(stashNode);
        }

        _treeView.Roots = roots;
    }

    private void CheckoutBranch(string name)
    {
        _repo.Checkout(name);
        Refresh();
    }

    private static string FormatBranchTooltip(GitBranch b)
    {
        var parts = new List<string> { b.Name };
        if (b.TrackingBranch != null) parts.Add($"Tracks: {b.TrackingBranch}");
        if (b.AheadCount > 0) parts.Add($"Ahead: {b.AheadCount}");
        if (b.BehindCount > 0) parts.Add($"Behind: {b.BehindCount}");
        return string.Join("\n", parts);
    }
}
