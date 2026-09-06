using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using Junction.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Theme;

using HultUI.Icons;

namespace Junction;

public partial class JunctionApp
{

    /// <summary>Junction's Nocturne ramp, handed to the shared file-icon library. A property,
    /// not a static readonly field: the ramp it reads changes with the theme, and a field
    /// would hold whichever one was current when this type was first touched.</summary>
    private static FileIconPalette JunctionFileIconPalette => new(
        Nocturne.Accent400, Nocturne.Accent500, Nocturne.Accent600,
        Nocturne.Neutral400, Nocturne.Neutral500, Nocturne.Neutral600);

    // ── Files view: browse the repository as it was at the selected commit ──
    //
    // Left: the commit's full tree (folders collapse/expand). Right: the selected
    // file's contents, syntax-highlighted, with image and binary fallbacks. The
    // commit follows the History/Commit selection, so stepping commits re-roots
    // the browser at that revision.

    private string? _filesSelPath;
    // Expansion, flattening and the ←/→ steps live in the framework's tree model;
    // what stays here is what the model deliberately leaves out: the cursor, the
    // selection, and what a row looks like. Created in the ctor (needs MarkDirty).
    private readonly TreeView<string> _filesTree;
    private string? _filesTreeKey;   // the commit the expansion/selection belongs to

    // Keyboard navigation of the tree. The cursor may sit on a folder (to expand /
    // collapse) or a file (which also drives the content pane); _filesSelPath stays
    // the *file* whose contents show. _filesFlat is the visible-row order rebuilt each
    // Build, so the arrow keys can step through exactly what's on screen.
    private string? _filesCursorPath;
    private List<TreeNode<string>> _filesFlat = [];
    private readonly Ref<ScrollViewElement> _filesTreeScrollRef = new();
    // Find in the open file. The body is one TextArea rather than a row list, so matches
    // become spans at absolute offsets into the flattened text — see FileBodyTextAndSpans.
    private readonly FindBarState _filesFind;
    private readonly Ref<TextAreaElement> _fileBodyRef = new();
    private bool _pendingFilesTreeScroll;

    // Find a file *by name* in the browsed tree — the other half of "find in file". Held
    // across commits deliberately: stepping through history with a filter on is how you
    // watch one file's neighbourhood change.
    private string _filesQuery = "";
    private readonly Ref<TextInputElement> _filesSearchRef = new();
    private bool _pendingFilesSearchFocus;
    private bool _filesCursorToFirstMatch;

    // Bounded: browsing walks a new commit tree and a new file body per selection,
    // each pinning its parsed lines/spans, so these must not accumulate forever.
    private readonly KeyedAsyncLoader<string, IReadOnlyList<string>> _treeLoaders =
        new([], capacity: 12, comparer: StringComparer.Ordinal);
    private readonly KeyedAsyncLoader<string, FileContent> _fileContentLoaders =
        new(FileContent.Empty, capacity: 24, onEvicted: DisposeFileContent,
            comparer: StringComparer.Ordinal);

    private sealed record FileContent(
        string[]? Lines,
        IReadOnlyList<TextSpan>[]? Spans,
        ImageSide? Image,
        bool IsBinary,
        int Bytes)
    {
        public static readonly FileContent Empty = new(null, null, null, false, 0);
    }

    /// <summary>An evicted file body may hold a decoded image; its text lines and
    /// spans are plain managed arrays and need no help.</summary>
    private static void DisposeFileContent(FileContent content) =>
        (content.Image?.Source as IDisposable)?.Dispose();

    private const float FileTreeRowHeight = 24f;

    /// <summary>The commit the Files view is browsing (falls back to HEAD).</summary>
    private string FilesRef => _selectedCommitHash ?? "HEAD";

    private Element BuildFilesView()
    {
        if (!HasActiveRepo)
            return BuildStubView("Files", "Open a repository first.");

        var reference = FilesRef;
        var key = $"{_activeRepoPath}|{reference}|{DataGen(_activeRepoPath)}";
        SyncFilesTree(key);

        var loader = _treeLoaders.For(key);
        var repoPath = _activeRepoPath;
        loader.Load(key, () => RepoFor(repoPath).GetTreeFiles(reference), onLoaded: PickInitialTreeFile);
        var paths = loader.Value;

        var col = Flex.Column(gap: 0).FlexGrow(1);
        col.AddChild(BuildFilesHeader(paths.Count));

        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        row.AddChild(BuildFileTreePane(paths, loader.IsLoading));
        if (!_hideDiff)
            row.AddChild(BuildFileContentPane(reference));
        col.AddChild(row);
        return col;
    }

