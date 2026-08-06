using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using GitClient.Git;
using GitClient.Theme;

namespace GitClient.Junction;

public partial class JunctionApp
{
    // ── Diff pane (header + body), shared by Commit / Changes / fullscreen ──

    private Element BuildDiffPane(string path, GitFileDiff? diff, DiffInteract? interact = null)
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);

        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(8, 12, 8, 12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Ui.Mono(path.Length > 0 ? path : "—", 12f, Nocturne.Text)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        header.AddChild(DiffModeToggle());
        header.AddChild(IgnoreWhitespaceToggle());
        header.AddChild(Ui.IconButton(Icons.FileCode, OpenSelectedDiffExternally, "Open in VS Code (Ctrl D)").FlexShrink(0));
        header.AddChild(Ui.IconButton(Icons.ArrowsOut, () => { _diffFull = true; MarkDirty(); }, "Fullscreen diff").FlexShrink(0));
        col.AddChild(header);

        // Interactive staging: an action bar appears while lines are selected, plus
        // a one-line hint when nothing is selected yet (so the affordance is discoverable).
        if (interact != null && diff != null)
        {
            if (_stageSel.Count > 0)
                col.AddChild(BuildStageActionBar(diff, interact));
            else
                col.AddChild(BuildStageHint(interact));
        }
        else if (diff != null && View == JunctionView.Changes && !_changeSelConflicted
                 && _settings.Git.IgnoreWhitespace)
        {
            // Explain the missing affordance rather than just dropping it.
            col.AddChild(Flex.Row(gap: 6).FlexShrink(0).AlignItems(AlignItems.Center)
                .Padding(new Thickness(5, 12, 5, 12))
                .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0))
                .Children(
                    Ui.Mono("W", 11f, Nocturne.Accent300),
                    Text.New("Ignoring whitespace — line staging is off (this view no longer matches the file exactly)")
                        .FontSize(10.5f).TextColor(Nocturne.On(0.45))));
        }

        // Images have no textual diff (git reports "Binary files … differ"), so they
        // get a before/after view instead of the empty "No textual changes" pane.
        if (IsImagePath(path) && BuildImageDiffBody(path) is { } imagePane)
        {
            col.AddChild(imagePane);
            return col;
        }

        col.AddChild(BuildDiffScroll(diff, interact));
        return col;
    }

    private Element BuildStageHint(DiffInteract it) =>
        Flex.Row(gap: 6).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, 12, 5, 12))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0))
            .Children(
                Ui.Icon(Icons.CheckSquare, 11f, Nocturne.On(0.4)),
                Text.New(it.Staged
                        ? "Click a line or hunk header to unstage part of this file"
                        : "Click a line or hunk header to stage or discard part of this file")
                    .FontSize(10.5f).TextColor(Nocturne.On(0.4)));

    private Element DiffModeToggle()
    {
        // FlexShrink(0) so the toggle keeps its size as the diff pane narrows (the
        // header path ellipsizes to absorb the shrink instead of the buttons
        // overlapping); compact windows drop the labels to icon-only.
        var seg = Flex.Row(gap: 2).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(2))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusRow);
        seg.AddChild(DiffModeButton("Unified", Icons.Rows, !_diffSplit, () => { _diffSplit = false; MarkDirty(); }));
        seg.AddChild(DiffModeButton("Split", Icons.Columns, _diffSplit, () => { _diffSplit = true; MarkDirty(); }));
        return seg;
    }

    // "Ignore whitespace" (git --ignore-all-space). Lit while active; the tooltip
    // names the staging trade-off, since a -w diff can't produce appliable patches.
    private Element IgnoreWhitespaceToggle()
    {
        bool on = _settings.Git.IgnoreWhitespace;
        var b = Flex.Row(gap: 0).Width(30).Height(30).FlexShrink(0)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .CornerRadius(Nocturne.RadiusSm)
            .BorderColor(on ? Nocturne.Accent : Nocturne.Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .Tooltip(on
                ? "Ignoring whitespace — line staging is off while active"
                : "Ignore whitespace changes");
        if (on) b.Background(Nocturne.Alpha(Nocturne.Accent, 0.14));
        b.AddChild(Ui.Mono("W", 12f, on ? Nocturne.Accent300 : Nocturne.On(0.55)));
        b.Key = "ws-toggle" + (on ? "#on" : "");
        b.OnClick(e => { e.StopPropagation = true; ToggleIgnoreWhitespace(); });
        if (!on) b.HoverBg(Nocturne.Alpha(Nocturne.Text, 0.07), Color.Transparent);
        return b;
    }

    private void ToggleIgnoreWhitespace()
    {
        _settings.Git.IgnoreWhitespace = !_settings.Git.IgnoreWhitespace;
        _stageSel.Clear();     // a -w diff's lines aren't stageable; drop any picks
        Persist();
        RefreshAll();          // diff loaders re-key on the flag and reload
    }

    private Element DiffModeButton(string label, string glyph, bool active, Action onClick)
    {
        var fg = active ? Nocturne.Accent : Nocturne.On(0.58);
        var b = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4, 9, 4, 9)).CornerRadius(5f).Cursor(CursorType.Pointer)
            .Tooltip(label);
        if (active) b.Background(Nocturne.Bg).BorderColor(Nocturne.Accent).BorderWidth(1f);
        b.AddChild(Ui.Icon(glyph, 12f, fg));
        if (!_compactChrome)
            b.AddChild(Text.New(label).FontSize(11.5f).FontWeight(FontWeight.Medium).TextColor(fg));
        b.OnClick(e => { e.StopPropagation = true; onClick(); });
        return b;
    }

    private const int DiffRowHeight = 22;

    private enum DiffRowType : byte { Hunk, Unified, Split }

    private sealed record DiffRow
    {
        public DiffRowType Type { get; init; }
        public string Hunk { get; init; } = "";
        public GitDiffHunk? HunkRef { get; init; }   // set on Hunk rows, for hunk-level staging
        public GitDiffLine? Line { get; init; }   // unified
        public GitDiffLine? Left { get; init; }   // split
        public GitDiffLine? Right { get; init; }
        // Syntax-highlight spans for the code cell(s), line-relative offsets.
        public IReadOnlyList<HultUI.Core.Rendering.TextSpan>? Spans { get; init; }        // unified
        public IReadOnlyList<HultUI.Core.Rendering.TextSpan>? LeftSpans { get; init; }    // split
        public IReadOnlyList<HultUI.Core.Rendering.TextSpan>? RightSpans { get; init; }
    }

    // Virtualized so large diffs keep constant row height and stay cheap — a
    // plain ScrollView over a tall column compresses its rows to fit.
    private Element BuildDiffScroll(GitFileDiff? diff, DiffInteract? interact = null)
    {
        if (diff == null || diff.Hunks.Count == 0)
            return Box.New().FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New(diff == null ? "Select a file" : "No textual changes")
                    .FontSize(13f).TextColor(Nocturne.On(0.45)));

        if (interact != null) SyncStageSelection(interact);

        var hl = HighlightForCurrent(diff.Path);
        var rows = _diffSplit ? FlattenSplit(diff, hl) : FlattenUnified(diff, hl);
        var list = VirtualListElement<DiffRow>.New()
            .ItemHeight(DiffRowHeight)
            .Items(rows)
            .RenderWith(r => RenderDiffRow(r, interact))
            .FlexGrow(1)
            .Background(Nocturne.Bg);
        // Key by file so each file gets its own scroll position instead of one shared
        // offset — switching files reconciles to a fresh list that starts at the top.
        list.Key = "diff:" + diff.Path;
        // Focusable so clicking the diff lets arrow / page / home / end scroll it.
        list.IsFocusable = true;
        list.TabIndex = 0;
        list.On<KeyDownEvent>(e => DiffScrollKey(e, list));
        return list;
    }

    private void DiffScrollKey(KeyDownEvent e, VirtualListElement<DiffRow> list)
    {
        if (e.Key == KeyCode.Escape && _diffFull)
        {
            _diffFull = false;
            MarkDirty();
            e.StopPropagation = true;
            return;
        }

        var page = Math.Max(DiffRowHeight, list.Layout.AbsoluteContentBox.Height - DiffRowHeight);
        var handled = true;
        switch (e.Key)
        {
            case KeyCode.Down: list.ScrollOffsetY += DiffRowHeight; break;
            case KeyCode.Up: list.ScrollOffsetY -= DiffRowHeight; break;
            case KeyCode.PageDown: list.ScrollOffsetY += page; break;
            case KeyCode.PageUp: list.ScrollOffsetY -= page; break;
            case KeyCode.Home: list.ScrollOffsetY = 0; break;
            case KeyCode.End: list.ScrollOffsetY = 1_000_000f; break;   // clamped to content
            default: handled = false; break;
        }
        if (handled) e.StopPropagation = true;
    }

    private static List<DiffRow> FlattenUnified(GitFileDiff diff, FileHighlight? hl)
    {
        var rows = new List<DiffRow>();
        foreach (var hunk in diff.Hunks)
        {
            rows.Add(new DiffRow { Type = DiffRowType.Hunk, Hunk = hunk.Header, HunkRef = hunk });
            var lines = hunk.Lines.Where(l => l.Kind != DiffLineKind.HunkHeader).ToList();
            var intra = IntralineByLine(lines);
            foreach (var line in lines)
                rows.Add(new DiffRow { Type = DiffRowType.Unified, Line = line, Spans = DiffSpans(hl, line, intra) });
        }
        return rows;
    }

    private static List<DiffRow> FlattenSplit(GitFileDiff diff, FileHighlight? hl)
    {
        var rows = new List<DiffRow>();
        foreach (var hunk in diff.Hunks)
        {
            rows.Add(new DiffRow { Type = DiffRowType.Hunk, Hunk = hunk.Header, HunkRef = hunk });
            var lines = hunk.Lines.Where(l => l.Kind != DiffLineKind.HunkHeader).ToList();
            var intra = IntralineByLine(lines);
            foreach (var (left, right) in SplitPairs(lines))
                rows.Add(new DiffRow
                {
                    Type = DiffRowType.Split, Left = left, Right = right,
                    LeftSpans = left == null ? null : DiffSpans(hl, left, intra),
                    RightSpans = right == null ? null : DiffSpans(hl, right, intra),
                });
        }
        return rows;
    }

    // ── Intra-line (word-level) change emphasis ──────────────────────────
    //
    // Deletion/addition lines that SplitPairs puts side by side get a word
    // diff; the changed ranges render as brighter background spans on top of
    // the syntax colors. Keyed by line identity: identical lines can repeat
    // within a hunk, and each occurrence pairs with a different partner.

    private static Dictionary<GitDiffLine, IReadOnlyList<(int Start, int End)>> IntralineByLine(
        List<GitDiffLine> lines)
    {
        var byLine = new Dictionary<GitDiffLine, IReadOnlyList<(int Start, int End)>>(
            ReferenceEqualityComparer.Instance);
        foreach (var (left, right) in SplitPairs(lines))
        {
            if (left is not { Kind: DiffLineKind.Deletion } || right is not { Kind: DiffLineKind.Addition })
                continue;
            var (old, @new) = IntralineDiff.Compute(left.Content, right.Content);
            if (old != null) byLine[left] = old;
            if (@new != null) byLine[right] = @new;
        }
        return byLine;
    }

    private static IReadOnlyList<HultUI.Core.Rendering.TextSpan>? DiffSpans(FileHighlight? hl, GitDiffLine line,
        Dictionary<GitDiffLine, IReadOnlyList<(int Start, int End)>> intra)
    {
        var syntax = SpansFor(hl, line);
        if (!intra.TryGetValue(line, out var ranges)) return syntax;
        var bg = line.Kind == DiffLineKind.Addition ? Nocturne.DiffAddHl : Nocturne.DiffDelHl;
        return MergeHighlight(syntax, ranges, bg);
    }

    // Overlays background ranges onto (possibly absent) syntax spans, keeping
    // the result sorted and non-overlapping as TextSpan requires. Segments are
    // cut at every syntax-span or range boundary; a segment with no syntax
    // color gets a transparent foreground (= element text color).
    private static IReadOnlyList<HultUI.Core.Rendering.TextSpan> MergeHighlight(
        IReadOnlyList<HultUI.Core.Rendering.TextSpan>? syntax,
        IReadOnlyList<(int Start, int End)> ranges, Color bg)
    {
        var cuts = new SortedSet<int>();
        foreach (var (s, e) in ranges) { cuts.Add(s); cuts.Add(e); }
        if (syntax != null)
            foreach (var s in syntax) { cuts.Add(s.Start); cuts.Add(s.End); }

        var result = new List<HultUI.Core.Rendering.TextSpan>();
        int prev = -1;
        foreach (var cut in cuts)
        {
            if (prev >= 0 && cut > prev)
            {
                var fg = SyntaxColorAt(syntax, prev);
                bool highlighted = RangeContains(ranges, prev);
                if (fg.A > 0 || highlighted)
                    result.Add(new HultUI.Core.Rendering.TextSpan(
                        prev, cut - prev, fg, highlighted ? bg : default));
            }
            prev = cut;
        }
        return result;
    }

    private static Color SyntaxColorAt(IReadOnlyList<HultUI.Core.Rendering.TextSpan>? syntax, int pos)
    {
        if (syntax != null)
            foreach (var s in syntax)
                if (pos >= s.Start && pos < s.End)
                    return s.Color;
        return Color.Transparent;
    }

    private static bool RangeContains(IReadOnlyList<(int Start, int End)> ranges, int pos)
    {
        foreach (var (s, e) in ranges)
            if (pos >= s && pos < e)
                return true;
        return false;
    }

    private Element RenderDiffRow(DiffRow r, DiffInteract? interact) => r.Type switch
    {
        DiffRowType.Hunk => HunkHeaderRow(r.Hunk, r.HunkRef, interact),
        DiffRowType.Split => SplitRow(r.Left, r.Right, r.LeftSpans, r.RightSpans),
        _ => UnifiedRow(r.Line!, r.Spans, interact),
    };

    private Element HunkHeaderRow(string header, GitDiffHunk? hunk, DiffInteract? interact)
    {
        var box = Box.New()
            .Padding(new Thickness(5, 14, 5, 14))
            .Background(Nocturne.Alpha(Nocturne.Accent, 0.06))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0))
            .Children(Ui.Mono(header, 11.5f, Nocturne.Accent300).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));

        // Interactive: clicking the hunk header toggles all its changed lines.
        if (interact != null && hunk != null && HunkChangedLines(hunk).Any())
        {
            box.Cursor(CursorType.Pointer).Tooltip("Select all lines in this hunk");
            box.OnClick(_ => ToggleStageHunk(hunk));
        }
        return box;
    }

    private Element UnifiedRow(GitDiffLine line, IReadOnlyList<HultUI.Core.Rendering.TextSpan>? spans,
        DiffInteract? interact)
    {
        var (bg, code, sign, signColor) = line.Kind switch
        {
            DiffLineKind.Addition => (Nocturne.DiffAddBg, Nocturne.DiffAddText, "+", Nocturne.DiffAddSign),
            DiffLineKind.Deletion => (Nocturne.DiffDelBg, Nocturne.DiffDelText, "−", Nocturne.DiffDelSign),
            _ => (Color.Transparent, Nocturne.On(0.80), " ", Color.Transparent),
        };

        bool selectable = interact != null && line.Kind is DiffLineKind.Addition or DiffLineKind.Deletion;
        bool selected = selectable && _stageSel.Contains(line);
        if (selected) bg = Nocturne.Alpha(Nocturne.Accent, 0.22);

        var row = Flex.Row(gap: 0).AlignItems(AlignItems.Center).Background(bg);

        // Interactive mode gets a leading checkbox column (transparent on context
        // rows) so the sign/number columns stay aligned across every row.
        if (interact != null)
        {
            var mark = Box.New().Width(20).FlexShrink(0).JustifyContent(JustifyContent.Center);
            if (selectable)
                mark.AddChild(Ui.Icon(selected ? Icons.CheckSquare : Icons.Square, 12f,
                    selected ? Nocturne.Accent : Nocturne.On(0.34)));
            row.AddChild(mark);
        }

        row.AddChild(NumCell(line.OldLineNumber, 46f));
        row.AddChild(NumCell(line.NewLineNumber, 46f));
        row.AddChild(Box.New().Width(16).JustifyContent(JustifyContent.Center)
            .Children(Ui.Mono(sign, 12f, signColor)));
        row.AddChild(Ui.Mono(line.Content.Length == 0 ? " " : line.Content, 12f, code)
            .Spans(spans)
            .FlexGrow(1).Padding(new Thickness(2, 14, 2, 0))
            .MaxLines(1).OverflowMode(TextOverflow.Ellipsis));

        if (selectable)
        {
            row.Cursor(CursorType.Pointer);
            row.OnClick(_ => ToggleStageLine(line));
        }
        return row;
    }

    private static IEnumerable<(GitDiffLine? Left, GitDiffLine? Right)> SplitPairs(List<GitDiffLine> lines)
    {
        var i = 0;
        while (i < lines.Count)
        {
            var cur = lines[i];
            if (cur.Kind == DiffLineKind.Context)
            {
                yield return (cur, cur);
                i++;
                continue;
            }
            var dels = new List<GitDiffLine>();
            var adds = new List<GitDiffLine>();
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Deletion) dels.Add(lines[i++]);
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Addition) adds.Add(lines[i++]);
            var n = Math.Max(dels.Count, adds.Count);
            for (var k = 0; k < n; k++)
                yield return (k < dels.Count ? dels[k] : null, k < adds.Count ? adds[k] : null);
        }
    }

    private static Element SplitRow(GitDiffLine? left, GitDiffLine? right,
        IReadOnlyList<HultUI.Core.Rendering.TextSpan>? leftSpans,
        IReadOnlyList<HultUI.Core.Rendering.TextSpan>? rightSpans) =>
        Flex.Row(gap: 0).AlignItems(AlignItems.Stretch)
            .Children(
                SplitHalf(left, isLeft: true, leftSpans),
                SplitHalf(right, isLeft: false, rightSpans));

    private static Element SplitHalf(GitDiffLine? line, bool isLeft,
        IReadOnlyList<HultUI.Core.Rendering.TextSpan>? spans)
    {
        var (bg, code) = line?.Kind switch
        {
            DiffLineKind.Addition => (Nocturne.DiffAddBg, Nocturne.DiffAddText),
            DiffLineKind.Deletion => (Nocturne.DiffDelBg, Nocturne.DiffDelText),
            DiffLineKind.Context => (Color.Transparent, Nocturne.On(0.80)),
            _ => (Nocturne.DiffBlankBg, Color.Transparent),
        };
        var num = isLeft ? line?.OldLineNumber : line?.NewLineNumber;
        var text = line == null ? " " : (line.Content.Length == 0 ? " " : line.Content);

        var half = Flex.Row(gap: 0).AlignItems(AlignItems.Center).FlexGrow(1).FlexBasis(Dimension.Px(0))
            .Background(bg);
        if (isLeft) half.BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        half.AddChild(NumCell(num, 44f));
        half.AddChild(Ui.Mono(text, 12f, code).Spans(spans).FlexGrow(1).FlexShrink(1)
            .Padding(new Thickness(2, 10, 2, 0)).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
        return half;
    }

    private static Element NumCell(int? n, float width) =>
        Box.New().Width(width).FlexShrink(0)
            .JustifyContent(JustifyContent.FlexEnd)
            .Padding(new Thickness(0, 8, 0, 0))
            .Children(Ui.Mono(n?.ToString() ?? "", 11f, Nocturne.On(0.30)));

    // ── Fullscreen diff overlay ──────────────────────────────────────────

    private Element BuildFullscreenDiff()
    {
        var (path, diff) = CurrentDiffTarget();

        var overlay = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(Nocturne.Bg)
            .ZIndex(20);
        overlay.Position.Type = PositionType.Absolute;
        overlay.Position.Top = Dimension.Px(0);
        overlay.Position.Left = Dimension.Px(0);

        var col = Flex.Column(gap: 0).FlexGrow(1);

        var bar = Flex.Row(gap: 12).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(11, 16, 11, 16))
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        bar.AddChild(Ui.Icon(Icons.FileCode, 16f, Nocturne.Accent300));
        bar.AddChild(Ui.Mono(path.Length > 0 ? path : "—", 13f, Nocturne.Text)
            .FlexGrow(1).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        bar.AddChild(DiffModeToggle());
        bar.AddChild(Ui.SecondaryButton("Exit fullscreen (Esc)", Icons.ArrowsIn,
            () => { _diffFull = false; MarkDirty(); }, 12f));
        col.AddChild(bar);

        col.AddChild(BuildDiffScroll(diff));
        overlay.AddChild(col);
        return overlay;
    }

    /// <summary>The (path, diff) the fullscreen overlay should show for the active view.</summary>
    private (string Path, GitFileDiff? Diff) CurrentDiffTarget()
    {
        if (View == JunctionView.Commit && _selectedFilePath != null)
        {
            _commitDetail.Value.DiffByPath.TryGetValue(_selectedFilePath, out var d);
            return (_selectedFilePath, d);
        }
        if (View == JunctionView.Changes && _changeSelPath != null)
        {
            var dict = _changeSelStaged ? _changes.Value.StagedDiffs : _changes.Value.UnstagedDiffs;
            dict.TryGetValue(_changeSelPath, out var d);
            return (_changeSelPath, d);
        }
        return (_selectedFilePath ?? "", null);
    }

    private enum ExternalDiffMode { Commit, Staged, Unstaged }

    // Ctrl+D in the Commit / Changes views: open the selected file's diff in VS Code
    // (`code --wait --diff <left> <right>`). Reuses ExternalDiffLauncher's PATH/.cmd
    // resolution and its temp-file plumbing; --wait keeps the launched process alive
    // until the diff tab closes so its temp files survive, then are cleaned on exit.
    private async void OpenSelectedDiffExternally()
    {
        if (ActiveRepo is not { } repo) return;

        string path;
        ExternalDiffMode mode;
        string? commitHash = null;
        string? baseHash = null;
        if (View == JunctionView.Commit && !string.IsNullOrEmpty(_selectedFilePath) && _detailRange is { } r)
        {
            path = _selectedFilePath!;
            mode = ExternalDiffMode.Commit;
            commitHash = r.HeadHash;
            baseHash = r.BaseHash;
        }
        else if (View == JunctionView.Commit && !string.IsNullOrEmpty(_selectedFilePath) && _detailHash is { } h)
        {
            path = _selectedFilePath!;
            mode = ExternalDiffMode.Commit;
            commitHash = h;
        }
        else if (View == JunctionView.Changes && !string.IsNullOrEmpty(_changeSelPath))
        {
            path = _changeSelPath!;
            mode = _changeSelStaged ? ExternalDiffMode.Staged : ExternalDiffMode.Unstaged;
        }
        else
        {
            Flash("Select a file to diff first");
            return;
        }

        Flash("Opening diff in VS Code…");
        var (ok, err) = await Task.Run(() => LaunchVsCodeDiff(repo, path, mode, commitHash, baseHash));
        if (!ok) Flash(string.IsNullOrEmpty(err) ? "Could not open VS Code" : err);
    }

    // Runs off the UI thread (git show + temp-file writes + process start).
    private static (bool ok, string err) LaunchVsCodeDiff(
        GitRepository repo, string path, ExternalDiffMode mode, string? commitHash, string? baseHash = null)
    {
        string leftLabel, rightLabel, left, right;
        switch (mode)
        {
            case ExternalDiffMode.Commit:
                var hash = commitHash!;
                leftLabel = baseHash == null ? "parent" : Short7(baseHash);   // range compare: oldest selected
                rightLabel = Short7(hash);
                left = repo.GetFileText(baseHash ?? hash + "^", path) ?? "";   // "" for an added file / root commit
                right = repo.GetFileText(hash, path) ?? "";
                break;
            case ExternalDiffMode.Staged:
                leftLabel = "HEAD"; rightLabel = "index";
                left = repo.GetFileText("HEAD", path) ?? "";
                right = repo.GetIndexFileText(path) ?? "";
                break;
            default: // Unstaged
                leftLabel = "index"; rightLabel = "working";
                left = repo.GetIndexFileText(path) ?? repo.GetFileText("HEAD", path) ?? "";
                right = repo.GetWorkingFileText(path) ?? "";
                break;
        }

        var request = new ExternalDiffRequest
        {
            RepoPath = repo.Path,
            RelativePath = path,
            LeftLabel = leftLabel,
            LeftContent = left,
            RightLabel = rightLabel,
            RightContent = right,
        };
        var opts = new ExternalToolOptions
        {
            ExternalDiffToolPath = "code",
            ExternalDiffToolArgs = "--wait --diff {left} {right}",
        };
        bool ok = ExternalDiffLauncher.TryLaunch(opts, request, out var err);
        return (ok, err);
    }
}
