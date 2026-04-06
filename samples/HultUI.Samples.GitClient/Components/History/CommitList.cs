using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
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

    [Reactive] public partial GitCommit? SelectedCommit { get; set; }
    [Reactive] public partial bool WorkingCopySelected { get; set; }
    [Reactive] public partial int RefreshTrigger { get; set; }

    public Action<GitCommit?>? OnCommitSelected { get; set; }
    public Action? OnWorkingCopySelected { get; set; }
    public Action<string>? CopyToClipboard { get; set; }
    public Action<string>? OnCheckoutCommit { get; set; }
    public Action<string>? OnCherryPick { get; set; }

    public CommitList(GitRepository repo)
    {
        _repo = repo;
    }

    public override Element Build()
    {
        _ = RefreshTrigger;
        var selected = SelectedCommit;
        var wcSelected = WorkingCopySelected;
        _commits = _repo.GetLog();
        var status = _repo.GetStatus();

        var filtered = FilterCommits(_commits, _searchQuery);

        return Flex.Column(gap: 0).FlexGrow(1).Background(GitClientColors.Background).Children(
            // Working copy row (always visible at top)
            BuildWorkingCopyRow(status.Count, wcSelected),

            // Search bar
            BuildSearchBar(),

            // Separator
            Box.New().Height(1).Background(GitClientColors.Border).FlexShrink(0),

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
                    .RenderWith(commit => CommitRow.Build(commit, !wcSelected && commit == selected, SelectCommit, CopyToClipboard, OnCheckoutCommit, OnCherryPick))
                    .FlexGrow(1)
        );
    }

    public void Refresh()
    {
        RefreshTrigger++;
    }

    private Element BuildWorkingCopyRow(int changedCount, bool isSelected)
    {
        var row = Flex.Row(gap: 8)
            .Height(Dimension.Px(36))
            .Padding(new Thickness(0, 12, 0, 12))
            .AlignItems(AlignItems.Center)
            .Background(isSelected ? GitClientColors.Selection : Color.Transparent)
            .Cursor(CursorType.Pointer)
            .FlexShrink(0);

        // Pencil icon
        row.AddChild(
            Box.New().Width(20).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New("\u270E").FontSize(12f).TextColor(GitClientColors.StatusModified))
        );

        // Label
        row.AddChild(
            Text.New("Working Copy").FontSize(13f).FontWeight(FontWeight.SemiBold)
                .TextColor(GitClientColors.TextPrimary).FlexGrow(1)
        );

        // Changed file count badge
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

        // Hover
        row.OnPointerEnter(_ => { if (!isSelected) row.BackgroundColor = GitClientColors.SurfaceHover; });
        row.OnPointerLeave(_ => { if (!isSelected) row.BackgroundColor = Color.Transparent; });
        row.OnClick(_ => SelectWorkingCopy());

        return row;
    }

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

    private void SelectWorkingCopy()
    {
        SelectedCommit = null;
        WorkingCopySelected = true;
        OnWorkingCopySelected?.Invoke();
    }

    private void SelectCommit(GitCommit commit)
    {
        WorkingCopySelected = false;
        SelectedCommit = commit;
        OnCommitSelected?.Invoke(commit);
    }
}
