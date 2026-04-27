using System.Globalization;
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
    private bool _searchVisible;
    private bool _searchFocused;
    private bool _pendingSearchFocus;

    // Inline tag-creation state
    private string? _taggingCommitHash;
    private string _newTagName = "";
    private readonly Ref<TextInputElement> _searchRef = new();

    // ── Filter state ──
    private string? _pathFilter;
    private bool _pathFollow;
    private string? _authorFilter;
    private DateTimeOffset? _dateFrom;
    private DateTimeOffset? _dateTo;

    private FilterInputKind _showingInput = FilterInputKind.None;
    private string _inputValue = "";
    private string _dateFromInput = "";
    private string _dateToInput = "";
    private readonly Ref<VirtualListElement<GitCommit>> _virtualListRef = new();

    private bool HasServerFilters =>
        _pathFilter != null || _authorFilter != null || _dateFrom != null || _dateTo != null;

    /// <summary>
    /// When viewing the history of a specific file (via <see cref="ShowFileHistory"/>),
    /// returns that file's path; otherwise null. Used by the App to pre-select that
    /// file in CommitDetail when the user navigates between commits in the filtered log.
    /// </summary>
    public string? FollowedFilePath => _pathFollow ? _pathFilter : null;

    [Reactive] public partial string? SelectedCommitHash { get; set; }

    public Action<string, bool>? OnCommitSelected { get; set; }
    public Action<string>? CopyToClipboard { get; set; }
    public Action<string>? OnCheckoutCommit { get; set; }
    public Action<string>? OnCherryPick { get; set; }
    public Action<string>? OnRevert { get; set; }
    public Action<string, ResetMode>? OnReset { get; set; }
    public Action<string>? OnInteractiveRebase { get; set; }
    public Action<string>? OnBisectStart { get; set; }
    public string? BisectLabel { get; set; }
    public Action? OnRepoChanged { get; set; }

    public CommitList(GitRepository repo)
    {
        _repo = repo;
    }

    public override Element Build()
    {
        var selected = SelectedCommitHash;
        _commits = _repo.GetLog(
            author: _authorFilter,
            path: _pathFilter,
            after: _dateFrom,
            before: _dateTo,
            followPath: _pathFollow);

        // When server-side filters are active, parent hashes may reference commits
        // outside the result set. Strip those so the graph doesn't open phantom lanes.
        var graphInput = _commits;
        if (HasServerFilters)
        {
            var present = new HashSet<string>(_commits.Select(c => c.Hash));
            graphInput = _commits.Select(c => c with
            {
                ParentHashes = c.ParentHashes.Where(p => present.Contains(p)).ToArray()
            }).ToList();
        }
        var graph = GitGraphLayout.Compute(graphInput);
        var filtered = FilterCommits(_commits, _searchQuery);

        var children = new List<Element>();

        // Filter chips (above search bar, only when filters are active)
        if (HasServerFilters)
            children.Add(BuildFilterChips());

        // Search bar + filter buttons
        if (_searchVisible)
            children.Add(BuildSearchBar());

        // Inline filter input (when one is open)
        if (_showingInput != FilterInputKind.None)
            children.Add(BuildFilterInput());

        // Inline tag-name input
        if (_taggingCommitHash != null)
            children.Add(BuildNewTagInput());

        children.Add(
            Box.New().Height(1).Background(GitClientColors.Border).FlexShrink(0)
        );

        children.Add(
            filtered.Count == 0
                ? Box.New().FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                    .Children(
                        Text.New(_searchQuery.Length > 0 || HasServerFilters ? "No matching commits" : "No commits yet")
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
                        OnRevert,
                        StartTagCommit,
                        OnReset,
                        OnInteractiveRebase,
                        OnBisectStart,
                        BisectLabel))
                    .FlexGrow(1)
                    .WithRef(_virtualListRef)
        );

        return Flex.Column(gap: 0).FlexGrow(1).Background(GitClientColors.Background)
            .Children(children.ToArray());
    }

    public void Refresh() => MarkDirty();

    public void FocusSearch()
    {
        _searchVisible = true;
        _pendingSearchFocus = true;
        Refresh();
    }

    public void ToggleSearchFromShortcut()
    {
        if (_searchVisible && _searchFocused)
            HideSearch();
        else
            FocusSearch();
    }

    protected override void OnAfterBuild()
    {
        if (_pendingSearchFocus)
        {
            _pendingSearchFocus = false;
            _searchRef.Current?.Focus();
        }
    }

    // ── Search bar ──

    private Element BuildSearchBar()
    {
        var container = Flex.Row(gap: 6)
            .Padding(new Thickness(6, 8, 6, 8))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .FlexShrink(0);

        container.AddChild(
            Text.New("\u2315").FontSize(13f).TextColor(GitClientColors.TextMuted)
        );

        var input = TextInput.New(_searchQuery)
            .PlaceholderText("Filter by message, author, or hash\u2026")
            .OnTextChanged(OnSearchChanged)
            .FontSize(12f)
            .FlexGrow(1)
            .Height(Dimension.Px(26))
            .Background(GitClientColors.Background)
            .CornerRadius(4f)
            .BorderColor(GitClientColors.Border)
            .TextColor(GitClientColors.TextPrimary)
            .WithRef(_searchRef);
        input.On<FocusEvent>(_ => _searchFocused = true);
        input.On<BlurEvent>(_ => _searchFocused = false);
        container.AddChild(input);

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

        var closeBtn = Box.New()
            .Width(20).Height(20)
            .CornerRadius(10f)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Tooltip("Close search")
            .Children(Text.New("\u2715").FontSize(10f).TextColor(GitClientColors.TextMuted));

        closeBtn.OnPointerEnter(_ => closeBtn.BackgroundColor = GitClientColors.SurfaceHover);
        closeBtn.OnPointerLeave(_ => closeBtn.BackgroundColor = Color.Transparent);
        closeBtn.OnClick(_ => HideSearch());

        container.AddChild(closeBtn);

        // Filter buttons
        container.AddChild(Box.New().Width(1).Height(16).Background(GitClientColors.Border));
        container.AddChild(FilterButton("Path", FilterInputKind.Path));
        container.AddChild(FilterButton("Author", FilterInputKind.Author));
        container.AddChild(FilterButton("Date", FilterInputKind.Date));

        return container;
    }

    private Element FilterButton(string label, FilterInputKind kind)
    {
        var isOpen = _showingInput == kind;
        var bg = isOpen ? GitClientColors.ButtonPrimary : GitClientColors.ButtonSecondary;
        var fg = isOpen ? Color.White : GitClientColors.TextSecondary;

        var btn = Box.New()
            .Padding(new Thickness(3, 6, 3, 6))
            .CornerRadius(3f)
            .Background(bg)
            .Cursor(CursorType.Pointer)
            .Children(Text.New(label).FontSize(10f).FontWeight(FontWeight.SemiBold).TextColor(fg));

        if (!isOpen)
        {
            btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondaryHover);
            btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = GitClientColors.ButtonSecondary);
        }
        btn.On<ClickEvent>(_ => ToggleFilterInput(kind));

        return btn;
    }

    // ── Filter chips ──

    private Element BuildFilterChips()
    {
        var row = Flex.Row(gap: 6)
            .Padding(new Thickness(4, 8, 4, 8))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .FlexShrink(0);

        if (_pathFilter != null)
            row.AddChild(FilterChip(_pathFollow ? "History of" : "Path", _pathFilter,
                () => { _pathFilter = null; _pathFollow = false; Refresh(); }));
        if (_authorFilter != null)
            row.AddChild(FilterChip("Author", _authorFilter, () => { _authorFilter = null; Refresh(); }));
        if (_dateFrom != null)
            row.AddChild(FilterChip("After", _dateFrom.Value.ToString("yyyy-MM-dd"),
                () => { _dateFrom = null; Refresh(); }));
        if (_dateTo != null)
            row.AddChild(FilterChip("Before", _dateTo.Value.ToString("yyyy-MM-dd"),
                () => { _dateTo = null; Refresh(); }));

        if (HasServerFilters)
        {
            row.AddChild(Box.New().FlexGrow(1));
            var clearAll = Box.New()
                .Padding(new Thickness(3, 6, 3, 6))
                .CornerRadius(3f)
                .Cursor(CursorType.Pointer)
                .Children(Text.New("Clear all").FontSize(10f).TextColor(GitClientColors.TextMuted));
            clearAll.On<PointerEnterEvent>(_ => clearAll.BackgroundColor = GitClientColors.SurfaceHover);
            clearAll.On<PointerLeaveEvent>(_ => clearAll.BackgroundColor = Color.Transparent);
            clearAll.On<ClickEvent>(_ => ClearAllFilters());
            row.AddChild(clearAll);
        }

        return row;
    }

    private static Element FilterChip(string label, string value, Action onRemove)
    {
        var chip = Flex.Row(gap: 4)
            .Padding(new Thickness(3, 8, 3, 8))
            .CornerRadius(12f)
            .Background(GitClientColors.SurfaceActive)
            .AlignItems(AlignItems.Center)
            .FlexShrink(0);

        chip.AddChild(
            Text.New(label).FontSize(10f).FontWeight(FontWeight.Bold)
                .TextColor(GitClientColors.TextMuted)
        );
        chip.AddChild(
            Text.New(value).FontSize(10f).TextColor(GitClientColors.TextPrimary)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1)
        );

        var removeBtn = Box.New()
            .Width(14).Height(14)
            .CornerRadius(7f)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Children(Text.New("\u2715").FontSize(8f).TextColor(GitClientColors.TextMuted));
        removeBtn.On<PointerEnterEvent>(_ => removeBtn.BackgroundColor = GitClientColors.SurfaceHover);
        removeBtn.On<PointerLeaveEvent>(_ => removeBtn.BackgroundColor = Color.Transparent);
        removeBtn.On<ClickEvent>(e => { e.StopPropagation = true; onRemove(); });
        chip.AddChild(removeBtn);

        return chip;
    }

    // ── Inline filter inputs ──

    private void ToggleFilterInput(FilterInputKind kind)
    {
        if (_showingInput == kind)
        {
            _showingInput = FilterInputKind.None;
        }
        else
        {
            _showingInput = kind;
            // Pre-fill with current filter value
            switch (kind)
            {
                case FilterInputKind.Path:
                    _inputValue = _pathFilter ?? "";
                    break;
                case FilterInputKind.Author:
                    _inputValue = _authorFilter ?? "";
                    break;
                case FilterInputKind.Date:
                    _dateFromInput = _dateFrom?.ToString("yyyy-MM-dd") ?? "";
                    _dateToInput = _dateTo?.ToString("yyyy-MM-dd") ?? "";
                    break;
            }
        }
        Refresh();
    }

    private Element BuildFilterInput()
    {
        var placeholder = _showingInput switch
        {
            FilterInputKind.Path => "Path (e.g. src/Components/)",
            FilterInputKind.Author => "Author name or email",
            _ => "",
        };

        return _showingInput switch
        {
            FilterInputKind.Path or FilterInputKind.Author =>
                BuildTextFilterInput(placeholder, _inputValue, v => _inputValue = v, ConfirmTextFilter),
            FilterInputKind.Date => BuildDateFilterInput(),
            _ => Box.New(),
        };
    }

    private Element BuildTextFilterInput(string placeholder, string value,
        Action<string> onChanged, Action onConfirm)
    {
        var input = TextInput.New(value)
            .PlaceholderText(placeholder)
            .OnTextChanged(onChanged)
            .FontSize(12f)
            .FlexGrow(1)
            .Height(Dimension.Px(24))
            .Background(GitClientColors.Background)
            .CornerRadius(3f)
            .BorderColor(GitClientColors.Border)
            .TextColor(GitClientColors.TextPrimary);

        input.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Enter) onConfirm();
            else if (e.Key == KeyCode.Escape) CloseFilterInput();
        });

        return Flex.Row(gap: 4)
            .Padding(new Thickness(4, 8, 4, 8))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .FlexShrink(0)
            .Children(
                input,
                ConfirmButton(onConfirm),
                CancelButton(CloseFilterInput)
            );
    }

    private Element BuildDateFilterInput()
    {
        var fromInput = TextInput.New(_dateFromInput)
            .PlaceholderText("From (YYYY-MM-DD)")
            .OnTextChanged(v => _dateFromInput = v)
            .FontSize(12f)
            .FlexGrow(1)
            .Height(Dimension.Px(24))
            .Background(GitClientColors.Background)
            .CornerRadius(3f)
            .BorderColor(GitClientColors.Border)
            .TextColor(GitClientColors.TextPrimary);

        fromInput.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Enter) ConfirmDateFilter();
            else if (e.Key == KeyCode.Escape) CloseFilterInput();
        });

        var toInput = TextInput.New(_dateToInput)
            .PlaceholderText("To (YYYY-MM-DD)")
            .OnTextChanged(v => _dateToInput = v)
            .FontSize(12f)
            .FlexGrow(1)
            .Height(Dimension.Px(24))
            .Background(GitClientColors.Background)
            .CornerRadius(3f)
            .BorderColor(GitClientColors.Border)
            .TextColor(GitClientColors.TextPrimary);

        toInput.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Enter) ConfirmDateFilter();
            else if (e.Key == KeyCode.Escape) CloseFilterInput();
        });

        return Flex.Row(gap: 4)
            .Padding(new Thickness(4, 8, 4, 8))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .FlexShrink(0)
            .Children(
                fromInput,
                Text.New("\u2013").FontSize(12f).TextColor(GitClientColors.TextMuted),
                toInput,
                ConfirmButton(ConfirmDateFilter),
                CancelButton(CloseFilterInput)
            );
    }

    private void ConfirmTextFilter()
    {
        var val = _inputValue.Trim();
        var nonEmpty = val.Length > 0 ? val : null;

        if (_showingInput == FilterInputKind.Path)
        {
            _pathFilter = nonEmpty;
            _pathFollow = false;
        }
        else if (_showingInput == FilterInputKind.Author)
            _authorFilter = nonEmpty;

        _showingInput = FilterInputKind.None;
        Refresh();
    }

    private void ConfirmDateFilter()
    {
        _dateFrom = DateTimeOffset.TryParseExact(_dateFromInput.Trim(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ? from : null;
        _dateTo = DateTimeOffset.TryParseExact(_dateToInput.Trim(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) ? to : null;
        _showingInput = FilterInputKind.None;
        Refresh();
    }

    private void CloseFilterInput()
    {
        _showingInput = FilterInputKind.None;
        Refresh();
    }

    private void ClearAllFilters()
    {
        _pathFilter = null;
        _pathFollow = false;
        _authorFilter = null;
        _dateFrom = null;
        _dateTo = null;
        _showingInput = FilterInputKind.None;
        Refresh();
    }

    /// <summary>
    /// Filters the log to the history of a single file, following renames via
    /// <c>git log --follow</c>. Called when the user picks "Show history of this file"
    /// from a file context menu.
    /// </summary>
    public void ShowFileHistory(string path)
    {
        _pathFilter = path;
        _pathFollow = true;
        _searchQuery = "";
        _showingInput = FilterInputKind.None;
        Refresh();
    }

    // ── Shared small buttons ──

    private static Element ConfirmButton(Action onClick)
    {
        var btn = Box.New()
            .Width(24).Height(24)
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonPrimary)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Tooltip("Apply filter")
            .Children(Text.New("\u2713").FontSize(12f).TextColor(Color.White));
        btn.On<ClickEvent>(_ => onClick());
        return btn;
    }

    private static Element CancelButton(Action onClick)
    {
        var btn = Box.New()
            .Width(24).Height(24)
            .CornerRadius(3f)
            .Background(GitClientColors.ButtonSecondary)
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .Tooltip("Cancel")
            .Children(Text.New("\u2715").FontSize(12f).TextColor(GitClientColors.TextSecondary));
        btn.On<ClickEvent>(_ => onClick());
        return btn;
    }

    // ── Text search (client-side) ──

    private void OnSearchChanged(string query)
    {
        _searchQuery = query;
        Refresh();
    }

    private void HideSearch()
    {
        _searchVisible = false;
        _searchFocused = false;
        _pendingSearchFocus = false;
        _searchQuery = "";
        _showingInput = FilterInputKind.None;
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

    // ── Tag creation ──

    private Element BuildNewTagInput()
    {
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

        return Flex.Row(gap: 4)
            .Padding(new Thickness(4, 8, 4, 8))
            .AlignItems(AlignItems.Center)
            .Background(GitClientColors.Surface)
            .FlexShrink(0)
            .Children(
                input,
                ConfirmButton(ConfirmCreateTag),
                CancelButton(CancelCreateTag)
            );
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

    private void SelectCommit(string hash)
    {
        SelectedCommitHash = hash;
        OnCommitSelected?.Invoke(hash, false);
    }

    private enum FilterInputKind { None, Path, Author, Date }

    public void ScrollTo(string hash)
    {
        var index = _commits.FindIndex(x => x.Hash == hash);
        if (index == -1 || _virtualListRef.Current is not { } vl) return;
        vl.ScrollOffsetY = index * vl.ItemHeight;
        vl.MarkLayoutDirty();
        vl.MarkPaintDirty();
    }
}
