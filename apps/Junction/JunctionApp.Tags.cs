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
    // The compact table's, on the branch table's breakpoints (see CompactTableContentWidth):
    // the date alone, KIND as narrow as "lightweight", and Checkout an icon beside push and
    // delete. 438px of fixed columns become 260; below the narrow breakpoint COMMIT goes too.
    private const float ColTagKindCompact = 76f;
    private const float ColTagActionsCompact = 3 * 30f + 2 * 5f;
    // Shared by the header and every leaf row; see BranchColumns.
    private static readonly string TagColumnsFull = FormattableString.Invariant(
        $"34fr 40fr {ColTagCreated}px {ColTagKind}px {ColTagActions}px");
    private static readonly string TagColumnsCompact = FormattableString.Invariant(
        $"34fr 40fr {ColUpdatedCompact}px {ColTagKindCompact}px {ColTagActionsCompact}px");
    private static readonly string TagColumnsNarrow = FormattableString.Invariant(
        $"1fr 0px {ColUpdatedCompact}px {ColTagKindCompact}px {ColTagActionsCompact}px");

    private string TagColumns =>
        _narrowTables ? TagColumnsNarrow : _compactTables ? TagColumnsCompact : TagColumnsFull;

    private Element BuildTagsView()
    {
        if (!HasActiveRepo)
            return BuildStubView("Tags", "Open a repository first.");

        EnsureTagsLoaded(_activeRepoPath);
        var rows = TagRows();
        if (_tagSel >= rows.Count) _tagSel = Math.Max(0, rows.Count - 1);

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildTagsHeader());
        col.AddChild(BuildTagTableHeader());
        col.AddChild(BuildTagScroll(rows));
        return col;
    }

    /// <summary>Starts (or reuses) the tag load for <paramref name="path"/> — from the view
    /// as it builds, or from the preload while another tab is showing.</summary>
    private void EnsureTagsLoaded(string path) =>
        _tagsLoaders.For(path).Load($"{path}|{DataGen(path)}",
            () => new TagsData(RepoFor(path).GetTags()),
            onLoaded: data => { if (PathEquals(path, _activeRepoPath)) ClampTagSel(data); });

    private void ClampTagSel(TagsData data)
    {
        var count = TagRowsOf(data.All).Count;
        if (_tagSel >= count) _tagSel = Math.Max(0, count - 1);
    }

    /// <summary>The tags the query keeps, on the same terms the branch search uses: a plain
    /// query is a substring of the name, the commit subject or the annotation message; a
    /// query carrying <c>*</c> or <c>?</c> is a wildcard pattern matched against the tag
    /// name and its leaf segment — <c>v1.*</c>, <c>release/2024-??</c> — which is what
    /// <c>git tag --list &lt;pattern&gt;</c> means by it. Subjects and messages stay out of
    /// the pattern case: an anchored pattern never matches a sentence.</summary>
    private List<GitTag> FilterTags(IReadOnlyList<GitTag> all)
    {
        var q = _tagQuery.Trim();
        if (q.Length == 0) return all.ToList();
        if (Glob.HasWildcard(q))
            return all.Where(t => Glob.IsMatch(t.Name, q) || Glob.IsMatch(LeafOf(t.Name), q)).ToList();
        return all.Where(t =>
            t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.Subject.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            t.Message.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>The last <c>/</c> segment — what the row is labelled inside a folder, and so
    /// what a pattern typed while looking at that row should be matched against.</summary>
    private static string LeafOf(string name)
    {
        var i = name.LastIndexOf('/');
        return i >= 0 ? name[(i + 1)..] : name;
    }

    /// <summary>The rows the table shows. Tag names are slash-hierarchical too
    /// (<c>v1/rc1</c>, <c>release/2024-06</c>); the ones that carry no slash — the usual
    /// <c>v1.2.3</c> — stay at the top level, so a plain semver repo looks the same in
    /// either mode.</summary>
    private List<TreeNode<GitTag>> TagRowsOf(IReadOnlyList<GitTag> all) =>
        _tagRowsMemo.Get(this, all, _tagQuery.Trim(), static (app, tags, query) =>
            app.RefRows(app.FilterTags(tags), RefKindTag, t => t.Name, flag: null,
                filtering: query.Length > 0));

    private readonly RefRowsMemo<GitTag> _tagRowsMemo = new();

    private List<TreeNode<GitTag>> TagRows() => TagRowsOf(_tags.Value.All);

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
        input.PlaceholderText("Press / to search tags — * and ? match…")
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
        var row = Grid.Of(TagColumns).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 26, 6, 26))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        row.AddChild(HeaderCell("TAG", 0, JustifyContent.FlexStart));
        if (!_narrowTables)
            row.AddChild(HeaderCell("COMMIT", 1, JustifyContent.FlexStart));
        row.AddChild(HeaderCell("CREATED", 2, JustifyContent.FlexStart));
        row.AddChild(HeaderCell("KIND", 3, JustifyContent.Center));
        row.AddChild(HeaderCell("ACTIONS", 4, JustifyContent.FlexEnd));
        return row;
    }

    private Element BuildTagScroll(List<TreeNode<GitTag>> rows)
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
        // flattened into one row list of kept nodes, so the list keeps every row a key does
        // not move the caret onto or off. What else a row shows is in the shape.
        if (TagRowShapeChanged()) _tagScrollRef.Current?.InvalidateRows();

        return VirtualListElement<TreeNode<GitTag>>.New()
            .ItemHeight(TagRowHeight)
            .Items(rows)
            .RenderWith((n, focused) => TagRow(n, focused))
            // The virtual list surfaces items to assistive tech by this label alone
            // (rows off-screen are never realized as elements), so it carries
            // everything the row says: name, kind, annotation message, commit. One
            // delegate, reading the node alone, so the list keeps each item's label.
            .AccessibleItemLabel(_tagItemLabel ??= n =>
                n.IsFolder ? RefFolderLabel(RefKindTag, n) : TagLabel(n.Item))
            .OnActivateItem(n =>
            {
                // Assistive-tech activation mirrors a plain row click.
                var i = _tagRowsMemo.IndexOf(n);
                if (i < 0) return;
                if (n.IsFolder) { _tagSel = i; ToggleFolder(RefKindTag, n); }
                else SelectTag(i);
            })
            .SelectedIndex(_tagSel)
            .FlexGrow(1)
            // No side padding, for the reason the branch list carries none: the scrollbar
            // is drawn inside the padding box, so a right pad holds it off the edge.
            .Padding(new Thickness(0, 0, 12, 0))
            .WithRef(_tagScrollRef);
    }

    private static string TagLabel(GitTag t) => t.Message.Length > 0
        ? $"{t.Name}, {(t.Annotated ? "annotated" : "lightweight")}, {t.Message}, {ShortHash(t.Hash)} {t.Subject}"
        : $"{t.Name}, {(t.Annotated ? "annotated" : "lightweight")}, {ShortHash(t.Hash)} {t.Subject}";

    private Func<TreeNode<GitTag>, string?>? _tagItemLabel;

    /// <summary>What a tag row shows besides its node and whether it has the caret: the
    /// table's columns (compact, narrow) and the colour scheme.</summary>
    private readonly record struct TagRowShape(bool Compact, bool Narrow, object Theme);

    private TagRowShape _tagRowShape;

    /// <summary>Rows the tag table's template has made, for tests.</summary>
    internal int TagRowsMade { get; private set; }

    /// <summary>Whether anything the tag rows show besides their nodes changed since the last
    /// build asked; takes the new state either way.</summary>
    private bool TagRowShapeChanged()
    {
        var shape = new TagRowShape(_compactTables, _narrowTables, Nocturne.Theme);
        bool changed = shape != _tagRowShape;
        _tagRowShape = shape;
        return changed;
    }

    private Element TagRow(TreeNode<GitTag> node, bool focused)
    {
        TagRowsMade++;
        // The node is one of the kept rows, so its place is the memo's (the row's click and
        // context menu select it by index).
        var index = _tagRowsMemo.IndexOf(node);
        return node.IsFolder
            ? RefFolderRow(RefKindTag, node, index, focused, TagRowHeight, SelectTagRow)
            : TagLeafRow(node, index, focused);
    }

    private Element TagLeafRow(TreeNode<GitTag> node, int index, bool focused)
    {
        var t = node.Item;
        var depth = node.Depth;
        var row = Grid.Of(TagColumns, rows: "1fr").AlignItems(AlignItems.Center)   // see BranchLeafRow
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
        // It ends 8px short of the next column, as COMMIT does before CREATED, so a truncated
        // name's ellipsis does not run into the hash (or, without COMMIT, the date).
        var tagCell = Flex.Row(gap: 9).AlignItems(AlignItems.Center).GridColumn(0)
            .Padding(new Thickness(0, 8, 0, 0));
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

        if (_narrowTables)
        {
            // No COMMIT column: the commit is the tag cell's tooltip (and stays in the row's
            // accessible label).
            tagCell.Tooltip($"{ShortHash(t.Hash)} {(t.Subject.Length > 0 ? t.Subject : "—")}");
        }
        else
        {
            // The commit it points at: short hash then subject.
            var commitCell = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
                .GridColumn(1).Padding(new Thickness(0, 8, 0, 0));
            commitCell.AddChild(Ui.Mono(ShortHash(t.Hash), 11.5f, Nocturne.On(0.45)).FlexShrink(0));
            commitCell.AddChild(Text.New(t.Subject.Length > 0 ? t.Subject : "—").FontSize(12.5f)
                .TextColor(Nocturne.On(0.68)).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            row.AddChild(commitCell);
        }

        row.AddChild(DateCell(t.Date, 2));

        var kind = Flex.Row(gap: 0).GridColumn(3)
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
        var box = Flex.Row(gap: 5).GridColumn(4)
            .JustifyContent(JustifyContent.FlexEnd).AlignItems(AlignItems.Center);
        box.AddChild(CheckoutButton(() => CheckoutTag(t), "tags.checkout"));
        box.AddChild(SmallIconButton(Icons.ArrowUp, () => PushTag(t), "Push tag (p)"));
        box.AddChild(SmallIconButton(Icons.Trash, () => DeleteTagAction(t), "Delete"));
        return box;
    }

    private static string ShortHash(string hash) => hash.Length > 7 ? hash[..7] : hash;

    // ── Keyboard ─────────────────────────────────────────────────────────

    private bool HandleTagKey(KeyDownEvent e)
    {
        if (HandleTagNavKey(e)) return true;
        if (Shortcuts.Pressed("tags.checkout", e)) { CheckoutSelectedTag(); return true; }
        if (Shortcuts.Pressed("tags.push", e)) { PushSelectedTag(); return true; }
        if (Shortcuts.Pressed("tags.tree", e)) { ToggleRefTreeView(); return true; }
        // The folder keys, kept out of HandleTagNavKey for the same reason the
        // branch ones are: that subset also runs from inside the search box.
        if (_refTreeView && Shortcuts.Pressed("tags.folder", e))
        {
            StepTagTree(right: e.Key == KeyCode.Right);
            return true;
        }
        return false;
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
        RunGitCommand(() => repo.Checkout(t.Name), result =>
        {
            Flash(result.Success
                ? $"Checked out {t.Name} — HEAD is detached"
                : FirstLine(result.Stderr, "Checkout failed"));
            RefreshAll();
        });
    }

    private async void PushTag(GitTag t)
    {
        if (ActiveRepo is not { } repo || RemoteOpRunning) return;
        BeginRemoteOp(RemoteOp.Push);
        Flash($"Pushing {t.Name}…");
        GitResult res;
        try { res = await repo.PushTagAsync(DefaultRemote(), t.Name, NoAskPass); }
        finally { EndRemoteOp(); }
        Flash(res.Success ? $"Pushed {t.Name}" : FirstLine(res.Stderr, "Push failed"));
        RefreshAll();
    }

    /// <summary>Delete the tag here. The flash carries the commit it pointed at, because
    /// that is the only thing needed to put it back and git does not keep it anywhere
    /// else — there is no reflog for a deleted tag.</summary>
    private void DeleteTagAction(GitTag t)
    {
        if (ActiveRepo is not { } repo) return;
        RunGitCommand(() => repo.DeleteTag(t.Name), res =>
        {
            Flash(res.Success
                ? $"Deleted {t.Name} (was at {ShortHash(t.Hash)})"
                : FirstLine(res.Stderr, "Delete failed"));
            RefreshAll();
        });
    }

    private async void DeleteRemoteTag(GitTag t, string remote)
    {
        if (ActiveRepo is not { } repo || RemoteOpRunning) return;
        BeginRemoteOp(RemoteOp.Other);
        Flash($"Deleting {t.Name} on {remote}…");
        GitResult res;
        try { res = await repo.DeleteRemoteTagAsync(remote, t.Name, NoAskPass); }
        finally { EndRemoteOp(); }
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
        RunGitCommand(() => repo.CreateTag(name, commit, annotated ? name : null), res =>
        {
            Flash(res.Success ? $"Created {name}" : FirstLine(res.Stderr, "Create tag failed"));
            RefreshAll();
        });
    }
}