    /// <summary>Resets expansion/selection when the browsed commit changes.</summary>
    private void SyncFilesTree(string key)
    {
        if (_filesTreeKey == key) return;
        _filesTreeKey = key;
        _filesTree.ClearOverrides();
        // Keep the selected path across commits when it still exists — checked in
        // PickInitialTreeFile once the new tree lands.
    }

    private void PickInitialTreeFile(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) { _filesSelPath = null; return; }
        if (_filesSelPath == null || !paths.Contains(_filesSelPath))
            _filesSelPath = paths[0];
        _filesTree.ExpandAncestors(_filesSelPath!);
    }

    private Element BuildFilesHeader(int fileCount)
    {
        var commit = _history.Value.Commits.FirstOrDefault(c => c.Hash == _selectedCommitHash);
        var bar = Flex.Row(gap: 10).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(11, 16, 11, 16))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(Ui.Icon(Icons.Folders, 14f, Nocturne.Accent300).FlexShrink(0));
        bar.AddChild(Text.New("Files").FontSize(15f).FontWeight(FontWeight.SemiBold).FlexShrink(0));
        bar.AddChild(Ui.Mono(commit?.ShortHash ?? "HEAD", 11.5f, Nocturne.On(0.55)).FlexShrink(0));
        if (commit != null)
            bar.AddChild(Text.New(commit.Subject).FontSize(12f).TextColor(Nocturne.On(0.6))
                .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        else
            bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(Text.New($"{fileCount} files").FontSize(11f).TextColor(Nocturne.On(0.4)).FlexShrink(0));
        // Same commit stepping as the Commit view, so the browser can walk history
        // without leaving this tab.
        bar.AddChild(Ui.IconButton(Icons.CaretUp, () => MoveCommitSelection(-1),
            "Previous commit (Ctrl ↑)", 30f).FlexShrink(0));
        bar.AddChild(Ui.IconButton(Icons.CaretDown, () => MoveCommitSelection(1),
            "Next commit (Ctrl ↓)", 30f).FlexShrink(0));
        return bar;
    }

    // ── Tree ─────────────────────────────────────────────────────────────
    // The nodes are the framework's TreeNode<string> (Item = the file's path); the
    // folders-first ordering git trees conventionally get is Tree.FoldersFirst.

    private static readonly Comparison<TreeNode<string>> FilesTreeSort = Tree.FoldersFirst<string>();

    private Element BuildFileTreePane(IReadOnlyList<string> paths, bool loading)
    {
        var pane = Flex.Column(gap: 0)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        if (_hideDiff) pane.FlexGrow(1);
        else pane.Width(300).FlexShrink(0);

        var query = _filesQuery.Trim();
        var shown = FilterFilePaths(paths, query);
        pane.AddChild(BuildFilesSearch(paths.Count, shown.Count, query));

        var scroll = ScrollView.New().FlexGrow(1).Padding(new Thickness(6, 6, 6, 6))
            .WithRef(_filesTreeScrollRef);
        _filesFlat.Clear();
        if (loading && paths.Count == 0)
        {
            scroll.AddChild(Box.New().Padding(new Thickness(24)).JustifyContent(JustifyContent.Center)
                .Children(Text.New("Loading tree…").FontSize(12.5f).TextColor(Nocturne.On(0.45))));
        }
        else if (paths.Count == 0)
        {
            scroll.AddChild(Box.New().Padding(new Thickness(24)).JustifyContent(JustifyContent.Center)
                .Children(Text.New("No files at this commit").FontSize(12.5f).TextColor(Nocturne.On(0.45))));
        }
        else if (shown.Count == 0)
        {
            scroll.AddChild(Box.New().Padding(new Thickness(24)).JustifyContent(JustifyContent.Center)
                .Children(Text.New($"No files match \u201c{query}\u201d")
                    .FontSize(12.5f).TextColor(Nocturne.On(0.45))
                    .MaxLines(2).TextOverflow(TextOverflow.Ellipsis)));
        }
        else
        {
            _filesTree.Bind(shown, p => p, sort: FilesTreeSort);
            // A running search opens the tree outright — a match five folders down is no use
            // behind a collapsed folder. The collapsed set is untouched and comes back when
            // the query clears, which is what the Branches and Tags searches do too.
            _filesFlat = _filesTree.Rows(expandAll: query.Length > 0);
            // A query that has just changed puts the cursor on its first match, so Enter
            // opens what the search found without arrowing to it. Otherwise the cursor
            // only moves when the row it was on has gone.
            if (_filesCursorToFirstMatch)
            {
                _filesCursorToFirstMatch = false;
                _filesCursorPath = FirstFileRow() ?? _filesFlat.FirstOrDefault()?.Path;
            }
            else if (_filesCursorPath == null || !_filesFlat.Any(r => r.Path == _filesCursorPath))
                _filesCursorPath = _filesSelPath ?? _filesFlat.FirstOrDefault()?.Path;
            foreach (var node in _filesFlat)
                scroll.AddChild(TreeRow(node));
        }
        pane.AddChild(scroll);
        return pane;
    }

    /// <summary>The first file (not folder) among the visible rows — where the cursor goes
    /// when a search has just changed what is on screen.</summary>
    private string? FirstFileRow() => _filesFlat.FirstOrDefault(r => !r.IsFolder)?.Path;

    /// <summary>
    /// The paths the tree shows. A plain query is a case-insensitive substring of the whole
    /// path, so typing "diff" finds src/Core/DiffEngine.cs as readily as a folder called
    /// diff. A query carrying * or ? is a shell pattern matched against the path and against
    /// the bare file name, so both *.cs and src/*.cs mean what they look like — the same
    /// rule the branch and tag searches follow.
    /// </summary>
    private static List<string> FilterFilePaths(IReadOnlyList<string> paths, string query)
    {
        if (query.Length == 0) return [.. paths];
        if (Glob.HasWildcard(query))
            return paths.Where(p => Glob.IsMatch(p, query) || Glob.IsMatch(FileNameOf(p), query)).ToList();
        return paths.Where(p => p.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static string FileNameOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private Element BuildFilesSearch(int total, int shown, string query)
    {
        var bar = Flex.Row(gap: 6).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 8, 6, 8))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));

        var box = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Height(30).FlexGrow(1)
            .Padding(new Thickness(0, 8, 0, 8))
            .Background(Nocturne.Bg)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd);
        box.AddChild(Ui.Icon(Icons.MagnifyingGlass, 12f, Nocturne.On(0.45)).FlexShrink(0));

        var input = TextInput.New(_filesQuery)
            .PlaceholderText("Press / to find a file")
            .OnTextChanged(OnFilesQueryChanged)
            .Height(Dimension.Px(26))
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .FontSize(12f)
            .TextColor(Nocturne.Text)
            .WithRef(_filesSearchRef);
        input.AutomationId = "files-search";
        input.On<KeyDownEvent>(FilesSearchKey);
        box.AddChild(input);

        if (query.Length > 0)
        {
            box.AddChild(Text.New($"{shown}/{total}").FontSize(10.5f)
                .TextColor(Nocturne.On(0.42)).FlexShrink(0));
            box.AddChild(Ui.Icon(Icons.X, 11f, Nocturne.On(0.5)).FlexShrink(0)
                .Cursor(CursorType.Pointer)
                .Tooltip("Clear (Esc)")
                .OnClick(e => { e.StopPropagation = true; ClearFilesQuery(); }));
        }
        bar.AddChild(box);
        return bar;
    }

    private void OnFilesQueryChanged(string q)
    {
        _filesQuery = q;
        // The cursor follows the query. The *selected* file is left alone: the content pane
        // should not flicker through every file that matches a half-typed query.
        _filesCursorToFirstMatch = true;
        MarkDirty();
    }

    private void ClearFilesQuery()
    {
        if (_filesQuery.Length == 0) return;
        _filesQuery = "";
        // Back to the file the pane is actually showing, which is where someone who has
        // just abandoned a search expects to be.
        _filesCursorToFirstMatch = false;
        _filesCursorPath = _filesSelPath;
        MarkDirty();
    }

    /// <summary>
    /// Keys inside the file-search field. The tree is driven from here rather than by handing
    /// focus back to it — the point of the box is to reach a file, and typing two letters,
    /// pressing Down twice and opening what is under the cursor should not need the mouse in
    /// between. This is what the Branches search does with its table.
    /// </summary>
    private void FilesSearchKey(KeyDownEvent e)
    {
        // Alt and Ctrl belong to the global repo/workspace shortcuts.
        if (e.Modifiers.HasFlag(KeyModifiers.Alt) || e.Modifiers.HasFlag(KeyModifiers.Ctrl)) return;
        if (e.Key == KeyCode.Escape)
        {
            ClearFilesQuery();
            e.StopPropagation = true;
            return;
        }
        // Home/End (and Shift+PageUp/Down) move the caret inside the query, not the cursor
        // through the tree. Left/Right are the field's too — collapsing a folder from here
        // would make the query uneditable.
        if (IsFieldEditKey(e) || e.Key is KeyCode.Left or KeyCode.Right) return;
        if (HandleFilesTreeKey(e))
            e.StopPropagation = true;
    }

    private Element TreeRow(TreeNode<string> node)
    {
        bool selected = !node.IsFolder && node.Path == _filesSelPath;
        bool expanded = node.IsFolder && _filesTree.IsExpanded(node);
        bool cursor = node.Path == _filesCursorPath;
        int depth = node.Depth;

        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Height(FileTreeRowHeight).FlexShrink(0)
            .Padding(new Thickness(0, 6, 0, 6 + depth * 12))
            .CornerRadius(Nocturne.RadiusSm)
            .Cursor(CursorType.Pointer).Tooltip(node.Path);
        row.Key = (node.IsFolder ? "d:" : "f:") + node.Path
            + (selected ? "#s" : "") + (expanded ? "#e" : "") + (cursor ? "#c" : "");
        // Tree semantics for assistive tech: each row is a tree item, and folders
        // carry their open/closed state.
        row.Role = AccessibilityRole.TreeItem;
        if (node.IsFolder) row.AccessibleExpanded = expanded;
        // Selected file wins the accent fill; the keyboard cursor on any other row
        // (a folder, or a file not yet opened) gets a subtler highlight.
        if (selected) row.Background(Nocturne.Accent900);
        else if (cursor) row.Background(Nocturne.Neutral800);

        if (node.IsFolder)
        {
            row.AddChild(Ui.Icon(expanded ? Icons.CaretDown : Icons.CaretRight, 10f, Nocturne.On(0.5)).FlexShrink(0));
            row.AddChild(Ui.Icon(Icons.Folders, 11f, Nocturne.Accent300).FlexShrink(0));
        }
        else
        {
            row.AddChild(Box.New().Width(10).FlexShrink(0));
            // The shared file-type glyphs, same as Vesper's panes: a Unicode stand-in
            // could only tell images from everything-else, so a .csproj, a .zip and a
            // .cs all looked alike in a tree that is mostly source files.
            row.AddChild(FileIcons.Draw(FileIcons.KindFor(node.Segment), JunctionFileIconPalette, size: Ui.IconBox(12f))
                .FlexShrink(0));
        }

        row.AddChild(Text.New(node.Segment).FontSize(12f)
            .TextColor(node.IsFolder ? Nocturne.Text : Nocturne.On(selected ? 0.9 : 0.72))
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));

        // Both clicks of a double-click reach the same handler, as count 1 then count 2.
        // A folder must ignore the second, or the pair toggles it open and shut again;
        // a file uses it the way a file manager does, to launch what was just selected.
        if (node.IsFolder)
            row.OnClick(e =>
            {
                if (e.ClickCount >= 2) return;
                _filesCursorPath = node.Path;
                _filesTree.Toggle(node);   // MarkDirty via the model's onChanged
            });
        else
            row.OnClick(e =>
            {
                _filesCursorPath = node.Path;
                if (e.ClickCount >= 2) LaunchFilesPath(node.Path);
                else SelectTreeFile(node.Path);
            });
        row.ContextMenu(() => FilesTreeMenuItems(node));
        if (!selected && !cursor) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private void SelectTreeFile(string path)
    {
        _filesSelPath = path;
        // Rerender, not MarkDirty: this activates a NEW per-file content loader, and
        // a MarkDirty rebuild is untracked — the Effect would never subscribe to that
        // loader's signals, so its async completion goes unobserved and the pane sits
        // on "Loading…" until some other tracked rebuild (e.g. switching tabs).
        Rerender();
    }

    // ── Tree keyboard navigation ─────────────────────────────────────────
    // Driven from the global HandleKeyDown when View == Files (see JunctionApp.cs).
    // Up/Down step visible rows; Right expands a folder or descends; Left collapses or
    // ascends; Enter/Space opens a file or toggles a folder. Landing on a file opens it
    // (like a click), so arrowing through the tree previews contents as you go.

    private bool HandleFilesTreeKey(KeyDownEvent e)
    {
        if (_filesFlat.Count == 0) return false;
        int idx = FilesCursorIndex();
        switch (e.Key)
        {
            case KeyCode.Down: MoveFilesCursor(idx + 1); return true;
            case KeyCode.Up: MoveFilesCursor(idx - 1); return true;
            case KeyCode.PageDown: MoveFilesCursor(idx + FilesTreePage()); return true;
            case KeyCode.PageUp: MoveFilesCursor(idx - FilesTreePage()); return true;
            case KeyCode.Home: MoveFilesCursor(0); return true;
            case KeyCode.End: MoveFilesCursor(_filesFlat.Count - 1); return true;
            case KeyCode.Right: FilesTreeStep(idx, right: true); return true;
            case KeyCode.Left: FilesTreeStep(idx, right: false); return true;
            // Enter launches, like a file manager; Space is the quieter half that only
            // opens the file in the pane, for walking a tree without starting programs.
            case KeyCode.Enter: FilesTreeActivate(idx, launch: true); return true;
            case KeyCode.Space: FilesTreeActivate(idx, launch: false); return true;
            default: return false;
        }
    }

    private int FilesCursorIndex()
    {
        string? target = _filesCursorPath ?? _filesSelPath;
        for (int i = 0; i < _filesFlat.Count; i++)
            if (_filesFlat[i].Path == target) return i;
        return 0;
    }

    private int FilesTreePage()
    {
        float h = _filesTreeScrollRef.Current?.Layout.AbsoluteContentBox.Height ?? 400f;
        return Math.Max(1, (int)(h / FileTreeRowHeight) - 1);
    }

    /// <summary>Moves the cursor to a visible row (clamped). A file row opens too, so the
    /// content pane follows the cursor; a folder row just moves the highlight.</summary>
    private void MoveFilesCursor(int newIdx)
    {
        newIdx = Math.Clamp(newIdx, 0, _filesFlat.Count - 1);
        var node = _filesFlat[newIdx];
        _filesCursorPath = node.Path;
        _pendingFilesTreeScroll = true;
        if (!node.IsFolder && node.Path != _filesSelPath)
            SelectTreeFile(node.Path);   // Rerender: opens the file (tracked, see SelectTreeFile)
        else
            MarkDirty();
    }

    /// <summary>←/→ via the model's tree step: expand/collapse happens in the model
    /// (which marks dirty through onChanged); a step that lands on another row goes
    /// through the cursor move so a file landing opens it, like a click would.</summary>
    private void FilesTreeStep(int idx, bool right)
    {
        int landed = _filesTree.Step(_filesFlat, idx, right);
        if (landed != idx) MoveFilesCursor(landed);
        else _pendingFilesTreeScroll = true;
    }

    private void FilesTreeActivate(int idx, bool launch)
    {
        var node = _filesFlat[idx];
        _filesCursorPath = node.Path;
        _pendingFilesTreeScroll = true;
        if (node.IsFolder) { _filesTree.Toggle(node); return; }   // MarkDirty via onChanged
        SelectTreeFile(node.Path);                                // Rerender inside
        if (launch) LaunchFilesPath(node.Path);
    }

    // ── Content pane ─────────────────────────────────────────────────────

    // Runs off the UI thread: read the blob, decide text/binary/image, tokenize.
    private FileContent LoadFileContent(string repoPath, string reference, string path)
    {
        var repo = RepoFor(repoPath);
        var bytes = repo.GetFileBytes(reference, path);
        if (bytes == null) return FileContent.Empty;

        if (IsImagePath(path))
            return new FileContent(null, null, MakeSide(bytes), false, bytes.Length);

        if (DecodeBlobText(bytes) is not { } text)
            return new FileContent(null, null, null, true, bytes.Length);

        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        IReadOnlyList<TextSpan>[]? spans = null;
        try { spans = SyntaxHighlighter.TokenizeLines(path, text); } catch { /* highlight is best-effort */ }
        return new FileContent(lines, spans, null, false, bytes.Length);
    }

    /// <summary>
    /// A blob's bytes as text, or null when they are binary.
    ///
    /// <para>A UTF-16 byte-order mark settles it and picks the encoding: UTF-16 puts a NUL
    /// after every ASCII character, so it can never pass the scan below, and Windows writes
    /// its own <c>desktop.ini</c> that way. A UTF-8 mark is dropped. Without a mark, a NUL in
    /// the first 8 KB is git's own heuristic for binary, and stays the rule: guessing mark-less
    /// UTF-16 from byte patterns would risk showing a real binary as text.</para>
    ///
    /// <para>This browser shows a blob, not a diff, so reading UTF-16 here contradicts nothing
    /// git says. The diff pane is left to git's verdict — git calls the same file binary, and
    /// <see cref="GitRepository.LooksBinary"/> copies that on purpose so an untracked file is
    /// judged the way it will be once staged.</para>
    /// </summary>
    private static string? DecodeBlobText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;

        int probe = Math.Min(bytes.Length, 8000);
        for (int i = start; i < probe; i++)
            if (bytes[i] == 0)
                return null;
        return System.Text.Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    private Element BuildFileContentPane(string reference)
    {
        var pane = Flex.Column(gap: 0).FlexGrow(1);

        if (_filesSelPath is not { } path)
        {
            pane.AddChild(Box.New().FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New("Select a file").FontSize(13f).TextColor(Nocturne.On(0.45))));
            return pane;
        }

        var key = $"{_activeRepoPath}|{reference}|{path}";
        var loader = _fileContentLoaders.For(key);
        var repoPath = _activeRepoPath;
        loader.Load(key, () => LoadFileContent(repoPath, reference, path));
        var content = loader.Value;

        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(8, 12, 8, 12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Ui.Mono(path, 12f, Nocturne.Text)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        if (content.Bytes > 0)
            header.AddChild(Text.New(FormatBytes(content.Bytes)).FontSize(11f)
                .TextColor(Nocturne.On(0.45)).FlexShrink(0));
        if (content.Lines is { } l)
            header.AddChild(Text.New($"{l.Length} lines").FontSize(11f)
                .TextColor(Nocturne.On(0.45)).FlexShrink(0));
        // Blame is only meaningful over text: an image or a binary blob has no lines for git
        // to attribute, so the toggle is not offered rather than offered and empty.
        if (content.Lines is { Length: > 0 })
            header.AddChild(BlameToggle());
        // Keyed: these two come and go with whether the file is still in the working tree,
        // and an unkeyed button appearing beside an unkeyed one is exactly the case where a
        // patched-over element keeps the handler of whatever last held the slot.
        if (WorktreePath(path) != null)
        {
            var open = Ui.IconButton(Icons.RocketLaunch, () => LaunchFilesPath(path),
                "Open with the default app (Enter)", 28f).FlexShrink(0);
            open.Key = "files-open";
            header.AddChild(open);
            var reveal = Ui.IconButton(Icons.FolderOpen, () => RevealFilesPath(path),
                ShellLauncher.RevealLabel, 28f).FlexShrink(0);
            reveal.Key = "files-reveal";
            header.AddChild(reveal);
        }
        pane.AddChild(header);
        if (_filesFind.Open)
        {
            var bar = BuildFindBar(_filesFind, "Find in file…");
            // Keyed so it reconciles as itself across rebuilds rather than inheriting the
            // caret and focus of whatever last held its slot.
            bar.Key = "files-find";
            pane.AddChild(bar);
        }

        // Keyed: the two bodies are different elements in one slot, and the source body is the
        // one carrying a focusable TextArea — swapping them unkeyed is the case where a
        // patched-over element keeps the previous variant's handlers.
        Element body;
        if (_filesBlame && content.Lines is { Length: > 0 })
        {
            body = BuildBlameBody(reference, path, content);
            body.Key = "files-body-blame";
        }
        else
        {
            body = BuildFileBody(loader, content);
            body.Key = "files-body-source";
        }
        pane.AddChild(body);
        return pane;
    }

    private Element BuildFileBody(AsyncLoader<FileContent> loader, FileContent content)
    {
        if (loader.IsLoading && content.Lines == null && content.Image == null && !content.IsBinary)
            return Box.New().FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New("Loading…").FontSize(13f).TextColor(Nocturne.On(0.45)));

        if (content.Image is { } img)
        {
            var box = ScrollView.New().FlexGrow(1).Padding(new Thickness(14)).Background(Nocturne.Bg);
            box.AddChild(ImageSideCard("Contents", img, Nocturne.Accent300));
            return box;
        }

        if (content.IsBinary)
            return Box.New().FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Background(Nocturne.Bg)
                .Children(Text.New($"Binary file · {FormatBytes(content.Bytes)}")
                    .FontSize(13f).TextColor(Nocturne.On(0.45)));

        if (content.Lines is not { Length: > 0 } lines)
            return Box.New().FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Background(Nocturne.Bg)
                .Children(Text.New("Empty file").FontSize(13f).TextColor(Nocturne.On(0.45)));

        // One read-only TextArea holds the whole file, so a selection can span lines and
        // Ctrl+C / right-click Copy work (the framework wires the clipboard on focus).
        // The TextArea only paints its visible lines, so large files stay cheap. Keyed
        // per file so scroll/selection reset when the selection changes.
        BindFilesSearch(content, lines);
        var (text, spans) = FileBodyTextAndSpans(content, lines);
        spans = DecorateFileBody(lines, spans);
        var wrap = Box.New().FlexGrow(1).Overflow(Overflow.Hidden).Background(Nocturne.Bg)
            .Padding(new Thickness(6, 12, 6, 12));
        var area = TextArea.New(text).ReadOnly().FlexGrow(1)
            .FontFamily(CodeFont).FontSize(DiffCodeSize).TextColor(Nocturne.On(0.85)).LineHeight(1.5f);
        area.ScrollbarWidth = 7f;
        if (spans != null) area.Spans(spans);
        area.Key = "filebody:" + (_filesSelPath ?? "");
        area.IsFocusable = true;
        area.TabIndex = 0;
        area.WithRef(_fileBodyRef);
        wrap.AddChild(area);
        return wrap;
    }

    // Whole-file text + syntax spans in absolute offsets, memoized on the content
    // instance so a plain rebuild (e.g. moving the tree cursor) doesn't re-flatten.
    private FileContent? _fileBodyMemo;
    private string _fileBodyText = "";
    private IReadOnlyList<TextSpan>? _fileBodySpans;

    private (string text, IReadOnlyList<TextSpan>? spans) FileBodyTextAndSpans(FileContent content, string[] lines)
    {
        if (ReferenceEquals(content, _fileBodyMemo)) return (_fileBodyText, _fileBodySpans);

        var text = string.Join("\n", lines);
        List<TextSpan>? flat = null;
        if (content.Spans is { } perLine)
        {
            flat = [];
            int offset = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (i < perLine.Length && perLine[i] is { } lineSpans)
                    foreach (var s in lineSpans)
                        flat.Add(new TextSpan(offset + s.Start, s.Length, s.Color, s.Background));
                offset += lines[i].Length + 1;   // +1 for the '\n' join separator
            }
        }

        _fileBodyMemo = content;
        _fileBodyText = text;
        _fileBodySpans = flat;
        return (text, flat);
    }

    // ── Launching into the desktop ───────────────────────────────────────
    //
    // The tree lists a *commit's* files, and a commit is not a folder. A row here exists on
    // disk only while the working tree still carries it — browse an old commit, or a file
    // deleted since, and there is nothing to hand the desktop. So every entry point asks the
    // filesystem first and says why when the answer is no, rather than launching a path that
    // is not there and letting the OS produce the error.

    /// <summary>Where a repo-relative path lives in the working tree, or null when nothing is
    /// there. Git spells paths with '/' on every platform; the filesystem may not.</summary>
    private string? WorktreePath(string relPath)
    {
        if (string.IsNullOrEmpty(_activeRepoPath) || string.IsNullOrEmpty(relPath)) return null;
        var full = System.IO.Path.Combine(_activeRepoPath,
            relPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        return File.Exists(full) || Directory.Exists(full) ? full : null;
    }

    /// <summary>Opens a tree row with the OS default handler. Off the UI thread: a Windows
    /// association lookup goes through COM and can stall for the better part of a second, and
    /// this is on the double-click path.</summary>
    private async void LaunchFilesPath(string relPath)
    {
        if (WorktreePath(relPath) is not { } full) { Flash(NotOnDisk(relPath)); return; }
        var (ok, error) = await Task.Run(() =>
        {
            var started = ShellLauncher.Open(full, out var err);
            return (started, err);
        });
        if (!ok)
            Flash(string.IsNullOrEmpty(error) ? "Nothing is registered to open this file" : error);
    }

    private async void RevealFilesPath(string relPath)
    {
        if (WorktreePath(relPath) is not { } full) { Flash(NotOnDisk(relPath)); return; }
        var (ok, error) = await Task.Run(() =>
        {
            var started = ShellLauncher.Reveal(full, out var err);
            return (started, err);
        });
        if (!ok)
            Flash(string.IsNullOrEmpty(error) ? "Could not open the file manager" : error);
    }

    /// <summary>Ctrl+O in the Commit and Changes views, and the Changes list's "Open file":
    /// hands the selected file to the desktop's own handler.
    ///
    /// <para>The Commit view lists a <i>commit's</i> files, so the file it names is only on
    /// disk while the working tree still carries it — <see cref="LaunchFilesPath"/> says so
    /// by name when it does not, rather than launching a path that is not there.</para></summary>
    private void OpenSelectedFile()
    {
        var path = View == JunctionView.Changes ? _changeSelPath : _selectedFilePath;
        if (string.IsNullOrEmpty(path)) { Flash("Select a file to open first"); return; }
        LaunchFilesPath(path);
    }

    private static string NotOnDisk(string relPath) =>
        $"{System.IO.Path.GetFileName(relPath)} isn't in the working tree";

    private IReadOnlyList<ContextMenuItem> FilesTreeMenuItems(TreeNode<string> node)
    {
        var onDisk = WorktreePath(node.Path) is { } full ? full : null;
        var items = new List<ContextMenuItem>();

        if (onDisk != null)
        {
            items.Add(Item(node.IsFolder ? Icons.FolderOpen : Icons.RocketLaunch,
                node.IsFolder ? "Open folder" : "Open",
                () => LaunchFilesPath(node.Path)));
            items.Add(Item(Icons.FolderOpen, ShellLauncher.RevealLabel, () => RevealFilesPath(node.Path)));
        }
        else
        {
            // Named rather than dropped: browsing an old commit is the common way to land
            // here, and a row that simply loses its Open entry reads as a broken menu.
            items.Add(Unavailable(Icons.RocketLaunch, "Open — not in the working tree"));
        }

        if (!node.IsFolder)
        {
            items.Add(ContextMenuItem.Separator());
            // Blame reads the commit's blob, not the disk, so this is offered whether or not
            // the working tree still has the file.
            items.Add(Item(Icons.Blame, "Blame", () => BlameTreeFile(node.Path)));
        }

        items.Add(ContextMenuItem.Separator());
        items.Add(Item(Icons.Copy, "Copy path", () => CopyToClipboard(node.Path)));
        if (onDisk != null)
            items.Add(Item(Icons.Copy, "Copy full path", () => CopyToClipboard(onDisk)));
        return items;
    }
}

