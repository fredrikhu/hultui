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
        var rows = TagRows();
        if (_tagSel >= rows.Count) _tagSel = Math.Max(0, rows.Count - 1);

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildTagsHeader());
        col.AddChild(BuildTagTableHeader());
        col.AddChild(BuildTagScroll(rows));
        return col;
    }

    // Runs off the UI thread.
    private TagsData LoadTags() => new(RepoFor(_activeRepoPath).GetTags());

    private void ClampTagSel(TagsData data)
    {
        var count = TagRowsOf(data.All).Count;
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

    /// <summary>The rows the table shows. Tag names are slash-hierarchical too
    /// (<c>v1/rc1</c>, <c>release/2024-06</c>); the ones that carry no slash — the usual
    /// <c>v1.2.3</c> — stay at the top level, so a plain semver repo looks the same in
    /// either mode.</summary>
    private List<RefNode<GitTag>> TagRowsOf(IReadOnlyList<GitTag> all) =>
        RefRows(FilterTags(all), RefKindTag, t => t.Name, flag: null,
            filtering: _tagQuery.Trim().Length > 0);

    private List<RefNode<GitTag>> TagRows() => TagRowsOf(_tags.Value.All);

    /// <summary>The tag under the caret, or null when the caret is on a folder row.</summary>
    private GitTag? SelectedTag()
    {
        var rows = TagRows();
        return _tagSel >= 0 && _tagSel < rows.Count && rows[_tagSel] is { IsFolder: false } leaf
            ? leaf.Item
            : null;
    }

    // ── Header (title · search · new tag) ────────────────────────────────

    private Element BuildTagsHeader()
    {
        var bar = Flex.Row(gap: 12).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(14, 26, 10, 26));

        bar.AddChild(Text.New("Tags").FontSize(18f).FontWeight(FontWeight.SemiBold));
        bar.AddChild(BuildTagSearch());
        bar.AddChild(RefViewToggle());
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

    private Element BuildTagScroll(List<RefNode<GitTag>> rows)
    {
        if (rows.Count == 0)
        {
            return Box.New().FlexGrow(1).Padding(new Thickness(26))
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New(_tagQuery.Trim().Length > 0
                        ? $"No tags match \"{_tagQuery.Trim()}\""
                        : "This repository has no tags")
                    .FontSize(13f).TextColor(Nocturne.On(0.45)));
        }

        // Virtualized like the branch table, and fed the same way: the expanded tree
        // flattened into one row list, indexed by node identity.
        var indexOf = new Dictionary<RefNode<GitTag>, int>(rows.Count);
        for (var i = 0; i < rows.Count; i++) indexOf[rows[i]] = i;

        return VirtualListElement<RefNode<GitTag>>.New()
            .ItemHeight(TagRowHeight)
            .Items(rows)
            .RenderWith(n => TagRow(n, indexOf[n]))
            // The virtual list surfaces items to assistive tech by this label alone
            // (rows off-screen are never realized as elements), so it carries
            // everything the row says: name, kind, annotation message, commit.
            .AccessibleItemLabel(n => n.IsFolder ? RefFolderLabel(RefKindTag, n) : TagLabel(n.Item))
            .OnActivateItem(n =>
            {
                // Assistive-tech activation mirrors a plain row click.
                if (!indexOf.TryGetValue(n, out var i)) return;
                if (n.IsFolder) { _tagSel = i; ToggleFolder(RefKindTag, n); }
                else SelectTag(i);
            })
            .SelectedIndex(_tagSel)
            .FlexGrow(1)
            .Padding(new Thickness(0, 26, 12, 26))
            .WithRef(_tagScrollRef);
    }

    private static string TagLabel(GitTag t) => t.Message.Length > 0
        ? $"{t.Name}, {(t.Annotated ? "annotated" : "lightweight")}, {t.Message}, {ShortHash(t.Hash)} {t.Subject}"
        : $"{t.Name}, {(t.Annotated ? "annotated" : "lightweight")}, {ShortHash(t.Hash)} {t.Subject}";

    private Element TagRow(RefNode<GitTag> node, int index)
    {
        var focused = index == _tagSel;
        return node.IsFolder
            ? RefFolderRow(RefKindTag, node, index, focused, TagRowHeight, SelectTagRow)
            : TagLeafRow(node, index, focused);
    }

    private Element TagLeafRow(RefNode<GitTag> node, int index, bool focused)
    {
        var t = node.Item;
        var depth = node.Depth;
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
        // The tree's indent lives inside this cell so the columns beside it stay aligned.
        var tagCell = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
            .FlexGrow(34).FlexBasis(Dimension.Px(0));
        if (depth > 0)
            tagCell.AddChild(Box.New().Width(depth * RefIndent).FlexShrink(0));
        tagCell.AddChild(Ui.Chip(Icons.Tag, "TAG",
            t.Annotated ? Nocturne.Accent2_800 : Nocturne.Neutral800,
            t.Annotated ? Nocturne.Accent2_100 : Nocturne.Neutral100));

        var titleCol = Flex.Column(gap: 1).FlexShrink(1).FlexBasis(Dimension.Px(0)).FlexGrow(1);
        // Segment, not the full name: inside a folder the prefix is already on screen.
        // The flat rows carry the whole name as their segment, so this is the same text
        // there as it always was.
        titleCol.AddChild(Ui.Mono(node.Segment, 12.5f, Nocturne.Text)
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
            .Children(Text.New(t.Date == default ? "" : t.Date.ToString("g"))
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
            case KeyCode.T: ToggleRefTreeView(); return true;
            // The folder keys, kept out of HandleTagNavKey for the same reason the
            // branch ones are: that subset also runs from inside the search box.
            case KeyCode.Right when _refTreeView: StepTagTree(right: true); return true;
            case KeyCode.Left when _refTreeView: StepTagTree(right: false); return true;
            default: return false;
        }
    }

    private void StepTagTree(bool right)
    {
        var rows = TagRows();
        var next = RefTreeStep(rows, RefKindTag, _tagSel, right);
        if (next != _tagSel)
        {
            _tagSel = next;
            if (!rows[next].IsFolder) SelectTag(next);
            _tagScrollRef.Current?.ScrollRangeIntoView(next * TagRowHeight, TagRowHeight);
        }
        MarkDirty();
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
            case KeyCode.Enter: ActivateSelectedTagRow(); return true;
            default: return false;
        }
    }

    /// <summary>Enter on the caret row: a folder opens or closes, a tag is checked out.</summary>
    private void ActivateSelectedTagRow()
    {
        var rows = TagRows();
        if (_tagSel >= 0 && _tagSel < rows.Count && rows[_tagSel] is { IsFolder: true } folder)
            ToggleFolder(RefKindTag, folder);
        else
            CheckoutSelectedTag();
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
        // points at — the same thing clicking a branch does with its tip. A folder row
        // names no commit, so it only moves the caret.
        if (SelectedTag() is { } t)
        {
            _selectedCommitHash = t.Hash;
            _selectedCommitHashes.Clear();
        }
        MarkDirty();
    }

    /// <summary>Moves the caret without touching the commit selection — what a folder row's
    /// click does before it opens or closes.</summary>
    private void SelectTagRow(int index)
    {
        _tagSel = index;
        MarkDirty();
    }

    private void MoveTagSel(int delta)
    {
        var rows = TagRows();
        if (rows.Count == 0) return;
        _tagSel = Math.Clamp(_tagSel + delta, 0, rows.Count - 1);
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
