using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Worktrees in the sidebar, under the repository they belong to.
///
/// <para>A linked worktree is the same repository checked out a second time. Opening one used
/// to register it as a repository of its own, and the sidebar then showed two entries with the
/// same name, the same history and the same remotes — a repository that does not exist. This
/// shows them for what they are instead: a repository row with its checkouts nested under it,
/// derived from <c>git worktree list</c> rather than from anything the user has to add.</para>
///
/// <para>Registered worktrees from before are folded in the same way, so an old settings file
/// stops showing them twice without anything being deleted from it.</para>
/// </summary>
public partial class JunctionApp
{
    /// <summary>The repositories the sidebar actually lists — the workspace's repositories
    /// minus any that are a linked worktree of another one in the list. This is the order the
    /// digit shortcuts and Alt+↑/↓ follow, because it is the order on screen.
    ///
    /// <para>Recomputed by <see cref="BuildSidebar"/> each frame and read by the keyboard
    /// handlers afterwards; before the first build it is simply the workspace's own list.</para>
    /// </summary>
    private IReadOnlyList<string> _sidebarRepos = [];

    private IReadOnlyList<string> SidebarRepos =>
        _sidebarRepos.Count > 0 ? _sidebarRepos : ActiveWorkspaceRepos;

    /// <summary>One sidebar entry: a repository and the checkouts nested under it.</summary>
    private readonly record struct RepoGroup(string Path, IReadOnlyList<GitWorktree> Worktrees);

    /// <summary>A repository's worktrees, loaded off the UI thread and cached per repo and
    /// per data generation — the same loader (and so the same result) the worktrees panel
    /// uses. Mirrors <see cref="SummaryFor"/>, which the sidebar already calls per row.</summary>
    private IReadOnlyList<GitWorktree> WorktreesFor(string path)
    {
        if (string.IsNullOrEmpty(path)) return [];
        var loader = _worktreeLoaders.For(path);
        loader.Load($"{path}|{DataGen(path)}", () => RepoFor(path).GetWorktrees());
        return loader.Value;
    }

    /// <summary>
    /// The sidebar's entries: every workspace repository that is not itself listed under
    /// another one, each with its own checkouts.
    /// </summary>
    /// <remarks>A worktree is nested only when it is not a top-level entry in its own right,
    /// which is what makes the two directions agree. Add just the linked worktree to a
    /// workspace and it is the entry, with the main checkout nested under it; add both and the
    /// main one wins, because it is listed first.</remarks>
    private List<RepoGroup> SidebarRepoGroups()
    {
        var repos = ActiveWorkspaceRepos;
        if (repos.Count == 0) return [];

        // A repo is nested under the first *earlier* entry that claims it. Walking in order
        // keeps the rule stable — otherwise two mutual worktrees could each swallow the other
        // depending on dictionary order, and one of them would vanish from the sidebar.
        var nested = new HashSet<string>(PathComparer);
        var groups = new List<RepoGroup>();
        foreach (var path in repos)
        {
            if (nested.Contains(path)) continue;
            var children = WorktreesFor(path)
                .Where(w => !PathEquals(w.Path, path))
                .ToList();
            foreach (var child in children) nested.Add(child.Path);
            groups.Add(new RepoGroup(path, children));
        }
        return groups;
    }

    /// <summary>A checkout under its repository: the branch it holds, and the folder holding
    /// it. The branch leads because that is what picks one worktree out of several — the
    /// folder names tend to differ by a suffix and read alike at a glance.</summary>
    private Element WorktreeChildRow(GitWorktree wt, string repoPath)
    {
        var active = PathEquals(wt.Path, _activeRepoPath);
        var folder = System.IO.Path.GetFileName(wt.Path.TrimEnd('/', '\\'));
        var label = wt.IsDetached ? Short(wt.Head) : wt.RefLabel;

        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 8, 5, 22))   // indented past the parent's tile
            .CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer).Tooltip(wt.Path);
        // Everything that changes the row's shape is in the key: a lock or a prune swaps a
        // tag in and out at a fixed position, and rows come and go as worktrees do.
        row.Key = "wtchild:" + wt.Path + (active ? "#a" : "")
            + (wt.IsMain ? "#m" : "") + (wt.IsLocked ? "#l" : "") + (wt.Prunable ? "#p" : "");
        if (active)
            row.Background(Nocturne.Accent900).BorderColor(Nocturne.Accent).BorderWidth(new Thickness(0, 0, 0, 2));

        row.AddChild(Ui.Icon(wt.IsMain ? Icons.GitRepository : Icons.TreeStructure, 12f,
            active ? Nocturne.Accent200 : Nocturne.On(0.45)).FlexShrink(0));

        var text = Flex.Column(gap: 0).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        text.AddChild(Ui.Mono(label, 11.5f, active ? Nocturne.Text : Nocturne.On(0.78))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        text.AddChild(Text.New(folder).FontSize(9.5f).TextColor(Nocturne.On(0.38))
            .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(text);

        // Only the states that change what you may do with the row are worth the width here;
        // the panel carries the rest.
        if (wt.IsMain) row.AddChild(Ui.TagOutline("main", Nocturne.On(0.45), 8.5f).FlexShrink(0));
        if (wt.IsLocked) row.AddChild(Ui.TagOutline("locked", Nocturne.Accent2, 8.5f).FlexShrink(0));
        if (wt.Prunable) row.AddChild(Ui.TagOutline("gone", Nocturne.DelCount, 8.5f).FlexShrink(0));

        row.OnClick(_ => SelectRepo(wt.Path));
        row.On<PointerDownEvent>(e => { if (e.Button == 2 && !active) SelectRepo(wt.Path); });
        row.ContextMenu(() => WorktreeMenuItems(wt, active));
        if (!active) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    /// <summary>The row under a repository's checkouts that adds another one. Present only
    /// when there already are some — a repository with a single checkout should not spend a
    /// line of the sidebar advertising a feature most repositories never use; the command
    /// palette and the branch menu are where that starts.</summary>
    private Element AddWorktreeRow(string repoPath)
    {
        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4, 8, 4, 22)).CornerRadius(Nocturne.RadiusMd)
            .Cursor(CursorType.Pointer).Tooltip("Add a worktree to this repository");
        row.Key = "wtadd:" + repoPath;
        row.AddChild(Ui.Icon(Icons.Plus, 11f, Nocturne.On(0.4)).FlexShrink(0));
        row.AddChild(Text.New("New worktree").FontSize(10.5f).TextColor(Nocturne.On(0.45)).FlexGrow(1));
        row.OnClick(_ =>
        {
            SelectRepo(repoPath);
            PromptNewWorktree();
        });
        row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }
}