public partial class JunctionApp
{
    // ── Find in the Files view's open file ───────────────────────────────
    //
    // The body is a single read-only TextArea, not a list of rows, so everything here is
    // about the one conversion that costs: the search works in lines and columns, and the
    // TextArea works in absolute offsets into the joined text.

    /// <summary>What the search was last pointed at. The Files pane rebuilds on every repaint,
    /// so without this the file would be rescanned every frame.</summary>
    private object? _filesSearchKey;

    private void BindFilesSearch(FileContent content, string[] lines)
    {
        var key = (content, lines.Length);
        if (Equals(_filesSearchKey, key)) return;
        _filesSearchKey = key;
        _filesFind.Search.Bind(lines.Length, lanes: 1,
            (line, _) => line >= 0 && line < lines.Length ? lines[line] : null);
    }

    /// <summary>
    /// Start offsets of every line in the flattened text, memoized alongside it. Rebuilt only
    /// when the file changes — a search re-runs on every keystroke, and recomputing this each
    /// time would make a large file's find field lag behind the typing.
    /// </summary>
    private string[]? _fileLineStartsFor;
    private int[] _fileLineStarts = [];

    private int[] LineStarts(string[] lines)
    {
        if (ReferenceEquals(_fileLineStartsFor, lines)) return _fileLineStarts;
        var starts = new int[lines.Length];
        int offset = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            starts[i] = offset;
            offset += lines[i].Length + 1;   // +1 for the newline the join puts back
        }
        _fileLineStartsFor = lines;
        _fileLineStarts = starts;
        return starts;
    }

    /// <summary>
    /// Adds the search's tinting to the file's syntax spans, both in absolute offsets.
    ///
    /// <para>Not <see cref="TextSearch.Decorate"/>: that answers per line, against
    /// line-relative syntax spans, which is what a row-per-line view has. Here there is one
    /// span list over the whole file, so the matches are translated to absolute offsets and
    /// appended. A match carries only a background, so it layers over the syntax colour
    /// already there rather than replacing it.</para>
    /// </summary>
    private IReadOnlyList<TextSpan>? DecorateFileBody(string[] lines, IReadOnlyList<TextSpan>? syntax)
    {
        var search = _filesFind.Search;
        if (!search.HasHits) return syntax;

        var starts = LineStarts(lines);
        var current = search.Current;
        var merged = syntax is null ? new List<TextSpan>() : new List<TextSpan>(syntax);
        foreach (var hit in search.Hits)
        {
            if (hit.Length <= 0 || hit.Line >= starts.Length) continue;   // nothing to tint
            bool isCurrent = current is { } c && c.Line == hit.Line && c.Start == hit.Start;
            merged.Add(new TextSpan(starts[hit.Line] + hit.Start, hit.Length, default,
                isCurrent ? Nocturne.FindCurrentBg : Nocturne.FindHitBg));
        }
        // Back into offset order. TextAreaElement slices the list per visible line and stops
        // at the first span that starts past the end of that line, so a tint appended after
        // the syntax spans sits behind the stop and is never drawn — every match but the one
        // on the last decorated line went missing, which looked like search not highlighting
        // at all. The tints still overlap the syntax spans they sit inside; that is the point
        // of them (background over foreground) and the renderer handles it. Only the ordering
        // was wrong.
        merged.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return merged;
    }

    /// <summary>
    /// Brings the current match on screen.
    ///
    /// <para>Handed to the body as an absolute offset rather than a line number. The body wraps,
    /// so one logical line can be several drawn rows: a line index only names the row the line
    /// <i>starts</i> on, and a match near the end of a paragraph-long line would be scrolled to
    /// just off the bottom. The offsets are the ones already computed for the tinting, so this
    /// costs nothing extra.</para>
    /// </summary>
    private void RevealFilesHit()
    {
        if (_fileBodyRef.Current is not { } area) return;
        if (_filesFind.Search.Current is not { } hit) return;
        if (hit.Line >= 0 && hit.Line < _fileLineStarts.Length)
            area.ScrollOffsetIntoView(_fileLineStarts[hit.Line] + hit.Start);
        else
            area.ScrollLineIntoView(hit.Line);   // starts not built yet (no tinting pass ran)
    }
}
