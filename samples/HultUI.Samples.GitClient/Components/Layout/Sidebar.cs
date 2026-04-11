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

namespace GitClient.Components.Layout;

public partial class Sidebar : Component
{
    private readonly GitRepository _repo;
    private readonly TreeView _treeView;

    private bool _isCreatingBranch;
    private string _newBranchName = "";

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

        var children = new List<Element>
        {
            // Section header
            Box.New()
                .Padding(new Thickness(10, 12, 6, 12))
                .Children(
                    Text.New("BRANCHES").FontSize(10f).FontWeight(FontWeight.Bold)
                        .TextColor(GitClientColors.TextMuted)
                ),
        };

        // Inline branch creation input
        if (_isCreatingBranch)
            children.Add(BuildNewBranchInput());

        // Tree content
        children.Add(
            ScrollView.New().FlexGrow(1).Children(
                _treeView.Mount()
            )
        );

        return Flex.Column(gap: 0)
            .Width(Dimension.Px(200))
            .Background(GitClientColors.Sidebar)
            .FlexShrink(0)
            .Children(children.ToArray());
    }

    public void Refresh() => MarkDirty();

    private Element BuildNewBranchInput()
    {
        var input = TextInput.New(_newBranchName)
            .PlaceholderText("Branch name…")
            .OnTextChanged(text => _newBranchName = text)
            .FontSize(12f)
            .FlexGrow(1)
            .Height(Dimension.Px(24))
            .Background(GitClientColors.Background)
            .CornerRadius(3f)
            .BorderColor(GitClientColors.Border)
            .TextColor(GitClientColors.TextPrimary);

        input.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Enter) ConfirmCreateBranch();
            else if (e.Key == KeyCode.Escape) CancelCreateBranch();
        });

        var confirmBtn = Box.New()
            .Width(24).Height(24)
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonPrimary)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Tooltip("Create branch")
            .Children(Text.New("\u2713").FontSize(12f).TextColor(Color.White));
        confirmBtn.On<ClickEvent>(_ => ConfirmCreateBranch());

        var cancelBtn = Box.New()
            .Width(24).Height(24)
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonSecondary)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Tooltip("Cancel")
            .Children(Text.New("\u2715").FontSize(12f).TextColor(GitClientColors.TextSecondary));
        cancelBtn.On<ClickEvent>(_ => CancelCreateBranch());

        return Flex.Row(gap: 4)
            .Padding(new Thickness(4, 8, 4, 8))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .FlexShrink(0)
            .Children(input, confirmBtn, cancelBtn);
    }

    private void StartCreateBranch()
    {
        _newBranchName = "";
        _isCreatingBranch = true;
        Refresh();
    }

    private void ConfirmCreateBranch()
    {
        var name = _newBranchName.Trim();
        if (name.Length > 0)
        {
            _repo.CreateBranch(name);
        }
        _isCreatingBranch = false;
        _newBranchName = "";
        Refresh();
    }

    private void CancelCreateBranch()
    {
        _isCreatingBranch = false;
        _newBranchName = "";
        Refresh();
    }

    private void DeleteBranch(string name)
    {
        _repo.DeleteBranch(name);
        Refresh();
    }

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
            ContextMenuItems = () =>
            [
                new ContextMenuItem { Label = "New Branch…", Action = StartCreateBranch },
            ],
            Children = localBranches.Select(b =>
            {
                var isCurrent = b.IsCurrent || b.Name == currentBranch;
                return new TreeNode
                {
                    Label = b.Name,
                    Icon = isCurrent ? "\u25CF" : "\u25CB",
                    IconColor = isCurrent
                        ? GitClientColors.BranchCurrent
                        : GitClientColors.BranchLocal,
                    Tooltip = FormatBranchTooltip(b),
                    OnClick = () => OnBranchSelected?.Invoke(b.Name),
                    ContextMenuItems = () =>
                    {
                        var items = new List<ContextMenuItem>
                        {
                            new() { Label = $"Checkout '{b.Name}'", Action = () => CheckoutBranch(b.Name) },
                            new() { Label = "New Branch…", Action = StartCreateBranch },
                        };
                        if (!isCurrent)
                        {
                            items.Add(ContextMenuItem.Separator());
                            items.Add(new ContextMenuItem { Label = $"Delete '{b.Name}'", Action = () => DeleteBranch(b.Name) });
                        }
                        items.Add(ContextMenuItem.Separator());
                        items.Add(new ContextMenuItem { Label = "Copy name", Action = () => CopyToClipboard?.Invoke(b.Name) });
                        return items;
                    },
                    Tag = b,
                };
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
