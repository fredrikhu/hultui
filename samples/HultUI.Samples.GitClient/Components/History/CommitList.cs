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

namespace GitClient.Components.History;

public partial class CommitList : Component
{
    private readonly GitRepository _repo;
    private List<GitCommit> _commits = [];
    private string _searchQuery = "";

    // Inline tag-creation state. When _taggingCommitHash is non-null,
    // a small input strip is shown above the list capturing the new tag name.
    private string? _taggingCommitHash;
    private string _newTagName = "";

    [Reactive] public partial string? SelectedCommitHash { get; set; }

    public Action<GitCommit?>? OnCommitSelected { get; set; }
    public Action<string>? CopyToClipboard { get; set; }
    public Action<string>? OnCheckoutCommit { get; set; }
    public Action<string>? OnCherryPick { get; set; }

    /// <summary>
    /// Fired after a commit-list action that mutates the repo (e.g. tagging).
    /// The host wires this to refresh peer components like the sidebar.
    /// </summary>
    public Action? OnRepoChanged { get; set; }

    public CommitList(GitRepository repo)
    {
        _repo = repo;
    }

    public override Element Build()
    {
        var selected = SelectedCommitHash;
        _commits = _repo.GetLog();

        // Compute lane assignments over the full log, not the filtered subset,
        // so a hidden parent still produces a stable lane in its children.
        var graph = GitGraphLayout.Compute(_commits);
        var filtered = FilterCommits(_commits, _searchQuery);

        var children = new List<Element>
        {
            // Search bar
            BuildSearchBar(),
        };

        // Inline tag-name input (only when tagging a commit)
        if (_taggingCommitHash != null)
            children.Add(BuildNewTagInput());

        children.Add(
            // Separator
            Box.New().Height(1).Background(GitClientColors.Border).FlexShrink(0)
        );

        children.Add(
            // Commit list
            filtered.Count == 0
                ? Box.New().FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                    .Children(
                        Text.New(_searchQuery.Length > 0 ? "No matching commits" : "No commits yet")
                            .FontSize(14f).TextColor(GitClientColors.TextMuted)
                    )
                : VirtualListElement<GitCommit>.New()
                    .ItemHeight(36)
                    .Items(filtered)
                    .RenderWith(commit => CommitRow.Build(
                        commit,
                        graph.TryGetValue(commit.Hash, out var g) ? g : default,
                        commit.Hash == selected,
                        SelectCommit,
                        CopyToClipboard,
                        OnCheckoutCommit,
                        OnCherryPick,
                        StartTagCommit))
                    .FlexGrow(1)
        );

        return Flex.Column(gap: 0).FlexGrow(1).Background(GitClientColors.Background)
            .Children(children.ToArray());
    }

    public void Refresh() => MarkDirty();

    private Element BuildSearchBar()
    {
        var container = Flex.Row(gap: 6)
            .Padding(new Thickness(6, 8, 6, 8))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .FlexShrink(0);

        // Search icon
        container.AddChild(
            Text.New("\u2315").FontSize(13f).TextColor(GitClientColors.TextMuted)
        );

        // Search input
        container.AddChild(
            TextInput.New(_searchQuery)
                .PlaceholderText("Filter by message, author, or hash\u2026")
                .OnTextChanged(OnSearchChanged)
                .FontSize(12f)
                .FlexGrow(1)
                .Height(Dimension.Px(26))
                .Background(GitClientColors.Background)
                .CornerRadius(4f)
                .BorderColor(GitClientColors.Border)
                .TextColor(GitClientColors.TextPrimary)
        );

        // Clear button (only when there's a query)
        if (_searchQuery.Length > 0)
        {
            var clearBtn = Box.New()
                .Width(20).Height(20)
                .CornerRadius(10f)
                .JustifyContent(JustifyContent.Center)
                .AlignItems(AlignItems.Center)
                .Cursor(CursorType.Pointer)
                .Children(Text.New("\u2715").FontSize(10f).TextColor(GitClientColors.TextMuted));

            clearBtn.OnPointerEnter(_ => clearBtn.BackgroundColor = GitClientColors.SurfaceHover);
            clearBtn.OnPointerLeave(_ => clearBtn.BackgroundColor = Color.Transparent);
            clearBtn.OnClick(_ => { _searchQuery = ""; Refresh(); });

            container.AddChild(clearBtn);
        }

        return container;
    }

    private void OnSearchChanged(string query)
    {
        _searchQuery = query;
        Refresh();
    }

    private static List<GitCommit> FilterCommits(List<GitCommit> commits, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return commits;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return commits.Where(c => terms.All(term =>
            c.Subject.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            c.AuthorName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            c.AuthorEmail.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            c.ShortHash.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            c.Hash.Contains(term, StringComparison.OrdinalIgnoreCase)
        )).ToList();
    }

    private Element BuildNewTagInput()
    {
        // Find the commit being tagged so we can show its short hash as a hint.
        var target = _commits.FirstOrDefault(c => c.Hash == _taggingCommitHash);
        var hintText = target != null
            ? $"Tag {target.ShortHash}\u2026"
            : "Tag name\u2026";

        var input = TextInput.New(_newTagName)
            .PlaceholderText(hintText)
            .OnTextChanged(text => _newTagName = text)
            .FontSize(12f)
            .FlexGrow(1)
            .Height(Dimension.Px(24))
            .Background(GitClientColors.Background)
            .CornerRadius(3f)
            .BorderColor(GitClientColors.Border)
            .TextColor(GitClientColors.TextPrimary);

        input.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Enter) ConfirmCreateTag();
            else if (e.Key == KeyCode.Escape) CancelCreateTag();
        });

        var confirmBtn = Box.New()
            .Width(24).Height(24)
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonPrimary)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Tooltip("Create tag")
            .Children(Text.New("\u2713").FontSize(12f).TextColor(Color.White));
        confirmBtn.On<ClickEvent>(_ => ConfirmCreateTag());

        var cancelBtn = Box.New()
            .Width(24).Height(24)
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonSecondary)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Tooltip("Cancel")
            .Children(Text.New("\u2715").FontSize(12f).TextColor(GitClientColors.TextSecondary));
        cancelBtn.On<ClickEvent>(_ => CancelCreateTag());

        return Flex.Row(gap: 4)
            .Padding(new Thickness(4, 8, 4, 8))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .FlexShrink(0)
            .Children(input, confirmBtn, cancelBtn);
    }

    private void StartTagCommit(string commitHash)
    {
        _taggingCommitHash = commitHash;
        _newTagName = "";
        Refresh();
    }

    private void ConfirmCreateTag()
    {
        var name = _newTagName.Trim();
        var hash = _taggingCommitHash;
        if (name.Length > 0 && hash != null)
        {
            _repo.CreateTag(name, hash);
            OnRepoChanged?.Invoke();
        }
        _taggingCommitHash = null;
        _newTagName = "";
        Refresh();
    }

    private void CancelCreateTag()
    {
        _taggingCommitHash = null;
        _newTagName = "";
        Refresh();
    }

    private void SelectCommit(GitCommit commit)
    {
        SelectedCommitHash = commit.Hash;
        OnCommitSelected?.Invoke(commit);
    }
}
