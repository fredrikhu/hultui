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
    private bool _pendingFocus;
    private string _newBranchName = "";
    private string? _selectedBranchFullName;
    private readonly Ref<TextInputElement> _branchInputRef = new();

    public Action<string>? OnBranchSelected { get; set; }
    public Action<string>? OnBranchChanged { get; set; }
    public Action<GitStash>? OnStashSelected { get; set; }
    public Action? OnWorkingCopySelected { get; set; }
    public Action<string>? CopyToClipboard { get; set; }
    public bool WorkingCopyActive { get; set; }

    /// <summary>
    /// Fired after any sidebar action that mutates the repo (stash apply,
    /// drop, etc.). The host wires this to refresh peer components like
    /// the commit list and staging area.
    /// </summary>
    public Action? OnRepoChanged { get; set; }

    /// <summary>
    /// Surfaces git operation results (errors) to the status bar.
    /// </summary>
    public Action<string, bool>? ShowStatusMessage { get; set; }

    /// <summary>
    /// Request a merge of the given branch into the current branch.
    /// The host (App) handles mode selection and conflict state.
    /// </summary>
    public Action<string>? OnMergeBranch { get; set; }

    /// <summary>
    /// Request a rebase of the current branch onto the given branch.
    /// </summary>
    public Action<string>? OnRebaseBranch { get; set; }

    /// <summary>
    /// Shows a modal confirm dialog. The host (App) owns the dialog overlay.
    /// </summary>
    public Action<ConfirmDialogState>? ShowConfirm { get; set; }

    public Sidebar(GitRepository repo)
    {
        _repo = repo;
        _treeView = new TreeView();
    }

    public override Element Build()
    {
        BuildTree();
        var status = _repo.GetStatus();

        var children = new List<Element>
        {
            // Working copy entry
            BuildWorkingCopyRow(status.Count),

            // Separator
            Box.New().Height(1).Background(GitClientColors.Border).FlexShrink(0),

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
            .FlexGrow(1)
            .Background(GitClientColors.Sidebar)
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
            .TextColor(GitClientColors.TextPrimary)
            .WithRef(_branchInputRef);

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
        _pendingFocus = true;
        Refresh();
    }

    protected override void OnAfterBuild()
    {
        if (_pendingFocus)
        {
            _pendingFocus = false;
            _branchInputRef.Current?.Focus();
        }
    }

    private void ConfirmCreateBranch()
    {
        var name = _newBranchName.Trim();
        if (name.Length > 0)
        {
            var result = _repo.CreateBranch(name);
            if (!result.Success) ShowStatusMessage?.Invoke(result.Stderr.Trim(), true);
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
        ShowConfirm?.Invoke(new ConfirmDialogState
        {
            Title = "Delete Branch",
            Body = $"Are you sure you want to delete '{name}'? This cannot be undone.",
            ConfirmLabel = "Delete",
            Destructive = true,
            OnConfirm = () =>
            {
                var result = _repo.DeleteBranch(name);
                if (!result.Success) ShowStatusMessage?.Invoke(result.Stderr.Trim(), true);
                OnRepoChanged?.Invoke();
                Refresh();
            },
        });
    }

    private void BuildTree()
    {
        var branches = _repo.GetBranches();
        var tags = _repo.GetTags();
        var stashes = _repo.GetStashes();
        var currentBranch = _repo.GetCurrentBranch();
        EnsureSelectedBranch(branches, currentBranch);

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
            Children = BuildBranchTree(localBranches, currentBranch, isRemote: false),
        };
        roots.Add(localNode);

        // Remote branches — strip remote prefix (e.g. "origin/") for grouping,
        // then build path tree per remote
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
                Children = BuildBranchTree(g.ToList(), currentBranch, isRemote: true),
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
                Children = tags.Take(20).Select(MakeTagLeafNode).ToList(),
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
                Children = stashes.Select(MakeStashLeafNode).ToList(),
            };
            roots.Add(stashNode);
        }

        // Submodules
        var submodules = _repo.GetSubmodules();
        if (submodules.Count > 0)
        {
            var submoduleNode = new TreeNode
            {
                Label = "Submodules",
                Icon = "\u29C9",
                IconColor = GitClientColors.TextSecondary,
                IsExpanded = false,
                Children = submodules.Select(MakeSubmoduleLeafNode).ToList(),
            };
            roots.Add(submoduleNode);
        }

        _treeView.Roots = roots;
    }

    private void EnsureSelectedBranch(List<GitBranch> branches, string currentBranch)
    {
        if (_selectedBranchFullName != null && branches.Any(b => b.FullName == _selectedBranchFullName))
            return;

        _selectedBranchFullName = branches
            .FirstOrDefault(b => !b.IsRemote && (b.IsCurrent || b.Name == currentBranch))
            ?.FullName;
    }

    /// <summary>
    /// Builds a nested tree from branch names by splitting on '/'.
    /// e.g. ["feature/auth/login", "feature/auth/signup", "main"]
    /// becomes: feature > auth > [login, signup], main
    /// </summary>
    private List<TreeNode> BuildBranchTree(List<GitBranch> branches, string currentBranch, bool isRemote)
    {
        // For remote branches, strip the "origin/" prefix for display
        string DisplayName(GitBranch b)
        {
            if (!isRemote) return b.Name;
            var idx = b.Name.IndexOf('/');
            return idx >= 0 ? b.Name[(idx + 1)..] : b.Name;
        }

        // Intermediate tree structure: each node is either a folder or a leaf branch
        var root = new Dictionary<string, object>(); // value is either Dictionary<string,object> (folder) or GitBranch (leaf)

        foreach (var b in branches)
        {
            var display = DisplayName(b);
            var segments = display.Split('/');
            var current = root;

            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (i == segments.Length - 1)
                {
                    // Leaf — store the branch
                    current[seg] = b;
                }
                else
                {
                    // Folder — get or create sub-dictionary
                    if (!current.TryGetValue(seg, out var existing) || existing is not Dictionary<string, object> dict)
                    {
                        dict = new Dictionary<string, object>();
                        current[seg] = dict;
                    }
                    current = dict;
                }
            }
        }

        return BuildTreeNodesFromDict(root, currentBranch, isRemote);
    }

    private List<TreeNode> BuildTreeNodesFromDict(Dictionary<string, object> dict, string currentBranch, bool isRemote)
    {
        var nodes = new List<TreeNode>();

        // Sort: folders first, then leaves, both alphabetically
        var folders = dict.Where(kv => kv.Value is Dictionary<string, object>).OrderBy(kv => kv.Key);
        var leaves = dict.Where(kv => kv.Value is GitBranch).OrderBy(kv => kv.Key);

        foreach (var (label, value) in folders)
        {
            var children = BuildTreeNodesFromDict((Dictionary<string, object>)value, currentBranch, isRemote);
            nodes.Add(new TreeNode
            {
                Label = label,
                Icon = "\u25B8",
                IconColor = isRemote ? GitClientColors.BranchRemote : GitClientColors.BranchLocal,
                Children = children,
            });
        }

        foreach (var (label, value) in leaves)
        {
            var b = (GitBranch)value;
            var isCurrent = !isRemote && (b.IsCurrent || b.Name == currentBranch);
            nodes.Add(MakeBranchLeafNode(b, label, isCurrent, isRemote));
        }

        return nodes;
    }

    private TreeNode MakeBranchLeafNode(GitBranch b, string displayLabel, bool isCurrent, bool isRemote)
    {
        return new TreeNode
        {
            Label = displayLabel,
            Icon = isCurrent ? "\u25CF" : "\u25CB",
            IconColor = isCurrent ? GitClientColors.BranchCurrent
                : isRemote ? GitClientColors.BranchRemote
                : GitClientColors.BranchLocal,
            IsSelected = b.FullName == _selectedBranchFullName,
            Tooltip = FormatBranchTooltip(b),
            Trailing = !isRemote ? BuildBranchAheadBehind(b) : null,
            OnClick = () =>
            {
                _selectedBranchFullName = b.FullName;
                OnBranchSelected?.Invoke(b.Hash);
            },
            OnDoubleClick = () =>
            {
                _selectedBranchFullName = b.FullName;
                OnBranchChanged?.Invoke(b.Name);
            },
            ContextMenuItems = isRemote ? null : () =>
            {
                var items = new List<ContextMenuItem>
                {
                    new() { Label = $"Checkout '{b.Name}'", Action = () => CheckoutBranch(b.Name) },
                    new() { Label = "New Branch…", Action = StartCreateBranch },
                };
                if (!isCurrent)
                {
                    items.Add(new ContextMenuItem { Label = $"Merge '{b.Name}' into current", Action = () => OnMergeBranch?.Invoke(b.Name) });
                    items.Add(new ContextMenuItem { Label = $"Rebase current onto '{b.Name}'", Action = () => OnRebaseBranch?.Invoke(b.Name) });
                    items.Add(ContextMenuItem.Separator());
                    items.Add(new ContextMenuItem { Label = $"Delete '{b.Name}'", Action = () => DeleteBranch(b.Name) });
                }
                items.Add(ContextMenuItem.Separator());
                items.Add(new ContextMenuItem { Label = "Copy name", Action = () => CopyToClipboard?.Invoke(b.Name) });
                return items;
            },
            Tag = b,
        };
    }

    private static Element? BuildBranchAheadBehind(GitBranch b)
    {
        if (b.AheadCount == 0 && b.BehindCount == 0) return null;

        var row = Flex.Row(gap: 4)
            .AlignItems(AlignItems.Center)
            .FlexShrink(0);

        if (b.AheadCount > 0)
        {
            row.AddChild(Text.New($"\u2191{b.AheadCount}")
                .FontSize(11f)
                .FontWeight(FontWeight.SemiBold)
                .TextColor(GitClientColors.BranchLocal)
                .Tooltip($"Ahead of {b.TrackingBranch ?? "upstream"} by {b.AheadCount}"));
        }

        if (b.BehindCount > 0)
        {
            row.AddChild(Text.New($"\u2193{b.BehindCount}")
                .FontSize(11f)
                .FontWeight(FontWeight.SemiBold)
                .TextColor(GitClientColors.BranchRemote)
                .Tooltip($"Behind {b.TrackingBranch ?? "upstream"} by {b.BehindCount}"));
        }

        return row;
    }

    private void CheckoutBranch(string name)
    {
        var result = _repo.Checkout(name);
        if (!result.Success) ShowStatusMessage?.Invoke(result.Stderr.Trim(), true);
        else OnRepoChanged?.Invoke();
        Refresh();
    }

    private TreeNode MakeTagLeafNode(string tag)
    {
        return new TreeNode
        {
            Label = tag,
            Icon = "\u2691",
            IconColor = GitClientColors.TagColor,
            ContextMenuItems = () =>
            [
                new ContextMenuItem { Label = "Copy name", Action = () => CopyToClipboard?.Invoke(tag) },
                ContextMenuItem.Separator(),
                new ContextMenuItem { Label = $"Delete '{tag}'", Action = () => DeleteTag(tag) },
            ],
        };
    }

    private void DeleteTag(string name)
    {
        var result = _repo.DeleteTag(name);
        if (!result.Success) ShowStatusMessage?.Invoke(result.Stderr.Trim(), true);
        OnRepoChanged?.Invoke();
    }

    private TreeNode MakeStashLeafNode(GitStash stash)
    {
        var label = string.IsNullOrWhiteSpace(stash.Subject) ? stash.Ref : stash.Subject;
        return new TreeNode
        {
            Label = label,
            Icon = "\u2397",
            IconColor = GitClientColors.StashColor,
            Tooltip = $"{stash.Ref}\nOn {stash.Branch}\n{stash.Subject}",
            OnClick = () => OnStashSelected?.Invoke(stash),
            ContextMenuItems = () =>
            [
                new ContextMenuItem { Label = "Apply", Action = () => ApplyStash(stash.Ref) },
                new ContextMenuItem { Label = "Pop", Action = () => PopStash(stash.Ref) },
                ContextMenuItem.Separator(),
                new ContextMenuItem { Label = "Drop", Action = () => DropStash(stash.Ref) },
                ContextMenuItem.Separator(),
                new ContextMenuItem { Label = "Copy ref", Action = () => CopyToClipboard?.Invoke(stash.Ref) },
            ],
            Tag = stash,
        };
    }

    private TreeNode MakeSubmoduleLeafNode(GitSubmodule sub)
    {
        var statusIcon = sub.Uninitialized ? "\u25CB" : sub.Dirty ? "\u25D2" : "\u25CF";
        var statusColor = sub.Uninitialized ? GitClientColors.TextMuted
            : sub.Dirty ? GitClientColors.StatusModified
            : GitClientColors.BranchCurrent;

        var tooltip = $"{sub.Path}\n{sub.Hash[..Math.Min(7, sub.Hash.Length)]}";
        if (sub.Describe != null) tooltip += $"\n{sub.Describe}";
        if (sub.Dirty) tooltip += "\n(modified)";
        if (sub.Uninitialized) tooltip += "\n(not initialized)";

        return new TreeNode
        {
            Label = sub.Path,
            Icon = statusIcon,
            IconColor = statusColor,
            Tooltip = tooltip,
            ContextMenuItems = () =>
            [
                new ContextMenuItem { Label = "Copy path", Action = () => CopyToClipboard?.Invoke(sub.Path) },
                new ContextMenuItem { Label = "Copy hash", Action = () => CopyToClipboard?.Invoke(sub.Hash) },
            ],
        };
    }

    private void ApplyStash(string stashRef)
    {
        var result = _repo.StashApply(stashRef);
        if (!result.Success) ShowStatusMessage?.Invoke(result.Stderr.Trim(), true);
        OnRepoChanged?.Invoke();
    }

    private void PopStash(string stashRef)
    {
        var result = _repo.StashPop(stashRef);
        if (!result.Success) ShowStatusMessage?.Invoke(result.Stderr.Trim(), true);
        OnRepoChanged?.Invoke();
    }

    private void DropStash(string stashRef)
    {
        var result = _repo.StashDrop(stashRef);
        if (!result.Success) ShowStatusMessage?.Invoke(result.Stderr.Trim(), true);
        OnRepoChanged?.Invoke();
    }

    private Element BuildWorkingCopyRow(int changedCount)
    {
        var isSelected = WorkingCopyActive;

        var row = Flex.Row(gap: 8)
            .Height(Dimension.Px(36))
            .Padding(new Thickness(0, 12, 0, 12))
            .AlignItems(AlignItems.Center)
            .Background(isSelected ? GitClientColors.Selection : Color.Transparent)
            .Cursor(CursorType.Pointer)
            .FlexShrink(0);

        row.AddChild(
            Box.New().Width(20).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New("\u270E").FontSize(12f).TextColor(GitClientColors.StatusModified))
        );

        row.AddChild(
            Text.New("Working Copy").FontSize(13f).FontWeight(FontWeight.SemiBold)
                .TextColor(GitClientColors.TextPrimary).FlexGrow(1)
        );

        if (changedCount > 0)
        {
            row.AddChild(
                Box.New()
                    .Padding(new Thickness(2, 8, 2, 8))
                    .CornerRadius(10f)
                    .Background(new Color(GitClientColors.StatusModified.R, GitClientColors.StatusModified.G, GitClientColors.StatusModified.B, 30))
                    .Children(
                        Text.New(changedCount.ToString()).FontSize(11f).FontWeight(FontWeight.Bold)
                            .TextColor(GitClientColors.StatusModified)
                    )
            );
        }

        row.OnPointerEnter(_ => { if (!isSelected) row.BackgroundColor = GitClientColors.SurfaceHover; });
        row.OnPointerLeave(_ => { if (!isSelected) row.BackgroundColor = Color.Transparent; });
        row.OnClick(_ => OnWorkingCopySelected?.Invoke());

        return row;
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
