using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Git;
using GitClient.Highlighting;
using GitClient.Theme;

namespace GitClient.Junction;

public partial class JunctionApp
{
    // ── Files view: browse the repository as it was at the selected commit ──
    //
    // Left: the commit's full tree (folders collapse/expand). Right: the selected
    // file's contents, syntax-highlighted, with image and binary fallbacks. The
    // commit follows the History/Commit selection, so stepping commits re-roots
    // the browser at that revision.

    private string? _filesSelPath;
    private readonly HashSet<string> _filesExpanded = new(StringComparer.Ordinal);
    private string? _filesTreeKey;   // the commit the expansion/selection belongs to

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
    private const int FileLineHeight = 20;

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
        _filesExpanded.Clear();
        // Keep the selected path across commits when it still exists — checked in
        // PickInitialTreeFile once the new tree lands.
    }

    private void PickInitialTreeFile(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) { _filesSelPath = null; return; }
        if (_filesSelPath == null || !paths.Contains(_filesSelPath))
            _filesSelPath = paths[0];
        ExpandAncestors(_filesSelPath!);
    }

    private void ExpandAncestors(string path)
    {
        var parts = path.Split('/');
        var acc = "";
        for (int i = 0; i < parts.Length - 1; i++)
        {
            acc = acc.Length == 0 ? parts[i] : acc + "/" + parts[i];
            _filesExpanded.Add(acc);
        }
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

    private sealed class TreeNode
    {
        public string Name = "";
        public string Path = "";
        public bool IsDir;
        public List<TreeNode> Children = [];
    }

    /// <summary>Builds a nested folder/file tree from git's flat sorted path list.</summary>
    private static TreeNode BuildTree(IReadOnlyList<string> paths)
    {
        var root = new TreeNode { IsDir = true };
        foreach (var path in paths)
        {
            var parts = path.Split('/');
            var node = root;
            var acc = "";
            for (int i = 0; i < parts.Length; i++)
            {
                acc = acc.Length == 0 ? parts[i] : acc + "/" + parts[i];
                bool leaf = i == parts.Length - 1;
                var existing = node.Children.FirstOrDefault(c => c.Name == parts[i] && c.IsDir != leaf);
                if (existing == null)
                {
                    existing = new TreeNode { Name = parts[i], Path = acc, IsDir = !leaf };
                    node.Children.Add(existing);
                }
                node = existing;
            }
        }
        SortTree(root);
        return root;
    }

    // Folders first, then files; both alphabetical (git's order is byte-wise).
    private static void SortTree(TreeNode node)
    {
        node.Children.Sort((a, b) =>
            a.IsDir != b.IsDir ? (a.IsDir ? -1 : 1)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var c in node.Children) SortTree(c);
    }

    private Element BuildFileTreePane(IReadOnlyList<string> paths, bool loading)
    {
        var pane = Flex.Column(gap: 0)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        if (_hideDiff) pane.FlexGrow(1);
        else pane.Width(300).FlexShrink(0);

        var scroll = ScrollView.New().FlexGrow(1).Padding(new Thickness(6, 6, 6, 6));
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
        else
        {
            foreach (var child in BuildTree(paths).Children)
                AddTreeRows(scroll, child, depth: 0);
        }
        pane.AddChild(scroll);
        return pane;
    }

    private void AddTreeRows(Element parent, TreeNode node, int depth)
    {
        parent.AddChild(TreeRow(node, depth));
        if (!node.IsDir || !_filesExpanded.Contains(node.Path)) return;
        foreach (var child in node.Children)
            AddTreeRows(parent, child, depth + 1);
    }

    private Element TreeRow(TreeNode node, int depth)
    {
        bool selected = !node.IsDir && node.Path == _filesSelPath;
        bool expanded = node.IsDir && _filesExpanded.Contains(node.Path);

        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Height(FileTreeRowHeight).FlexShrink(0)
            .Padding(new Thickness(0, 6, 0, 6 + depth * 12))
            .CornerRadius(Nocturne.RadiusSm)
            .Cursor(CursorType.Pointer).Tooltip(node.Path);
        row.Key = (node.IsDir ? "d:" : "f:") + node.Path + (selected ? "#s" : "") + (expanded ? "#e" : "");
        if (selected) row.Background(Nocturne.Accent900);

        if (node.IsDir)
        {
            row.AddChild(Ui.Icon(expanded ? Icons.CaretDown : Icons.CaretRight, 10f, Nocturne.On(0.5)).FlexShrink(0));
            row.AddChild(Ui.Icon(Icons.Folders, 11f, Nocturne.Accent300).FlexShrink(0));
        }
        else
        {
            row.AddChild(Box.New().Width(10).FlexShrink(0));
            row.AddChild(Ui.Icon(IsImagePath(node.Path) ? Icons.FileImage : Icons.FileCode, 11f,
                Nocturne.On(0.45)).FlexShrink(0));
        }

        row.AddChild(Text.New(node.Name).FontSize(12f)
            .TextColor(node.IsDir ? Nocturne.Text : Nocturne.On(selected ? 0.9 : 0.72))
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));

        if (node.IsDir)
            row.OnClick(_ => ToggleFolder(node.Path));
        else
            row.OnClick(_ => SelectTreeFile(node.Path));
        if (!selected) row.HoverBg(Nocturne.Neutral900, Color.Transparent);
        return row;
    }

    private void ToggleFolder(string path)
    {
        if (!_filesExpanded.Add(path)) _filesExpanded.Remove(path);
        MarkDirty();
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

    // ── Content pane ─────────────────────────────────────────────────────

    // Runs off the UI thread: read the blob, decide text/binary/image, tokenize.
    private FileContent LoadFileContent(string repoPath, string reference, string path)
    {
        var repo = RepoFor(repoPath);
        var bytes = repo.GetFileBytes(reference, path);
        if (bytes == null) return FileContent.Empty;

        if (IsImagePath(path))
            return new FileContent(null, null, MakeSide(bytes), false, bytes.Length);

        // A NUL byte in the first 8 KB is git's own heuristic for "binary".
        int probe = Math.Min(bytes.Length, 8000);
        for (int i = 0; i < probe; i++)
            if (bytes[i] == 0)
                return new FileContent(null, null, null, true, bytes.Length);

        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        IReadOnlyList<TextSpan>[]? spans = null;
        try { spans = SyntaxHighlighter.TokenizeLines(path, text); } catch { /* highlight is best-effort */ }
        return new FileContent(lines, spans, null, false, bytes.Length);
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
        pane.AddChild(header);

        pane.AddChild(BuildFileBody(loader, content));
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

        // Virtualized: a large file must not build one element per line.
        var rows = lines.Select((text, i) => new FileLine(i + 1, text,
            content.Spans is { } s && i < s.Length ? s[i] : null)).ToList();

        var list = VirtualListElement<FileLine>.New()
            .ItemHeight(FileLineHeight)
            .Items(rows)
            .RenderWith(RenderFileLine)
            .FlexGrow(1)
            .Background(Nocturne.Bg);
        list.Key = "filebody:" + (_filesSelPath ?? "");
        list.IsFocusable = true;
        list.TabIndex = 0;
        list.KeyboardScroll();   // click the body, then arrow/page through the file
        return list;
    }

    private sealed record FileLine(int Number, string Text, IReadOnlyList<TextSpan>? Spans);

    private static Element RenderFileLine(FileLine l) =>
        Flex.Row(gap: 0).AlignItems(AlignItems.Center).Children(
            Box.New().Width(52f).FlexShrink(0)
                .JustifyContent(JustifyContent.FlexEnd).Padding(new Thickness(0, 10, 0, 0))
                .Children(Ui.Mono(l.Number.ToString(), 11f, Nocturne.On(0.28))),
            Ui.Mono(l.Text.Length == 0 ? " " : l.Text, 12f, Nocturne.On(0.85))
                .Spans(l.Spans)
                .FlexGrow(1).Padding(new Thickness(2, 14, 2, 0))
                .MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
}
