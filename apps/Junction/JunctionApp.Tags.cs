using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    private const float TagRowHeight = 52f;

    // Column widths for the tag table (Tag/Commit flex; rest fixed).
    private const float ColTagCreated = 118f;
    private const float ColTagKind = 110f;
    private const float ColTagActions = 210f;

    private Element BuildTagsView()
    {
        if (!HasActiveRepo)
            return BuildStubView("Tags", "Open a repository first.");

        _tags.Load($"{_activeRepoPath}|{DataGen(_activeRepoPath)}", LoadTags, onLoaded: ClampTagSel);
        var list = FilterTags(_tags.Value.All);
        if (_tagSel >= list.Count) _tagSel = Math.Max(0, list.Count - 1);

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildTagsHeader());
        col.AddChild(BuildTagTableHeader());
        col.AddChild(BuildTagScroll(list));
        return col;
    }

    // Runs off the UI thread.
    private TagsData LoadTags() => new(RepoFor(_activeRepoPath).GetTags());

    private void ClampTagSel(TagsData data)
    {
        var count = FilterTags(data.All).Count;
        if (_tagSel >= count) _tagSel = Math.Max(0, count - 1);
    }

    private List<GitTag> FilterTags(IReadOnlyList<GitTag> all)
    {
        var q = _tagQuery.Trim();
        if (q.Length == 0) return all.ToList();
        return all.Where(t =>
            t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.Subject.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.Message.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private List<GitTag> FilteredTags() => FilterTags(_tags.Value.All);

    private GitTag? SelectedTag()
    {
        var list = FilteredTags();
        return _tagSel >= 0 && _tagSel < list.Count ? list[_tagSel] : null;
    }

    // ── Header (title · search · new tag) ────────────────────────────────

    private Element BuildTagsHeader()
    {
        var bar = Flex.Row(gap: 12).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(14, 26, 10, 26));

        bar.AddChild(Text.New("Tags").FontSize(18f).FontWeight(FontWeight.SemiBold));
        bar.AddChild(BuildTagSearch());
        bar.AddChild(Ui.PrimaryButton("New tag", Icons.Plus, PromptNewTag, 12.5f));
        return bar;
    }

    private Element BuildTagSearch()
    {
        var box = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Height(34).FlexGrow(1).MaxWidth(420)
            .Padding(new Thickness(0, 10, 0, 10))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd);
        box.AddChild(Ui.Icon(Icons.MagnifyingGlass, 12f, Nocturne.On(0.45)).FlexShrink(0));

        var input = TextInput.New(_tagQuery);
        input.PlaceholderText("Press / to search tags…")
            .OnTextChanged(OnTagQueryChanged)
            .Height(Dimension.Px(30))
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .TextColor(Nocturne.Text)
            .WithRef(_tagSearchRef);
        input.On<KeyDownEvent>(TagSearchKey);
        box.AddChild(input);
        return box;
    }

    private void OnTagQueryChanged(string q)
    {
        _tagQuery = q;
        _tagSel = 0;
        MarkDirty();
    }

    // ── Table ────────────────────────────────────────────────────────────

    private Element BuildTagTableHeader()
    {
        var row = Flex.Row(gap: 0).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 26, 6, 26))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        row.AddChild(HeaderCell("TAG", flexGrow: 34, align: JustifyContent.FlexStart));
        row.AddChild(HeaderCell("COMMIT", flexGrow: 40, align: JustifyContent.FlexStart));
        row.AddChild(HeaderCell("CREATED", width: ColTagCreated, align: JustifyContent.FlexStart));
        row.AddChild(HeaderCell("KIND", width: ColTagKind, align: JustifyContent.Center));
        row.AddChild(HeaderCell("ACTIONS", width: ColTagActions, align: JustifyContent.FlexEnd));
        return row;
    }

    private Element BuildTagScroll(List<GitTag> list)
    {
        var scroll = ScrollView.New().FlexGrow(1)
            .Padding(new Thickness(0, 26, 12, 26));
        scroll.WithRef(_tagScrollRef);

        if (list.Count == 0)
        {
            scroll.AddChild(Box.New().Padding(new Thickness(26)).JustifyContent(JustifyContent.Center)
                .AlignItems(AlignItems.Center)
                .Children(Text.New(_tagQuery.Trim().Length > 0
                        ? $"No tags match \"{_tagQuery.Trim()}\""
                        : "This repository has no tags")
                    .FontSize(13f).TextColor(Nocturne.On(0.45))));
            return scroll;
        }

        for (var i = 0; i < list.Count; i++)
            scroll.AddChild(TagRow(list[i], i));
        return scroll;
    }

    private Element TagRow(GitTag t, int index)
    {
        var focused = index == _tagSel;

        var row = Flex.Row(gap: 0).AlignItems(AlignItems.Center)
            .Height(TagRowHeight).FlexShrink(0)
            .Padding(new Thickness(0, 26, 0, 26))
            .CornerRadius(Nocturne.RadiusRow)
            .BorderColor(Nocturne.On(0.06)).BorderWidth(new Thickness(0, 0, 1, 0))
            .Cursor(CursorType.Pointer);
        // Keyed on the focus state so a flip replaces the row outright — the hover
        // baseline differs per state, as on the branch rows.
        row.Key = "tag:" + t.Name + (focused ? "#f" : "");
        row.Background(focused ? Nocturne.Neutral900 : Color.Transparent);

        // Tag cell: chip + name, with the annotation message underneath when there is one.
        var tagCell = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
            .FlexGrow(34).FlexBasis(Dimension.Px(0));
        tagCell.AddChild(Ui.Chip(Icons.Tag, "TAG",
            t.Annotated ? Nocturne.Accent2_800 : Nocturne.Neutral800,
            t.Annotated ? Nocturne.Accent2_100 : Nocturne.Neutral100));

        var titleCol = Flex.Column(gap: 1).FlexShrink(1).FlexBasis(Dimension.Px(0)).FlexGrow(1);
        titleCol.AddChild(Ui.Mono(t.Name, 12.5f, Nocturne.Text)
            .FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (t.Message.Length > 0)
            titleCol.AddChild(Text.New(t.Message).FontSize(10.5f).TextColor(Nocturne.On(0.42))
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        tagCell.AddChild(titleCol);
        row.AddChild(tagCell);

        // The commit it points at: short hash then subject.
        var commitCell = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .FlexGrow(40).FlexBasis(Dimension.Px(0)).Padding(new Thickness(0, 8, 0, 0));
        commitCell.AddChild(Ui.Mono(ShortHash(t.Hash), 11.5f, Nocturne.On(0.45)).FlexShrink(0));
        commitCell.AddChild(Text.New(t.Subject.Length > 0 ? t.Subject : "—").FontSize(12.5f)
            .TextColor(Nocturne.On(0.68)).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        row.AddChild(commitCell);

        row.AddChild(Box.New().Width(ColTagCreated).FlexShrink(0)
            .Children(Text.New(t.Date == default ? "" : RelativeTime(t.Date))
                .FontSize(12f).TextColor(Nocturne.On(0.55))));

        var kind = Flex.Row(gap: 0).Width(ColTagKind).FlexShrink(0)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center);
        kind.AddChild(Text.New(t.Annotated ? "annotated" : "lightweight")
            .FontSize(11.5f).TextColor(Nocturne.On(t.Annotated ? 0.62 : 0.40)));
        row.AddChild(kind);

        row.AddChild(TagActions(t));

        row.OnClick(_ => SelectTag(index));
        row.On<PointerDownEvent>(e => { if (e.Button == 2) SelectTag(index); });
        row.ContextMenu(() => TagMenuItems(t));
        return row;
    }

    private Element TagActions(GitTag t)
    {
        var box = Flex.Row(gap: 5).Width(ColTagActions).FlexShrink(0)
            .JustifyContent(JustifyContent.FlexEnd).AlignItems(AlignItems.Center);
        box.AddChild(Ui.SecondaryButton("Checkout", Icons.BendDownRight, () => CheckoutTag(t), 11.5f));
        box.AddChild(SmallIconButton(Icons.ArrowUp, () => PushTag(t), "Push tag (p)"));
        box.AddChild(SmallIconButton(Icons.Trash, () => DeleteTagAction(t), "Delete"));
        return box;
    }

    private static string ShortHash(string hash) => hash.Length > 7 ? hash[..7] : hash;

    // ── Keyboard ─────────────────────────────────────────────────────────

    private bool HandleTagKey(KeyDownEvent e)
    {
        if (HandleTagNavKey(e)) return true;
        switch (e.Key)
        {
            case KeyCode.C: CheckoutSelectedTag(); return true;
            case KeyCode.P: PushSelectedTag(); return true;
            default: return false;
        }
    }

    private bool HandleTagNavKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Down: MoveTagSel(1); return true;
            case KeyCode.Up: MoveTagSel(-1); return true;
            case KeyCode.PageDown: MoveTagSel(6); return true;
            case KeyCode.PageUp: MoveTagSel(-6); return true;
            case KeyCode.Home: MoveTagSel(int.MinValue / 2); return true;
            case KeyCode.End: MoveTagSel(int.MaxValue / 2); return true;
            case KeyCode.Enter: CheckoutSelectedTag(); return true;
            default: return false;
        }
    }

    private void TagSearchKey(KeyDownEvent e)
    {
        // Same split as the branch search box: Alt and Ctrl belong to the global
        // shortcuts, the field-editing keys belong to the text box, and what is left
        // drives the list so it can be walked without leaving the query.
        if (e.Modifiers.HasFlag(KeyModifiers.Alt) || e.Modifiers.HasFlag(KeyModifiers.Ctrl)) return;
        if (e.Key == KeyCode.Escape)
        {
            _tagQuery = "";
            _tagSel = 0;
            MarkDirty();
            e.StopPropagation = true;
            return;
        }
        if (IsFieldEditKey(e)) return;
        if (HandleTagNavKey(e))
            e.StopPropagation = true;
    }

    private void SelectTag(int index)
    {
        _tagSel = index;
        // The tag's commit becomes the selection, so Commit and Files show what it
        // points at — the same thing clicking a branch does with its tip.
        if (SelectedTag() is { } t)
        {
            _selectedCommitHash = t.Hash;
            _selectedCommitHashes.Clear();
        }
        MarkDirty();
    }

    private void MoveTagSel(int delta)
    {
        var list = FilteredTags();
        if (list.Count == 0) return;
        _tagSel = Math.Clamp(_tagSel + delta, 0, list.Count - 1);
        SelectTag(_tagSel);
        _tagScrollRef.Current?.ScrollRangeIntoView(_tagSel * TagRowHeight, TagRowHeight);
        MarkDirty();
    }

    // ── Actions ──────────────────────────────────────────────────────────

    private void CheckoutSelectedTag() { if (SelectedTag() is { } t) CheckoutTag(t); }
    private void PushSelectedTag() { if (SelectedTag() is { } t) PushTag(t); }

    /// <summary>Check out a tag. A tag is not a branch, so this lands on a detached
    /// HEAD — said plainly in the flash rather than left to be discovered.</summary>
    private void CheckoutTag(GitTag t)
    {
        if (ActiveRepo is not { } repo) return;
        var result = repo.Checkout(t.Name);
        Flash(result.Success
            ? $"Checked out {t.Name} — HEAD is detached"
            : FirstLine(result.Stderr, "Checkout failed"));
        RefreshAll();
    }

    private async void PushTag(GitTag t)
    {
        if (ActiveRepo is not { } repo || _remoteOpRunning) return;
        _remoteOpRunning = true;
        Flash($"Pushing {t.Name}…");
        var res = await repo.PushTagAsync(DefaultRemote(), t.Name, NoAskPass);
        _remoteOpRunning = false;
        Flash(res.Success ? $"Pushed {t.Name}" : FirstLine(res.Stderr, "Push failed"));
        RefreshAll();
    }

    /// <summary>Delete the tag here. The flash carries the commit it pointed at, because
    /// that is the only thing needed to put it back and git does not keep it anywhere
    /// else — there is no reflog for a deleted tag.</summary>
    private void DeleteTagAction(GitTag t)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.DeleteTag(t.Name);
        Flash(res.Success
            ? $"Deleted {t.Name} (was at {ShortHash(t.Hash)})"
            : FirstLine(res.Stderr, "Delete failed"));
        RefreshAll();
    }

    private async void DeleteRemoteTag(GitTag t, string remote)
    {
        if (ActiveRepo is not { } repo || _remoteOpRunning) return;
        _remoteOpRunning = true;
        Flash($"Deleting {t.Name} on {remote}…");
        var res = await repo.DeleteRemoteTagAsync(remote, t.Name, NoAskPass);
        _remoteOpRunning = false;
        Flash(res.Success ? $"Deleted {t.Name} on {remote}" : FirstLine(res.Stderr, "Delete failed"));
        RefreshAll();
    }

    /// <summary>The remote to push tags to: the only one, "origin" when it is among
    /// several, else whichever comes first. Same assumption the branch push makes.</summary>
    private string DefaultRemote()
    {
        if (ActiveRepo is not { } repo) return "origin";
        var remotes = repo.GetRemotes();
        if (remotes.Count == 0) return "origin";
        return remotes.Contains("origin") ? "origin" : remotes[0];
    }

    private IReadOnlyList<ContextMenuItem> TagMenuItems(GitTag t)
    {
        var remote = DefaultRemote();
        return
        [
            Item(Icons.BendDownRight, "Checkout (detached)", () => CheckoutTag(t)),
            Item(Icons.GitBranch, $"Create branch at {t.Name}…", () => PromptCreateBranchAt(t.Name, t.Name)),
            ContextMenuItem.Separator(),
            Item(Icons.ArrowUp, $"Push to {remote}", () => PushTag(t)),
            Item(Icons.Copy, "Copy tag name", () => CopyToClipboard(t.Name)),
            Item(Icons.Copy, "Copy commit hash", () => CopyToClipboard(t.Hash)),
            ContextMenuItem.Separator(),
            Item(Icons.Trash, "Delete tag", () => DeleteTagAction(t)),
            Item(Icons.Trash, $"Delete on {remote}", () => DeleteRemoteTag(t, remote)),
        ];
    }

    // ── New tag ──────────────────────────────────────────────────────────

    /// <summary>Tag the commit the app currently has selected, or HEAD when nothing is.
    /// The checkbox is the annotated/lightweight choice: an annotated tag needs a message,
    /// and the name stands in for one rather than leaving the prompt with two fields.</summary>
    private void PromptNewTag()
    {
        var at = _selectedCommitHash;
        var where = at is { Length: > 0 } ? $"at {ShortHash(at)}" : "at the current HEAD";
        OpenDialog(new DialogSpec(
            "New tag",
            $"Creates a tag {where}.",
            "v1.0.0",
            "Create",
            (name, annotated) => DoCreateTag(name, at, annotated),
            Validate: n => string.IsNullOrWhiteSpace(n) ? "Enter a tag name" : null,
            CheckboxLabel: "Annotated (records who made it and when)"));
    }

    private void DoCreateTag(string name, string? commit, bool annotated)
    {
        if (ActiveRepo is not { } repo) return;
        var res = repo.CreateTag(name, commit, annotated ? name : null);
        Flash(res.Success ? $"Created {name}" : FirstLine(res.Stderr, "Create tag failed"));
        RefreshAll();
    }
}
